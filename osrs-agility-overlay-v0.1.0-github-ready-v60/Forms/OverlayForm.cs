using System.Diagnostics;
using System.Runtime.InteropServices;
using OSRSAgilityOverlay.Input;
using OSRSAgilityOverlay.Models;
using OSRSAgilityOverlay.Rendering;
using OSRSAgilityOverlay.Services;

namespace OSRSAgilityOverlay.Forms;

public sealed class OverlayForm : Form
{
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int GWL_EXSTYLE = -20;
    private const int MOD_CONTROL = 0x0002;
    private const int VK_LBUTTON = 0x01;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly ConfigService _configService = new();
    private readonly RuneLiteWindowService _windowService = new();
    private readonly OverlayRenderer _overlayRenderer = new();
    private readonly MarkerRenderer _markerRenderer = new();
    private readonly TickSyncService _tickSync = new();
    private readonly TimingDebugLogger _debugLogger = new();
    private readonly NotifyIcon _tray;

    private EditorForm? _editor;
    private bool _editMode;
    private bool _paused;
    private bool _suppressTickSyncDebugDrawing;
    private bool _leftWasDown;
    private bool _loggingMode;
    private bool _quitting;
    private DateTime _nextTickClockSampleAt = DateTime.MinValue;
    private DateTime _pixelSyncNoticeUntil = DateTime.MinValue;
    private DateTime _loggingNoticeUntil = DateTime.MinValue;
    private int _dragIndex = -1;
    private Point _dragOffset;

    public OverlayConfig Config { get; private set; }
    public TickClock Clock { get; }
    public MarkerSequenceEngine Sequence { get; private set; }
    public StatsService Stats { get; } = new();
    public TickSyncService TickSync => _tickSync;
    public TimingDebugLogger DebugLogger => _debugLogger;
    public bool EditMode => _editMode;
    public bool DebugTimingLogActive => _debugLogger.Enabled;
    public bool LoggingMode => _loggingMode;
    public bool IsQuitting => _quitting;

    private bool EffectiveDebugTimingLogEnabled() => _loggingMode;

