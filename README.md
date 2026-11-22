# Achsen – Achsensysteme auf Kurven für CATIA V5 (VB.NET)

Windows-Anwendung (VB.NET, WinForms), die in CATIA V5 Kurven nach Krümmungsradius zerlegt,
darauf regelbasiert Punkte setzt und auf jedem Punkt ein Achsensystem erzeugt.

## Ablauf

| Schritt | Was passiert | In CATIA erzeugt |
|---|---|---|
| 1. Einlesen | Kurven markieren **oder** ein Geometrisches Set wählen (Unter-Sets werden mit durchsucht) | – |
| 2. Analyse | Kurve wird abgetastet (Standard alle 0,5 mm): alle Messpunkte in ein temporäres Geoset, **ein** Update, Koordinaten lesen, Geoset mit **einem** Löschvorgang entfernen. Krümmungsradius aus je 3 Nachbarpunkten, Median-Glättung | – (temporär, wird gelöscht) |
| 3. Zerlegen | Wechselt der Radius in einen anderen Regelbereich → Schnitt. Zu kurze Abschnitte werden mit dem Nachbarn verschmolzen | `Abschnitte` (Split) |
| 4. Punkte | Je Abschnitt Punktabstand laut Regel (z. B. R 10–15 → alle 1 mm) | `Punkte` |
| 5. Normale | Linie 90° zur gewählten Fläche durch jeden Punkt | `Normalen` |
| 6. Achsenkreuz | Ursprung = Punkt, **Z** = Normale, **X** = Tangente am Kurventeilstück | Achsensysteme `AX_<Kurve>_<Nr>` |
| 7. Umkehren | X und/oder Z beim Erzeugen oder nachträglich umkehren | – |

Struktur im Baum:

```
Achsen_Ergebnis
 └─ 01_<Kurvenname>
     ├─ Abschnitte        Abschnitt_01_Gerade, Abschnitt_02_R10-15, …
     ├─ Punkte            P_AX_01_0001, …
     ├─ Normalen          N_AX_01_0001, …
     └─ Hilfsgeometrie    Tangenten, Tangentialebenen, Projektionen (ausgeblendet)
Achsensysteme             AX_01_0001, AX_01_0002, …
```

## Bauen und Starten

Voraussetzungen: Windows, CATIA V5 (läuft, ein CATPart ist aktiv), .NET Framework 4.8 (auf CATIA-Arbeitsplätzen
üblicherweise vorhanden).

**Bauen** – eine der beiden Varianten:

