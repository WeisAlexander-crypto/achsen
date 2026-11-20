import math

from catia_achsen.config import Rule, Settings


def test_roundtrip(tmp_path):
    s = Settings(regeln=[Rule(0, 10, 0.5), Rule(10, math.inf, 4)], x_umkehren=True, praefix="P")
    f = tmp_path / "r.json"
    s.save(f)
    t = Settings.load(f)
    assert t.regeln[1].r_max == math.inf and t.x_umkehren and t.praefix == "P"


def test_validation():
    assert Settings().validate() == []
    bad = Settings(regeln=[Rule(0, 10, 1), Rule(5, 20, 2), Rule(30, 20, 0)])
    errs = bad.validate()
    assert any("ueberschneiden" in e for e in errs)
    assert any("Regel 3" in e for e in errs)


def test_gaps():
    s = Settings(regeln=[Rule(10, 15, 1), Rule(16, 30, 3)])
    assert s.gaps() == [(15, 16)]


def test_shipped_default_config_is_valid():
    from pathlib import Path
    s = Settings.load(Path(__file__).parent.parent / "config" / "regeln_standard.json")
    assert s.validate() == []
