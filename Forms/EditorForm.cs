using OSRSAgilityOverlay.Models;
using OSRSAgilityOverlay.Services;

namespace OSRSAgilityOverlay.Forms;

public sealed class EditorForm : Form
{
    private readonly OverlayForm _overlay;
    private readonly TickSyncPanel _syncPanel;

    private readonly Panel _content = new();
    private readonly ListBox _list = new();
    private readonly TextBox _name = new();
    private readonly NumericUpDown _ticks = new();
    private readonly NumericUpDown _radius = new();
    private readonly NumericUpDown _globalClick = new();
    private readonly NumericUpDown _worldLag = new();
    private readonly NumericUpDown _queueSafety = new();
    private readonly Label _syncOffset = new();
    private readonly Label _autoSync = new();
    private readonly CheckBox _tickSyncEnabled = new();
    private readonly Label _debugLogPath = new();
    private readonly Label _timer = new();
    private readonly Label _status = new();

    private bool _updating;
    private DateTime _statusUntil = DateTime.MinValue;

    public EditorForm(OverlayForm overlay, TickClock clock, Func<DateTime?> sessionStartProvider)
    {
        _overlay = overlay;
        _syncPanel = new TickSyncPanel(clock, sessionStartProvider);

        Text = "Agility Overlay Editor";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        Width = 620;
        Height = 820;
        MinimumSize = new Size(540, 520);

        _content.Dock = DockStyle.Fill;
        _content.AutoScroll = true;
        Controls.Add(_content);

        BuildUi();
        Shown += (_, _) => SnapEditorToBestSize();
    }

    private void BuildUi()
    {
        int left = 14;
        int top = 12;
        int full = 560;
        int half = 270;
        int gap = 12;

        Add(Header("OSRS tick pixel sync", left, top, full)); top += 24;
        _syncPanel.SetBounds(left, top, full, 42); Add(_syncPanel); top += 50;

        _tickSyncEnabled.SetBounds(left, top + 4, 165, 24);
        _tickSyncEnabled.Text = "Use tick pixel sync";
        _tickSyncEnabled.CheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _overlay.SetTickSyncEnabled(_tickSyncEnabled.Checked);
        };
        Add(_tickSyncEnabled);

        Add(Button("Set tick pixel", left + 175, top, 155, (_, _) => _overlay.StartTickSyncAreaSelection()));
        Add(Button("Clear pixel", left + 340, top, 100, (_, _) => _overlay.ClearTickSyncArea()));
        top += 34;

        Add(Button("Toggle minimal/info (Ctrl+I)", left, top, 230, (_, _) => _overlay.ToggleInfoOverlay()));
        Add(Button("Open logs", left + 240, top, 110, (_, _) => _overlay.OpenDebugLogFolder()));
        _debugLogPath.SetBounds(left + 360, top + 5, full - 360, 22);
        _debugLogPath.Text = "Log: off (Ctrl+L)";
        Add(_debugLogPath);
        top += 36;

        _autoSync.SetBounds(left, top, full, 44);
        _autoSync.Text = "Tick sync: no pixel selected";
        Add(_autoSync);
        top += 50;

        Add(Header("Manual fallback offset", left, top, full)); top += 24;
        Add(Button("-0.1s", left, top, 90, (_, _) => _overlay.NudgeTickOffset(-0.1)));
        Add(Button("+0.1s", left + 100, top, 90, (_, _) => _overlay.NudgeTickOffset(0.1)));
        Add(Button("Reset sync", left + 200, top, 110, (_, _) => _overlay.ResetTickOffset()));

        _syncOffset.SetBounds(left + 330, top + 5, 220, 22);
        _syncOffset.Text = "Offset: 0.0s";
        Add(_syncOffset);
        top += 38;


