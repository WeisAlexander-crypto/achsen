Imports Achsen.Core
Imports Xunit

Public Class GeometryTests

    ''' <summary>Gerade 20 -> Bogen R12 (90 Grad) -> Bogen R25 (90 Grad, Gegenrichtung) -> Gerade 20.</summary>
    Private Shared Function CompositeCurve(Optional ds As Double = 0.25) As Tuple(Of List(Of Vec3), Double)
        Dim parts = {Tuple.Create(0.0, 20.0, 0), Tuple.Create(12.0, 12.0 * Math.PI / 2, 1),
                     Tuple.Create(25.0, 25.0 * Math.PI / 2, -1), Tuple.Create(0.0, 20.0, 0)}
        Dim total = parts.Sum(Function(p) p.Item2)
        Dim at = Function(sIn As Double) As Vec3
                     Dim s = sIn, x = 0.0, y = 0.0, h = 0.0
                     For Each p In parts
                         Dim t = Math.Min(s, p.Item2)
                         If p.Item3 = 0 Then
                             x += t * Math.Cos(h) : y += t * Math.Sin(h)
                         Else
                             Dim R = p.Item1, sg = p.Item3
                             Dim cx = x - sg * R * Math.Sin(h), cy = y + sg * R * Math.Cos(h)
                             Dim h2 = h + sg * t / R
                             x = cx + sg * R * Math.Sin(h2) : y = cy - sg * R * Math.Cos(h2)
                             If t = p.Item2 Then h = h2
                         End If
                         If s <= p.Item2 Then Return New Vec3(x, y, 0)
                         s -= p.Item2
                     Next
                     Return New Vec3(x, y, 0)
                 End Function
        Dim n = CInt(Math.Ceiling(total / ds))
        Dim pts = Enumerable.Range(0, n + 1).Select(Function(i) at(total * i / n)).ToList()
        Return Tuple.Create(pts, total)
    End Function

    <Fact> Public Sub CircumradiusWorks()
        Dim R = 7.0
        Dim p = {0.0, 0.3, 0.7}.Select(Function(a) New Vec3(R * Math.Cos(a), R * Math.Sin(a), 0)).ToArray()
        Assert.Equal(R, Geometry.Circumradius(p(0), p(1), p(2)), 9)
        Assert.True(Double.IsInfinity(Geometry.Circumradius(New Vec3(0, 0, 0), New Vec3(1, 0, 0), New Vec3(2, 0, 0))))
    End Sub

    <Fact> Public Sub ClassifyWithGapAndStraight()
        Dim rules As New List(Of Rule) From {New Rule(10, 15, 1), New Rule(16, 30, 3)}
        Assert.Equal(0, Geometry.Classify(12, rules))
        Assert.Equal(1, Geometry.Classify(20, rules))
        Assert.Equal(0, Geometry.Classify(15.3, rules))
        Assert.Equal(1, Geometry.Classify(15.7, rules))
        Assert.Equal(0, Geometry.Classify(15.5, rules))   ' Gleichstand -> feinere Regel
        Assert.Equal(0, Geometry.Classify(5, rules))
        Assert.Equal(Geometry.STRAIGHT, Geometry.Classify(31, rules))
        Assert.Equal(Geometry.STRAIGHT, Geometry.Classify(Double.PositiveInfinity, rules))
    End Sub

    <Fact> Public Sub MedianRemovesSpike()
        Assert.Equal(New Double() {1, 1, 1, 1, 1}, Geometry.MedianFilter(New Double() {1, 1, 100, 1, 1}, 3))
    End Sub

    <Fact> Public Sub EvenPointsNeverExceedSpacing()
        Dim p = Geometry.PlacePoints(0, 10, 3, Verteilung.Gleichmaessig, True)
        Assert.Equal(5, p.Count)
        Assert.Equal(0.0, p(0)) : Assert.Equal(10.0, p(4), 9)
    End Sub

    <Fact> Public Sub ExactPoints()
        Assert.Equal(New Double() {0, 3, 6, 9, 10}, Geometry.PlacePoints(0, 10, 3, Verteilung.Exakt, True).ToArray())
        Assert.Equal(New Double() {0, 3, 6, 9}, Geometry.PlacePoints(0, 10, 3, Verteilung.Exakt, False).ToArray())
    End Sub

    <Fact> Public Sub AnalyzeCompositeCurve()
        Dim c = CompositeCurve()
        Dim cfg As New Settings With {.Regeln = New List(Of Rule) From {New Rule(10, 15, 1), New Rule(15, 30, 3)},
                                      .AbstandSonst = 10, .MinAbschnittslaenge = 2, .Glaettung = 5}
        Dim res = Geometry.Analyze("K1", c.Item1, c.Item2, cfg)

        Assert.Equal(New Integer() {Geometry.STRAIGHT, 0, 1, Geometry.STRAIGHT}, res.Segments.Select(Function(sg) sg.RuleIndex).ToArray())
        Dim expected = {20.0, 20.0 + 6 * Math.PI, 20.0 + 6 * Math.PI + 12.5 * Math.PI}
        For i = 0 To 2
            Assert.InRange(res.Segments(i + 1).SStart, expected(i) - 1, expected(i) + 1)
        Next
        Assert.InRange(res.Segments(1).RMin, 11.76, 12.24)
        Assert.InRange(res.Segments(1).RMax, 11.76, 12.24)
        For Each sg In res.Segments
            For k = 1 To sg.Punkte.Count - 1
                Assert.True(sg.Punkte(k) - sg.Punkte(k - 1) <= sg.Abstand + 0.000000001)
            Next
        Next
        Dim all = res.AllPoints().Select(Function(kv) kv.Key).ToList()
        Assert.Equal(all.OrderBy(Function(v) v), all)
        Assert.Equal(all.Count, all.Select(Function(v) Math.Round(v, 6)).Distinct().Count())
        Assert.Equal(0.0, all(0)) : Assert.Equal(c.Item2, all(all.Count - 1), 9)
    End Sub

    <Fact> Public Sub ClosedCircleSingleSegment()
        Dim R = 20.0, n = 400
        Dim pts = Enumerable.Range(0, n + 1).Select(Function(i) New Vec3(R * Math.Cos(2 * Math.PI * i / n), R * Math.Sin(2 * Math.PI * i / n), 0)).ToList()
        Dim L = 2 * Math.PI * R
        Dim res = Geometry.Analyze("Kreis", pts, L, New Settings With {.Regeln = New List(Of Rule) From {New Rule(15, 30, 3)}})
        Assert.True(res.Closed)
        Assert.Single(res.Segments)
        Assert.Equal(0, res.Segments(0).RuleIndex)
        Assert.True(res.AllPoints().Last().Key < L - 0.000001)
    End Sub

    <Fact> Public Sub ShortSegmentIsMerged()
        Dim c = CompositeCurve()
        Dim cfg As New Settings With {.Regeln = New List(Of Rule) From {New Rule(10, 15, 1), New Rule(15, 30, 3)},
                                      .AbstandSonst = 10, .MinAbschnittslaenge = 25}
        Dim res = Geometry.Analyze("K1", c.Item1, c.Item2, cfg)
        Assert.DoesNotContain(0, res.Segments.Select(Function(sg) sg.RuleIndex))
    End Sub

    <Fact> Public Sub GrenzrasterGanzeMm()
        Dim c = CompositeCurve()
        Dim cfg As New Settings With {.Regeln = New List(Of Rule) From {New Rule(10, 15, 1), New Rule(15, 30, 3)},
                                      .AbstandSonst = 10, .Grenzraster = 1}
        Dim res = Geometry.Analyze("K1", c.Item1, c.Item2, cfg)
        Assert.Equal(4, res.Segments.Count)
        For i = 1 To res.Segments.Count - 1
            Dim b = res.Segments(i).SStart
            Assert.Equal(Math.Round(b), b, 9)                              ' Grenze auf ganzem mm
            Assert.Equal(res.Segments(i - 1).SEnd, b, 9)                   ' lueckenlos
        Next
        Assert.Equal(0.0, res.Segments(0).SStart)
        Assert.Equal(c.Item2, res.Segments.Last().SEnd, 9)
        Assert.Equal(39.0, res.Segments(2).SStart, 9)                      ' 20 + 6*pi = 38.85 -> 39
    End Sub

    <Fact> Public Sub GrobesRasterEntferntLeereAbschnitte()
        Dim c = CompositeCurve()
        Dim cfg As New Settings With {.Regeln = New List(Of Rule) From {New Rule(10, 15, 1), New Rule(15, 30, 3)},
                                      .AbstandSonst = 10, .Grenzraster = 50}
        Dim res = Geometry.Analyze("K1", c.Item1, c.Item2, cfg)
        Assert.All(res.Segments, Sub(sg) Assert.True(sg.Laenge > 0))
        For i = 1 To res.Segments.Count - 1
            Assert.Equal(res.Segments(i - 1).SEnd, res.Segments(i).SStart, 9)
            Assert.NotEqual(res.Segments(i - 1).RuleIndex, res.Segments(i).RuleIndex)
        Next
        Assert.Equal(c.Item2, res.Segments.Last().SEnd, 9)
    End Sub

    <Fact> Public Sub ShortLabelFuerElementnamen()
        Dim rules As New List(Of Rule) From {New Rule(10, 15, 1)}
        Assert.Equal("R10-15", New Segment With {.RuleIndex = 0}.ShortLabel(rules))
        Assert.Equal("Gerade", New Segment With {.RuleIndex = Geometry.STRAIGHT}.ShortLabel(rules))
    End Sub
End Class