    public OverlayForm()
    {
        Config = _configService.Load();
        Clock = new TickClock { TickSeconds = Config.TickSeconds, OffsetSeconds = Config.TickOffsetSeconds };
        Sequence = new MarkerSequenceEngine(Config, Clock);
        Sequence.ResetToMarkerOneReady(DateTime.Now);
        Stats.Reset(DateTime.Now);
        _debugLogger.Configure(false, Program.AppVersion);
        _debugLogger.Log("APP_START", DateTime.Now, Sequence, Clock, _tickSync, Stats, notes: $"log={_debugLogger.LogPath}");
        if (Config.TickSyncEnabled && Config.HasTickSyncArea)
        {
            _tickSync.BeginPixelSyncWarmup(5);
            _pixelSyncNoticeUntil = DateTime.Now.AddSeconds(6);
        }

        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Lime;
        TransparencyKey = Color.Lime;
        DoubleBuffered = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

        RegisterHotkeys();

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = $"OSRS Agility Overlay {Program.AppVersion}",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("Edit mode", null, (_, _) => { if (!_editMode) ToggleEditMode(); });
        _tray.ContextMenuStrip.Items.Add("Save", null, (_, _) => SaveConfig());
        _tray.ContextMenuStrip.Items.Add("Reset stats", null, (_, _) => ConfirmResetStats());
        _tray.ContextMenuStrip.Items.Add("Toggle minimal/info", null, (_, _) => ToggleInfoOverlay());
        _tray.ContextMenuStrip.Items.Add("Toggle logging mode", null, (_, _) => ToggleLoggingMode());
        _tray.ContextMenuStrip.Items.Add("Open debug logs", null, (_, _) => OpenDebugLogFolder());
        _tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => QuitApplication());

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
            if (!_editMode) cp.ExStyle |= WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyClickThrough();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _quitting = true;
        for (int i = 1; i <= 12; i++) UnregisterHotKey(Handle, i);
        _tray.Visible = false;
        _tray.Dispose();
        _debugLogger.Log("APP_CLOSE", DateTime.Now, Sequence, Clock, _tickSync, Stats);
        _debugLogger.Dispose();
        _editor?.Close();
        base.OnFormClosed(e);
    }

    private void Tick()
    {
        DateTime now = DateTime.Now;
        _windowService.Update(Config.TargetWindowTitleContains, Config.AnchorToRuneLiteWindow, Bounds);

        bool debugLoggingActive = _debugLogger.Enabled;
        DateTime sampleBefore = debugLoggingActive ? _tickSync.LastSampleAt : DateTime.MinValue;
        DateTime edgeBefore = debugLoggingActive ? _tickSync.LastTrackedBoundaryAt : DateTime.MinValue;
        string statusBefore = debugLoggingActive ? _tickSync.Status : string.Empty;

        RunTickClockSyncIfDue(now);

        if (debugLoggingActive && _tickSync.LastSampleAt != sampleBefore)
            _debugLogger.Log("TICK_SAMPLE", now, Sequence, Clock, _tickSync, Stats);

        if (debugLoggingActive && _tickSync.LastTrackedBoundaryAt != edgeBefore)
            _debugLogger.Log("TICK_EDGE", now, Sequence, Clock, _tickSync, Stats, notes: $"edge_changed_from={edgeBefore:O}");

        if (debugLoggingActive && !string.Equals(_tickSync.Status, statusBefore, StringComparison.Ordinal))
            _debugLogger.Log("TICK_STATUS", now, Sequence, Clock, _tickSync, Stats, notes: $"status_from={statusBefore}");

        if (Sequence.CheckLapCompleted(now))
        {
            Stats.OnLapCompleted(now);
            if (debugLoggingActive)
                _debugLogger.Log("LAP_COMPLETE", now, Sequence, Clock, _tickSync, Stats);
        }

        Stats.TrackLostTime(now, Sequence.PerfectUntil, Config.TickSeconds, _paused || !Sequence.LapRunning, _editMode);

        if (debugLoggingActive && _debugLogger.ShouldLogFrame(now, Sequence))
            _debugLogger.Log("TIMER_FRAME", now, Sequence, Clock, _tickSync, Stats, notes: _paused ? "paused" : string.Empty);

        PollClick(now);
        _editor?.TickRefresh();
        Invalidate();
    }


    private void RunTickClockSyncIfDue(DateTime now)
    {
        _tickSync.RefreshStatus(now, Config, _windowService.TargetRect, Clock);

        if (!Config.TickSyncEnabled || !Config.HasTickSyncArea)
        {
            _nextTickClockSampleAt = DateTime.MinValue;
            return;
        }

        if (_nextTickClockSampleAt != DateTime.MinValue && now < _nextTickClockSampleAt)
            return;

        DateTime edgeBefore = _tickSync.LastTrackedBoundaryAt;
        bool wasSyncing = _tickSync.IsPixelSyncing;

        _tickSync.TrySampleTickClockOnce(now, Config, _windowService.TargetRect, Clock);
        if (_tickSync.IsPixelSyncing)
            _pixelSyncNoticeUntil = now.AddMilliseconds(900);

        bool transitionDetected = _tickSync.LastTrackedBoundaryAt != edgeBefore;
        ScheduleNextTickClockSample(now, transitionDetected || (wasSyncing && !_tickSync.IsPixelSyncing));
    }

    private void ScheduleNextTickClockSample(DateTime now, bool transitionDetected = false)
    {
        if (!Clock.ExternalSyncLocked || _tickSync.IsPixelSyncing)
        {
            // Startup / newly selected pixel / lost pixel: sample quickly until we have
            // seen a few real blue/white transitions.
            _nextTickClockSampleAt = now.AddMilliseconds(20);
            return;
        }

        if (transitionDetected)
        {
            // We just caught the flip.  Skip to just before the next expected tick
            // instead of sampling the rest of this tick.
            DateTime afterWindow = now.AddMilliseconds(25);
            _nextTickClockSampleAt = Clock.NextTickBoundaryAfter(afterWindow).AddMilliseconds(-20);
            if (_nextTickClockSampleAt <= now)
                _nextTickClockSampleAt = now.AddMilliseconds(20);
            return;
        }

        DateTime previous = Clock.TickBoundaryAtOrBefore(now);
        DateTime next = previous.AddSeconds(Math.Max(0.1, Clock.TickSeconds));
        double msAfterPrevious = (now - previous).TotalMilliseconds;
        double msBeforeNext = (next - now).TotalMilliseconds;

        // v54: only watch closely inside the expected flip window:
        // 20ms before the tick to 20ms after it, or until the pixel changes.
        if (msAfterPrevious >= 0 && msAfterPrevious <= 20)
        {
            _nextTickClockSampleAt = now.AddMilliseconds(8);
            return;
        }

        if (msBeforeNext <= 20)
        {
            _nextTickClockSampleAt = now.AddMilliseconds(8);
            return;
        }

        _nextTickClockSampleAt = next.AddMilliseconds(-20);
        if (_nextTickClockSampleAt <= now)
            _nextTickClockSampleAt = now.AddMilliseconds(20);
    }

    private void PollClick(DateTime now)
    {
        if (_paused || _editMode || Config.Markers.Count == 0) return;

        bool leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

        if (leftDown && !_leftWasDown)
        {
            Point mouse = Cursor.Position;
            int markerIndexBefore = Sequence.CurrentIndex;
            Marker marker = Config.Markers[markerIndexBefore];
            MarkerState stateBefore = Sequence.GetState(now);
            DateTime readyAtBefore = Sequence.ReadyAt;
            double msBeforeReady = Math.Max(0, (readyAtBefore - now).TotalMilliseconds);
            bool wouldQueueEarly = stateBefore.VisualState == MarkerVisualState.Waiting
                && !Sequence.IsReadyPrompt
                && msBeforeReady <= Config.EffectiveEarlyClickQueueMs;
            DateTime predictedActionTime = Sequence.ActionTimeForAcceptedClick(now, stateBefore, wouldQueueEarly);

            bool inside = ClickDetector.IsInside(
                mouse,
                marker,
                _windowService.TargetRect,
                Config.AnchorToRuneLiteWindow,
                Config.GlobalClickExtraRadius);

            _debugLogger.Log(
                inside ? "MOUSE_DOWN_INSIDE" : "MOUSE_DOWN_OUTSIDE",
                now,
                Sequence,
                Clock,
                _tickSync,
                Stats,
                mouse: mouse,
                inside: inside,
                actionTime: predictedActionTime,
                markerIndexOverride: markerIndexBefore,
                stateOverride: stateBefore,
                readyAtBefore: readyAtBefore,
                notes: inside ? "before TryClick" : "click ignored by radius");

            if (inside)
            {
                bool startingLap = Sequence.CurrentIndex == 0 && !Sequence.LapRunning;
                bool countClickForStats = Sequence.LapRunning || startingLap;
                ClickResult result = Sequence.TryClick(now);

                int nextIndex = Sequence.CurrentIndex;
                int nextDelay = Config.Markers.Count > 0 ? Config.Markers[nextIndex].DelayTicks : 0;
                DateTime nextReadyAt = Sequence.ReadyAt;
                DateTime actualActionTime = predictedActionTime;

                _debugLogger.Log(
                    result.Accepted ? (result.QueuedEarly ? "CLICK_ACCEPTED_QUEUED" : "CLICK_ACCEPTED") : "CLICK_REJECTED",
                    now,
                    Sequence,
                    Clock,
                    _tickSync,
                    Stats,
                    mouse: mouse,
                    inside: inside,
                    result: result,
                    actionTime: actualActionTime,
                    markerIndexOverride: markerIndexBefore,
                    stateOverride: stateBefore,
                    readyAtBefore: readyAtBefore,
                    nextMarkerIndex: nextIndex,
                    nextDelayTicks: nextDelay,
                    nextReadyAt: nextReadyAt,
                    notes: $"startingLap={startingLap}; countClickForStats={countClickForStats}; action_from_external_sync={Clock.IsExternalSyncFresh(now)}");

                if (result.Accepted)
                {
                    if (startingLap)
                    {
                        Stats.OnLapStarted(Sequence.LapStartedAt);
                        _debugLogger.Log("LAP_START", now, Sequence, Clock, _tickSync, Stats, actionTime: Sequence.LapStartedAt, markerIndexOverride: markerIndexBefore, stateOverride: stateBefore, readyAtBefore: readyAtBefore);
                    }

                    if (countClickForStats)
                    {
                        Stats.OnClick(result);
                        _debugLogger.Log("CLICK_STATS_APPLIED", now, Sequence, Clock, _tickSync, Stats, result: result, actionTime: actualActionTime, markerIndexOverride: markerIndexBefore, stateOverride: stateBefore, readyAtBefore: readyAtBefore);
                    }

                    if (result.Perfect)
                        _markerRenderer.ShowPerfect(MarkerToClientPoint(marker));

                    Stats.ResetLostTicker();
                    RefreshEditor();
                }
            }
        }

        _leftWasDown = leftDown;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        Text = $"OSRS Agility Overlay {Program.AppVersion}";
        DateTime now = DateTime.Now;
        bool minimalPlayMode = Config.MinimalMode && !_editMode;
        if (!minimalPlayMode)
            DrawVersionTag(e.Graphics);
        int timingWidth = 0;

        // Ctrl+I hides the play-session info/stat panels while leaving markers visible.
        // Ctrl+M minimal mode also hides the per-marker tick hit/target panel and
        // large top-left click slider, but keeps lap/info panels unless Ctrl+I hides them.
        if (Config.ShowInfoOverlay || _editMode)
        {
            if (!minimalPlayMode)
                timingWidth = _overlayRenderer.DrawTimingBox(e.Graphics, Sequence, Config, Stats.Stats.LastLapSeconds);

            int infoBoxRight = _overlayRenderer.DrawInfoBox(e.Graphics, timingWidth, Config, Stats, Sequence, now);
            _overlayRenderer.DrawTickSyncStatus(e.Graphics, infoBoxRight, _tickSync, now);
        }

        if (!_editMode && Config.ShowInfoOverlay && !minimalPlayMode)
            _overlayRenderer.DrawTickSlider(e.Graphics, timingWidth, Sequence, Config, now);

        if (Config.Markers.Count == 0)
        {
            _overlayRenderer.DrawTopBanner(e.Graphics, "No markers loaded. Press F5 to add one at your cursor.", 12, 130);
            return;
        }

        if (_editMode)
        {
            DrawEditMode(e.Graphics);
            return;
        }

        if (_paused)
            _overlayRenderer.DrawTopBanner(e.Graphics, "PAUSED - F9 to resume", 12, 130);

        if (Config.TickSyncEnabled && _tickSync.IsPixelSyncing && now <= _pixelSyncNoticeUntil)
            _overlayRenderer.DrawTopBanner(e.Graphics, "SYNCING TICK PIXEL...", 12, 170);

        if (_loggingMode && now <= _loggingNoticeUntil)
            _overlayRenderer.DrawTopBanner(e.Graphics, "LOGGING MODE ENABLED FOR TESTING - PRESS CTRL+L TO DISABLE", 12, 210);

        DrawMarker(e.Graphics, Sequence.CurrentIndex, active: true, showName: false, showTimer: true);
    }

    private void DrawEditMode(Graphics g)
    {
        _overlayRenderer.DrawTopBanner(g, "EDIT MODE | F5 add | drag markers | Delete remove | F11 save | Esc/F10 exit | Ctrl+I info | Ctrl+R stats | Ctrl+Q quit", 12, 130);

        if (!_windowService.TargetRect.IsEmpty)
        {
            Rectangle clientRect = RectangleToClient(_windowService.TargetRect);
            using Pen targetPen = new(Color.FromArgb(120, Color.DeepSkyBlue), 2);
            g.DrawRectangle(targetPen, clientRect);
        }

        if (Config.HasTickSyncArea && !_suppressTickSyncDebugDrawing)
        {
            Rectangle tickScreen = TickSyncService.ResolveScreenArea(Config, _windowService.TargetRect);
            Point pixel = RectangleToClient(tickScreen).Location;

            Rectangle pixelBox = new(pixel.X - 5, pixel.Y - 5, 11, 11);
            using Pen tickPen = new(Color.FromArgb(230, Color.Lime), 2);
            g.DrawRectangle(tickPen, pixelBox);
            g.DrawLine(tickPen, pixel.X - 8, pixel.Y, pixel.X + 8, pixel.Y);
            g.DrawLine(tickPen, pixel.X, pixel.Y - 8, pixel.X, pixel.Y + 8);

            using Font small = new(FontFamily.GenericSansSerif, 8, FontStyle.Bold);
            using SolidBrush bg = new(Color.FromArgb(170, Color.Black));
            using SolidBrush fg = new(Color.Lime);
            string label = $"tick pixel {Config.TickSyncAreaX},{Config.TickSyncAreaY}";
            SizeF labelSize = g.MeasureString(label, small);

            float labelX = pixel.X + 10;
            float labelY = pixel.Y - labelSize.Height - 8;
            Rectangle client = ClientRectangle;
            labelX = Math.Clamp(labelX, 2, Math.Max(2, client.Width - labelSize.Width - 10));
            labelY = Math.Clamp(labelY, 2, Math.Max(2, client.Height - labelSize.Height - 8));

            RectangleF labelRect = new(labelX, labelY, labelSize.Width + 8, labelSize.Height + 4);
            g.FillRectangle(bg, labelRect);
            g.DrawString(label, small, fg, labelRect.X + 4, labelRect.Y + 2);
        }

        using Pen routePen = new(Color.FromArgb(110, Color.White), 2);
        for (int i = 0; i < Config.Markers.Count - 1; i++)
        {
            Point a = MarkerToClientPoint(Config.Markers[i]);
            Point b = MarkerToClientPoint(Config.Markers[i + 1]);
            g.DrawLine(routePen, a, b);
        }

        for (int i = 0; i < Config.Markers.Count; i++)
            DrawMarker(g, i, i == Sequence.CurrentIndex, true, i == Sequence.CurrentIndex);

        DrawCursorCrosshair(g);
    }

    private void DrawMarker(Graphics g, int index, bool active, bool showName, bool showTimer)
    {
        if (index < 0 || index >= Config.Markers.Count) return;

        Marker marker = Config.Markers[index];
        Point point = MarkerToClientPoint(marker);
        DateTime now = DateTime.Now;
        bool minimalPreview = !_editMode && Config.MinimalMode && active && index == Sequence.CurrentIndex && Sequence.ShouldUseMinimalMarkerPreview(now);
        _markerRenderer.DrawMarker(g, marker, point, index, active, showName, showTimer, _editMode, Sequence.LapRunning, Sequence, Config, now, minimalPreview);
    }

    private void DrawCursorCrosshair(Graphics g)
    {
        Point c = PointToClient(Cursor.Position);
        using Pen pen = new(Color.FromArgb(160, Color.White), 1);
        g.DrawLine(pen, c.X - 10, c.Y, c.X + 10, c.Y);
        g.DrawLine(pen, c.X, c.Y - 10, c.X, c.Y + 10);

        using Font font = new(FontFamily.GenericSansSerif, 8, FontStyle.Regular);
        using SolidBrush brush = new(Color.White);
        Point relative = _windowService.TargetRect.IsEmpty ? Cursor.Position : new Point(Cursor.Position.X - _windowService.TargetRect.Left, Cursor.Position.Y - _windowService.TargetRect.Top);
        g.DrawString($"cursor {relative.X},{relative.Y}", font, brush, c.X + 12, c.Y + 8);
    }

    private Point MarkerToClientPoint(Marker marker)
    {
        Point screen = Config.AnchorToRuneLiteWindow && !_windowService.TargetRect.IsEmpty
            ? new Point(_windowService.TargetRect.Left + marker.X, _windowService.TargetRect.Top + marker.Y)
            : new Point(marker.X, marker.Y);

        return PointToClient(screen);
    }

    private Point ClientToMarkerPoint(Point client)
    {
        Point screen = PointToScreen(client);

        if (Config.AnchorToRuneLiteWindow && !_windowService.TargetRect.IsEmpty)
            return new Point(screen.X - _windowService.TargetRect.Left, screen.Y - _windowService.TargetRect.Top);

        return screen;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!_editMode || e.Button != MouseButtons.Left) return;

        for (int i = Config.Markers.Count - 1; i >= 0; i--)
        {
            Point p = MarkerToClientPoint(Config.Markers[i]);
            int r = Config.Markers[i].Radius + Config.GlobalClickExtraRadius;
            double dx = e.X - p.X;
            double dy = e.Y - p.Y;

            if ((dx * dx + dy * dy) <= (r * r))
            {
                _dragIndex = i;
                _dragOffset = new Point(e.X - p.X, e.Y - p.Y);
                SelectMarker(i);
                return;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_editMode || _dragIndex < 0) return;

        Point markerPoint = ClientToMarkerPoint(new Point(e.X - _dragOffset.X, e.Y - _dragOffset.Y));
        Config.Markers[_dragIndex].X = markerPoint.X;
        Config.Markers[_dragIndex].Y = markerPoint.Y;

        RefreshEditor(true);
    }

    protected override void OnMouseUp(MouseEventArgs e) => _dragIndex = -1;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_editMode && e.KeyCode == Keys.Delete)
        {
            DeleteCurrentMarker();
            e.Handled = true;
        }
        else if (_editMode && e.KeyCode == Keys.Escape)
        {
            ToggleEditMode();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }


    private void DrawVersionTag(Graphics g)
    {
        using Font font = new(FontFamily.GenericSansSerif, 8, FontStyle.Bold);
        using SolidBrush bg = new(Color.FromArgb(150, Color.Black));
        using SolidBrush fg = new(Color.White);
        string text = Program.AppVersion;
        SizeF s = g.MeasureString(text, font);
        RectangleF rect = new(Bounds.Width - s.Width - 18, 8, s.Width + 10, s.Height + 6);
        g.FillRectangle(bg, rect);
        g.DrawString(text, font, fg, rect.X + 5, rect.Y + 3);
    }

    private void RegisterHotkeys()
    {
        RegisterHotKey(Handle, 1, 0, Keys.F7);
        RegisterHotKey(Handle, 2, 0, Keys.F8);
        RegisterHotKey(Handle, 3, 0, Keys.F6);
        RegisterHotKey(Handle, 4, 0, Keys.F9);
        RegisterHotKey(Handle, 5, 0, Keys.F10);
        RegisterHotKey(Handle, 6, 0, Keys.F11);
        RegisterHotKey(Handle, 7, 0, Keys.F12);
        RegisterHotKey(Handle, 8, 0, Keys.F5);
        RegisterHotKey(Handle, 9, MOD_CONTROL, Keys.Q);
        RegisterHotKey(Handle, 10, MOD_CONTROL, Keys.R);
        RegisterHotKey(Handle, 11, MOD_CONTROL, Keys.I);
        RegisterHotKey(Handle, 12, MOD_CONTROL, Keys.L);
    }



    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312)
        {
            switch (m.WParam.ToInt32())
            {
                case 1: PrevMarker(); break;
                case 2: NextMarker(); break;
                case 3: ResetLapSequence(); break;
                case 4: _paused = !_paused; break;
                case 5: ToggleEditMode(); break;
                case 6: SaveConfig(); break;
                case 7: ReloadConfig(); break;
                case 8: AddMarkerAtCursorAsNext(); break;
                case 9: QuitApplication(); break;
                case 10: ConfirmResetStats(); break;
                case 11: ToggleInfoOverlay(); break;
                case 12: ToggleLoggingMode(); break;
            }
        }

        base.WndProc(ref m);
    }

    public void SelectMarker(int index)
    {
        SelectMarkerReady(index, resetCombo: true);
    }

    private void SelectMarkerReady(int index, bool resetCombo)
    {
        DateTime now = DateTime.Now;
        Sequence.SelectMarkerReady(index, now);
        Stats.CancelCurrentLap(resetCombo);
        _debugLogger.Log("MANUAL_SELECT_READY", now, Sequence, Clock, _tickSync, Stats, notes: $"selected_marker={index + 1}; resetCombo={resetCombo}");
        RefreshEditor(true);
        Invalidate();
    }

    public void NextMarker()
    {
        SelectMarker((Sequence.CurrentIndex + 1) % Math.Max(1, Config.Markers.Count));
    }

    public void PrevMarker()
    {
        SelectMarker((Sequence.CurrentIndex - 1 + Math.Max(1, Config.Markers.Count)) % Math.Max(1, Config.Markers.Count));
    }

    public void ResetLapSequence()
    {
        DateTime now = DateTime.Now;
        Sequence.ResetToMarkerOneReady(now);
        Stats.CancelCurrentLap(resetCombo: true);
        _debugLogger.Log("RESET_LAP_SEQUENCE", now, Sequence, Clock, _tickSync, Stats);
        RefreshEditor();
    }

    public void RestartCurrentTimer()
    {
        SelectMarkerReady(Sequence.CurrentIndex, resetCombo: true);
    }

    public void AddMarkerAtCursorAsNext()
    {
        Point markerPoint = ClientToMarkerPoint(PointToClient(Cursor.Position));
        int insertIndex = Config.Markers.Count == 0 ? 0 : Sequence.CurrentIndex + 1;
        int delayTicks = Config.Markers.Count > 0 ? Config.Markers[Sequence.CurrentIndex].DelayTicks : 4;
        int radius = Config.Markers.Count > 0 ? Config.Markers[Sequence.CurrentIndex].Radius : 16;

        Config.Markers.Insert(insertIndex, new Marker
        {
            Name = $"Obstacle {insertIndex + 1}",
            X = markerPoint.X,
            Y = markerPoint.Y,
            Radius = radius,
            DelayTicks = delayTicks
        });

        RenameDefaultMarkers();
        Sequence.ApplyConfig(Config, DateTime.Now);
        Sequence.SelectMarkerReady(insertIndex, DateTime.Now);
        SaveConfig();
        RefreshEditor(true);
    }

    public void DeleteCurrentMarker()
    {
        if (Config.Markers.Count == 0) return;

        Config.Markers.RemoveAt(Sequence.CurrentIndex);
        RenameDefaultMarkers();
        Sequence.ApplyConfig(Config, DateTime.Now);
        SaveConfig();
        RefreshEditor(true);
    }

    public void MoveCurrentMarkerUp()
    {
        int i = Sequence.CurrentIndex;
        if (i <= 0 || Config.Markers.Count < 2) return;

        (Config.Markers[i - 1], Config.Markers[i]) = (Config.Markers[i], Config.Markers[i - 1]);
        RenameDefaultMarkers();
        Sequence.ApplyConfig(Config, DateTime.Now);
        Sequence.SelectMarkerReady(i - 1, DateTime.Now);
        SaveConfig();
        RefreshEditor(true);
    }

    public void MoveCurrentMarkerDown()
    {
        int i = Sequence.CurrentIndex;
        if (i >= Config.Markers.Count - 1 || Config.Markers.Count < 2) return;

        (Config.Markers[i + 1], Config.Markers[i]) = (Config.Markers[i], Config.Markers[i + 1]);
        RenameDefaultMarkers();
        Sequence.ApplyConfig(Config, DateTime.Now);
        Sequence.SelectMarkerReady(i + 1, DateTime.Now);
        SaveConfig();
        RefreshEditor(true);
    }

    public void UpdateCurrentMarker(string name, int delayTicks, int radius)
    {
        if (Config.Markers.Count == 0) return;

        Marker marker = Config.Markers[Sequence.CurrentIndex];
        marker.Name = string.IsNullOrWhiteSpace(name) ? $"Obstacle {Sequence.CurrentIndex + 1}" : name;
        marker.DelayTicks = Math.Max(0, delayTicks);
        marker.Radius = Math.Clamp(radius, 4, 200);

        Sequence.ApplyConfigWithoutReset(Config);
        SaveConfig();
        Invalidate();
    }

    public void UpdateGlobalClickExtraRadius(int value)
    {
        Config.GlobalClickExtraRadius = Math.Clamp(value, 0, 100);
        SaveConfig();
        Invalidate();
    }

    public void UpdateWorldLagMs(int value)
    {
        Config.WorldLagMs = Math.Clamp(value, 0, 250);
        SaveConfig();
        RefreshEditor();
        Invalidate();
    }

    public void UpdateQueueSafetyMs(int value)
    {
        Config.QueueSafetyMs = Math.Clamp(value, 0, 100);
        SaveConfig();
        RefreshEditor();
        Invalidate();
    }

    public void SetMinimalMode(bool enabled)
    {
        if (Config.MinimalMode == enabled) return;

        Config.MinimalMode = enabled;
        _editor?.SetStatus(enabled ? "Minimal/info mode on." : "Minimal/info mode off.");
        SaveConfig();
        RefreshEditor();
        Invalidate();
    }

    public void ToggleMinimalMode() => ToggleInfoOverlay();

    public void NudgeTickOffset(double seconds)
    {
        Clock.Nudge(seconds);
        _debugLogger.Log("MANUAL_NUDGE", DateTime.Now, Sequence, Clock, _tickSync, Stats, notes: $"nudge_seconds={seconds:+0.000;-0.000;0.000}");
        _tickSync.ResetLatch();
        _nextTickClockSampleAt = DateTime.MinValue;
        Config.TickOffsetSeconds = Clock.OffsetSeconds;
        Sequence.ApplyConfig(Config, DateTime.Now);
        SaveConfig();
        RefreshEditor();
    }

    public void ResetTickOffset()
    {
        Clock.OffsetSeconds = 0;
        _debugLogger.Log("MANUAL_RESET_TICK_OFFSET", DateTime.Now, Sequence, Clock, _tickSync, Stats);
        _tickSync.ResetLatch();
        _nextTickClockSampleAt = DateTime.MinValue;
        Config.TickOffsetSeconds = 0;
        Sequence.ApplyConfig(Config, DateTime.Now);
        SaveConfig();
        RefreshEditor();
    }

    public void StartTickSyncAreaSelection()
    {
        _editor?.SetStatus("Click a single pixel inside the blue/white tick plugin circle. Esc cancels.");

        using TickAreaSelectionForm selector = new();
        DialogResult result = selector.ShowDialog();

        if (result == DialogResult.OK && selector.SelectedRectangle.Width >= 1 && selector.SelectedRectangle.Height >= 1)
        {
            Rectangle selected = selector.SelectedRectangle;
            _editor?.SetStatus($"Tick pixel captured at {selected.Left},{selected.Top}.");
            Application.DoEvents();
            SetTickSyncAreaFromScreenRect(selected);
        }
        else
        {
            _editor?.SetStatus("Tick pixel selection cancelled.");
            BringOverlayWindowsToFront();
        }
    }

    private void BringOverlayWindowsToFront()
    {
        try
        {
            if (_editMode)
            {
                Show();
                TopMost = true;
                BringToFront();
                Activate();
            }

            if (_editor is { IsDisposed: false })
            {
                _editor.Show();
                _editor.TopMost = true;
                _editor.BringToFront();
                _editor.Activate();
            }
        }
        catch { }
    }

    public void ToggleInfoOverlay()
    {
        bool enterMinimal = !Config.MinimalMode || Config.ShowInfoOverlay;

        Config.MinimalMode = enterMinimal;
        Config.ShowInfoOverlay = !enterMinimal;

        SaveConfig();
        _editor?.SetStatus(enterMinimal
            ? "Minimal mode on and info overlay hidden. Press Ctrl+I to restore."
            : "Minimal mode off and info overlay shown.");
        RefreshEditor();
        Invalidate();
    }


    public void OpenDebugLogFolder()
    {
        try
        {
            string dir = string.IsNullOrWhiteSpace(_debugLogger.LogDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "Logs")
                : _debugLogger.LogDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            _editor?.SetStatus($"Opened debug log folder: {dir}");
        }
        catch (Exception ex)
        {
            _editor?.SetStatus($"Could not open debug log folder: {ex.Message}");
        }
    }

    public void ToggleLoggingMode()
    {
        _loggingMode = !_loggingMode;
        Config.DebugTimingLogEnabled = false;
        _debugLogger.Configure(_loggingMode, Program.AppVersion);

        if (_loggingMode)
        {
            _debugLogger.Log("LOGGING_MODE_ENABLED", DateTime.Now, Sequence, Clock, _tickSync, Stats, notes: $"log={_debugLogger.LogPath}");
            _loggingNoticeUntil = DateTime.Now.AddSeconds(5);
            _editor?.SetStatus("Logging mode enabled for testing. Press Ctrl+L to disable.");
        }
        else
        {
            _editor?.SetStatus("Logging mode disabled.");
        }

        SaveConfig();
        RefreshEditor();
        Invalidate();
    }

    public void SetDebugTimingLogEnabled(bool enabled)
    {
        if (enabled != _loggingMode)
            ToggleLoggingMode();
    }

    public void SetTickSyncEnabled(bool enabled)
    {
        if (enabled && !Config.HasTickSyncArea)
        {
            Config.TickSyncEnabled = false;
            SaveConfig();
            _editor?.SetStatus("No tick pixel selected yet. Click the pixel in the blue/white tick circle.");
            StartTickSyncAreaSelection();
            RefreshEditor();
            return;
        }

        Config.TickSyncEnabled = enabled;
        _tickSync.ResetLatch();
        _nextTickClockSampleAt = DateTime.MinValue;
        if (Config.TickSyncEnabled)
        {
            Clock.ClearExternalSync();
            _tickSync.BeginPixelSyncWarmup(5);
            _pixelSyncNoticeUntil = DateTime.Now.AddSeconds(6);
            _editor?.SetStatus("Tick pixel sync enabled. Syncing tick pixel...");
        }
        else
        {
            Clock.ClearExternalSync();
            _editor?.SetStatus("Tick pixel sync disabled.");
        }

        SaveConfig();
        RefreshEditor();
        Invalidate();
    }

    public void ClearTickSyncArea()
    {
        Config.TickSyncEnabled = false;
        Config.TickSyncAreaX = 0;
        Config.TickSyncAreaY = 0;
        Config.TickSyncAreaWidth = 0;
        Config.TickSyncAreaHeight = 0;
        Clock.ClearExternalSync();
        _tickSync.ResetLatch();
        _nextTickClockSampleAt = DateTime.MinValue;
        SaveConfig();
        RefreshEditor();
        Invalidate();
    }

    private void SetTickSyncAreaFromScreenRect(Rectangle screenRect)
    {
        Rectangle pixel = new(screenRect.Left, screenRect.Top, 1, 1);
        StoreTickSyncArea(pixel);
        _editor?.SetStatus($"Tick pixel saved at {pixel.Left},{pixel.Top}. Syncing tick pixel...");
        BringOverlayWindowsToFront();
    }

    public void SnapCurrentTickSyncArea()
    {
        _editor?.SetStatus("Single-pixel tick mode: click 'Set tick pixel', then click a pixel inside the blue/white tick circle.");
    }

    public void AdjustTickSyncSearchArea(int pixels)
    {
        _editor?.SetStatus("Single-pixel tick mode does not use a search area. Use 'Set tick pixel' and click the pixel again if it needs changing.");
    }


    private bool TrySnapTickSyncArea(Rectangle screenRect, int paddingPixels, out Rectangle snapped, out string detail)
    {
        // When edit mode is showing, our own green/yellow debug rectangles sit directly
        // over the RuneLite tick bar. Suppress those for one paint before taking the
        // screenshot, otherwise the snap code can accidentally lock onto our overlay.
        _suppressTickSyncDebugDrawing = true;
        Invalidate();
        Application.DoEvents();
        Thread.Sleep(50);

        try
        {
            return _tickSync.TrySnapScreenAreaToTickBar(screenRect, paddingPixels, out snapped, out detail);
        }
        finally
        {
            _suppressTickSyncDebugDrawing = false;
            Invalidate();
        }
    }

    private void StoreTickSyncArea(Rectangle screenRect)
    {
        Rectangle targetRect = _windowService.TargetRect;
        bool storeRelative = Config.AnchorToRuneLiteWindow && !targetRect.IsEmpty && targetRect.Contains(screenRect);

        Config.TickSyncAreaRelativeToRuneLite = storeRelative;
        Config.TickSyncAreaX = storeRelative ? screenRect.Left - targetRect.Left : screenRect.Left;
        Config.TickSyncAreaY = storeRelative ? screenRect.Top - targetRect.Top : screenRect.Top;
        Config.TickSyncAreaWidth = screenRect.Width;
        Config.TickSyncAreaHeight = screenRect.Height;
        Config.TickSyncEnabled = true;
        Clock.ClearExternalSync();
        _tickSync.ResetLatch();
        _tickSync.BeginPixelSyncWarmup(5);
        _nextTickClockSampleAt = DateTime.MinValue;
        _pixelSyncNoticeUntil = DateTime.Now.AddSeconds(6);

        _debugLogger.Log("TICK_AREA_STORED", DateTime.Now, Sequence, Clock, _tickSync, Stats,
            notes: $"relative={Config.TickSyncAreaRelativeToRuneLite}; x={Config.TickSyncAreaX}; y={Config.TickSyncAreaY}; w={Config.TickSyncAreaWidth}; h={Config.TickSyncAreaHeight}; screen={screenRect}");

        SaveConfig();
        _windowService.Update(Config.TargetWindowTitleContains, Config.AnchorToRuneLiteWindow, Bounds);
        _nextTickClockSampleAt = DateTime.MinValue;
        RunTickClockSyncIfDue(DateTime.Now);
        RefreshEditor();
        BringOverlayWindowsToFront();
        Invalidate();
    }

    private static Rectangle ClampRectangleToBounds(Rectangle rect, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return rect;
        int width = Math.Min(rect.Width, bounds.Width);
        int height = Math.Min(rect.Height, bounds.Height);
        int left = Math.Clamp(rect.Left, bounds.Left, bounds.Right - width);
        int top = Math.Clamp(rect.Top, bounds.Top, bounds.Bottom - height);
        return new Rectangle(left, top, width, height);
    }

    private void RenameDefaultMarkers()
    {
        for (int i = 0; i < Config.Markers.Count; i++)
        {
            if (Config.Markers[i].Name.StartsWith("Obstacle ", StringComparison.OrdinalIgnoreCase) ||
                Config.Markers[i].Name.Equals("Marker", StringComparison.OrdinalIgnoreCase))
                Config.Markers[i].Name = $"Obstacle {i + 1}";
        }
    }

    public void SaveConfig()
    {
        _configService.Save(Config);
        _debugLogger.Log("SAVE_CONFIG", DateTime.Now, Sequence, Clock, _tickSync, Stats, notes: $"config={_configService.ConfigPath}");
        _editor?.SetStatus("Saved markers.json");
    }

    private void ReloadConfig()
    {
        Config = _configService.Load();
        Clock.TickSeconds = Config.TickSeconds;
        Clock.OffsetSeconds = Config.TickOffsetSeconds;
        Sequence.ApplyConfig(Config, DateTime.Now);
        RefreshEditor(true);
    }

    private void ConfirmResetStats()
    {
        DialogResult result = MessageBox.Show(
            "Reset all stats to 0?\n\nMarkers are not changed.",
            "Reset stats",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result == DialogResult.Yes)
        {
            DateTime now = DateTime.Now;
            Stats.Reset(now);
            Sequence.ResetToMarkerOneReady(now);
            _debugLogger.Log("RESET_STATS", now, Sequence, Clock, _tickSync, Stats);
            RefreshEditor();
        }
    }

    private void ToggleEditMode()
    {
        _editMode = !_editMode;
        ApplyClickThrough();

        if (_editMode)
            ShowEditor();
        else
            _editor?.Hide();

        Invalidate();
    }

    private void ShowEditor()
    {
        if (_editor == null || _editor.IsDisposed)
        {
            _editor = new EditorForm(this, Clock, () => Stats.Stats.SessionStarted ? Stats.Stats.SessionStartedAt : null);
            _editor.StartPosition = FormStartPosition.Manual;
            _editor.Location = new Point(40, 100);
        }

        _editor.RefreshAll(true);
        _editor.Show(this);
        _editor.BringToFront();
    }

    private void RefreshEditor(bool updateFields = false)
    {
        if (_editor != null && !_editor.IsDisposed)
            _editor.RefreshAll(updateFields);
    }


    public void ExitEditMode()
    {
        if (!_editMode) return;
        _editMode = false;
        ApplyClickThrough();
        _editor?.Hide();
        Invalidate();
    }

    public void QuitApplication()
    {
        _quitting = true;
        try
        {
            _editor?.Close();
        }
        catch { }

        Close();
        Application.Exit();
    }

    private void ApplyClickThrough()
    {
        if (!IsHandleCreated) return;

        int style = GetWindowLong(Handle, GWL_EXSTYLE);
        style |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;

        if (_editMode)
            style &= ~WS_EX_TRANSPARENT;
        else
            style |= WS_EX_TRANSPARENT;

        SetWindowLong(Handle, GWL_EXSTYLE, style);
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, Keys vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
