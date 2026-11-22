Option Strict Off   ' Late Binding auf die CATIA-COM-Objekte (keine Interop-DLLs noetig)
Option Explicit On

' Anbindung an CATIA V5 ueber COM.
'
' Erzeugte Struktur im Part:
'   <ErgebnisSet>
'     01_<Kurvenname>
'       Abschnitte        Kurventeilstuecke (Split)
'       Punkte            Punkte auf der Kurve
'       Normalen          Linien senkrecht (90 Grad) zur Flaeche
'       Hilfsgeometrie    Tangenten, Tangentialebenen, Projektionen (ausgeblendet)
'   Achsensysteme <Praefix>_<Kurve>_<Nr>  (Ursprung = Punkt, Z = Normale, X = Tangente Teilstueck)

Imports Achsen.Core

Public Class CatiaException
    Inherits Exception
    Public Sub New(msg As String, Optional inner As Exception = Nothing)
        MyBase.New(msg, inner)
    End Sub
End Class

''' <summary>Eingelesenes CATIA-Element (Kurve oder Flaeche) mit Referenz.</summary>
Public Class GeoInput
    Public Property Name As String
    Public Property Obj As Object
    Public Property Ref As Object
End Class

Public Class CatiaSession

    ' --- CATIA Enumerationen ---
    Public Const CAT_ORIGIN_BY_POINT As Integer = 1      ' catAxisSystemOriginByPoint
    Public Const CAT_AXIS_SAME As Integer = 1            ' catAxisSystemAxisSameDirection
    Public Const CAT_AXIS_OPPOSITE As Integer = 2        ' catAxisSystemAxisOppositeDirection
    Private Const CAT_VIS_HIDE As Integer = 1            ' catVisPropertyNoShowAttr
    Private Const CAT_VBSCRIPT As Integer = 0            ' CATVBScriptLanguage
    Private Const CAT_MULTISEL_VALIDATE As Integer = 2   ' CATMultiSelTriggWhenUserValidatesSelection
    Private Shared ReadOnly MeasurableCurves As Integer() = {7, 8, 9}  ' CatMeasurableName: Curve, Circle, Line

    ' Abtastung komplett innerhalb von CATIA (eine COM-Rundreise):
    ' alle Messpunkte in das temporaere Geoset legen -> EIN Part.Update -> Koordinaten lesen.
    ' Das Geoset wird danach mit EINEM Loeschvorgang entfernt (siehe Sample).
    Private Const VBS_SAMPLE As String =
"Function SampleCurve(oPart, oHB, oRef, n)
    Dim fac, spa, m, i
    Dim c(2)
    Dim pts()
    Dim res()
    ReDim pts(n)
    ReDim res(3 * (n + 1) - 1)
    Set fac = oPart.HybridShapeFactory
    For i = 0 To n
        Set pts(i) = fac.AddNewPointOnCurveFromPercent(oRef, i / n, False)
        oHB.AppendHybridShape pts(i)
    Next
    On Error Resume Next
    oPart.Update
    If Err.Number <> 0 Then
        ' Part enthaelt fremde fehlerhafte Elemente -> nur die Messpunkte aktualisieren
        Err.Clear
        On Error GoTo 0
        For i = 0 To n
            oPart.UpdateObject pts(i)
        Next
    End If
    On Error GoTo 0
    Set spa = oPart.Parent.GetWorkbench(""SPAWorkbench"")
    For i = 0 To n
        Set m = spa.GetMeasurable(oPart.CreateReferenceFromObject(pts(i)))
        m.GetPoint c
        res(3 * i) = c(0)
        res(3 * i + 1) = c(1)
        res(3 * i + 2) = c(2)
    Next
    SampleCurve = res
End Function"

    Private ReadOnly _log As Action(Of String)
    Private _catia As Object
    Private _doc As Object
    Private _part As Object

    Public Sub New(log As Action(Of String))
        _log = If(log, Sub(s) Debug.WriteLine(s))
    End Sub

    ' ------------------------------------------------------------------ Verbindung
    Public Sub Connect()
        Try
            _catia = GetObject(, "CATIA.Application")
        Catch ex As Exception
            Throw New CatiaException("CATIA V5 laeuft nicht oder ist nicht erreichbar.", ex)
        End Try
        Try
            _doc = _catia.ActiveDocument
            _part = _doc.Part
        Catch ex As Exception
            Throw New CatiaException("Aktives Dokument muss ein CATPart sein.", ex)
        End Try
        _log($"Verbunden mit CATIA - Part: {_part.Name}")
    End Sub

    Private Sub Ensure()
        ' bei jedem Aufruf neu verbinden: Benutzer kann zwischendurch das Dokument wechseln
        Connect()
    End Sub

    Private ReadOnly Property Factory As Object
        Get
            Return _part.HybridShapeFactory
        End Get
    End Property

    Private ReadOnly Property Spa As Object
        Get
            Return _doc.GetWorkbench("SPAWorkbench")
        End Get
    End Property

    Private Function Ref(obj As Object) As Object
        Return _part.CreateReferenceFromObject(obj)
    End Function

    ' ------------------------------------------------------------------ Auswahl
    Private Function IsCurve(reference As Object) As Boolean
        Try
            Return MeasurableCurves.Contains(CInt(Spa.GetMeasurable(reference).GeometryName))
        Catch
            Return False
        End Try
    End Function

    Private Sub CollectFromBody(hb As Object, out As List(Of GeoInput))
        Dim shapes = hb.HybridShapes
        For i = 1 To CInt(shapes.Count)
            Dim s = shapes.Item(i)
            Dim r = Ref(s)
            If IsCurve(r) Then out.Add(New GeoInput With {.Name = s.Name, .Obj = s, .Ref = r})
        Next
        Dim bodies = hb.HybridBodies
        For i = 1 To CInt(bodies.Count)
            CollectFromBody(bodies.Item(i), out)
        Next
    End Sub

    ''' <summary>Kurven aus aktueller Auswahl: einzelne Kurven und/oder Geometrische Sets.</summary>
    Public Function CurvesFromSelection() As List(Of GeoInput)
        Ensure()
        Dim sel = _doc.Selection
        Dim out As New List(Of GeoInput)
        For i = 1 To CInt(sel.Count)
            Dim item = sel.Item(i)
            Dim obj = item.Value
            If CStr(item.Type) = "HybridBody" Then
                CollectFromBody(obj, out)
                Continue For
            End If
            Dim r As Object
            Try
                r = Ref(obj)
            Catch
                r = item.Reference
            End Try
            If IsCurve(r) Then
                out.Add(New GeoInput With {.Name = obj.Name, .Obj = obj, .Ref = r})
            Else
                _log($"Uebersprungen (keine Kurve): {obj.Name}")
            End If
        Next
        _log($"{out.Count} Kurve(n) uebernommen.")
        Return out
    End Function

    ''' <summary>Interaktive Mehrfachauswahl in CATIA, Abschluss mit dem Haken (OK).</summary>
    Public Function PickCurves() As List(Of GeoInput)
        Ensure()
        Dim sel = _doc.Selection
        sel.Clear()
        Dim filter As Object() = {"HybridShape", "HybridBody", "MonoDimInfinite", "MonoDim"}
        Dim status As String = sel.SelectElement3(filter, "Kurven oder Geometrisches Set waehlen, dann bestaetigen",
                                                  False, CAT_MULTISEL_VALIDATE, False)
        If status <> "Normal" Then Return New List(Of GeoInput)
        Return CurvesFromSelection()
    End Function

    Public Function SurfaceFromSelection(interactive As Boolean) As GeoInput
        Ensure()
        Dim sel = _doc.Selection
        If interactive Then
            sel.Clear()
            Dim filter As Object() = {"HybridShape", "Face", "BiDim"}
            Dim status As String = sel.SelectElement2(filter, "Referenzflaeche waehlen", False)
            If status <> "Normal" Then Return Nothing
        End If
        If CInt(sel.Count) < 1 Then Return Nothing
        Dim item = sel.Item(1)
        Dim obj = item.Value
        Dim r As Object
        Try
            r = Ref(obj)
        Catch
            r = item.Reference
        End Try
        Dim name As String = "Flaeche"
        Try : name = obj.Name : Catch : End Try
        _log($"Flaeche: {name}")
        Return New GeoInput With {.Name = name, .Obj = obj, .Ref = r}
    End Function

    ' ------------------------------------------------------------------ Geosets
    Private Function GetOrAddBody(parentBodies As Object, name As String) As Object
        For i = 1 To CInt(parentBodies.Count)
            Dim b = parentBodies.Item(i)
            If CStr(b.Name) = name Then Return b
        Next
        Dim nb = parentBodies.Add()
        nb.Name = name
        Return nb
    End Function

    Private Sub Hide(obj As Object)
        Dim sel = _doc.Selection
        sel.Clear()
        sel.Add(obj)
        sel.VisProperties.SetShow(CAT_VIS_HIDE)
        sel.Clear()
    End Sub

    Private Sub DeleteObj(obj As Object)
        Dim sel = _doc.Selection
        sel.Clear()
        sel.Add(obj)
        sel.Delete()
    End Sub

    ' ------------------------------------------------------------------ Analyse
    Private Function CurveLength(reference As Object) As Double
        Return CDbl(Spa.GetMeasurable(reference).Length)
    End Function

    Private Function Sample(curve As GeoInput, cfg As Settings, ByRef length As Double) As List(Of Vec3)
        length = CurveLength(curve.Ref)
        Dim n = CInt(Math.Min(cfg.MaxAbtastpunkte, Math.Max(20, Math.Ceiling(length / cfg.Abtastabstand))))
        Dim tmp = _part.HybridBodies.Add()
        tmp.Name = "_tmp_abtastung"
        Dim raw As Object
        Dim oldRefresh As Boolean = True
        Try : oldRefresh = CBool(_catia.RefreshDisplay) : _catia.RefreshDisplay = False : Catch : End Try
        Try
            raw = _catia.SystemService.Evaluate(VBS_SAMPLE, CAT_VBSCRIPT, "SampleCurve",
                                                New Object() {_part, tmp, curve.Ref, n})
        Finally
            DeleteObj(tmp)      ' ein Loeschvorgang fuer alle Messpunkte
            Try : _catia.RefreshDisplay = oldRefresh : Catch : End Try
        End Try
        Dim vals = DirectCast(raw, Array)
        Dim lb = vals.GetLowerBound(0)
        Dim pts As New List(Of Vec3)(n + 1)
        For i = 0 To n
            pts.Add(New Vec3(CDbl(vals.GetValue(lb + 3 * i)), CDbl(vals.GetValue(lb + 3 * i + 1)), CDbl(vals.GetValue(lb + 3 * i + 2))))
        Next
        Return pts
    End Function

    Public Function Analyze(curves As IList(Of GeoInput), cfg As Settings) As List(Of CurveAnalysis)
        Ensure()
        Dim results As New List(Of CurveAnalysis)
        For Each c In curves
            Dim L As Double
            Dim pts = Sample(c, cfg, L)
            Dim res = Geometry.Analyze(c.Name, pts, L, cfg)
            _log($"{c.Name}: L={L:0.00} mm, {res.Segments.Count} Abschnitt(e), {res.PointCount} Punkt(e)")
            results.Add(res)
        Next
        Return results
    End Function

    ' ------------------------------------------------------------------ Erzeugung
    Private Function MakePoint(hb As Object, curveRef As Object, s As Double, L As Double, name As String) As Object
        Dim ratio = If(L <= 0, 0.0, Math.Min(1.0, Math.Max(0.0, s / L)))
        Dim p = Factory.AddNewPointOnCurveFromPercent(curveRef, ratio, False)
        p.Name = name
        hb.AppendHybridShape(p)
        _part.UpdateObject(p)
        Return p
    End Function

    ''' <summary>Split an einem Punkt; behaelt die Seite, auf der der Pruefpunkt liegt.</summary>
    Private Function SplitKeep(baseRef As Object, cutRef As Object, probeRef As Object, targetHb As Object, name As String) As Object
        Dim sp = Factory.AddNewHybridSplit(baseRef, cutRef, 1)
        sp.Name = name
        targetHb.AppendHybridShape(sp)
        _part.UpdateObject(sp)
        If CDbl(Spa.GetMeasurable(Ref(sp)).GetMinimumDistance(probeRef)) > 0.001 Then
            sp.InvertOrientation()
            _part.UpdateObject(sp)
        End If
        Return sp
    End Function

    ''' <summary>Kurventeilstueck [SStart, SEnd] per (doppeltem) Split. Liefert die Referenz.</summary>
    Private Function MakeSegment(curve As GeoInput, an As CurveAnalysis, seg As Segment,
                                 hbSeg As Object, hbHelp As Object, tag As String) As Object
        Dim L = an.Laenge, tol = 0.0001
        Dim cutStart = seg.SStart > tol
        Dim cutEnd = seg.SEnd < L - tol
        If Not (cutStart OrElse cutEnd) Then Return curve.Ref   ' Abschnitt = ganze Kurve
        Dim probeRef = Ref(MakePoint(hbHelp, curve.Ref, 0.5 * (seg.SStart + seg.SEnd), L, tag & "_Mitte"))
        Dim baseRef = curve.Ref
        Dim result As Object = Nothing
        If cutStart Then
            Dim cut = MakePoint(hbHelp, curve.Ref, seg.SStart, L, tag & "_Start")
            ' Zwischenergebnis -> Hilfsgeometrie, Endergebnis -> Abschnitte
            result = SplitKeep(baseRef, Ref(cut), probeRef, If(cutEnd, hbHelp, hbSeg), If(cutEnd, tag & "_tmp", tag))
            baseRef = Ref(result)
        End If
        If cutEnd Then
            Dim cut = MakePoint(hbHelp, curve.Ref, seg.SEnd, L, tag & "_Ende")
            result = SplitKeep(baseRef, Ref(cut), probeRef, hbSeg, tag)
        End If
        Return Ref(result)
    End Function

    ''' <summary>Kontrolle: Summe der erzeugten Teilstuecke = Kurvenlaenge.</summary>
    Private Sub CheckSegmentLengths(curve As GeoInput, an As CurveAnalysis, segRefs As Dictionary(Of Integer, Object))
        If an.Segments.Count < 2 OrElse segRefs.Values.Any(Function(r) r Is curve.Ref) Then Return
        Dim sum = 0.0
        For Each seg In an.Segments
            Dim l = CurveLength(segRefs(seg.Index))
            If Math.Abs(l - seg.Laenge) > 0.01 Then
                _log($"  WARNUNG Abschnitt {seg.Index + 1}: Laenge {l:0.000} mm, erwartet {seg.Laenge:0.000} mm")
            End If
            sum += l
        Next
        If Math.Abs(sum - an.Laenge) > 0.01 * an.Segments.Count Then
            _log($"  WARNUNG Laengenpruefung: Summe Teilstuecke {sum:0.000} mm <> Kurve {an.Laenge:0.000} mm")
        Else
            _log($"  Laengenpruefung OK: {an.Segments.Count} Teilstuecke = {sum:0.000} mm")
        End If
    End Sub

    ''' <summary>Erzeugt Abschnitte, Punkte, Normalen und Achsensysteme. Liefert Anzahl Achsensysteme.</summary>
    Public Function Build(curves As IList(Of GeoInput), analyses As IList(Of CurveAnalysis), surface As GeoInput,
                          cfg As Settings, Optional progress As Action(Of Integer, Integer) = Nothing) As Integer
        Ensure()
        If surface Is Nothing Then Throw New CatiaException("Keine Referenzflaeche gewaehlt.")
        Dim fac = Factory
        Dim root = GetOrAddBody(_part.HybridBodies, cfg.ErgebnisSet)
        Dim total = analyses.Sum(Function(a) a.PointCount)
        Dim done = 0, created = 0, errors = 0
        Dim lx = cfg.Linienlaenge
        Dim oldRefresh As Boolean = True
        Try : oldRefresh = CBool(_catia.RefreshDisplay) : _catia.RefreshDisplay = False : Catch : End Try
        Dim rules = cfg.SortedRules()
        Try
            For ci = 1 To curves.Count
                Dim curve = curves(ci - 1), an = analyses(ci - 1)
                Dim cset = GetOrAddBody(root.HybridBodies, $"{ci:00}_{curve.Name}")
                Dim hbSeg = GetOrAddBody(cset.HybridBodies, "Abschnitte")
                Dim hbPt = GetOrAddBody(cset.HybridBodies, "Punkte")
                Dim hbNrm = GetOrAddBody(cset.HybridBodies, "Normalen")
                Dim hbHelp = GetOrAddBody(cset.HybridBodies, "Hilfsgeometrie")

                Dim segRefs As New Dictionary(Of Integer, Object)
                For Each seg In an.Segments
                    Dim tag = $"Abschnitt_{seg.Index + 1:00}_{seg.ShortLabel(rules)}"
                    If cfg.AbschnitteErzeugen AndAlso an.Segments.Count > 1 Then
                        Try
                            segRefs(seg.Index) = MakeSegment(curve, an, seg, hbSeg, hbHelp, tag)
                        Catch ex As Exception
                            _log($"  {tag}: Split fehlgeschlagen ({ex.Message}) - X referenziert Originalkurve")
                            segRefs(seg.Index) = curve.Ref
                        End Try
                    Else
                        segRefs(seg.Index) = curve.Ref
                    End If
                Next
                CheckSegmentLengths(curve, an, segRefs)

                Dim pi = 0
                For Each kv In an.AllPoints()
                    pi += 1
                    Dim s = kv.Key, segIdx = kv.Value
                    Dim name = $"{cfg.Praefix}_{ci:00}_{pi:0000}"
                    Try
                        Dim pt = MakePoint(hbPt, curve.Ref, s, an.Laenge, "P_" & name)
                        Dim ptRef = Ref(pt)

                        ' Linie 90 Grad zur Flaeche
                        Dim normal = fac.AddNewLineNormal(surface.Ref, ptRef, 0.0, lx, False)
                        normal.Name = "N_" & name
                        hbNrm.AppendHybridShape(normal)
                        _part.UpdateObject(normal)

                        ' Tangente am Kurventeilstueck
                        Dim tan = fac.AddNewLineTangency(segRefs(segIdx), ptRef, 0.0, lx, False)
                        tan.Name = "T_" & name
                        hbHelp.AppendHybridShape(tan)
                        _part.UpdateObject(tan)
                        Dim xRef = Ref(tan)

                        If cfg.XOrthogonalisieren Then
                            Dim plane = fac.AddNewPlaneTangent(surface.Ref, ptRef)
                            plane.Name = "E_" & name
                            hbHelp.AppendHybridShape(plane)
                            _part.UpdateObject(plane)
                            Dim proj = fac.AddNewProject(xRef, Ref(plane))
                            proj.Normal = True
                            proj.Name = "TP_" & name
                            hbHelp.AppendHybridShape(proj)
                            _part.UpdateObject(proj)
                            xRef = Ref(proj)
                        End If

                        Dim axs = _part.AxisSystems.Add()
                        axs.OriginType = CAT_ORIGIN_BY_POINT
                        axs.OriginPoint = ptRef
                        axs.XAxisType = If(cfg.XUmkehren, CAT_AXIS_OPPOSITE, CAT_AXIS_SAME)
                        axs.XAxisDirection = xRef
                        axs.ZAxisType = If(cfg.ZUmkehren, CAT_AXIS_OPPOSITE, CAT_AXIS_SAME)
                        axs.ZAxisDirection = Ref(normal)
                        axs.Name = name
                        _part.UpdateObject(axs)
                        created += 1
                    Catch ex As Exception
                        errors += 1
                        _log($"  Fehler bei {name} (s={s:0.000} mm): {ex.Message}")
                    End Try
                    done += 1
                    progress?.Invoke(done, total)
                Next

                If cfg.HilfsgeometrieAusblenden Then Hide(hbHelp)
            Next
            _part.Update()
        Finally
            Try : _catia.RefreshDisplay = oldRefresh : Catch : End Try
        End Try
        _log($"Fertig: {created} Achsensystem(e) erzeugt, {errors} Fehler.")
        Return created
    End Function

    ' ------------------------------------------------------------------ Umkehren
    ''' <summary>Kehrt X und/oder Z ("X", "Z", "XZ") der erzeugten Achsensysteme um.
    ''' onlySelected: nur in CATIA ausgewaehlte Achsensysteme, sonst alle mit Praefix.</summary>
    Public Function ToggleAxes(which As String, prefix As String, onlySelected As Boolean) As Integer
        Ensure()
        Dim targets As New List(Of Object)
        If onlySelected Then
            Dim sel = _doc.Selection
            For i = 1 To CInt(sel.Count)
                If CStr(sel.Item(i).Type) = "AxisSystem" Then targets.Add(sel.Item(i).Value)
            Next
        Else
            Dim axes = _part.AxisSystems
            For i = 1 To CInt(axes.Count)
                Dim a = axes.Item(i)
                If CStr(a.Name).StartsWith(prefix & "_") Then targets.Add(a)
            Next
        End If
        For Each a In targets
            If which.Contains("X") Then a.XAxisType = Flip(CInt(a.XAxisType))
            If which.Contains("Z") Then a.ZAxisType = Flip(CInt(a.ZAxisType))
            _part.UpdateObject(a)
        Next
        _part.Update()
        _log($"{which} umgekehrt bei {targets.Count} Achsensystem(en).")
        Return targets.Count
    End Function

    Private Shared Function Flip(t As Integer) As Integer
        Return If(t = CAT_AXIS_OPPOSITE, CAT_AXIS_SAME, CAT_AXIS_OPPOSITE)
    End Function
End Class
