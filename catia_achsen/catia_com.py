"""Anbindung an CATIA V5 ueber COM (pywin32). Nur unter Windows mit laufendem CATIA lauffaehig.

Erzeugte Struktur im Part:

    <ergebnis_set>                      (Geometrisches Set)
      <Kurvenname>
        Abschnitte                      Kurventeilstuecke (Split)
        Punkte                          Punkte auf der Kurve
        Normalen                        Linien senkrecht (90 Grad) zur Flaeche
        Hilfsgeometrie (ausgeblendet)   Tangenten, Tangentialebenen, Projektionen
    Achsensysteme: <praefix>_<Kurve>_<Nr>   (Ursprung = Punkt, Z = Normale, X = Tangente Teilstueck)
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Any, Callable

from .config import Settings
from .geometry import CurveAnalysis, analyze

# --- CATIA Enumerationen ----------------------------------------------------
CAT_ORIGIN_BY_POINT = 1          # CatAxisSystemOriginType.catAxisSystemOriginByPoint
CAT_AXIS_SAME = 1                # CatAxisSystemAxisType.catAxisSystemAxisSameDirection
CAT_AXIS_OPPOSITE = 2            # CatAxisSystemAxisType.catAxisSystemAxisOppositeDirection
CAT_VIS_HIDE = 1                 # CatVisPropertyShow.catVisPropertyNoShowAttr
CAT_VBSCRIPT = 0                 # CATScriptLanguage.CATVBScriptLanguage
CAT_MULTISEL_VALIDATE = 2        # CATMultiSelTriggWhenUserValidatesSelection
MEASURABLE_CURVES = {7, 8, 9}    # CatMeasurableName: Curve, Circle, Line

# Abtastung komplett innerhalb von CATIA (eine COM-Rundreise statt tausender)
_VBS_SAMPLE = """
Function SampleCurve(oPart, oHB, oRef, n)
    Dim fac, pt, spa, ptRef, m, i
    Dim c(2)
    Dim res()
    ReDim res(3 * (n + 1) - 1)
    Set fac = oPart.HybridShapeFactory
    Set pt = fac.AddNewPointOnCurveFromPercent(oRef, 0.0, False)
    oHB.AppendHybridShape pt
    Set spa = oPart.Parent.GetWorkbench("SPAWorkbench")
    Set ptRef = oPart.CreateReferenceFromObject(pt)
    For i = 0 To n
        pt.Ratio.Value = i / n
        oPart.UpdateObject pt
        Set m = spa.GetMeasurable(ptRef)
        m.GetPoint c
        res(3 * i) = c(0)
        res(3 * i + 1) = c(1)
        res(3 * i + 2) = c(2)
    Next
    fac.DeleteObjectForDatum ptRef
    SampleCurve = res
