import math

import pytest

from catia_achsen.config import Rule, Settings
from catia_achsen.geometry import (STRAIGHT, analyze, circumradius, classify, median_filter,
                                   place_points)


def composite_curve(ds=0.25):
    """Gerade 20 mm -> Bogen R12 (90 Grad) -> Bogen R25 (90 Grad, Gegenrichtung) -> Gerade 20 mm.
    Liefert Punkte gleichmaessig ueber die Bogenlaenge und die Gesamtlaenge."""
    parts = [("line", 20.0, None), ("arc", 12.0 * math.pi / 2, (12.0, 1)),
             ("arc", 25.0 * math.pi / 2, (25.0, -1)), ("line", 20.0, None)]
    total = sum(p[1] for p in parts)

    def at(s):
        x, y, h = 0.0, 0.0, 0.0  # Position und Richtungswinkel
        for kind, L, arc in parts:
            t = min(s, L)
            if kind == "line":
                x += t * math.cos(h); y += t * math.sin(h)
            else:
                R, sign = arc
                cx, cy = x - sign * R * math.sin(h), y + sign * R * math.cos(h)
                h2 = h + sign * t / R
                x, y = cx + sign * R * math.sin(h2), cy - sign * R * math.cos(h2)
                if t == L:
                    h = h2
            if s <= L:
                return (x, y, 0.0)
            s -= L
        return (x, y, 0.0)

    n = math.ceil(total / ds)
    return [at(total * i / n) for i in range(n + 1)], total


def test_circumradius():
    R = 7.0
    pts = [(R * math.cos(a), R * math.sin(a), 0.0) for a in (0.0, 0.3, 0.7)]
    assert circumradius(*pts) == pytest.approx(R)
    assert math.isinf(circumradius((0, 0, 0), (1, 0, 0), (2, 0, 0)))


def test_classify_with_gap_and_straight():
    rules = [Rule(10, 15, 1), Rule(16, 30, 3)]
    assert classify(12, rules) == 0
    assert classify(20, rules) == 1
    assert classify(15.3, rules) == 0          # naeher an Regel 1
    assert classify(15.7, rules) == 1          # naeher an Regel 2
    assert classify(15.5, rules) == 0          # Gleichstand -> feinere Regel
    assert classify(5, rules) == 0             # unterhalb -> erste Regel
    assert classify(31, rules) == STRAIGHT
    assert classify(math.inf, rules) == STRAIGHT


def test_median_filter_removes_spike():
    assert median_filter([1, 1, 100, 1, 1], 3) == [1, 1, 1, 1, 1]


def test_place_points_even_never_exceeds_spacing():
    pts = place_points(0, 10, 3, "gleichmaessig", True)
    assert pts[0] == 0 and pts[-1] == pytest.approx(10)
    assert len(pts) == 5                       # 4 Intervalle a 2.5 mm
    assert max(b - a for a, b in zip(pts, pts[1:])) <= 3 + 1e-9


def test_place_points_exact():
    assert place_points(0, 10, 3, "exakt", True) == pytest.approx([0, 3, 6, 9, 10])
    assert place_points(0, 10, 3, "exakt", False) == pytest.approx([0, 3, 6, 9])


def test_analyze_composite_curve():
    pts, total = composite_curve()
    cfg = Settings(regeln=[Rule(10, 15, 1), Rule(15, 30, 3)], abstand_sonst=10,
                   min_abschnittslaenge=2.0, glaettung=5)
    res = analyze("K1", pts, total, cfg)

    assert [s.rule for s in res.segments] == [STRAIGHT, 0, 1, STRAIGHT]
    b = [s.s_start for s in res.segments[1:]]
    expected = [20.0, 20.0 + 6 * math.pi, 20.0 + 6 * math.pi + 12.5 * math.pi]
    for got, exp in zip(b, expected):
        assert got == pytest.approx(exp, abs=1.0)

    arc12 = res.segments[1]
    assert arc12.r_min == pytest.approx(12, rel=0.02) and arc12.r_max == pytest.approx(12, rel=0.02)
    for seg in res.segments:
        gaps = [q - p for p, q in zip(seg.punkte, seg.punkte[1:])]
        assert all(g <= seg.abstand + 1e-9 for g in gaps)

    allp = [p for p, _ in res.punkte]
    assert allp == sorted(allp)
    assert len(allp) == len(set(round(p, 6) for p in allp))   # keine Duplikate an Grenzen
    assert allp[0] == 0 and allp[-1] == pytest.approx(total)


def test_closed_circle_single_segment():
    R, n = 20.0, 400
    pts = [(R * math.cos(2 * math.pi * i / n), R * math.sin(2 * math.pi * i / n), 0.0)
           for i in range(n + 1)]
    L = 2 * math.pi * R
    res = analyze("Kreis", pts, L, Settings(regeln=[Rule(15, 30, 3)]))
    assert res.closed and len(res.segments) == 1 and res.segments[0].rule == 0
    assert res.punkte[-1][0] < L - 1e-6        # kein doppelter Start/Endpunkt


def test_short_segment_is_merged():
    pts, total = composite_curve()
    cfg = Settings(regeln=[Rule(10, 15, 1), Rule(15, 30, 3)], abstand_sonst=10,
                   min_abschnittslaenge=25.0)  # R12-Bogen (~18.8 mm) ist zu kurz
    res = analyze("K1", pts, total, cfg)
    assert 0 not in [s.rule for s in res.segments]
