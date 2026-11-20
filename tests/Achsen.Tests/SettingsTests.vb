Imports System.IO
Imports Achsen.Core
Imports Xunit

Public Class SettingsTests

    <Fact> Public Sub JsonRoundtripWithInfinity()
        Dim s As New Settings With {.Regeln = New List(Of Rule) From {New Rule(0, 10, 0.5), New Rule(10, Double.PositiveInfinity, 4)},
                                    .XUmkehren = True, .Praefix = "P", .Verteilung = Verteilung.Exakt}
        Dim t = Settings.FromJson(s.ToJson())
        Assert.True(Double.IsPositiveInfinity(t.Regeln(1).RMax))
        Assert.True(t.XUmkehren)
        Assert.Equal("P", t.Praefix)
        Assert.Equal(Verteilung.Exakt, t.Verteilung)
    End Sub

    <Fact> Public Sub Validation()
        Assert.Empty(New Settings().Validate())
        Dim bad As New Settings With {.Regeln = New List(Of Rule) From {New Rule(0, 10, 1), New Rule(5, 20, 2), New Rule(30, 20, 0)}}
        Dim errs = bad.Validate()
        Assert.Contains(errs, Function(e) e.Contains("ueberschneiden"))
        Assert.Contains(errs, Function(e) e.Contains("Regel 3"))
    End Sub

    <Fact> Public Sub GapsAreReported()
        Dim s As New Settings With {.Regeln = New List(Of Rule) From {New Rule(10, 15, 1), New Rule(16, 30, 3)}}
        Assert.Equal(Tuple.Create(15.0, 16.0), s.Gaps().Single())
    End Sub

    <Fact> Public Sub ParseRadius()
        Assert.True(Double.IsPositiveInfinity(Settings.ParseRadius("inf")))
        Assert.Equal(12.5, Settings.ParseRadius("12,5"))
    End Sub

    <Fact> Public Sub ShippedDefaultConfigIsValid()
        Dim p = Path.Combine(AppContext.BaseDirectory, "regeln_standard.json")
        Assert.Empty(Settings.Load(p).Validate())
    End Sub
End Class
