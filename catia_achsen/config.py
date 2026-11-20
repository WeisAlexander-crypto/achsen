"""Einstellungen und Punktregeln (anpassbar, als JSON speicherbar)."""
from __future__ import annotations

import json
import math
from dataclasses import asdict, dataclass, field
from pathlib import Path


@dataclass
class Rule:
    """Eine Punktregel: Radius im Bereich [r_min, r_max) -> alle `abstand` mm ein Punkt."""

    r_min: float
    r_max: float
    abstand: float

    def contains(self, r: float) -> bool:
        return self.r_min <= r < self.r_max

    def label(self) -> str:
        return f"R {self.r_min:g}-{self.r_max:g} mm / {self.abstand:g} mm"


def default_rules() -> list[Rule]:
    # Beispielwerte - in der Oberflaeche oder in der JSON-Datei anpassbar.
    return [
        Rule(0.0, 10.0, 0.5),
        Rule(10.0, 15.0, 1.0),
        Rule(15.0, 30.0, 3.0),
        Rule(30.0, 100.0, 5.0),
        Rule(100.0, 500.0, 10.0),
    ]


@dataclass
class Settings:
    regeln: list[Rule] = field(default_factory=default_rules)
    # Abstand fuer Geraden bzw. Radien oberhalb aller Regeln
    abstand_sonst: float = 20.0

    # --- Analyse ---
    abtastabstand: float = 0.5          # mm zwischen zwei Messpunkten auf der Kurve
    max_abtastpunkte: int = 4000         # Obergrenze pro Kurve
    glaettung: int = 5                   # Fensterbreite Median-Filter (ungerade, 1 = aus)
    min_abschnittslaenge: float = 2.0    # kuerzere Abschnitte werden mit dem Nachbarn verschmolzen

    # --- Punkte ---
    verteilung: str = "gleichmaessig"    # "gleichmaessig" oder "exakt"
    endpunkt_erzwingen: bool = True      # bei "exakt": Abschnittsende immer mit Punkt

    # --- Geometrie / Achsen ---
    abschnitte_erzeugen: bool = True     # Kurve in Teilstuecke splitten (X referenziert Teilstueck)
    linienlaenge: float = 10.0           # Laenge Normalen- und Tangentenlinien
    x_orthogonalisieren: bool = True     # Tangente in Tangentialebene projizieren (X exakt senkrecht zu Z)
    x_umkehren: bool = False
    z_umkehren: bool = False
    hilfsgeometrie_ausblenden: bool = True

    # --- Benennung ---
    ergebnis_set: str = "Achsen_Ergebnis"
    praefix: str = "AX"

    # ------------------------------------------------------------------
    def validate(self) -> list[str]:
        """Liefert Fehler (leere Liste = gueltig). Luecken sind erlaubt, werden aber gemeldet."""
        errors: list[str] = []
        if not self.regeln:
            errors.append("Mindestens eine Regel ist erforderlich.")
        for i, r in enumerate(self.regeln, 1):
            if r.r_min < 0 or r.r_max <= r.r_min:
                errors.append(f"Regel {i}: r_min muss >= 0 und kleiner als r_max sein.")
            if r.abstand <= 0:
                errors.append(f"Regel {i}: Abstand muss > 0 sein.")
        rs = sorted(self.regeln, key=lambda r: r.r_min)
        for a, b in zip(rs, rs[1:]):
            if b.r_min < a.r_max - 1e-12:
                errors.append(f"Regeln ueberschneiden sich: {a.label()} und {b.label()}.")
        if self.abstand_sonst <= 0:
            errors.append("Abstand fuer Geraden muss > 0 sein.")
        if self.abtastabstand <= 0:
            errors.append("Abtastabstand muss > 0 sein.")
        if self.glaettung < 1:
            errors.append("Glaettung muss >= 1 sein.")
        if self.min_abschnittslaenge < 0:
            errors.append("Mindestabschnittslaenge muss >= 0 sein.")
        if self.verteilung not in ("gleichmaessig", "exakt"):
            errors.append("Verteilung muss 'gleichmaessig' oder 'exakt' sein.")
        if self.linienlaenge <= 0:
            errors.append("Linienlaenge muss > 0 sein.")
        return errors

    def gaps(self) -> list[tuple[float, float]]:
        """Radiusbereiche zwischen den Regeln, die von keiner Regel abgedeckt sind."""
        rs = sorted(self.regeln, key=lambda r: r.r_min)
        return [(a.r_max, b.r_min) for a, b in zip(rs, rs[1:]) if b.r_min > a.r_max + 1e-12]

    def sorted_rules(self) -> list[Rule]:
        return sorted(self.regeln, key=lambda r: r.r_min)

    # ------------------------------------------------------------------
    def to_dict(self) -> dict:
        d = asdict(self)
        return d

    @classmethod
    def from_dict(cls, d: dict) -> "Settings":
        base = cls()
        known = {k: v for k, v in d.items() if k in base.__dataclass_fields__ and k != "regeln"}
        s = cls(**known)
        if "regeln" in d:
            s.regeln = [Rule(float(r["r_min"]), _to_float(r["r_max"]), float(r["abstand"]))
                        for r in d["regeln"]]
        return s

    def save(self, path: str | Path) -> None:
        d = self.to_dict()
        for r in d["regeln"]:
            if math.isinf(r["r_max"]):
                r["r_max"] = "inf"
        Path(path).write_text(json.dumps(d, indent=2, ensure_ascii=False), encoding="utf-8")

    @classmethod
    def load(cls, path: str | Path) -> "Settings":
        return cls.from_dict(json.loads(Path(path).read_text(encoding="utf-8")))


def _to_float(v) -> float:
    if isinstance(v, str) and v.strip().lower() in ("inf", "unendlich", "infinity"):
        return math.inf
    return float(v)
