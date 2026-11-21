' Reine Geometrie-Logik ohne CATIA: Kruemmungsradius, Abschnitte, Punktverteilung.
' Eingabe sind Messpunkte, gleichmaessig ueber die Bogenlaenge verteilt (Punkt i bei s = L * i / n).

Public Structure Vec3
    Public X As Double, Y As Double, Z As Double

    Public Sub New(x As Double, y As Double, z As Double)
        Me.X = x : Me.Y = y : Me.Z = z
    End Sub

    Public Shared Operator -(a As Vec3, b As Vec3) As Vec3
        Return New Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z)
    End Operator

    Public Function Length() As Double
        Return Math.Sqrt(X * X + Y * Y + Z * Z)
    End Function

    Public Shared Function Cross(a As Vec3, b As Vec3) As Vec3
        Return New Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X)
    End Function
End Structure

Public Class Segment
    Public Property Index As Integer
    Public Property SStart As Double
    Public Property SEnd As Double
    ''' <summary>Index in Settings.SortedRules() oder Geometry.STRAIGHT.</summary>
    Public Property RuleIndex As Integer
    Public Property Abstand As Double
    Public Property RMin As Double
    Public Property RMax As Double
    ''' <summary>Bogenlaengen der Punkte in diesem Abschnitt.</summary>
    Public Property Punkte As New List(Of Double)

    Public ReadOnly Property Laenge As Double
        Get
            Return SEnd - SStart
        End Get
    End Property

    ''' <summary>Kurzname fuer CATIA-Elementnamen, z. B. "R10-15" oder "Gerade".</summary>
    Public Function ShortLabel(rules As IList(Of Rule)) As String
        If RuleIndex = Geometry.STRAIGHT Then Return "Gerade"
        Return $"R{Rule.Fmt(rules(RuleIndex).RMin)}-{Rule.Fmt(rules(RuleIndex).RMax)}"
    End Function

    Public Function RuleLabel(rules As IList(Of Rule)) As String
        Return If(RuleIndex = Geometry.STRAIGHT, "Gerade/sonst", rules(RuleIndex).Label())
    End Function
End Class

Public Class CurveAnalysis
    Public Property Name As String
    Public Property Laenge As Double
    Public Property Closed As Boolean
    Public Property S As Double()
    Public Property Radius As Double()
    Public Property Segments As List(Of Segment)

    ''' <summary>Alle Punkte als (Bogenlaenge, Abschnittsindex).</summary>
    Public Function AllPoints() As List(Of KeyValuePair(Of Double, Integer))
        Dim out As New List(Of KeyValuePair(Of Double, Integer))
        For Each seg In Segments
            For Each p In seg.Punkte
                out.Add(New KeyValuePair(Of Double, Integer)(p, seg.Index))
            Next
        Next
        Return out
    End Function

    Public ReadOnly Property PointCount As Integer
        Get
            Return Segments.Sum(Function(sg) sg.Punkte.Count)
        End Get
    End Property
End Class