        Add(Header("Markers", left, top, full)); top += 24;
        _list.SetBounds(left, top, full, 205);
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_updating) return;
            if (_list.SelectedIndex >= 0) _overlay.SelectMarker(_list.SelectedIndex);
        };
        Add(_list); top += 215;

        Add(Button("Add at cursor (F5)", left, top, half, (_, _) => _overlay.AddMarkerAtCursorAsNext()));
        Add(Button("Delete selected", left + half + gap, top, half, (_, _) => _overlay.DeleteCurrentMarker()));
        top += 36;

        Add(Button("Move up", left, top, half, (_, _) => _overlay.MoveCurrentMarkerUp()));
        Add(Button("Move down", left + half + gap, top, half, (_, _) => _overlay.MoveCurrentMarkerDown()));
        top += 48;

        Add(Label("Name", left, top, 90));
        _name.SetBounds(left + 100, top - 3, full - 100, 28);
        _name.TextChanged += (_, _) => ApplyFieldChanges();
        Add(_name); top += 36;

        Add(Label("Delay ticks", left, top, 90));
        _ticks.SetBounds(left + 100, top - 3, 90, 28);
        _ticks.Minimum = 0; _ticks.Maximum = 100;
        _ticks.ValueChanged += (_, _) => ApplyFieldChanges();
        Add(_ticks);

        Add(Label("Visual radius", left + 220, top, 100));
        _radius.SetBounds(left + 330, top - 3, 90, 28);
        _radius.Minimum = 4; _radius.Maximum = 200;
        _radius.ValueChanged += (_, _) => ApplyFieldChanges();
        Add(_radius); top += 36;

        Add(Label("Global click +", left, top, 100));
        _globalClick.SetBounds(left + 100, top - 3, 90, 28);
        _globalClick.Minimum = 0; _globalClick.Maximum = 100;
        _globalClick.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _overlay.UpdateGlobalClickExtraRadius((int)_globalClick.Value);
            RefreshListOnly();
        };
        Add(_globalClick);

        Add(Label("World lag ms", left + 220, top, 100));
        _worldLag.SetBounds(left + 330, top - 3, 90, 28);
        _worldLag.Minimum = 0; _worldLag.Maximum = 250;
        _worldLag.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _overlay.UpdateWorldLagMs((int)_worldLag.Value);
        };
        Add(_worldLag); top += 36;

        Add(Label("Queue safety ms", left, top, 120));
        _queueSafety.SetBounds(left + 130, top - 3, 90, 28);
        _queueSafety.Minimum = 0; _queueSafety.Maximum = 100;
        _queueSafety.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _overlay.UpdateQueueSafetyMs((int)_queueSafety.Value);
        };
        Add(_queueSafety);

        Add(new Label
        {
            Text = $"Effective early queue = world lag + safety.",
            Left = left + 240, Top = top, Width = full - 240, Height = 24
        });
        top += 42;


        _timer.SetBounds(left, top, full, 28);
        _timer.Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold);
        Add(_timer); top += 42;

        Add(Button("Restart selected timer", left, top, half, (_, _) => _overlay.RestartCurrentTimer()));
        Add(Button("Previous (F7)", left + half + gap, top, half, (_, _) => _overlay.PrevMarker()));
        top += 36;

        Add(Button("Next (F8)", left, top, half, (_, _) => _overlay.NextMarker()));
        Add(Button("Reset lap (F6)", left + half + gap, top, half, (_, _) => _overlay.ResetLapSequence()));
        top += 42;

        Add(Button("Reset stats (Ctrl+R)", left, top, half, (_, _) => SendKeys.SendWait("^r")));
        Add(Button("Save (F11)", left + half + gap, top, half, (_, _) => { _overlay.SaveConfig(); SetStatus("Saved markers.json"); }));
        top += 36;

        Add(Button("Exit edit (Esc/F10)", left, top, half, (_, _) => SendKeys.SendWait("{F10}")));
        Add(Button("Quit app", left + half + gap, top, half, (_, _) => _overlay.QuitApplication()));
        top += 42;

        _status.SetBounds(left, top, full, 30);
        _status.Text = string.Empty;
        Add(_status); top += 42;

        _content.AutoScrollMinSize = new Size(0, top + 20);
    }

    private void SnapEditorToBestSize()
    {
        Rectangle workArea = Screen.FromControl(this).WorkingArea;

        int desiredWidth = Math.Min(Math.Max(620, _content.AutoScrollMinSize.Width + 42), Math.Max(MinimumSize.Width, workArea.Width - 24));
        int desiredHeight = Math.Min(Math.Max(MinimumSize.Height, _content.AutoScrollMinSize.Height + 70), Math.Max(MinimumSize.Height, workArea.Height - 24));

        Size = new Size(desiredWidth, desiredHeight);

        int left = Math.Clamp(Left, workArea.Left + 8, Math.Max(workArea.Left + 8, workArea.Right - Width - 8));
        int top = Math.Clamp(Top, workArea.Top + 8, Math.Max(workArea.Top + 8, workArea.Bottom - Height - 8));
        Location = new Point(left, top);
    }

    private void Add(Control control) => _content.Controls.Add(control);

    private static Label Header(string text, int left, int top, int width) => new()
    {
        Text = text,
        Left = left,
        Top = top,
        Width = width,
        Height = 22,
        Font = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Bold)
    };

    private static Label Label(string text, int left, int top, int width) => new()
    {
        Text = text,
        Left = left,
        Top = top + 2,
        Width = width,
        Height = 22
    };

    private static Button Button(string text, int left, int top, int width, EventHandler handler)
    {
        Button b = new() { Text = text, Left = left, Top = top, Width = width, Height = 28 };
        b.Click += handler;
        return b;
    }

    public void RefreshAll(bool updateFields = true)
    {
        _updating = true;
        _tickSyncEnabled.Checked = _overlay.Config.TickSyncEnabled;
        RefreshListOnly();

        if (_overlay.Config.Markers.Count > 0)
        {
            int index = Math.Clamp(_overlay.Sequence.CurrentIndex, 0, _overlay.Config.Markers.Count - 1);
            _list.SelectedIndex = index;

            if (updateFields)
            {
                Marker marker = _overlay.Config.Markers[index];
                _name.Text = marker.Name;
                _ticks.Value = Math.Clamp(marker.DelayTicks, (int)_ticks.Minimum, (int)_ticks.Maximum);
                _radius.Value = Math.Clamp(marker.Radius, (int)_radius.Minimum, (int)_radius.Maximum);
                _globalClick.Value = Math.Clamp(_overlay.Config.GlobalClickExtraRadius, (int)_globalClick.Minimum, (int)_globalClick.Maximum);
                _worldLag.Value = Math.Clamp(_overlay.Config.WorldLagMs, (int)_worldLag.Minimum, (int)_worldLag.Maximum);
                _queueSafety.Value = Math.Clamp(_overlay.Config.QueueSafetyMs, (int)_queueSafety.Minimum, (int)_queueSafety.Maximum);
            }
        }

        _updating = false;
        TickRefresh();
    }

    private void ApplyFieldChanges()
    {
        if (_updating || _overlay.Config.Markers.Count == 0) return;
        int caret = _name.SelectionStart;
        int selected = _list.SelectedIndex;

        _overlay.UpdateCurrentMarker(_name.Text, (int)_ticks.Value, (int)_radius.Value);

        RefreshListOnly();
        if (selected >= 0 && selected < _list.Items.Count)
            _list.SelectedIndex = selected;

        if (_name.Focused)
        {
            _name.Focus();
            _name.SelectionStart = Math.Clamp(caret, 0, _name.TextLength);
        }
    }

    private void RefreshListOnly()
    {
        bool wasUpdating = _updating;
        _updating = true;
        _list.BeginUpdate();

        try
        {
            _list.Items.Clear();

            for (int i = 0; i < _overlay.Config.Markers.Count; i++)
            {
                Marker marker = _overlay.Config.Markers[i];
                _list.Items.Add($"{i + 1}. {marker.Name}    {marker.DelayTicks}t    r{marker.Radius}");
            }

            if (_overlay.Config.Markers.Count > 0)
                _list.SelectedIndex = Math.Clamp(_overlay.Sequence.CurrentIndex, 0, _overlay.Config.Markers.Count - 1);
        }
        finally
        {
            _list.EndUpdate();
            _updating = wasUpdating;
        }
    }

    public void TickRefresh()
    {
        _syncPanel.Invalidate();
        _syncOffset.Text = $"Offset: {_overlay.Clock.OffsetSeconds:+0.000;-0.000;0.000}s | Info: {(_overlay.Config.ShowInfoOverlay ? "shown" : "hidden")} | Minimal: {(_overlay.Config.MinimalMode ? "on" : "off")}";
        _debugLogPath.Text = !_overlay.LoggingMode
            ? "Log: off (Ctrl+L)"
            : _overlay.DebugTimingLogActive
                ? $"Log: {Path.GetFileName(_overlay.DebugLogger.LogPath)}"
                : "Log: starting...";

        DateTime now = DateTime.Now;
        _autoSync.Text = $"{_overlay.TickSync.UiText(now)}\r\n{_overlay.TickSync.Detail}";

        var state = _overlay.Sequence.GetState(now);

        if (_overlay.Config.Markers.Count == 0)
        {
            _timer.Text = "No markers";
            return;
        }

        if (state.VisualState == MarkerVisualState.Waiting)
        {
            _timer.Text = $"Selected marker {_overlay.Sequence.CurrentIndex + 1}: WAIT - {state.RemainingTicks} ticks / {_overlay.Sequence.RemainingSeconds(now):0.0}s";
            _timer.ForeColor = Color.DarkOrange;
        }
        else if (_overlay.Sequence.IsReadyPrompt)
        {
            _timer.Text = $"Selected marker {_overlay.Sequence.CurrentIndex + 1}: READY - click to start from here";
            _timer.ForeColor = Color.Green;
        }
        else if (state.VisualState == MarkerVisualState.Perfect)
        {
            _timer.Text = $"Selected marker {_overlay.Sequence.CurrentIndex + 1}: PERFECT - click now";
            _timer.ForeColor = Color.Green;
        }
        else
        {
            _timer.Text = $"Selected marker {_overlay.Sequence.CurrentIndex + 1}: LATE - missed perfect tick";
            _timer.ForeColor = Color.Red;
        }

        if (_statusUntil != DateTime.MinValue && DateTime.Now > _statusUntil)
        {
            _status.Text = string.Empty;
            _statusUntil = DateTime.MinValue;
        }
    }

    public void SetStatus(string text)
    {
        _status.Text = text;
        _statusUntil = DateTime.Now.AddSeconds(3);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (ActiveControl is TextBoxBase && !e.Control && !e.Alt)
        {
            base.OnKeyDown(e);
            return;
        }

        if (e.Control && e.KeyCode == Keys.Q)
        {
            _overlay.QuitApplication();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_overlay.IsDisposed && _overlay.IsQuitting)
        {
            base.OnFormClosing(e);
            return;
        }

        if (!_overlay.IsDisposed && _overlay.EditMode)
        {
            e.Cancel = true;
            Hide();
            _overlay.ExitEditMode();
        }

        base.OnFormClosing(e);
    }
}
