Imports System.ComponentModel
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Serialization

''' <summary>Punktregel: Radius im Bereich [RMin, RMax) -> alle Abstand mm ein Punkt.</summary>
Public Class Rule
    <JsonPropertyName("r_min")> Public Property RMin As Double
    <JsonPropertyName("r_max")> Public Property RMax As Double
    <JsonPropertyName("abstand")> Public Property Abstand As Double

    Public Sub New()
    End Sub

    Public Sub New(rMin As Double, rMax As Double, abstand As Double)
        Me.RMin = rMin : Me.RMax = rMax : Me.Abstand = abstand
    End Sub

    Public Function Contains(r As Double) As Boolean
        Return RMin <= r AndAlso r < RMax
    End Function

    Public Function Label() As String
        Return $"R {Fmt(RMin)}-{Fmt(RMax)} mm / {Fmt(Abstand)} mm"
    End Function

    Friend Shared Function Fmt(v As Double) As String
        Return If(Double.IsInfinity(v), "inf", v.ToString("0.###", Globalization.CultureInfo.InvariantCulture))
    End Function
End Class

Public Enum Verteilung
    ''' <summary>Abstand nie groesser als Regelwert, Punkte auf Start und Ende des Abschnitts.</summary>
    Gleichmaessig
    ''' <summary>Genau der Regelwert ab Abschnittsstart.</summary>
    Exakt
End Enum

''' <summary>Alle Einstellungen. Die Attribute steuern die Anzeige im PropertyGrid der Oberflaeche.</summary>
Public Class Settings

    <Browsable(False)>
    <JsonPropertyName("regeln")>
    Public Property Regeln As List(Of Rule) = DefaultRules()

    <Category("1 Regeln"), DisplayName("Abstand Gerade / sonst [mm]"),
     Description("Punktabstand fuer Geraden und Radien groesser als alle Regeln.")>
    <JsonPropertyName("abstand_sonst")>
    Public Property AbstandSonst As Double = 20.0

    <Category("2 Analyse"), DisplayName("Abtastabstand [mm]"),
     Description("Abstand der Messpunkte fuer die Radiusanalyse. Kleiner = genauer, langsamer.")>
    <JsonPropertyName("abtastabstand")>
    Public Property Abtastabstand As Double = 0.5

    <Category("2 Analyse"), DisplayName("Max. Abtastpunkte"),
     Description("Obergrenze der Messpunkte pro Kurve.")>
    <JsonPropertyName("max_abtastpunkte")>
    Public Property MaxAbtastpunkte As Integer = 4000

    <Category("2 Analyse"), DisplayName("Glaettung (Fenster)"),
     Description("Median-Filter gegen Ausreisser an Uebergaengen. 1 = aus.")>
    <JsonPropertyName("glaettung")>
    Public Property Glaettung As Integer = 5

    <Category("2 Analyse"), DisplayName("Min. Abschnittslaenge [mm]"),
     Description("Kuerzere Abschnitte werden mit dem laengeren Nachbarn verschmolzen.")>
    <JsonPropertyName("min_abschnittslaenge")>
    Public Property MinAbschnittslaenge As Double = 2.0

    <Category("2 Analyse"), DisplayName("Grenzraster [mm] (0 = aus)"),
     Description("Abschnittsgrenzen auf Vielfache dieses Werts ab Kurvenanfang runden. 1 = Grenzen nur auf ganzen mm.")>
    <JsonPropertyName("grenzraster")>
    Public Property Grenzraster As Double = 0.0

    <Category("3 Punkte"), DisplayName("Verteilung"),
     Description("Gleichmaessig: Abstand <= Regelwert, Start- und Endpunkt je Abschnitt. Exakt: genau Regelwert ab Start.")>
    <JsonPropertyName("verteilung")>
    <JsonConverter(GetType(JsonStringEnumConverter))>
    Public Property Verteilung As Verteilung = Verteilung.Gleichmaessig

    <Category("3 Punkte"), DisplayName("Endpunkt erzwingen"),
     Description("Nur bei 'Exakt': Abschnittsende bekommt immer einen Punkt.")>
    <JsonPropertyName("endpunkt_erzwingen")>
    Public Property EndpunktErzwingen As Boolean = True

    <Category("4 Geometrie"), DisplayName("Kurventeilstuecke erzeugen"),
     Description("Kurve per Split in Abschnitte zerlegen. X referenziert dann das Teilstueck.")>
    <JsonPropertyName("abschnitte_erzeugen")>
    Public Property AbschnitteErzeugen As Boolean = True

    <Category("4 Geometrie"), DisplayName("Linienlaenge [mm]"),
     Description("Laenge der Normalen- und Tangentenlinien.")>
    <JsonPropertyName("linienlaenge")>
    Public Property Linienlaenge As Double = 10.0

    <Category("4 Geometrie"), DisplayName("X exakt senkrecht zu Z"),
     Description("Tangente auf die Tangentialebene der Flaeche projizieren.")>
    <JsonPropertyName("x_orthogonalisieren")>
    Public Property XOrthogonalisieren As Boolean = True

    <Category("4 Geometrie"), DisplayName("Hilfsgeometrie ausblenden")>
    <JsonPropertyName("hilfsgeometrie_ausblenden")>
    Public Property HilfsgeometrieAusblenden As Boolean = True

    <Category("5 Achsen"), DisplayName("X-Achse umkehren")>
    <JsonPropertyName("x_umkehren")>
    Public Property XUmkehren As Boolean = False

    <Category("5 Achsen"), DisplayName("Z-Achse umkehren")>
    <JsonPropertyName("z_umkehren")>
    Public Property ZUmkehren As Boolean = False

    <Category("6 Benennung"), DisplayName("Ergebnis-Geoset")>
    <JsonPropertyName("ergebnis_set")>
    Public Property ErgebnisSet As String = "Achsen_Ergebnis"

    <Category("6 Benennung"), DisplayName("Praefix Achsensysteme")>
    <JsonPropertyName("praefix")>
    Public Property Praefix As String = "AX"

    ' ------------------------------------------------------------------
    Public Shared Function DefaultRules() As List(Of Rule)
        ' Beispielwerte - in der Oberflaeche oder in der JSON-Datei anpassbar.
        Return New List(Of Rule) From {
            New Rule(0, 10, 0.5),
            New Rule(10, 15, 1),
            New Rule(15, 30, 3),
            New Rule(30, 100, 5),
            New Rule(100, 500, 10)
        }
    End Function

    Public Function SortedRules() As List(Of Rule)
        Return Regeln.OrderBy(Function(r) r.RMin).ToList()
    End Function

    ''' <summary>Fehlerliste (leer = gueltig). Luecken sind erlaubt.</summary>
    Public Function Validate() As List(Of String)
        Dim errs As New List(Of String)
        If Regeln Is Nothing OrElse Regeln.Count = 0 Then errs.Add("Mindestens eine Regel ist erforderlich.") : Return errs
        For i = 0 To Regeln.Count - 1
            Dim r = Regeln(i)
            If r.RMin < 0 OrElse r.RMax <= r.RMin Then errs.Add($"Regel {i + 1}: R von muss >= 0 und kleiner als R bis sein.")
            If r.Abstand <= 0 Then errs.Add($"Regel {i + 1}: Abstand muss > 0 sein.")
        Next
        Dim rs = SortedRules()
        For i = 0 To rs.Count - 2
            If rs(i + 1).RMin < rs(i).RMax - 0.000000000001 Then
                errs.Add($"Regeln ueberschneiden sich: {rs(i).Label()} und {rs(i + 1).Label()}.")
            End If
        Next
        If AbstandSonst <= 0 Then errs.Add("Abstand fuer Geraden muss > 0 sein.")
        If Abtastabstand <= 0 Then errs.Add("Abtastabstand muss > 0 sein.")
        If MaxAbtastpunkte < 20 Then errs.Add("Max. Abtastpunkte muss >= 20 sein.")
        If Glaettung < 1 Then errs.Add("Glaettung muss >= 1 sein.")
        If MinAbschnittslaenge < 0 Then errs.Add("Min. Abschnittslaenge muss >= 0 sein.")
        If Grenzraster < 0 Then errs.Add("Grenzraster muss >= 0 sein (0 = aus).")
        If Linienlaenge <= 0 Then errs.Add("Linienlaenge muss > 0 sein.")
        If String.IsNullOrWhiteSpace(Praefix) Then errs.Add("Praefix darf nicht leer sein.")
        If String.IsNullOrWhiteSpace(ErgebnisSet) Then errs.Add("Ergebnis-Geoset darf nicht leer sein.")
        Return errs
    End Function

    ''' <summary>Radiusbereiche, die von keiner Regel abgedeckt sind.</summary>
    Public Function Gaps() As List(Of Tuple(Of Double, Double))
        Dim rs = SortedRules()
        Dim out As New List(Of Tuple(Of Double, Double))
        For i = 0 To rs.Count - 2
            If rs(i + 1).RMin > rs(i).RMax + 0.000000000001 Then out.Add(Tuple.Create(rs(i).RMax, rs(i + 1).RMin))
        Next
        Return out
    End Function

    ' ------------------------------------------------------------------
    Private Shared ReadOnly JsonOpts As New JsonSerializerOptions With {
        .WriteIndented = True,
        .NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals Or JsonNumberHandling.AllowReadingFromString,
        .Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }

    Public Function ToJson() As String
        Return JsonSerializer.Serialize(Me, JsonOpts)
    End Function

    Public Shared Function FromJson(json As String) As Settings
        Dim s = JsonSerializer.Deserialize(Of Settings)(json, JsonOpts)
        If s.Regeln Is Nothing Then s.Regeln = DefaultRules()
        Return s
    End Function

    Public Sub Save(path As String)
        File.WriteAllText(path, ToJson(), New UTF8Encoding(False))
    End Sub

    Public Shared Function Load(path As String) As Settings
        Return FromJson(File.ReadAllText(path, Encoding.UTF8))
    End Function

    ''' <summary>Parst Radiuswerte inkl. "inf"/"unendlich"; Komma oder Punkt als Dezimaltrenner.</summary>
    Public Shared Function ParseRadius(text As String) As Double
        Dim t = If(text, "").Trim().ToLowerInvariant()
        If t = "inf" OrElse t = "unendlich" OrElse t = "infinity" OrElse t = "∞" Then Return Double.PositiveInfinity
        Return Double.Parse(t.Replace(","c, "."c), Globalization.CultureInfo.InvariantCulture)
    End Function
End Class