Public Module Geometry
    ''' <summary>Regelindex fuer "Gerade / sonstiger Radius".</summary>
    Public Const STRAIGHT As Integer = -1
    Private Const EPS As Double = 0.000000001

    ''' <summary>Radius des Kreises durch drei Punkte (+Unendlich bei kollinearen Punkten).</summary>
    Public Function Circumradius(a As Vec3, b As Vec3, c As Vec3) As Double
        Dim ab = (b - a).Length(), bc = (c - b).Length(), ca = (a - c).Length()
        Dim cr = Vec3.Cross(b - a, c - a).Length()
        If ab = 0 OrElse bc = 0 OrElse ca = 0 Then Return Double.PositiveInfinity
        If cr <= 0.000000000001 * ab * ca Then Return Double.PositiveInfinity
        Return ab * bc * ca / (2.0 * cr)
    End Function

    Public Function IsClosed(points As IList(Of Vec3), length As Double) As Boolean
        Return points.Count > 3 AndAlso (points(0) - points(points.Count - 1)).Length() < Math.Max(0.000001, 0.000001 * length)
    End Function

    ''' <summary>Lokaler Kruemmungsradius an jedem Messpunkt.</summary>
    Public Function Radii(points As IList(Of Vec3), closed As Boolean, Optional stp As Integer = 1) As Double()
        Dim n = points.Count
        Dim out(n - 1) As Double
        For i = 0 To n - 1 : out(i) = Double.PositiveInfinity : Next
        If n < 3 Then Return out
        If closed Then
            Dim m = n - 1   ' letzter Punkt == erster Punkt
            For i = 0 To m - 1
                out(i) = Circumradius(points(((i - stp) Mod m + m) Mod m), points(i), points((i + stp) Mod m))
            Next
            out(n - 1) = out(0)
            Return out
        End If
        For i = stp To n - 1 - stp
            out(i) = Circumradius(points(i - stp), points(i), points(i + stp))
        Next
        For i = 0 To stp - 1
            out(i) = out(stp)
            out(n - 1 - i) = out(n - 1 - stp)
        Next
        Return out
    End Function

    Public Function MedianFilter(values As IList(Of Double), window As Integer) As Double()
        If window <= 1 OrElse values.Count < 3 Then Return values.ToArray()
        Dim half = window \ 2, n = values.Count
        Dim out(n - 1) As Double
        For i = 0 To n - 1
            Dim lo = Math.Max(0, i - half), hi = Math.Min(n - 1, i + half)
            Dim w = Enumerable.Range(lo, hi - lo + 1).Select(Function(k) values(k)).OrderBy(Function(v) v).ToArray()
            out(i) = w(w.Length \ 2)
        Next
        Return out
    End Function

    ''' <summary>Regelindex fuer einen Radius. Luecken -> naechste Regel (Gleichstand: feinere).
    ''' Groesser als alle Regeln oder gerade -> STRAIGHT.</summary>
    Public Function Classify(r As Double, rules As IList(Of Rule)) As Integer
        If Double.IsInfinity(r) OrElse Double.IsNaN(r) Then Return STRAIGHT
        If r >= rules.Max(Function(x) x.RMax) Then Return STRAIGHT
        Dim best = STRAIGHT, bestD = Double.PositiveInfinity
        For i = 0 To rules.Count - 1
            Dim rule = rules(i)
            If rule.Contains(r) Then Return i
            Dim d = If(r < rule.RMin, rule.RMin - r, r - rule.RMax)
            If d < bestD - 0.000000000001 OrElse
               (Math.Abs(d - bestD) <= 0.000000000001 AndAlso rule.Abstand < rules(best).Abstand) Then
                best = i : bestD = d
            End If
        Next
        Return best
    End Function

    ''' <summary>Punktpositionen in [s0, s1].</summary>
    Public Function PlacePoints(s0 As Double, s1 As Double, abstand As Double,
                                mode As Verteilung, endpunkt As Boolean) As List(Of Double)
        Dim L = s1 - s0
        Dim pts As New List(Of Double)
        If L <= EPS Then pts.Add(s0) : Return pts
        If mode = Verteilung.Gleichmaessig Then
            Dim n = Math.Max(1, CInt(Math.Ceiling(L / abstand - EPS)))   ' Abstand nie groesser als gefordert
            For j = 0 To n : pts.Add(s0 + L * j / n) : Next
            Return pts
        End If
        Dim k = 0
        While s0 + k * abstand <= s1 + EPS
            pts.Add(Math.Min(s0 + k * abstand, s1))
            k += 1
        End While
        If endpunkt AndAlso s1 - pts(pts.Count - 1) > 0.000001 Then pts.Add(s1)
        Return pts
    End Function

    ' ---------------------------------------------------------------- Abschnitte
    Private Class Run
        Public Cls As Integer, S0 As Double, S1 As Double, I0 As Integer, I1 As Integer
        Public Sub New(cls As Integer, s0 As Double, s1 As Double, i0 As Integer, i1 As Integer)
            Me.Cls = cls : Me.S0 = s0 : Me.S1 = s1 : Me.I0 = i0 : Me.I1 = i1
        End Sub
        Public ReadOnly Property Len As Double
            Get
                Return S1 - S0
            End Get
        End Property
    End Class

    Private Function BuildRuns(classes As IList(Of Integer), s As IList(Of Double), length As Double) As List(Of Run)
        Dim runs As New List(Of Run)
        Dim startI = 0
        For i = 1 To classes.Count
            If i = classes.Count OrElse classes(i) <> classes(startI) Then
                Dim s0 = If(startI = 0, 0.0, 0.5 * (s(startI - 1) + s(startI)))
                Dim s1 = If(i = classes.Count, length, 0.5 * (s(i - 1) + s(i)))
                runs.Add(New Run(classes(startI), s0, s1, startI, i - 1))
                startI = i
            End If
        Next
        Return runs
    End Function

    ''' <summary>Verschmilzt zu kurze Abschnitte mit dem laengeren Nachbarn.</summary>
    Private Function MergeShort(runs As List(Of Run), minLen As Double) As List(Of Run)
        While runs.Count > 1
            Dim idx = 0
            For k = 1 To runs.Count - 1
                If runs(k).Len < runs(idx).Len Then idx = k
            Next
            If runs(idx).Len >= minLen Then Exit While
            Dim j As Integer
            If idx = 0 Then
                j = 1
            ElseIf idx = runs.Count - 1 Then
                j = idx - 1
            Else
                j = If(runs(idx - 1).Len >= runs(idx + 1).Len, idx - 1, idx + 1)
            End If
            Dim a = Math.Min(idx, j), b = Math.Max(idx, j)
            Dim merged As New Run(runs(j).Cls, runs(a).S0, runs(b).S1, runs(a).I0, runs(b).I1)
            runs.RemoveRange(a, 2)
            runs.Insert(a, merged)
            ' gleiche Nachbarklassen zusammenfassen
            Dim q = 0
            While q < runs.Count - 1
                If runs(q).Cls = runs(q + 1).Cls Then
                    Dim m As New Run(runs(q).Cls, runs(q).S0, runs(q + 1).S1, runs(q).I0, runs(q + 1).I1)
                    runs.RemoveRange(q, 2)
                    runs.Insert(q, m)
                Else
                    q += 1
                End If
            End While
        End While
        Return runs
    End Function

    ''' <summary>Rundet innere Grenzen auf Vielfache von raster (z. B. ganze mm).
    ''' Dabei leer gewordene Abschnitte entfallen, gleiche Nachbarn werden zusammengefasst.</summary>
    Private Function SnapBoundaries(runs As List(Of Run), raster As Double, length As Double) As List(Of Run)
        For k = 0 To runs.Count - 2
            Dim b = Math.Round(runs(k).S1 / raster, MidpointRounding.AwayFromZero) * raster
            b = Math.Min(Math.Max(b, 0.0), length)
            runs(k).S1 = b
            runs(k + 1).S0 = b
        Next
        Dim out As New List(Of Run)
        For Each r In runs
            If r.S1 - r.S0 <= EPS Then Continue For
            If out.Count > 0 AndAlso out(out.Count - 1).Cls = r.Cls Then
                out(out.Count - 1).S1 = r.S1
                out(out.Count - 1).I1 = r.I1
            Else
                If out.Count > 0 Then r.S0 = out(out.Count - 1).S1   ' lueckenlos halten
                out.Add(r)
            End If
        Next
        If out.Count > 0 Then out(0).S0 = 0.0 : out(out.Count - 1).S1 = length
        Return out
    End Function

    ''' <summary>Komplette Analyse: Radius -> Klassen -> Abschnitte -> Punkte.</summary>
    Public Function Analyze(name As String, points As IList(Of Vec3), length As Double, cfg As Settings) As CurveAnalysis
        Dim rules = cfg.SortedRules()
        Dim n = points.Count - 1
        If n < 1 Then Throw New ArgumentException("Mindestens zwei Messpunkte erforderlich.")
        Dim s(n) As Double
        For i = 0 To n : s(i) = length * i / n : Next
        Dim closed = IsClosed(points, length)
        Dim rad = MedianFilter(Radii(points, closed), cfg.Glaettung)
        Dim classes = rad.Select(Function(r) Classify(r, rules)).ToArray()

        Dim runs = MergeShort(BuildRuns(classes, s, length), cfg.MinAbschnittslaenge)
        If cfg.Grenzraster > 0 Then runs = SnapBoundaries(runs, cfg.Grenzraster, length)

        Dim segments As New List(Of Segment)
        Dim trim = cfg.Glaettung \ 2 + 1
        For k = 0 To runs.Count - 1
            Dim rn = runs(k)
            ' Messpunkte direkt an inneren Grenzen (Uebergangszone) nicht in die Statistik
            Dim a = rn.I0 + If(k > 0, trim, 0)
            Dim b = rn.I1 - If(k < runs.Count - 1, trim, 0)
            If b < a Then a = rn.I0 : b = rn.I1
            Dim finite = Enumerable.Range(a, b - a + 1).Select(Function(i) rad(i)).Where(Function(r) Not Double.IsInfinity(r)).ToList()
            segments.Add(New Segment With {
                .Index = k, .SStart = rn.S0, .SEnd = rn.S1, .RuleIndex = rn.Cls,
                .Abstand = If(rn.Cls = STRAIGHT, cfg.AbstandSonst, rules(rn.Cls).Abstand),
                .RMin = If(finite.Count > 0, finite.Min(), Double.PositiveInfinity),
                .RMax = If(finite.Count > 0, finite.Max(), Double.PositiveInfinity)})
        Next

        ' Punkte setzen, doppelte Punkte an Abschnittsgrenzen vermeiden
        Dim last As Double? = Nothing
        For Each seg In segments
            Dim pts = PlacePoints(seg.SStart, seg.SEnd, seg.Abstand, cfg.Verteilung, cfg.EndpunktErzwingen)
            If last.HasValue AndAlso pts.Count > 0 AndAlso Math.Abs(pts(0) - last.Value) < 0.000001 Then pts.RemoveAt(0)
            seg.Punkte = pts
            If pts.Count > 0 Then last = pts(pts.Count - 1)
        Next
        If closed AndAlso segments.Count > 0 Then
            Dim lp = segments(segments.Count - 1).Punkte
            If lp.Count > 0 AndAlso Math.Abs(lp(lp.Count - 1) - length) < 0.000001 Then lp.RemoveAt(lp.Count - 1) ' Ende == Start
        End If

        Return New CurveAnalysis With {.Name = name, .Laenge = length, .Closed = closed, .S = s, .Radius = rad, .Segments = segments}
    End Function
End Module