* `build.bat` doppelklicken (benötigt das [.NET SDK 8](https://dotnet.microsoft.com/download)) → Ergebnis in `dist\`
* `Achsen.sln` in Visual Studio 2022 öffnen → Startprojekt `Achsen.App` → F5

**Starten:** `dist\Achsen.exe`. Der Ordner `dist` kann komplett auf andere Rechner kopiert werden.

CATIA wird per COM **Late Binding** angesprochen – es werden keine CATIA-Interop-DLLs benötigt, die Anwendung
läuft daher mit jedem V5-Release.

## Bedienung

1. In CATIA Kurven oder ein Geoset markieren → **„Kurven / Geoset aus Auswahl"**
   (oder **„Kurven in CATIA wählen…"** für interaktive Mehrfachauswahl, mit dem Haken bestätigen).
2. Referenzfläche markieren → **„Fläche aus Auswahl"** (oder interaktiv wählen).
3. Regeln/Optionen prüfen → **„Analysieren (Vorschau)"** zeigt alle Abschnitte mit Radius von–bis und Punktanzahl.
4. **„Erzeugen in CATIA"**.
5. Bei Bedarf **„X / Z / X und Z umkehren"** – wirkt auf alle Achsensysteme mit dem Präfix oder nur auf die in
   CATIA ausgewählten.

## Regeln anpassen

Direkt in der Tabelle der Oberfläche (Zeilen hinzufügen/ändern, löschen mit Entf) oder in
[`config/regeln_standard.json`](config/regeln_standard.json) – diese Datei wird beim Start geladen
(im Programmordner unter `config\`). Eigene Regelsätze lassen sich speichern und laden.

```json
"regeln": [
  { "r_min": 10, "r_max": 15, "abstand": 1 },
  { "r_min": 15, "r_max": 30, "abstand": 3 }
],
"abstand_sonst": 20
```

* Bereiche gelten als **von ≤ R < bis**. In der Tabelle `inf` = unendlich (in JSON `"Infinity"`).
* Radien **größer als alle Regeln** sowie Geraden → `abstand_sonst`.
* **Lücken** (z. B. 15–16 bei „10–15“ und „16–30“) sind erlaubt: Es gilt die nächstgelegene Regel,
  bei Gleichstand die feinere. Die Anwendung weist im Protokoll darauf hin.

Optionen (rechts im Eigenschaftenfenster, mit Erklärung):

| Option | Bedeutung |
|---|---|
| Abtastabstand | Messpunktabstand für die Radiusanalyse (kleiner = genauer, langsamer) |
| Glättung | Median-Fenster gegen Ausreißer an Übergängen (1 = aus) |
| Min. Abschnittslänge | Abschnitte darunter werden mit dem längeren Nachbarn verschmolzen |
| Grenzraster | Abschnittsgrenzen auf Vielfache dieses Werts ab Kurvenanfang runden. `1` = Schnitte nur auf ganzen mm, `0` = aus (Standard) |
| Verteilung | `Gleichmaessig`: Abstand ≤ Regelwert, Punkte auf Start **und** Ende des Abschnitts · `Exakt`: genau Regelwert ab Abschnittsstart |
| Kurventeilstücke erzeugen | Kurventeilstücke per Split erzeugen; X referenziert dann das Teilstück |
| X exakt senkrecht zu Z | Tangente auf die Tangentialebene der Fläche projizieren → X exakt ⟂ Z, auch wenn die Kurve nicht exakt auf der Fläche liegt |
| X / Z umkehren | Achsrichtung beim Erzeugen umkehren |
| Linienlänge | Länge der Normalen- und Tangentenlinien |

## Hinweise

* Die Kurve sollte auf oder nahe der Fläche liegen; die Normale wird im Lotfußpunkt berechnet.
* Nach dem Zerlegen wird geprüft, ob die Summe der Teilstücke die Kurvenlänge ergibt (Meldung im Protokoll).
* Bei geschlossenen Kurven kann der Split scheitern – dann referenziert X automatisch die Originalkurve (Meldung im Protokoll).
* Alle Elemente sind parametrisch verknüpft: Ändert sich die Fläche, aktualisieren sich Normalen und Achsensysteme.
  Ändert sich die Kurve selbst, sollte neu analysiert werden, da sich die Abschnittsgrenzen verschieben.
* Sollten „gleiche Richtung“/„Gegenrichtung“ in Ihrer CATIA-Version vertauscht wirken, die Konstanten
  `CAT_AXIS_SAME` / `CAT_AXIS_OPPOSITE` in `CatiaSession.vb` tauschen.

## Projektstruktur

| Datei | Inhalt |
|---|---|
| `src/Achsen.Core/Geometry.vb` | Radius, Klassifizierung, Abschnitte, Punktverteilung (ohne CATIA, testbar) |
| `src/Achsen.Core/Settings.vb` | Regeln & Einstellungen, JSON laden/speichern, Validierung |
| `src/Achsen.App/CatiaSession.vb` | CATIA-V5-COM: Auswahl, Abtastung, Split, Punkte, Linien, Achsensysteme, Umkehren |
| `src/Achsen.App/MainForm.vb` | Oberfläche (WinForms, komplett im Code aufgebaut) |
| `tests/Achsen.Tests` | xUnit-Tests der Geometrie- und Regel-Logik |

Tests ausführen: `dotnet test`