End Function
"""

class CatiaError(RuntimeError):
    pass


@dataclass
class GeoInput:
    name: str
    obj: Any
    ref: Any


class CatiaSession:
    def __init__(self, log: Callable[[str], None] = print):
        self.log = log
        self.catia = None
        self.doc = None
        self.part = None

    # ------------------------------------------------------------------
    # Verbindung
    # ------------------------------------------------------------------
    def connect(self) -> None:
        try:
            import win32com.client  # noqa: WPS433
        except ImportError as e:
            raise CatiaError("pywin32 ist nicht installiert (pip install pywin32).") from e
        try:
            self.catia = win32com.client.GetActiveObject("CATIA.Application")
        except Exception as e:  # noqa: BLE001
            raise CatiaError("CATIA V5 laeuft nicht oder ist nicht erreichbar.") from e
        try:
            self.doc = self.catia.ActiveDocument
            self.part = self.doc.Part
        except Exception as e:  # noqa: BLE001
            raise CatiaError("Aktives Dokument muss ein CATPart sein.") from e
        self.log(f"Verbunden mit CATIA - Part: {self.part.Name}")

    def _ensure(self) -> None:
        if self.part is None:
            self.connect()

    @property
    def factory(self):
        return self.part.HybridShapeFactory

    @property
    def spa(self):
        return self.doc.GetWorkbench("SPAWorkbench")

    def ref(self, obj):
        return self.part.CreateReferenceFromObject(obj)

    # ------------------------------------------------------------------
    # Auswahl
    # ------------------------------------------------------------------
    def _is_curve(self, ref) -> bool:
        try:
            return int(self.spa.GetMeasurable(ref).GeometryName) in MEASURABLE_CURVES
        except Exception:  # noqa: BLE001
            return False

    def _collect_from_body(self, hb, out: list[GeoInput], recursive: bool = True) -> None:
        shapes = hb.HybridShapes
        for i in range(1, shapes.Count + 1):
            s = shapes.Item(i)
            r = self.ref(s)
            if self._is_curve(r):
                out.append(GeoInput(s.Name, s, r))
        if recursive:
            bodies = hb.HybridBodies
            for i in range(1, bodies.Count + 1):
                self._collect_from_body(bodies.Item(i), out, recursive)

    def curves_from_selection(self) -> list[GeoInput]:
        """Kurven aus aktueller Auswahl: einzelne Kurven und/oder Geometrische Sets."""
        self._ensure()
        sel = self.doc.Selection
        out: list[GeoInput] = []
        for i in range(1, sel.Count + 1):
            item = sel.Item(i)
            obj = item.Value
            if item.Type == "HybridBody":
                self._collect_from_body(obj, out)
                continue
            try:
                r = self.ref(obj)
            except Exception:  # noqa: BLE001
                r = item.Reference
            if self._is_curve(r):
                out.append(GeoInput(obj.Name, obj, r))
            else:
                self.log(f"Uebersprungen (keine Kurve): {getattr(obj, 'Name', '?')}")
        self.log(f"{len(out)} Kurve(n) uebernommen.")
        return out

    def pick_curves(self) -> list[GeoInput]:
        """Interaktive Mehrfachauswahl in CATIA (Kurven oder Geosets), Abschluss mit OK/Haken."""
        self._ensure()
        sel = self.doc.Selection
        sel.Clear()
        status = sel.SelectElement3(("HybridShape", "HybridBody", "MonoDimInfinite", "MonoDim"),
                                    "Kurven oder Geometrisches Set waehlen, dann bestaetigen",
                                    False, CAT_MULTISEL_VALIDATE, False)
        if status != "Normal":
            return []
        return self.curves_from_selection()

    def surface_from_selection(self, interactive: bool = False) -> GeoInput | None:
        self._ensure()
        sel = self.doc.Selection
        if interactive:
            sel.Clear()
            if sel.SelectElement2(("HybridShape", "Face", "BiDim"),
                                  "Referenzflaeche waehlen", False) != "Normal":
                return None
        if sel.Count < 1:
            return None
        item = sel.Item(1)
        obj = item.Value
        try:
            r = self.ref(obj)
        except Exception:  # noqa: BLE001
            r = item.Reference
        name = getattr(obj, "Name", "Flaeche")
        self.log(f"Flaeche: {name}")
        return GeoInput(name, obj, r)

    # ------------------------------------------------------------------
    # Geosets
    # ------------------------------------------------------------------
    def _get_or_add_body(self, parent_bodies, name: str):
        for i in range(1, parent_bodies.Count + 1):
            b = parent_bodies.Item(i)
            if b.Name == name:
                return b
        b = parent_bodies.Add()
        b.Name = name
        return b

    def _hide(self, obj) -> None:
        sel = self.doc.Selection
        sel.Clear()
        sel.Add(obj)
        sel.VisProperties.SetShow(CAT_VIS_HIDE)
        sel.Clear()

    def _delete(self, obj) -> None:
        sel = self.doc.Selection
        sel.Clear()
        sel.Add(obj)
        sel.Delete()

    # ------------------------------------------------------------------
    # Analyse
    # ------------------------------------------------------------------
    def length(self, ref) -> float:
        return float(self.spa.GetMeasurable(ref).Length)

    def sample(self, curve: GeoInput, cfg: Settings) -> tuple[list[tuple[float, float, float]], float]:
        self._ensure()
        L = self.length(curve.ref)
        n = int(min(cfg.max_abtastpunkte, max(20, math.ceil(L / cfg.abtastabstand))))
        tmp = self.part.HybridBodies.Add()
        tmp.Name = "_tmp_abtastung"
        try:
            raw = self.catia.SystemService.Evaluate(_VBS_SAMPLE, CAT_VBSCRIPT, "SampleCurve",
                                                    [self.part, tmp, curve.ref, n])
        finally:
            self._delete(tmp)
        vals = list(raw)
        pts = [(float(vals[3 * i]), float(vals[3 * i + 1]), float(vals[3 * i + 2])) for i in range(n + 1)]
        return pts, L

    def analyze(self, curves: list[GeoInput], cfg: Settings) -> list[CurveAnalysis]:
        results = []
        for c in curves:
            pts, L = self.sample(c, cfg)
            res = analyze(c.name, pts, L, cfg)
            self.log(f"{c.name}: L={L:.2f} mm, {len(res.segments)} Abschnitt(e), {len(res.punkte)} Punkt(e)")
            results.append(res)
        return results

    # ------------------------------------------------------------------
    # Erzeugung
    # ------------------------------------------------------------------
    def _point(self, hb, curve_ref, s: float, L: float, name: str):
        ratio = 0.0 if L <= 0 else min(1.0, max(0.0, s / L))
        p = self.factory.AddNewPointOnCurveFromPercent(curve_ref, ratio, False)
        p.Name = name
        hb.AppendHybridShape(p)
        self.part.UpdateObject(p)
        return p

    def _min_distance(self, ref_a, ref_b) -> float:
        return float(self.spa.GetMeasurable(ref_a).GetMinimumDistance(ref_b))

    def _split_keep(self, base_ref, cut_ref, probe_ref, target_hb, name: str):
        """Split an einem Punkt; behaelt die Seite, auf der der Pruefpunkt liegt."""
        sp = self.factory.AddNewHybridSplit(base_ref, cut_ref, 1)
        sp.Name = name
        target_hb.AppendHybridShape(sp)
        self.part.UpdateObject(sp)
        if self._min_distance(self.ref(sp), probe_ref) > 1e-3:
            sp.InvertOrientation()
            self.part.UpdateObject(sp)
        return sp

    def _make_segment(self, curve: GeoInput, an: CurveAnalysis, seg, hb_seg, hb_help, tag: str):
        """Kurventeilstueck [s_start, s_end] per (doppeltem) Split erzeugen."""
        L, tol = an.laenge, 1e-4
        cut_start = seg.s_start > tol
        cut_end = seg.s_end < L - tol
        if not (cut_start or cut_end):      # Abschnitt = ganze Kurve
            return curve.obj, curve.ref
        probe = self._point(hb_help, curve.ref, 0.5 * (seg.s_start + seg.s_end), L, f"{tag}_Mitte")
        probe_ref = self.ref(probe)
        base_ref = curve.ref
        base = None
        if cut_start:
            cut = self._point(hb_help, curve.ref, seg.s_start, L, f"{tag}_Start")
            target = hb_help if cut_end else hb_seg    # Zwischenergebnis -> Hilfsgeometrie
            base = self._split_keep(base_ref, self.ref(cut), probe_ref, target,
                                    f"{tag}_tmp" if cut_end else tag)
            base_ref = self.ref(base)
        if cut_end:
            cut = self._point(hb_help, curve.ref, seg.s_end, L, f"{tag}_Ende")
            base = self._split_keep(base_ref, self.ref(cut), probe_ref, hb_seg, tag)
        return base, self.ref(base)

    def build(self, curves: list[GeoInput], analyses: list[CurveAnalysis], surface: GeoInput,
              cfg: Settings, progress: Callable[[int, int], None] | None = None) -> int:
        """Erzeugt Abschnitte, Punkte, Normalen und Achsensysteme. Liefert Anzahl Achsensysteme."""
        self._ensure()
        if surface is None:
            raise CatiaError("Keine Referenzflaeche gewaehlt.")
        fac = self.factory
        root = self._get_or_add_body(self.part.HybridBodies, cfg.ergebnis_set)
        total = sum(len(a.punkte) for a in analyses)
        done, created, errors = 0, 0, 0
        lx = cfg.linienlaenge
        try:
            self.catia.RefreshDisplay = False
        except Exception:  # noqa: BLE001
            pass
        try:
            for ci, (curve, an) in enumerate(zip(curves, analyses), 1):
                cset = self._get_or_add_body(root.HybridBodies, f"{ci:02d}_{curve.name}")
                hb_seg = self._get_or_add_body(cset.HybridBodies, "Abschnitte")
                hb_pt = self._get_or_add_body(cset.HybridBodies, "Punkte")
                hb_nrm = self._get_or_add_body(cset.HybridBodies, "Normalen")
                hb_help = self._get_or_add_body(cset.HybridBodies, "Hilfsgeometrie")

                seg_refs = {}
                for seg in an.segments:
                    tag = f"Abschnitt_{seg.index + 1:02d}"
                    if cfg.abschnitte_erzeugen and len(an.segments) > 1:
                        try:
                            seg_refs[seg.index] = self._make_segment(curve, an, seg, hb_seg, hb_help, tag)[1]
                        except Exception as e:  # noqa: BLE001
                            self.log(f"  {tag}: Split fehlgeschlagen ({e}) - X referenziert Originalkurve")
                            seg_refs[seg.index] = curve.ref
                    else:
                        seg_refs[seg.index] = curve.ref

                for pi, (s, seg_idx) in enumerate(an.punkte, 1):
                    name = f"{cfg.praefix}_{ci:02d}_{pi:04d}"
                    try:
                        pt = self._point(hb_pt, curve.ref, s, an.laenge, f"P_{name}")
                        pt_ref = self.ref(pt)

                        normal = fac.AddNewLineNormal(surface.ref, pt_ref, 0.0, lx, False)
                        normal.Name = f"N_{name}"
                        hb_nrm.AppendHybridShape(normal)
                        self.part.UpdateObject(normal)

                        tan = fac.AddNewLineTangency(seg_refs[seg_idx], pt_ref, 0.0, lx, False)
                        tan.Name = f"T_{name}"
                        hb_help.AppendHybridShape(tan)
                        self.part.UpdateObject(tan)
                        x_ref = self.ref(tan)

                        if cfg.x_orthogonalisieren:
                            plane = fac.AddNewPlaneTangent(surface.ref, pt_ref)
                            plane.Name = f"E_{name}"
                            hb_help.AppendHybridShape(plane)
                            self.part.UpdateObject(plane)
                            proj = fac.AddNewProject(x_ref, self.ref(plane))
                            proj.Normal = True
                            proj.Name = f"TP_{name}"
                            hb_help.AppendHybridShape(proj)
                            self.part.UpdateObject(proj)
                            x_ref = self.ref(proj)

                        axs = self.part.AxisSystems.Add()
                        axs.OriginType = CAT_ORIGIN_BY_POINT
                        axs.OriginPoint = pt_ref
                        axs.XAxisType = CAT_AXIS_OPPOSITE if cfg.x_umkehren else CAT_AXIS_SAME
                        axs.XAxisDirection = x_ref
                        axs.ZAxisType = CAT_AXIS_OPPOSITE if cfg.z_umkehren else CAT_AXIS_SAME
                        axs.ZAxisDirection = self.ref(normal)
                        axs.Name = name
                        self.part.UpdateObject(axs)
                        created += 1
                    except Exception as e:  # noqa: BLE001
                        errors += 1
                        self.log(f"  Fehler bei {name} (s={s:.3f} mm): {e}")
                    done += 1
                    if progress:
                        progress(done, total)

                if cfg.hilfsgeometrie_ausblenden:
                    self._hide(hb_help)
            self.part.Update()
        finally:
            try:
                self.catia.RefreshDisplay = True
            except Exception:  # noqa: BLE001
                pass
        self.log(f"Fertig: {created} Achsensystem(e) erzeugt, {errors} Fehler.")
        return created

    # ------------------------------------------------------------------
    # Nachtraegliches Umkehren
    # ------------------------------------------------------------------
    def toggle_axes(self, which: str, prefix: str, only_selected: bool = False) -> int:
        """Kehrt X und/oder Z ('X', 'Z', 'XZ') der erzeugten Achsensysteme um.

        only_selected=True: nur in CATIA ausgewaehlte Achsensysteme, sonst alle mit Praefix."""
        self._ensure()
        targets = []
        if only_selected:
            sel = self.doc.Selection
            for i in range(1, sel.Count + 1):
                if sel.Item(i).Type == "AxisSystem":
                    targets.append(sel.Item(i).Value)
        else:
            axes = self.part.AxisSystems
            for i in range(1, axes.Count + 1):
                a = axes.Item(i)
                if a.Name.startswith(prefix + "_"):
                    targets.append(a)
        flip = {CAT_AXIS_SAME: CAT_AXIS_OPPOSITE, CAT_AXIS_OPPOSITE: CAT_AXIS_SAME}
        for a in targets:
            if "X" in which:
                a.XAxisType = flip.get(int(a.XAxisType), CAT_AXIS_SAME)
            if "Z" in which:
                a.ZAxisType = flip.get(int(a.ZAxisType), CAT_AXIS_SAME)
            self.part.UpdateObject(a)
        self.part.Update()
        self.log(f"{which} umgekehrt bei {len(targets)} Achsensystem(en).")
        return len(targets)
