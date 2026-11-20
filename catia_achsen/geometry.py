"""Reine Geometrie-Logik ohne CATIA: Kruemmungsradius, Abschnitte, Punktverteilung.

Eingabe ist immer eine Liste von Messpunkten, die gleichmaessig ueber die
Bogenlaenge der Kurve verteilt sind (Punkt i liegt bei s_i = L * i / n).
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field
from typing import Sequence

from .config import Rule, Settings

Vec = tuple[float, float, float]
STRAIGHT = -1  # Regelindex fuer "Gerade / sonstiger Radius"


# ----------------------------------------------------------------------------
# Vektorhilfen
# ----------------------------------------------------------------------------
def _sub(a: Vec, b: Vec) -> Vec:
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _norm(a: Vec) -> float:
    return math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2])


def _cross(a: Vec, b: Vec) -> Vec:
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def circumradius(a: Vec, b: Vec, c: Vec) -> float:
    """Radius des Kreises durch drei Punkte (inf bei kollinearen Punkten)."""
    ab, bc, ca = _norm(_sub(b, a)), _norm(_sub(c, b)), _norm(_sub(a, c))
    cr = _norm(_cross(_sub(b, a), _sub(c, a)))
    if ab == 0 or bc == 0 or ca == 0:
        return math.inf
    # relative Toleranz: praktisch gerade -> inf
    if cr <= 1e-12 * ab * ca:
        return math.inf
    return ab * bc * ca / (2.0 * cr)


# ----------------------------------------------------------------------------
# Radius-Analyse
# ----------------------------------------------------------------------------
def is_closed(points: Sequence[Vec], length: float) -> bool:
    return len(points) > 3 and _norm(_sub(points[0], points[-1])) < max(1e-6, 1e-6 * length)


def radii(points: Sequence[Vec], closed: bool = False, step: int = 1) -> list[float]:
    """Lokaler Kruemmungsradius an jedem Messpunkt."""
    n = len(points)
    if n < 3:
        return [math.inf] * n
    out = [math.inf] * n
    if closed:
        m = n - 1  # letzter Punkt == erster Punkt
        for i in range(m):
            out[i] = circumradius(points[(i - step) % m], points[i], points[(i + step) % m])
        out[-1] = out[0]
        return out
    for i in range(step, n - step):
        out[i] = circumradius(points[i - step], points[i], points[i + step])
    for i in range(step):
        out[i] = out[step]
        out[n - 1 - i] = out[n - 1 - step]
    return out


def median_filter(values: Sequence[float], window: int) -> list[float]:
    if window <= 1 or len(values) < 3:
        return list(values)
    half = window // 2
    n = len(values)
    out = []
    for i in range(n):
        w = sorted(values[max(0, i - half): min(n, i + half + 1)])
        out.append(w[len(w) // 2])
    return out


def classify(r: float, rules: Sequence[Rule]) -> int:
    """Regelindex fuer einen Radius. Luecken -> naechstgelegene Regel (bei Gleichstand die feinere).
    Groesser als alle Regeln oder gerade -> STRAIGHT."""
    if math.isinf(r) or math.isnan(r):
        return STRAIGHT
    best, best_d = STRAIGHT, math.inf
    top = max(rule.r_max for rule in rules)
    if r >= top:
        return STRAIGHT
    for i, rule in enumerate(rules):
        if rule.contains(r):
            return i
        d = rule.r_min - r if r < rule.r_min else r - rule.r_max
        if d < best_d - 1e-12 or (abs(d - best_d) <= 1e-12 and rule.abstand < rules[best].abstand):
            best, best_d = i, d
    return best


# ----------------------------------------------------------------------------
# Abschnitte
# ----------------------------------------------------------------------------
@dataclass
class Segment:
    index: int
    s_start: float
    s_end: float
    rule: int            # Index in Settings.sorted_rules() oder STRAIGHT
    abstand: float
    r_min: float
    r_max: float
    punkte: list[float] = field(default_factory=list)   # Bogenlaengen der Punkte

    @property
    def laenge(self) -> float:
        return self.s_end - self.s_start

    def rule_label(self, rules: Sequence[Rule]) -> str:
        return "Gerade/sonst" if self.rule == STRAIGHT else rules[self.rule].label()


@dataclass
class CurveAnalysis:
    name: str
    laenge: float
    closed: bool
    s: list[float]
    radius: list[float]          # geglaettet
    segments: list[Segment]

    @property
    def punkte(self) -> list[tuple[float, int]]:
        """Alle Punkte als (Bogenlaenge, Abschnittsindex)."""
        return [(p, seg.index) for seg in self.segments for p in seg.punkte]


def _runs(classes: Sequence[int], s: Sequence[float], length: float) -> list[list]:
    """Zusammenhaengende Bereiche gleicher Klasse -> [klasse, s_start, s_end, i0, i1]."""
    runs: list[list] = []
    start_i = 0
    for i in range(1, len(classes) + 1):
        if i == len(classes) or classes[i] != classes[start_i]:
            s0 = 0.0 if start_i == 0 else 0.5 * (s[start_i - 1] + s[start_i])
            s1 = length if i == len(classes) else 0.5 * (s[i - 1] + s[i])
            runs.append([classes[start_i], s0, s1, start_i, i - 1])
            start_i = i
    return runs


def _merge_short(runs: list[list], min_len: float) -> list[list]:
    """Verschmilzt zu kurze Abschnitte mit dem laengeren Nachbarn."""
    runs = [r[:] for r in runs]
    while len(runs) > 1:
        idx = min(range(len(runs)), key=lambda k: runs[k][2] - runs[k][1])
        if runs[idx][2] - runs[idx][1] >= min_len:
            break
        if idx == 0:
            j = 1
        elif idx == len(runs) - 1:
            j = idx - 1
        else:
            left, right = runs[idx - 1], runs[idx + 1]
            j = idx - 1 if (left[2] - left[1]) >= (right[2] - right[1]) else idx + 1
        a, b = sorted((idx, j))
        merged = [runs[j][0], runs[a][1], runs[b][2], runs[a][3], runs[b][4]]
        runs[a:b + 1] = [merged]
        # gleiche Nachbarklassen zusammenfassen
        k = 0
        while k < len(runs) - 1:
            if runs[k][0] == runs[k + 1][0]:
                runs[k:k + 2] = [[runs[k][0], runs[k][1], runs[k + 1][2], runs[k][3], runs[k + 1][4]]]
            else:
                k += 1
    return runs


def place_points(s0: float, s1: float, abstand: float, verteilung: str, endpunkt: bool) -> list[float]:
    """Punktpositionen in [s0, s1] inkl. Start- und (je nach Modus) Endpunkt."""
    L = s1 - s0
    if L <= 1e-9:
        return [s0]
    if verteilung == "gleichmaessig":
        n = max(1, math.ceil(L / abstand - 1e-9))   # Abstand nie groesser als gefordert
        return [s0 + L * j / n for j in range(n + 1)]
    pts = []
    j = 0
    while s0 + j * abstand <= s1 + 1e-9:
        pts.append(min(s0 + j * abstand, s1))
        j += 1
    if endpunkt and s1 - pts[-1] > 1e-6:
        pts.append(s1)
    return pts


def analyze(name: str, points: Sequence[Vec], length: float, cfg: Settings) -> CurveAnalysis:
    """Komplette Analyse: Radius -> Klassen -> Abschnitte -> Punkte."""
    rules = cfg.sorted_rules()
    n = len(points) - 1
    if n < 1:
        raise ValueError("Mindestens zwei Messpunkte erforderlich.")
    s = [length * i / n for i in range(n + 1)]
    closed = is_closed(points, length)
    rad = median_filter(radii(points, closed), cfg.glaettung)
    classes = [classify(r, rules) for r in rad]

    runs = _merge_short(_runs(classes, s, length), cfg.min_abschnittslaenge)

    segments: list[Segment] = []
    for k, (cls, s0, s1, i0, i1) in enumerate(runs):
        # Messpunkte direkt an inneren Grenzen (Uebergangszone) nicht in die Statistik
        trim = cfg.glaettung // 2 + 1
        a = i0 + (trim if k > 0 else 0)
        b = i1 - (trim if k < len(runs) - 1 else 0)
        rs = rad[a:b + 1] if b >= a else rad[i0:i1 + 1]
        finite = [r for r in rs if not math.isinf(r)]
        abstand = cfg.abstand_sonst if cls == STRAIGHT else rules[cls].abstand
        segments.append(Segment(k, s0, s1, cls, abstand,
                                min(finite) if finite else math.inf,
                                max(finite) if finite else math.inf))

    # Punkte setzen, doppelte Punkte an Abschnittsgrenzen vermeiden
    last = None
    for seg in segments:
        pts = place_points(seg.s_start, seg.s_end, seg.abstand, cfg.verteilung, cfg.endpunkt_erzwingen)
        if last is not None and pts and abs(pts[0] - last) < 1e-6:
            pts = pts[1:]
        seg.punkte = pts
        if pts:
            last = pts[-1]
    if closed and segments and segments[-1].punkte and abs(segments[-1].punkte[-1] - length) < 1e-6:
        segments[-1].punkte.pop()   # Endpunkt == Startpunkt

    return CurveAnalysis(name, length, closed, s, rad, segments)
