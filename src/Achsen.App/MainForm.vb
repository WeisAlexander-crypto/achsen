Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports Achsen.Core

''' <summary>Hauptfenster. Wird komplett im Code aufgebaut (kein Designer noetig).</summary>
Public Class MainForm
    Inherits Form

    Private Shared ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture
    Private ReadOnly _defaultCfgPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "regeln_standard.json")

    Private _cfg As Settings
    Private ReadOnly _session As CatiaSession
    Private _curves As New List(Of GeoInput)
    Private _surface As GeoInput
    Private _analyses As New List(Of CurveAnalysis)

    Private ReadOnly lblCurves As New Label With {.Text = "Keine Kurven", .AutoSize = True}
    Private ReadOnly lblSurface As New Label With {.Text = "Keine Flaeche", .AutoSize = True}
    Private ReadOnly gridRules As New DataGridView
    Private ReadOnly propGrid As New PropertyGrid
    Private ReadOnly gridResult As New DataGridView
    Private ReadOnly txtLog As New TextBox With {.Multiline = True}
    Private ReadOnly progress As New ProgressBar With {.Dock = DockStyle.Fill}
    Private ReadOnly chkOnlySel As New CheckBox With {.Text = "nur in CATIA ausgewaehlte", .AutoSize = True}

    Public Sub New()
        Text = "CATIA V5 - Achsensysteme auf Kurven"
        Width = 1250 : Height = 900
        StartPosition = FormStartPosition.CenterScreen
        Font = New Drawing.Font("Segoe UI", 9.0F)
        _session = New CatiaSession(AddressOf Log)
        Try
            _cfg = If(File.Exists(_defaultCfgPath), Settings.Load(_defaultCfgPath), New Settings())
        Catch ex As Exception
            _cfg = New Settings()
            MessageBox.Show("Standardregeln konnten nicht geladen werden: " & ex.Message)
        End Try
        BuildUi()
        LoadIntoUi()
    End Sub

    ' ================================================================== UI-Aufbau
    Private Sub BuildUi()
        Dim split As New SplitContainer With {.Dock = DockStyle.Fill, .FixedPanel = FixedPanel.Panel1}
        Controls.Add(split)
        split.SplitterDistance = 445

        Dim left As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown,
                                              .WrapContents = False, .AutoScroll = True, .Padding = New Padding(6)}
        split.Panel1.Controls.Add(left)
        Const W As Integer = 400

        ' 1. Eingabe
        Dim t1 = NewTable(2)
        t1.Controls.Add(Btn("Kurven / Geoset aus Auswahl", Sub() TakeCurves(False)), 0, 0)
        t1.Controls.Add(Btn("Kurven in CATIA waehlen...", Sub() TakeCurves(True)), 1, 0)
        t1.Controls.Add(lblCurves, 0, 1) : t1.SetColumnSpan(lblCurves, 2)
        t1.Controls.Add(Btn("Flaeche aus Auswahl", Sub() TakeSurface(False)), 0, 2)
        t1.Controls.Add(Btn("Flaeche in CATIA waehlen...", Sub() TakeSurface(True)), 1, 2)
        t1.Controls.Add(lblSurface, 0, 3) : t1.SetColumnSpan(lblSurface, 2)
        left.Controls.Add(Group("1. Eingabe", t1, W))

        ' 2. Regeln
        gridRules.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        gridRules.Height = 170
        gridRules.AllowUserToAddRows = True
        gridRules.AllowUserToDeleteRows = True
        gridRules.RowHeadersWidth = 24
        gridRules.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        gridRules.Columns.Add("rmin", "R von [mm]")
        gridRules.Columns.Add("rmax", "R bis [mm] (inf)")
        gridRules.Columns.Add("abst", "1 Punkt je [mm]")
        Dim t2 = NewTable(3)
        t2.Controls.Add(gridRules, 0, 0) : t2.SetColumnSpan(gridRules, 3)
        t2.Controls.Add(Btn("Standard", AddressOf ResetRules), 0, 1)
        t2.Controls.Add(Btn("Laden...", AddressOf LoadCfg), 1, 1)
        t2.Controls.Add(Btn("Speichern...", AddressOf SaveCfg), 2, 1)
        Dim hint As New Label With {.AutoSize = True, .MaximumSize = New Drawing.Size(W - 20, 0),
            .Text = "Bereich gilt als von <= R < bis. Zeile loeschen: markieren + Entf. Geraden: siehe Optionen."}
        t2.Controls.Add(hint, 0, 2) : t2.SetColumnSpan(hint, 3)
        left.Controls.Add(Group("2. Punktregeln (Radius von-bis -> Abstand)", t2, W))

        ' 3. Optionen
        propGrid.Height = 330
        propGrid.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        propGrid.ToolbarVisible = False
        propGrid.HelpVisible = True
        Dim t3 = NewTable(1)
        t3.Controls.Add(propGrid, 0, 0)
        left.Controls.Add(Group("3. Optionen", t3, W))

        ' 4. Ausfuehren
        Dim t4 = NewTable(3)
        t4.Controls.Add(Btn("Analysieren (Vorschau)", Sub() RunAnalyze()), 0, 0)
        t4.Controls.Add(Btn("Erzeugen in CATIA", AddressOf RunBuild), 1, 0)
        t4.Controls.Add(Btn("Analyse als CSV...", AddressOf ExportCsv), 2, 0)
        t4.Controls.Add(progress, 0, 1) : t4.SetColumnSpan(progress, 3)
        left.Controls.Add(Group("4. Ausfuehren", t4, W))

        ' 5. Umkehren
        Dim t5 = NewTable(3)
        t5.Controls.Add(chkOnlySel, 0, 0) : t5.SetColumnSpan(chkOnlySel, 3)
        t5.Controls.Add(Btn("X umkehren", Sub() Toggle("X")), 0, 1)
        t5.Controls.Add(Btn("Z umkehren", Sub() Toggle("Z")), 1, 1)
        t5.Controls.Add(Btn("X und Z umkehren", Sub() Toggle("XZ")), 2, 1)
        left.Controls.Add(Group("5. Erzeugte Achsensysteme umkehren", t5, W))

        ' Rechts: Ergebnis + Protokoll
        Dim rsplit As New SplitContainer With {.Dock = DockStyle.Fill, .Orientation = Orientation.Horizontal}
        split.Panel2.Controls.Add(rsplit)
        rsplit.SplitterDistance = 520

        gridResult.Dock = DockStyle.Fill
        gridResult.ReadOnly = True
        gridResult.AllowUserToAddRows = False
        gridResult.RowHeadersVisible = False
        gridResult.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        For Each h In {"Kurve", "Nr", "s von", "s bis", "Laenge", "R min", "R max", "Regel", "Punkte"}
            gridResult.Columns.Add(h, h)
        Next
        Dim gRes As New GroupBox With {.Text = "Analyse: Kurvenabschnitte", .Dock = DockStyle.Fill}
        gRes.Controls.Add(gridResult)
        rsplit.Panel1.Controls.Add(gRes)

        txtLog.Multiline = True
        txtLog.ScrollBars = ScrollBars.Vertical
        txtLog.ReadOnly = True
        txtLog.Dock = DockStyle.Fill
        txtLog.Font = New Drawing.Font("Consolas", 9.0F)
        Dim gLog As New GroupBox With {.Text = "Protokoll", .Dock = DockStyle.Fill}
        gLog.Controls.Add(txtLog)
        rsplit.Panel2.Controls.Add(gLog)
    End Sub

    Private Shared Function NewTable(cols As Integer) As TableLayoutPanel
        Dim t As New TableLayoutPanel With {.ColumnCount = cols, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}
        For i = 1 To cols
            t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F / cols))
        Next
        Return t
    End Function

    Private Shared Function Group(title As String, content As Control, width As Integer) As GroupBox
        Dim g As New GroupBox With {.Text = title, .Width = width, .AutoSize = True,
                                    .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(6)}
        content.Location = New Drawing.Point(7, 20)
        content.MinimumSize = New Drawing.Size(width - 16, 0)
        content.MaximumSize = New Drawing.Size(width - 16, 0)
        g.Controls.Add(content)
        g.MinimumSize = New Drawing.Size(width, 0)
        Return g
    End Function

    Private Shared Function Btn(text As String, action As Action) As Button
        Dim b As New Button With {.Text = text, .Anchor = AnchorStyles.Left Or AnchorStyles.Right, .Height = 30}
        AddHandler b.Click, Sub() action()
        Return b
    End Function

    ' ================================================================== Einstellungen
    Private Sub LoadIntoUi()
        gridRules.Rows.Clear()
        For Each r In _cfg.SortedRules()
            gridRules.Rows.Add(Fmt(r.RMin), Fmt(r.RMax), Fmt(r.Abstand))
        Next
        propGrid.SelectedObject = _cfg
    End Sub

    Private Shared Function Fmt(v As Double) As String
        Return If(Double.IsInfinity(v), "inf", v.ToString("0.###", Inv))
    End Function

    ''' <summary>Regeltabelle in die Einstellungen uebernehmen und alles pruefen.</summary>
    Private Function ReadUi() As Boolean
        gridRules.EndEdit()
        Dim rules As New List(Of Rule)
        For Each row As DataGridViewRow In gridRules.Rows
            If row.IsNewRow Then Continue For
            Dim cells = {row.Cells(0).Value, row.Cells(1).Value, row.Cells(2).Value}.Select(Function(v) Convert.ToString(v)).ToArray()
            If cells.All(Function(c) String.IsNullOrWhiteSpace(c)) Then Continue For
            Try
                rules.Add(New Rule(Settings.ParseRadius(cells(0)), Settings.ParseRadius(cells(1)), Settings.ParseRadius(cells(2))))
            Catch
                MessageBox.Show($"Regel in Zeile {row.Index + 1}: bitte Zahlen eingeben.", "Regeln", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return False
            End Try
        Next
        _cfg.Regeln = rules
        Dim errs = _cfg.Validate()
        If errs.Count > 0 Then
            MessageBox.Show(String.Join(Environment.NewLine, errs), "Einstellungen", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End If
        For Each g In _cfg.Gaps()
            Log($"Hinweis: Radius {Fmt(g.Item1)}-{Fmt(g.Item2)} mm von keiner Regel abgedeckt -> naechste Regel wird verwendet.")
        Next
        Return True
    End Function

    Private Sub ResetRules()
        _cfg.Regeln = Settings.DefaultRules()
        LoadIntoUi()
    End Sub

    Private Sub LoadCfg()
        Using d As New OpenFileDialog With {.Filter = "Regeln (*.json)|*.json", .InitialDirectory = Path.GetDirectoryName(_defaultCfgPath)}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                _cfg = Settings.Load(d.FileName)
                LoadIntoUi()
                Log("Regeln geladen: " & d.FileName)
            Catch ex As Exception
                MessageBox.Show("Datei konnte nicht gelesen werden: " & ex.Message)
            End Try
        End Using
    End Sub

    Private Sub SaveCfg()
        If Not ReadUi() Then Return
        Using d As New SaveFileDialog With {.Filter = "Regeln (*.json)|*.json", .DefaultExt = "json",
                                            .InitialDirectory = Path.GetDirectoryName(_defaultCfgPath)}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            _cfg.Save(d.FileName)
            Log("Regeln gespeichert: " & d.FileName)
        End Using
    End Sub

    ' ================================================================== Aktionen
    Private Sub Log(msg As String)
        txtLog.AppendText(msg & Environment.NewLine)
        Application.DoEvents()
    End Sub

    ''' <summary>Fuehrt eine CATIA-Aktion mit Fehlerbehandlung und Wartecursor aus.</summary>
    Private Function Catia(Of T)(fn As Func(Of T), ByRef result As T) As Boolean
        Cursor = Cursors.WaitCursor
        Try
            result = fn()
            Return True
        Catch ex As CatiaException
            MessageBox.Show(ex.Message, "CATIA", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Catch ex As Exception
            Log("Fehler: " & ex.Message)
            MessageBox.Show(ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            Cursor = Cursors.Default
        End Try
        Return False
    End Function

    Private Sub TakeCurves(interactive As Boolean)
        Dim c As List(Of GeoInput) = Nothing
        If Not Catia(If(interactive, New Func(Of List(Of GeoInput))(AddressOf _session.PickCurves),
                                     AddressOf _session.CurvesFromSelection), c) Then Return
        _curves = c
        _analyses = New List(Of CurveAnalysis)
        gridResult.Rows.Clear()
        Dim names = String.Join(", ", c.Take(4).Select(Function(x) x.Name)) & If(c.Count > 4, " ...", "")
        lblCurves.Text = If(c.Count > 0, $"{c.Count} Kurve(n): {names}", "Keine Kurven")
    End Sub

    Private Sub TakeSurface(interactive As Boolean)
        Dim s As GeoInput = Nothing
        If Not Catia(Function() _session.SurfaceFromSelection(interactive), s) Then Return
        If s Is Nothing Then
            MessageBox.Show("Keine Flaeche ausgewaehlt.")
            Return
        End If
        _surface = s
        lblSurface.Text = "Flaeche: " & s.Name
    End Sub

    Private Function RunAnalyze() As Boolean
        If _curves.Count = 0 Then
            MessageBox.Show("Zuerst Kurven waehlen.", "Analyse")
            Return False
        End If
        If Not ReadUi() Then Return False
        Dim res As List(Of CurveAnalysis) = Nothing
        If Not Catia(Function() _session.Analyze(_curves, _cfg), res) Then Return False
        _analyses = res
        Dim rules = _cfg.SortedRules()
        gridResult.Rows.Clear()
        For Each an In res
            For Each seg In an.Segments
                gridResult.Rows.Add(an.Name, seg.Index + 1, seg.SStart.ToString("0.00"), seg.SEnd.ToString("0.00"),
                                    seg.Laenge.ToString("0.00"), FmtR(seg.RMin), FmtR(seg.RMax),
                                    seg.RuleLabel(rules), seg.Punkte.Count)
            Next
        Next
        Log($"Analyse: {res.Sum(Function(a) a.PointCount)} Punkte geplant.")
        Return True
    End Function

    Private Shared Function FmtR(r As Double) As String
        Return If(Double.IsInfinity(r), "gerade", r.ToString("0.00"))
    End Function

    Private Sub RunBuild()
        If Not RunAnalyze() Then Return
        If _surface Is Nothing Then
            MessageBox.Show("Zuerst die Referenzflaeche waehlen.", "Erzeugen")
            Return
        End If
        Dim n = _analyses.Sum(Function(a) a.PointCount)
        If MessageBox.Show($"{n} Achsensysteme erzeugen?{Environment.NewLine}{Environment.NewLine}Tipp: CATPart vorher speichern (Sicherheitskopie).", "Erzeugen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        Dim dummy As Integer
        Catia(Function() _session.Build(_curves, _analyses, _surface, _cfg,
                                        Sub(done, total)
                                            progress.Maximum = Math.Max(1, total)
                                            progress.Value = Math.Min(done, progress.Maximum)
                                            If done Mod 10 = 0 OrElse done = total Then Application.DoEvents()
                                        End Sub), dummy)
    End Sub

    Private Sub Toggle(which As String)
        Dim dummy As Integer
        Catia(Function() _session.ToggleAxes(which, _cfg.Praefix, chkOnlySel.Checked), dummy)
    End Sub

    Private Sub ExportCsv()
        If _analyses.Count = 0 Then
            MessageBox.Show("Zuerst analysieren.", "CSV")
            Return
        End If
        Using d As New SaveFileDialog With {.Filter = "CSV (*.csv)|*.csv", .DefaultExt = "csv"}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim rules = _cfg.SortedRules()
            Dim sb As New StringBuilder
            sb.AppendLine("Kurve;Abschnitt;s_von;s_bis;Laenge;R_min;R_max;Regel;Abstand;Punkt_s")
            For Each an In _analyses
                For Each seg In an.Segments
                    For Each s In seg.Punkte
                        sb.AppendLine(String.Join(";", an.Name, seg.Index + 1, seg.SStart.ToString("0.000"),
                            seg.SEnd.ToString("0.000"), seg.Laenge.ToString("0.000"), FmtR(seg.RMin), FmtR(seg.RMax),
                            seg.RuleLabel(rules), seg.Abstand, s.ToString("0.000")))
                    Next
                Next
            Next
            File.WriteAllText(d.FileName, sb.ToString(), New UTF8Encoding(True))   ' BOM fuer Excel
            Log("CSV gespeichert: " & d.FileName)
        End Using
    End Sub
End Class
