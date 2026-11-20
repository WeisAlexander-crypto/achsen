# Achsen – Achsensysteme auf Kurven für CATIA V5

Python-Anwendung (GUI), die in CATIA V5 Kurven nach Krümmungsradius zerlegt, darauf
regelbasiert Punkte setzt und auf jedem Punkt ein Achsensystem erzeugt.

## Ablauf

| Schritt | Was passiert | In CATIA erzeugt |
|---|---|---|
| 1. Einlesen | Kurven markieren **oder** ein Geometrisches Set wählen (Unter-Sets werden mit durchsucht) | – |
| 2. Analyse | Kurve wird abgetastet (Standard alle 0,5 mm), Krümmungsradius aus je 3 Nachbarpunkten, Median-Glättung | – (temporär, wird gelöscht) |
| 3. Zerlegen | Wechselt der Radius in einen anderen Regelbereich → Schnitt. Zu kurze Abschnitte werden mit dem Nachbarn verschmolzen | `Abschnitte` (Split) |
| 4. Punkte | Je Abschnitt Punktabstand laut Regel (z. B. R 10–15 → alle 1 mm) | `Punkte` |
| 5. Normale | Linie 90° zur gewählten Fläche durch jeden Punkt | `Normalen` |
| 6. Achsenkreuz | Ursprung = Punkt, **Z** = Normale, **X** = Tangente am Kurventeilstück | Achsensysteme `AX_<Kurve>_<Nr>` |
| 7. Umkehren | X und/oder Z beim Erzeugen oder nachträglich umkehren | – |

Struktur im Baum:

```
Achsen_Ergebnis
 └─ 01_<Kurvenname>
     ├─ Abschnitte        Abschnitt_01, Abschnitt_02, …
     ├─ Punkte            P_AX_01_0001, …
     ├─ Normalen          N_AX_01_0001, …
     └─ Hilfsgeometrie    Tangenten, Tangentialebenen, Projektionen (ausgeblendet)
Achsensysteme             AX_01_0001, AX_01_0002, …
```

## Installation

Voraussetzungen: Windows, CATIA V5 (läuft, ein CATPart ist aktiv), Python 3.10+ (mit tkinter, Standard bei python.org).

```bat
pip install -r requirements.txt
start.bat            (oder: python -m catia_achsen)
```

## Bedienung

1. In CATIA Kurven oder ein Geoset markieren → **„Kurven / Geoset aus Auswahl"**
   (oder **„Kurven in CATIA wählen…"** für interaktive Mehrfachauswahl, mit dem Haken bestätigen).
2. Referenzfläche markieren → **„Fläche aus Auswahl"** (oder interaktiv wählen).
3. Regeln/Optionen prüfen → **„Analysieren (Vorschau)"** zeigt alle Abschnitte mit Radius von–bis und Punktanzahl.
4. **„Erzeugen in CATIA"**.
5. Bei Bedarf **„X / Z / XZ umkehren"** – wirkt auf alle Achsensysteme mit dem Präfix oder nur auf die in CATIA ausgewählten.

## Regeln anpassen

In der Oberfläche (Neu / Bearbeiten / Löschen, Doppelklick) oder direkt in
[`config/regeln_standard.json`](config/regeln_standard.json) – diese Datei wird beim Start geladen.
Eigene Regelsätze lassen sich speichern und laden.

```json
"regeln": [
  { "r_min": 10, "r_max": 15, "abstand": 1 },
  { "r_min": 15, "r_max": 30, "abstand": 3 }
],
"abstand_sonst": 20
```

* Bereiche gelten als **von ≤ R < bis**. `"r_max": "inf"` = unendlich.
* Radien **größer als alle Regeln** sowie Geraden → `abstand_sonst`.
* **Lücken** (z. B. 15–16 bei „10–15“ und „16–30“) sind erlaubt: Es gilt die nächstgelegene Regel,
  bei Gleichstand die feinere. Die Anwendung weist im Protokoll darauf hin.

| Option | Bedeutung |
|---|---|
| `abtastabstand` | Messpunktabstand für die Radiusanalyse (kleiner = genauer, langsamer) |
| `glaettung` | Median-Fenster gegen Ausreißer an Übergängen (1 = aus) |
| `min_abschnittslaenge` | Abschnitte darunter werden mit dem längeren Nachbarn verschmolzen |
| `verteilung` | `gleichmaessig`: Abstand ≤ Regelwert, Punkte auf Start **und** Ende des Abschnitts · `exakt`: genau Regelwert ab Abschnittsstart |
| `abschnitte_erzeugen` | Kurventeilstücke per Split erzeugen; X referenziert dann das Teilstück |
| `x_orthogonalisieren` | Tangente auf die Tangentialebene der Fläche projizieren → X exakt ⟂ Z, auch wenn die Kurve nicht exakt auf der Fläche liegt |
| `x_umkehren`, `z_umkehren` | Achsrichtung beim Erzeugen umkehren |
| `linienlaenge` | Länge der Normalen- und Tangentenlinien |

## Hinweise

* Die Kurve sollte auf oder nahe der Fläche liegen; die Normale wird im Lotfußpunkt berechnet.
* Bei geschlossenen Kurven kann der Split scheitern – dann referenziert X automatisch die Originalkurve (Meldung im Protokoll).
* Alle Elemente sind parametrisch verknüpft: Ändert sich die Fläche, aktualisieren sich Normalen und Achsensysteme.
  Ändert sich die Kurve selbst, sollte neu analysiert werden, da sich die Abschnittsgrenzen verschieben.

## Entwicklung

Die Geometrie-Logik (`catia_achsen/geometry.py`, `config.py`) ist ohne CATIA testbar:

```bash
pip install pytest
python -m pytest
```

| Datei | Inhalt |
|---|---|
| `catia_achsen/geometry.py` | Radius, Klassifizierung, Abschnitte, Punktverteilung (rein Python) |
| `catia_achsen/config.py` | Regeln & Einstellungen, JSON laden/speichern, Validierung |
| `catia_achsen/catia_com.py` | CATIA-V5-COM: Auswahl, Abtastung, Split, Punkte, Linien, Achsensysteme, Umkehren |
| `catia_achsen/gui.py` | Oberfläche (tkinter) |
