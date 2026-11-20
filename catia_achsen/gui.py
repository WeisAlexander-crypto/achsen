"""Bedienoberflaeche (tkinter) fuer die Achsensystem-Erzeugung."""
from __future__ import annotations

import csv
import math
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

from .catia_com import CatiaError, CatiaSession
from .config import Rule, Settings

DEFAULT_CFG = Path(__file__).resolve().parent.parent / "config" / "regeln_standard.json"


def _fmt_r(r: float) -> str:
    return "gerade" if math.isinf(r) else f"{r:.2f}"


class App(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("CATIA V5 - Achsensysteme auf Kurven")
        self.geometry("1100x800")
        self.cfg = Settings.load(DEFAULT_CFG) if DEFAULT_CFG.exists() else Settings()
        self.session = CatiaSession(log=self.log)
        self.curves = []
        self.surface = None
        self.analyses = []
        self.vars: dict[str, tk.Variable] = {}
        self._build()
        self._load_into_ui()

    # ------------------------------------------------------------------ UI
    def _build(self) -> None:
        main = ttk.Frame(self, padding=8)
        main.pack(fill="both", expand=True)
        left = ttk.Frame(main)
        left.pack(side="left", fill="y")
        right = ttk.Frame(main)
        right.pack(side="left", fill="both", expand=True, padx=(8, 0))

        # 1. Eingabe
        f = ttk.LabelFrame(left, text="1. Eingabe", padding=6)
        f.pack(fill="x")
        ttk.Button(f, text="Kurven / Geoset aus Auswahl", command=self.take_curves).grid(row=0, column=0, sticky="ew")
        ttk.Button(f, text="Kurven in CATIA waehlen...", command=self.pick_curves).grid(row=0, column=1, sticky="ew")
        self.lbl_curves = ttk.Label(f, text="Keine Kurven")
        self.lbl_curves.grid(row=1, column=0, columnspan=2, sticky="w")
        ttk.Button(f, text="Flaeche aus Auswahl", command=lambda: self.take_surface(False)).grid(row=2, column=0, sticky="ew")
        ttk.Button(f, text="Flaeche in CATIA waehlen...", command=lambda: self.take_surface(True)).grid(row=2, column=1, sticky="ew")
        self.lbl_surface = ttk.Label(f, text="Keine Flaeche")
        self.lbl_surface.grid(row=3, column=0, columnspan=2, sticky="w")

        # 2. Regeln
        f = ttk.LabelFrame(left, text="2. Punktregeln (Radius von-bis -> Abstand)", padding=6)
        f.pack(fill="x", pady=6)
        self.rules_tv = ttk.Treeview(f, columns=("rmin", "rmax", "abst"), show="headings", height=7)
        for c, t in (("rmin", "R von [mm]"), ("rmax", "R bis [mm]"), ("abst", "1 Punkt je [mm]")):
            self.rules_tv.heading(c, text=t)
            self.rules_tv.column(c, width=95, anchor="e")
        self.rules_tv.grid(row=0, column=0, columnspan=4)
        self.rules_tv.bind("<Double-1>", lambda e: self.edit_rule())
        ttk.Button(f, text="Neu", command=self.add_rule).grid(row=1, column=0, sticky="ew")
        ttk.Button(f, text="Bearbeiten", command=self.edit_rule).grid(row=1, column=1, sticky="ew")
        ttk.Button(f, text="Loeschen", command=self.del_rule).grid(row=1, column=2, sticky="ew")
        ttk.Button(f, text="Standard", command=self.reset_rules).grid(row=1, column=3, sticky="ew")
        self._entry(f, "abstand_sonst", "Gerade / groesser: 1 Punkt je [mm]", 2)
        ttk.Button(f, text="Regeln laden...", command=self.load_cfg).grid(row=3, column=0, columnspan=2, sticky="ew")
        ttk.Button(f, text="Regeln speichern...", command=self.save_cfg).grid(row=3, column=2, columnspan=2, sticky="ew")

        # 3. Optionen
        f = ttk.LabelFrame(left, text="3. Optionen", padding=6)
        f.pack(fill="x")
        rows = [("abtastabstand", "Abtastabstand [mm]"), ("glaettung", "Glaettung (Fenster)"),
                ("min_abschnittslaenge", "Min. Abschnittslaenge [mm]"), ("linienlaenge", "Linienlaenge [mm]"),
                ("ergebnis_set", "Ergebnis-Geoset"), ("praefix", "Praefix Achsensysteme")]
        for i, (k, t) in enumerate(rows):
            self._entry(f, k, t, i)
        r = len(rows)
        ttk.Label(f, text="Punktverteilung").grid(row=r, column=0, sticky="w")
        self.vars["verteilung"] = tk.StringVar()
        ttk.Combobox(f, textvariable=self.vars["verteilung"], values=("gleichmaessig", "exakt"),
                     state="readonly", width=14).grid(row=r, column=2, columnspan=2, sticky="w")
        checks = [("endpunkt_erzwingen", "Endpunkt erzwingen (exakt)"),
                  ("abschnitte_erzeugen", "Kurventeilstuecke erzeugen (Split)"),
                  ("x_orthogonalisieren", "X exakt senkrecht zu Z (Projektion)"),
                  ("hilfsgeometrie_ausblenden", "Hilfsgeometrie ausblenden"),
                  ("x_umkehren", "X-Achse umkehren"), ("z_umkehren", "Z-Achse umkehren")]
        for i, (k, t) in enumerate(checks, r + 1):
            self.vars[k] = tk.BooleanVar()
            ttk.Checkbutton(f, text=t, variable=self.vars[k]).grid(row=i, column=0, columnspan=4, sticky="w")

        # 4. Aktionen
        f = ttk.LabelFrame(left, text="4. Ausfuehren", padding=6)
        f.pack(fill="x", pady=6)
        ttk.Button(f, text="Analysieren (Vorschau)", command=self.run_analyze).grid(row=0, column=0, sticky="ew")
        ttk.Button(f, text="Erzeugen in CATIA", command=self.run_build).grid(row=0, column=1, sticky="ew")
        ttk.Button(f, text="Analyse als CSV...", command=self.export_csv).grid(row=0, column=2, sticky="ew")
        self.progress = ttk.Progressbar(f, mode="determinate")
        self.progress.grid(row=1, column=0, columnspan=3, sticky="ew", pady=4)

        # 5. Nachtraeglich umkehren
        f = ttk.LabelFrame(left, text="5. Erzeugte Achsensysteme umkehren", padding=6)
        f.pack(fill="x")
        self.vars["only_sel"] = tk.BooleanVar(value=False)
        ttk.Checkbutton(f, text="nur in CATIA ausgewaehlte", variable=self.vars["only_sel"]).grid(
            row=0, column=0, columnspan=3, sticky="w")
        for i, w in enumerate(("X", "Z", "XZ")):
            ttk.Button(f, text=f"{w} umkehren", command=lambda w=w: self.toggle(w)).grid(row=1, column=i, sticky="ew")

        # Rechts: Ergebnis + Log
        f = ttk.LabelFrame(right, text="Analyse: Kurvenabschnitte", padding=6)
        f.pack(fill="both", expand=True)
        cols = ("kurve", "abschn", "von", "bis", "laenge", "rmin", "rmax", "regel", "punkte")
        heads = ("Kurve", "Nr", "s von", "s bis", "Laenge", "R min", "R max", "Regel", "Punkte")
        self.res_tv = ttk.Treeview(f, columns=cols, show="headings")
        for c, h in zip(cols, heads):
            self.res_tv.heading(c, text=h)
            self.res_tv.column(c, width=70 if c not in ("kurve", "regel") else 140, anchor="e")
        sb = ttk.Scrollbar(f, orient="vertical", command=self.res_tv.yview)
        self.res_tv.configure(yscrollcommand=sb.set)
        self.res_tv.pack(side="left", fill="both", expand=True)
        sb.pack(side="left", fill="y")
        f = ttk.LabelFrame(right, text="Protokoll", padding=6)
        f.pack(fill="both", expand=True, pady=(6, 0))
        self.log_txt = tk.Text(f, height=12)
        self.log_txt.pack(fill="both", expand=True)

    def _entry(self, parent, key, text, row) -> None:
        ttk.Label(parent, text=text).grid(row=row, column=0, columnspan=2, sticky="w")
        self.vars[key] = tk.StringVar()
        ttk.Entry(parent, textvariable=self.vars[key], width=16).grid(row=row, column=2, columnspan=2, sticky="ew")

    # ------------------------------------------------------------- Settings
    def _load_into_ui(self) -> None:
        for k, v in self.vars.items():
            if hasattr(self.cfg, k):
                v.set(getattr(self.cfg, k))
        self._refresh_rules()

    def _read_ui(self) -> bool:
        try:
            for k, v in self.vars.items():
                if not hasattr(self.cfg, k):
                    continue
                cur = getattr(self.cfg, k)
                val = v.get()
                if isinstance(cur, bool):
                    setattr(self.cfg, k, bool(val))
                elif isinstance(cur, int):
                    setattr(self.cfg, k, int(val))
                elif isinstance(cur, float):
                    setattr(self.cfg, k, float(str(val).replace(",", ".")))
                else:
                    setattr(self.cfg, k, str(val))
        except ValueError as e:
            messagebox.showerror("Eingabe", f"Ungueltiger Wert: {e}")
            return False
        errs = self.cfg.validate()
        if errs:
            messagebox.showerror("Einstellungen", "\n".join(errs))
            return False
        for a, b in self.cfg.gaps():
            self.log(f"Hinweis: Radius {a:g}-{b:g} mm von keiner Regel abgedeckt -> naechste Regel wird verwendet.")
        return True

    def _refresh_rules(self) -> None:
        self.rules_tv.delete(*self.rules_tv.get_children())
        for r in self.cfg.sorted_rules():
            self.rules_tv.insert("", "end", values=(f"{r.r_min:g}", "inf" if math.isinf(r.r_max) else f"{r.r_max:g}",
                                                   f"{r.abstand:g}"))
        self.cfg.regeln = self.cfg.sorted_rules()

    def _rule_dialog(self, rule: Rule | None) -> Rule | None:
        d = tk.Toplevel(self)
        d.title("Regel")
        d.transient(self)
        d.grab_set()
        vals = [tk.StringVar(value="" if rule is None else f"{x:g}") for x in
                ((rule.r_min, rule.r_max, rule.abstand) if rule else (0, 0, 0))]
        for i, t in enumerate(("Radius von [mm]", "Radius bis [mm] (inf = unendlich)", "1 Punkt je [mm]")):
            ttk.Label(d, text=t).grid(row=i, column=0, sticky="w", padx=6, pady=3)
            ttk.Entry(d, textvariable=vals[i]).grid(row=i, column=1, padx=6)
        result: list[Rule] = []

        def ok():
            try:
                from .config import _to_float
                result.append(Rule(float(vals[0].get().replace(",", ".")),
                                   _to_float(vals[1].get().replace(",", ".")),
                                   float(vals[2].get().replace(",", "."))))
            except ValueError:
                messagebox.showerror("Regel", "Bitte Zahlen eingeben.", parent=d)
                return
            d.destroy()

        ttk.Button(d, text="OK", command=ok).grid(row=3, column=0, columnspan=2, pady=6)
        self.wait_window(d)
        return result[0] if result else None

    def add_rule(self) -> None:
        r = self._rule_dialog(None)
        if r:
            self.cfg.regeln.append(r)
            self._refresh_rules()

    def edit_rule(self) -> None:
        sel = self.rules_tv.selection()
        if not sel:
            return
        i = self.rules_tv.index(sel[0])
        r = self._rule_dialog(self.cfg.regeln[i])
        if r:
            self.cfg.regeln[i] = r
            self._refresh_rules()

    def del_rule(self) -> None:
        for s in sorted((self.rules_tv.index(x) for x in self.rules_tv.selection()), reverse=True):
            del self.cfg.regeln[s]
        self._refresh_rules()

    def reset_rules(self) -> None:
        from .config import default_rules
        self.cfg.regeln = default_rules()
        self._refresh_rules()

    def load_cfg(self) -> None:
        p = filedialog.askopenfilename(filetypes=[("JSON", "*.json")], initialdir=DEFAULT_CFG.parent)
        if p:
            self.cfg = Settings.load(p)
            self._load_into_ui()
            self.log(f"Regeln geladen: {p}")

    def save_cfg(self) -> None:
        if not self._read_ui():
            return
        p = filedialog.asksaveasfilename(defaultextension=".json", filetypes=[("JSON", "*.json")],
                                         initialdir=DEFAULT_CFG.parent)
        if p:
            self.cfg.save(p)
            self.log(f"Regeln gespeichert: {p}")

    # -------------------------------------------------------------- Aktionen
    def log(self, msg: str) -> None:
        self.log_txt.insert("end", msg + "\n")
        self.log_txt.see("end")
        self.update_idletasks()

    def _catia(self, fn, *args):
        try:
            return fn(*args)
        except CatiaError as e:
            messagebox.showerror("CATIA", str(e))
        except Exception as e:  # noqa: BLE001
            self.log(f"Fehler: {e}")
            messagebox.showerror("Fehler", str(e))
        return None

    def take_curves(self) -> None:
        c = self._catia(self.session.curves_from_selection)
        if c is not None:
            self._set_curves(c)

    def pick_curves(self) -> None:
        c = self._catia(self.session.pick_curves)
        if c is not None:
            self._set_curves(c)

    def _set_curves(self, curves) -> None:
        self.curves = curves
        self.analyses = []
        names = ", ".join(c.name for c in curves[:4]) + (" ..." if len(curves) > 4 else "")
        self.lbl_curves.config(text=f"{len(curves)} Kurve(n): {names}" if curves else "Keine Kurven")

    def take_surface(self, interactive: bool) -> None:
        s = self._catia(self.session.surface_from_selection, interactive)
        if s is not None:
            self.surface = s
            self.lbl_surface.config(text=f"Flaeche: {s.name}")

    def run_analyze(self) -> bool:
        if not self.curves:
            messagebox.showwarning("Analyse", "Zuerst Kurven waehlen.")
            return False
        if not self._read_ui():
            return False
        res = self._catia(self.session.analyze, self.curves, self.cfg)
        if res is None:
            return False
        self.analyses = res
        rules = self.cfg.sorted_rules()
        self.res_tv.delete(*self.res_tv.get_children())
        for an in res:
            for seg in an.segments:
                self.res_tv.insert("", "end", values=(
                    an.name, seg.index + 1, f"{seg.s_start:.2f}", f"{seg.s_end:.2f}", f"{seg.laenge:.2f}",
                    _fmt_r(seg.r_min), _fmt_r(seg.r_max), seg.rule_label(rules), len(seg.punkte)))
        self.log(f"Analyse: {sum(len(a.punkte) for a in res)} Punkte geplant.")
        return True

    def run_build(self) -> None:
        if not self.run_analyze():
            return
        if self.surface is None:
            messagebox.showwarning("Erzeugen", "Zuerst die Referenzflaeche waehlen.")
            return
        n = sum(len(a.punkte) for a in self.analyses)
        if not messagebox.askyesno("Erzeugen", f"{n} Achsensysteme erzeugen?"):
            return

        def prog(done, total):
            self.progress["maximum"] = max(1, total)
            self.progress["value"] = done
            if done % 10 == 0 or done == total:
                self.update_idletasks()

        self._catia(self.session.build, self.curves, self.analyses, self.surface, self.cfg, prog)

    def toggle(self, which: str) -> None:
        self._read_ui()
        self._catia(self.session.toggle_axes, which, self.cfg.praefix, bool(self.vars["only_sel"].get()))

    def export_csv(self) -> None:
        if not self.analyses:
            messagebox.showinfo("CSV", "Zuerst analysieren.")
            return
        p = filedialog.asksaveasfilename(defaultextension=".csv", filetypes=[("CSV", "*.csv")])
        if not p:
            return
        rules = self.cfg.sorted_rules()
        with open(p, "w", newline="", encoding="utf-8-sig") as fh:
            w = csv.writer(fh, delimiter=";")
            w.writerow(["Kurve", "Abschnitt", "s_von", "s_bis", "Laenge", "R_min", "R_max", "Regel", "Abstand",
                        "Punkt_s"])
            for an in self.analyses:
                for seg in an.segments:
                    for s in seg.punkte:
                        w.writerow([an.name, seg.index + 1, f"{seg.s_start:.3f}", f"{seg.s_end:.3f}",
                                    f"{seg.laenge:.3f}", _fmt_r(seg.r_min), _fmt_r(seg.r_max),
                                    seg.rule_label(rules), seg.abstand, f"{s:.3f}"])
        self.log(f"CSV gespeichert: {p}")


def main() -> None:
    App().mainloop()
