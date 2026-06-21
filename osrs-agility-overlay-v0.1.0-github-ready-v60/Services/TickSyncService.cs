using OSRSAgilityOverlay.Models;

namespace OSRSAgilityOverlay.Services;

public sealed class TickSyncService
{
    private const double RedBoundaryPhase = 0.83;

    private Rectangle? _latchedInnerRect;
    private int? _latchedSampleY;
    private double? _lastObservedPhase;
    private DateTime _lastObservedAt = DateTime.MinValue;

    public bool IsEnabled { get; private set; }
    public bool HasArea { get; private set; }
    public bool IsLocked { get; private set; }
    public string Status { get; private set; } = "OFF";
    public string Detail { get; private set; } = "No tick sync area selected";
    public Rectangle LastScreenArea { get; private set; } = Rectangle.Empty;
    public Rectangle LastLatchedBar { get; private set; } = Rectangle.Empty;
    public double LastPhase { get; private set; }
    public double Confidence { get; private set; }
    public double? LastCorrectionSeconds { get; private set; }
    public string LastCorrectionText { get; private set; } = string.Empty;
    public DateTime LastLockAt { get; private set; } = DateTime.MinValue;
    public DateTime LastAttemptAt { get; private set; } = DateTime.MinValue;
    public DateTime LastSampleAt { get; private set; } = DateTime.MinValue;
    public int? LastLineX { get; private set; }
    public double LastCaptureMilliseconds { get; private set; }
    public double LastScanMilliseconds { get; private set; }
    public DateTime LastTrackedBoundaryAt { get; private set; } = DateTime.MinValue;
    public double LastTrackedBoundaryConfidence { get; private set; }
    public string LastTrackerText { get; private set; } = string.Empty;

    private double? _lastTrackedPhase;
    private double _lastTrackedConfidence;
    private DateTime _lastTrackedSampleAt = DateTime.MinValue;

    private bool? _lastPixelBlueState;
    private DateTime _lastPixelToggleAt = DateTime.MinValue;
    private int _syncWarmupRemaining;
    private int _syncWarmupTotal;
    private int _pixelReadFailCount;

    public bool IsPixelSyncing => _syncWarmupRemaining > 0;

    public void BeginPixelSyncWarmup(int requiredTransitions = 5, bool resetPixelState = true)
    {
        _syncWarmupTotal = Math.Max(1, requiredTransitions);
        _syncWarmupRemaining = _syncWarmupTotal;
        _pixelReadFailCount = 0;

        if (resetPixelState)
        {
            _lastPixelBlueState = null;
            _lastPixelToggleAt = DateTime.MinValue;
        }

        Status = "SYNCING";
        Detail = $"syncing tick pixel 0/{_syncWarmupTotal}";
    }

    public void RefreshStatus(DateTime now, OverlayConfig config, Rectangle runeLiteRect, TickClock clock)
    {
        IsEnabled = config.TickSyncEnabled;
        HasArea = config.HasTickSyncArea;

        if (!IsEnabled)
        {
            SetInactive(clock, "OFF", config.HasTickSyncArea ? "Tick sync disabled" : "No tick sync pixel selected");
            return;
        }

        if (!HasArea)
        {
            SetInactive(clock, "NO PIXEL", "Click 'Set tick sync area' and choose a pixel in the blue/white tick circle");
            return;
        }

        LastScreenArea = ResolveScreenArea(config, runeLiteRect);
        if (!IsUsableScreenRect(LastScreenArea))
        {
            SetInactive(clock, "BAD PIXEL", "Selected pixel is outside the screen");
            return;
        }

        RefreshLockStatus(now, clock, waitingDetail: ArmedDetail());
    }

    public bool HasFreshTrackedBoundary(DateTime now, double tickSeconds)
    {
        if (LastTrackedBoundaryAt == DateTime.MinValue) return false;
        double age = Math.Abs((now - LastTrackedBoundaryAt).TotalSeconds);
        return age <= Math.Max(0.6, TickClock.ExternalSyncFreshSeconds);
    }

    public bool TryTrackTickBoundaryOnce(DateTime now, OverlayConfig config, Rectangle runeLiteRect, TickClock clock)
    {
        return TrySamplePixelTick(now, config, runeLiteRect, clock, readyPrompt: false);
    }

    public bool TryTrackReadyPromptTickBoundaryOnce(DateTime now, OverlayConfig config, Rectangle runeLiteRect, TickClock clock)
    {
        return TrySamplePixelTick(now, config, runeLiteRect, clock, readyPrompt: true);
    }

    public bool TrySampleTickClockOnce(DateTime now, OverlayConfig config, Rectangle runeLiteRect, TickClock clock)
    {
        return TrySamplePixelTick(now, config, runeLiteRect, clock, readyPrompt: false);
    }

    public bool TryCorrectOnce(DateTime now, OverlayConfig config, Rectangle runeLiteRect, TickClock clock)
    {
        return TrySamplePixelTick(now, config, runeLiteRect, clock, readyPrompt: false);
    }

    public string UiText(DateTime now)
    {
        string lockAge = LastLockAt == DateTime.MinValue ? "--" : $"{Math.Max(0, (now - LastLockAt).TotalMilliseconds):0}ms";
        string attemptAge = LastAttemptAt == DateTime.MinValue ? "--" : $"{Math.Max(0, (now - LastAttemptAt).TotalMilliseconds):0}ms";
        return $"Tick clock: {Status} | lock {lockAge} | attempt {attemptAge} | conf {Confidence * 100:0}%";
    }


    public bool TrySnapScreenAreaToTickBar(Rectangle screenRect, int paddingPixels, out Rectangle snappedScreenRect, out string detail)
    {
        int x = screenRect.Left + Math.Max(0, screenRect.Width / 2);
        int y = screenRect.Top + Math.Max(0, screenRect.Height / 2);
        snappedScreenRect = new Rectangle(x, y, 1, 1);
        detail = $"single-pixel mode saved {x},{y}";
        return true;
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

    public void ResetLatch()
    {
        _latchedInnerRect = null;
        _latchedSampleY = null;
        LastLatchedBar = Rectangle.Empty;
        LastLineX = null;
        LastCorrectionSeconds = null;
        LastCorrectionText = string.Empty;
        _lastTrackedPhase = null;
        _lastTrackedConfidence = 0;
        _lastTrackedSampleAt = DateTime.MinValue;
        _lastPixelBlueState = null;
        _lastPixelToggleAt = DateTime.MinValue;
        _pixelReadFailCount = 0;
    }

    public void RecordCorrectionResult(DateTime now, TickSyncCorrectionResult result, TickClock clock)
    {
        LastCorrectionSeconds = result.DidApply ? result.AppliedSeconds : null;
        LastCorrectionText = result.UiAdjustmentText;
        Confidence = result.Confidence;
        IsLocked = result.DidApply && clock.IsExternalSyncFresh(now);
        Status = result.Status;
        Detail = $"{result.Detail}; sample {SampleAgeText(now)}, cap {LastCaptureMilliseconds:0}ms scan {LastScanMilliseconds:0}ms";

        if (result.DidApply)
            LastLockAt = now;
        // v51: failed/ignored visual corrections must not clear the persistent
        // internal tick clock.  They are diagnostic only unless accepted.
    }

    public static Rectangle ResolveScreenArea(OverlayConfig config, Rectangle runeLiteRect)
    {
        int x = config.TickSyncAreaX;
        int y = config.TickSyncAreaY;

        if (config.TickSyncAreaRelativeToRuneLite && !runeLiteRect.IsEmpty)
        {
            x += runeLiteRect.Left;
            y += runeLiteRect.Top;
        }

        return new Rectangle(x, y, Math.Max(1, config.TickSyncAreaWidth), Math.Max(1, config.TickSyncAreaHeight));
    }

    public static bool TryAnalyzeTickBarBitmapForTest(Bitmap bitmap, double? expectedPhase, out double phase, out double confidence, out Rectangle innerRect, out int lineX)
    {
        phase = 0;
        confidence = 0;
        innerRect = Rectangle.Empty;
        lineX = 0;

        if (!TryLocateTickBar(bitmap, out innerRect, out int sampleY))
            return false;

        if (!TryAnalyzeLatchedLine(bitmap, innerRect, sampleY, expectedPhase, out LineDetection detection))
            return false;

        phase = detection.Phase;
        confidence = detection.Confidence;
        lineX = detection.LineX;
        return confidence >= 0.35;
    }

    private void SetInactive(TickClock clock, string status, string detail)
    {
        IsLocked = false;
        Status = status;
        Detail = detail;
        Confidence = 0;
        LastLineX = null;
        LastLockAt = DateTime.MinValue;
        LastCorrectionSeconds = null;
        LastCorrectionText = string.Empty;
        LastSampleAt = DateTime.MinValue;
        _lastObservedPhase = null;
        LastTrackedBoundaryAt = DateTime.MinValue;
        LastTrackedBoundaryConfidence = 0;
        LastTrackerText = string.Empty;
        ResetLatch();
        clock.ClearExternalSync();
    }

    private void RefreshLockStatus(DateTime now, TickClock clock, string waitingDetail)
    {
        IsLocked = clock.IsExternalSyncFresh(now);

        if (IsLocked)
        {
            Status = "LOCKED";
            string adjust = string.IsNullOrWhiteSpace(LastCorrectionText) ? "" : $", {LastCorrectionText}";
            string clockAdjust = string.IsNullOrWhiteSpace(clock.LastSyncAdjustmentText) ? "" : $", clock {clock.LastSyncAdjustmentText}";
            string edge = LastTrackedBoundaryAt == DateTime.MinValue ? "no edge" : $"edge {Math.Max(0, (now - LastTrackedBoundaryAt).TotalMilliseconds):0}ms ago";
            Detail = $"phase {LastPhase:0.00}, line {LastLineX?.ToString() ?? "?"}px, bar {BarDetail()}, {edge}, sample {SampleAgeText(now)}, cap {LastCaptureMilliseconds:0}ms scan {LastScanMilliseconds:0}ms{adjust}{clockAdjust}";
        }
        else
        {
            Status = "ARMED";
            Detail = waitingDetail;
        }
    }

    private string ArmedDetail()
    {
        string latency = LastSampleAt == DateTime.MinValue ? "" : $", last cap {LastCaptureMilliseconds:0}ms scan {LastScanMilliseconds:0}ms";
        string edge = LastTrackedBoundaryAt == DateTime.MinValue ? "" : $", edge {Math.Max(0, (DateTime.Now - LastTrackedBoundaryAt).TotalMilliseconds):0}ms ago";
        return _latchedInnerRect.HasValue
            ? $"Armed clock-only - reusing/extrapolating tick edge for click action time, bar {BarDetail()}{edge}{latency}"
            : "Armed clock-only - will latch tick box and track real tick edge";
    }

    private string BarDetail()
    {
        return LastLatchedBar.IsEmpty ? "?" : $"x{LastLatchedBar.Left}-{LastLatchedBar.Right}";
    }

    private string SampleAgeText(DateTime now)
    {
        return LastSampleAt == DateTime.MinValue ? "--" : $"{Math.Max(0, (now - LastSampleAt).TotalMilliseconds):0}ms ago";
    }

    private static bool IsUsableScreenRect(Rectangle rect)
    {
        if (rect.Width < 1 || rect.Height < 1) return false;
        Rectangle visible = Rectangle.Intersect(SystemInformation.VirtualScreen, rect);
        return visible.Width >= 1 && visible.Height >= 1;
    }

    private bool TrySamplePixelTick(DateTime now, OverlayConfig config, Rectangle runeLiteRect, TickClock clock, bool readyPrompt)
    {
        IsEnabled = config.TickSyncEnabled;
        HasArea = config.HasTickSyncArea;
        LastAttemptAt = now;

        if (!IsEnabled)
        {
            SetInactive(clock, "OFF", config.HasTickSyncArea ? "Tick sync disabled" : "No tick sync pixel selected");
            return false;
        }

        if (!HasArea)
        {
            SetInactive(clock, "NO PIXEL", "Click 'Set tick sync area' and choose a pixel in the blue/white tick circle");
            return false;
        }

        LastScreenArea = ResolveScreenArea(config, runeLiteRect);
        if (!IsUsableScreenRect(LastScreenArea))
        {
            SetInactive(clock, "BAD PIXEL", "Selected pixel is outside the screen");
            return false;
        }

        if (!TryReadTickPixel(LastScreenArea, out bool isBlue, out double confidence, out DateTime sampleAt))
        {
            _pixelReadFailCount++;
            Confidence = 0;

            if (_pixelReadFailCount >= 5)
            {
                clock.ClearExternalSync();
                BeginPixelSyncWarmup(5, resetPixelState: true);
                Status = "SYNCING LOST";
                Detail = "tick pixel lost - syncing again when the blue/white circle is visible";
                return false;
            }

            Status = clock.ExternalSyncLocked ? "PIXEL LOST" : "PIXEL WAIT";
            Detail = clock.ExternalSyncLocked
                ? $"could not read tick pixel ({_pixelReadFailCount}/5), keeping internal 600ms clock"
                : $"could not read tick pixel ({_pixelReadFailCount}/5)";
            return clock.ExternalSyncLocked;
        }

        _pixelReadFailCount = 0;

        Confidence = confidence;
        LastSampleAt = sampleAt;
        LastPhase = clock.Phase(sampleAt);
        LastLineX = isBlue ? 1 : 0;
        LastLatchedBar = LastScreenArea;

        string prefix = readyPrompt ? "PRESTART" : "PIXEL";

        if (!_lastPixelBlueState.HasValue)
        {
            _lastPixelBlueState = isBlue;
            _lastPixelToggleAt = sampleAt;
            Status = readyPrompt ? "PRESTART" : "PIXEL ARM";
            Detail = $"initial tick pixel sampled ({(isBlue ? "blue" : "white")}), waiting for first change; conf {confidence * 100:0}%";
            return clock.ExternalSyncLocked;
        }

        if (_lastPixelBlueState.Value != isBlue)
        {
            _lastPixelBlueState = isBlue;
            _lastPixelToggleAt = sampleAt;
            LastTrackedBoundaryAt = sampleAt;
            LastTrackedBoundaryConfidence = confidence;
            LastTrackerText = $"pixel flip {(isBlue ? "blue" : "white")}";
            IsLocked = true;

            // v55: the pixel flip is the best *verification* signal, but the exact
            // timestamp still depends on when our WinForms timer happened to sample.
            // v54 was feeding every observed flip into SetExternalPhase(), so a
            // consistent 15-40ms sampling delay kept applying -2/-3ms corrections
            // until the clock hit its safety cap.  Lock on the first real flip, then
            // keep the internal 600ms clock as the authority. Later flips only prove
            // the selected pixel is still alternating.
            bool firstClockLock = !clock.ExternalSyncLocked;
            if (firstClockLock)
            {
                clock.SetExternalPhase(sampleAt, 0, confidence);
                LastCorrectionSeconds = Math.Abs(clock.LastAppliedSyncCorrectionSeconds) > 0.000001 ? clock.LastAppliedSyncCorrectionSeconds : null;
                LastCorrectionText = clock.LastSyncAdjustmentText;
                LastLockAt = sampleAt;
            }
            else
            {
                LastCorrectionSeconds = null;
                LastCorrectionText = "pixel verified";
            }

            if (_syncWarmupRemaining > 0)
                _syncWarmupRemaining--;

            if (_syncWarmupRemaining > 0)
            {
                int done = Math.Max(0, _syncWarmupTotal - _syncWarmupRemaining);
                Status = "SYNCING";
                Detail = firstClockLock
                    ? $"syncing tick pixel {done}/{_syncWarmupTotal}; initial lock; changed to {(isBlue ? "blue" : "white")}; conf {confidence * 100:0}%"
                    : $"syncing tick pixel {done}/{_syncWarmupTotal}; verified {(isBlue ? "blue" : "white")}; conf {confidence * 100:0}%";
            }
            else
            {
                Status = readyPrompt ? "PRESTART LOCK" : "PIXEL LOCK";
                string clockText = firstClockLock ? clock.LastSyncAdjustmentText : "pixel verified";
                Detail = $"tick pixel changed to {(isBlue ? "blue" : "white")}; conf {confidence * 100:0}%, cap {LastCaptureMilliseconds:0}ms scan {LastScanMilliseconds:0}ms, clock {clockText}";
            }

            return true;
        }

        IsLocked = clock.ExternalSyncLocked;
        if (_syncWarmupRemaining > 0)
        {
            int done = Math.Max(0, _syncWarmupTotal - _syncWarmupRemaining);
            Status = "SYNCING";
            Detail = $"syncing tick pixel {done}/{_syncWarmupTotal}; waiting for next colour change; current {(isBlue ? "blue" : "white")}; conf {confidence * 100:0}%";
        }
        else
        {
            Status = readyPrompt ? "PRESTART HOLD" : "PIXEL HOLD";
            Detail = $"tick pixel unchanged ({(isBlue ? "blue" : "white")}); internal tick clock running, conf {confidence * 100:0}%";
        }
        return clock.ExternalSyncLocked;
    }

    private bool TryReadTickPixel(Rectangle screenRect, out bool isBlue, out double confidence, out DateTime sampleAt)
    {
        isBlue = false;
        confidence = 0;
        sampleAt = DateTime.MinValue;
        LastCaptureMilliseconds = 0;
        LastScanMilliseconds = 0;

        try
        {
            DateTime captureStart = DateTime.Now;
            using Bitmap bitmap = new(1, 1);
            using (Graphics g = Graphics.FromImage(bitmap))
                g.CopyFromScreen(screenRect.Left, screenRect.Top, 0, 0, new Size(1, 1), CopyPixelOperation.SourceCopy);
            DateTime captureEnd = DateTime.Now;

            LastCaptureMilliseconds = Math.Max(0, (captureEnd - captureStart).TotalMilliseconds);
            sampleAt = captureStart.AddTicks((captureEnd - captureStart).Ticks / 2);

            DateTime scanStart = DateTime.Now;
            Color c = bitmap.GetPixel(0, 0);

            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            int spread = max - min;
            int maxOther = Math.Max(c.R, c.G);
            int blueAdvantage = c.B - maxOther;

            // v56: RuneLite at 10% opacity makes the plugin colours very dark.
            // Treat the tick pixel as blue by colour dominance, not by full-brightness
            // channel values.  A 10%-opacity blue can be roughly RGB 0,0,25.
            isBlue =
                c.B >= 8 &&
                blueAdvantage >= 5 &&
                c.B >= Math.Max(8, (int)(maxOther * 1.12));

            // The "white" tick state can become a dark grey at 10% opacity, so accept
            // low-brightness neutral pixels too.  This is safe because the selected
            // pixel should be inside the blue/white plugin circle, not random scenery.
            bool isWhiteOrGrey =
                !isBlue &&
                max >= 8 &&
                spread <= Math.Max(10, (int)(max * 0.40));

            if (!isBlue && !isWhiteOrGrey)
            {
                LastScanMilliseconds = Math.Max(0, (DateTime.Now - scanStart).TotalMilliseconds);
                LastTrackerText = $"pixel unreadable rgb({c.R},{c.G},{c.B})";
                return false;
            }

            confidence = isBlue
                ? Math.Clamp(0.45 + (blueAdvantage / 40.0) + (c.B / 180.0), 0.50, 0.99)
                : Math.Clamp(0.50 + ((max - spread) / 160.0), 0.55, 0.97);

            LastTrackerText = $"pixel {(isBlue ? "blue" : "white")} rgb({c.R},{c.G},{c.B})";
            LastScanMilliseconds = Math.Max(0, (DateTime.Now - scanStart).TotalMilliseconds);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryDetectPhase(Rectangle screenRect, double tickSeconds, out double phase, out double confidence, out int lineX, out DateTime sampleAt)
    {
        phase = 0;
        confidence = 0;
        lineX = 0;
        sampleAt = DateTime.MinValue;
        LastCaptureMilliseconds = 0;
        LastScanMilliseconds = 0;

        try
        {
            DateTime captureStart = DateTime.Now;
            using Bitmap bitmap = new(screenRect.Width, screenRect.Height);
            using (Graphics g = Graphics.FromImage(bitmap))
                g.CopyFromScreen(screenRect.Left, screenRect.Top, 0, 0, screenRect.Size, CopyPixelOperation.SourceCopy);
            DateTime captureEnd = DateTime.Now;

            LastCaptureMilliseconds = Math.Max(0, (captureEnd - captureStart).TotalMilliseconds);
            sampleAt = captureStart.AddTicks((captureEnd - captureStart).Ticks / 2);

            DateTime scanStart = DateTime.Now;
            Rectangle innerRect;
            int sampleY;

            if (_latchedInnerRect.HasValue && _latchedSampleY.HasValue && IsValidLatchedRect(bitmap, _latchedInnerRect.Value, _latchedSampleY.Value))
            {
                innerRect = _latchedInnerRect.Value;
                sampleY = _latchedSampleY.Value;
            }
            else
            {
                if (!TryLocateTickBar(bitmap, out innerRect, out sampleY))
                {
                    ResetLatch();
                    LastScanMilliseconds = Math.Max(0, (DateTime.Now - scanStart).TotalMilliseconds);
                    return false;
                }

                _latchedInnerRect = innerRect;
                _latchedSampleY = sampleY;
            }

            LastLatchedBar = innerRect;

            double? expectedPhase = null;
            if (_lastObservedPhase.HasValue && _lastObservedAt != DateTime.MinValue)
            {
                double elapsedTicks = (sampleAt - _lastObservedAt).TotalSeconds / Math.Max(0.1, tickSeconds);
                expectedPhase = WrapPhase(_lastObservedPhase.Value + elapsedTicks);
            }

            bool detected = TryAnalyzeLatchedLine(bitmap, innerRect, sampleY, expectedPhase, out LineDetection detection);
            LastScanMilliseconds = Math.Max(0, (DateTime.Now - scanStart).TotalMilliseconds);
            if (!detected)
                return false;

            phase = detection.Phase;
            confidence = detection.Confidence;
            lineX = detection.LineX;
            return confidence >= 0.35;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLocateTickBar(Bitmap bitmap, out Rectangle innerRect, out int sampleY)
    {
        innerRect = Rectangle.Empty;
        sampleY = 0;

        if (!TryLocateTickBarCore(bitmap, out TickBarCandidate candidate))
            return false;

        innerRect = candidate.InnerRect;
        sampleY = candidate.SampleY;
        return true;
    }

    private static bool TryLocateTickBarOuter(Bitmap bitmap, out Rectangle innerRect, out Rectangle outerRect, out int sampleY, out string detail)
    {
        innerRect = Rectangle.Empty;
        outerRect = Rectangle.Empty;
        sampleY = 0;
        detail = string.Empty;

        if (!TryLocateTickBarCore(bitmap, out TickBarCandidate candidate))
        {
            detail = "could not find a full green/red tick bar with black border";
            return false;
        }

        innerRect = candidate.InnerRect;
        outerRect = candidate.OuterRect;
        sampleY = candidate.SampleY;
        detail = $"outer {outerRect.Width}x{outerRect.Height}, inner {innerRect.Width}x{innerRect.Height}, line y {sampleY}px";
        return true;
    }

    private static bool TryLocateTickBarCore(Bitmap bitmap, out TickBarCandidate candidate)
    {
        candidate = default;

        int width = bitmap.Width;
        int height = bitmap.Height;
        if (width < 40 || height < 10) return false;

        List<RowColorRun> rows = new();
        for (int y = 0; y < height; y++)
        {
            if (TryFindBestTickFillRunOnRow(bitmap, y, out RowColorRun run))
                rows.Add(run);
        }

        if (rows.Count == 0) return false;

        double bestScore = 0;
        TickBarCandidate best = default;
        List<RowColorRun> group = new();

        void EvaluateGroup()
        {
            if (group.Count < 6) return;

            int top = group[0].Y;
            int bottom = group[^1].Y;
            int heightPx = bottom - top + 1;
            if (heightPx < 6) return;

            int left = Median(group.Select(r => r.Start).ToList());
            int right = Median(group.Select(r => r.End).ToList());
            int innerWidth = right - left + 1;
            if (innerWidth < Math.Max(50, width / 5)) return;

            double avgFillRatio = group.Average(r => r.FillRatio);
            int green = group.Sum(r => r.GreenPixels);
            int red = group.Sum(r => r.RedPixels);
            double colourBalance = red > 0 && green > 0 ? 1.0 : 0.72;

            Rectangle rawInner = new(
                Math.Clamp(left, 0, width - 1),
                Math.Clamp(top, 0, height - 1),
                Math.Max(1, Math.Min(width - left, innerWidth)),
                Math.Max(1, Math.Min(height - top, heightPx)));

            Rectangle outer = FindOuterBorderAroundInner(bitmap, rawInner, out double borderScore);

            // Reject very thin overlay/debug lines.  The RuneLite tick box has real fill height.
            if (rawInner.Height < 8 && borderScore < 0.75) return;

            double widthScore = rawInner.Width;
            double heightScore = Math.Min(32, rawInner.Height);
            double score = widthScore * heightScore * Math.Max(0.35, avgFillRatio) * colourBalance * Math.Max(0.45, borderScore);

            // Prefer candidates that look like a bordered horizontal bar, not floating text/labels.
            if (outer.Width >= rawInner.Width + 2 && outer.Height >= rawInner.Height + 2)
                score *= 1.25;

            if (score <= bestScore) return;

            int sample = Math.Clamp(rawInner.Top + rawInner.Height / 2, rawInner.Top, rawInner.Bottom - 1);
            bestScore = score;
            best = new TickBarCandidate(rawInner, outer, sample, score);
        }

        foreach (RowColorRun row in rows)
        {
            if (group.Count == 0)
            {
                group.Add(row);
                continue;
            }

            RowColorRun last = group[^1];
            bool consecutive = row.Y <= last.Y + 1;
            bool similarWidth = Math.Abs(row.Width - last.Width) <= Math.Max(12, Math.Min(row.Width, last.Width) / 4);
            bool enoughOverlap = HorizontalOverlap(row.Start, row.End, last.Start, last.End) >= Math.Min(row.Width, last.Width) * 0.60;

            if (consecutive && (similarWidth || enoughOverlap))
            {
                group.Add(row);
            }
            else
            {
                EvaluateGroup();
                group.Clear();
                group.Add(row);
            }
        }

        EvaluateGroup();

        if (bestScore <= 0) return false;
        candidate = best;
        return true;
    }

    private static bool TryFindBestTickFillRunOnRow(Bitmap bitmap, int y, out RowColorRun bestRun)
    {
        bestRun = default;

        int width = bitmap.Width;
        int minWidth = Math.Max(35, width / 6);
        int bestScore = 0;

        int x = 0;
        while (x < width)
        {
            while (x < width && !IsTickFill(bitmap.GetPixel(x, y)))
                x++;

            if (x >= width) break;

            int start = x;
            int end = x;
            int green = 0;
            int red = 0;
            int tickPixels = 0;
            int gap = 0;

            while (x < width)
            {
                Color c = bitmap.GetPixel(x, y);
                if (IsTickFill(c))
                {
                    if (IsGreenFill(c)) green++;
                    if (IsRedFill(c)) red++;
                    tickPixels++;
                    end = x;
                    gap = 0;
                    x++;
                    continue;
                }

                // Merge across the moving tick line and the green/red split, but do not
                // bridge random scenery or separate UI elements.
                if (gap < 8)
                {
                    gap++;
                    x++;
                    continue;
                }

                break;
            }

            int runWidth = end - start + 1;
            if (runWidth >= minWidth)
            {
                double fillRatio = tickPixels / Math.Max(1.0, runWidth);
                if (fillRatio >= 0.45)
                {
                    int score = (int)(runWidth * fillRatio);
                    if (green > 0 && red > 0) score += runWidth / 3;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestRun = new RowColorRun(y, start, end, green, red, tickPixels, fillRatio);
                    }
                }
            }
        }

        return bestScore > 0;
    }

    private static Rectangle FindOuterBorderAroundInner(Bitmap bitmap, Rectangle inner, out double borderScore)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int search = Math.Clamp(Math.Max(6, inner.Height / 2), 6, 16);

        int top = FindBestDarkRow(bitmap, inner.Left, inner.Right - 1, inner.Top - search, inner.Top - 1, reverse: true, out double topScore);
        int bottom = FindBestDarkRow(bitmap, inner.Left, inner.Right - 1, inner.Bottom, inner.Bottom + search, reverse: false, out double bottomScore);

        if (top < 0) top = Math.Max(0, inner.Top - 2);
        if (bottom < 0) bottom = Math.Min(height - 1, inner.Bottom + 1);

        int left = FindBestDarkColumn(bitmap, top, bottom, inner.Left - search, inner.Left - 1, reverse: true, out double leftScore);
        int right = FindBestDarkColumn(bitmap, top, bottom, inner.Right, inner.Right + search, reverse: false, out double rightScore);

        if (left < 0) left = Math.Max(0, inner.Left - 2);
        if (right < 0) right = Math.Min(width - 1, inner.Right + 1);

        borderScore = Math.Clamp((topScore + bottomScore + leftScore + rightScore) / 4.0, 0, 1);

        int outerLeft = Math.Clamp(Math.Min(left, inner.Left), 0, width - 1);
        int outerTop = Math.Clamp(Math.Min(top, inner.Top), 0, height - 1);
        int outerRight = Math.Clamp(Math.Max(right, inner.Right - 1), outerLeft, width - 1);
        int outerBottom = Math.Clamp(Math.Max(bottom, inner.Bottom - 1), outerTop, height - 1);

        return new Rectangle(outerLeft, outerTop, outerRight - outerLeft + 1, outerBottom - outerTop + 1);
    }

    private static int FindBestDarkRow(Bitmap bitmap, int xStart, int xEnd, int yStart, int yEnd, bool reverse, out double bestRatio)
    {
        bestRatio = 0;
        int bestY = -1;
        int minY = Math.Clamp(Math.Min(yStart, yEnd), 0, bitmap.Height - 1);
        int maxY = Math.Clamp(Math.Max(yStart, yEnd), 0, bitmap.Height - 1);
        if (maxY < minY) return -1;

        IEnumerable<int> ys = reverse ? Enumerable.Range(minY, maxY - minY + 1).Reverse() : Enumerable.Range(minY, maxY - minY + 1);
        double bestScore = double.NegativeInfinity;
        int order = 0;
        foreach (int y in ys)
        {
            double ratio = DarkRatioOnRow(bitmap, y, xStart - 2, xEnd + 2);
            double score = ratio - (order * 0.015);
            if (score > bestScore)
            {
                bestScore = score;
                bestRatio = ratio;
                bestY = y;
            }
            order++;
        }

        return bestRatio >= 0.55 ? bestY : -1;
    }

    private static int FindBestDarkColumn(Bitmap bitmap, int yStart, int yEnd, int xStart, int xEnd, bool reverse, out double bestRatio)
    {
        bestRatio = 0;
        int bestX = -1;
        int minX = Math.Clamp(Math.Min(xStart, xEnd), 0, bitmap.Width - 1);
        int maxX = Math.Clamp(Math.Max(xStart, xEnd), 0, bitmap.Width - 1);
        if (maxX < minX) return -1;

        IEnumerable<int> xs = reverse ? Enumerable.Range(minX, maxX - minX + 1).Reverse() : Enumerable.Range(minX, maxX - minX + 1);
        double bestScore = double.NegativeInfinity;
        int order = 0;
        foreach (int x in xs)
        {
            double ratio = DarkRatioOnColumn(bitmap, x, yStart, yEnd);
            double score = ratio - (order * 0.015);
            if (score > bestScore)
            {
                bestScore = score;
                bestRatio = ratio;
                bestX = x;
            }
            order++;
        }

        return bestRatio >= 0.45 ? bestX : -1;
    }

    private static double DarkRatioOnRow(Bitmap bitmap, int y, int xStart, int xEnd)
    {
        int left = Math.Clamp(xStart, 0, bitmap.Width - 1);
        int right = Math.Clamp(xEnd, left, bitmap.Width - 1);
        int dark = 0;
        int total = 0;
        for (int x = left; x <= right; x++)
        {
            total++;
            if (IsDark(bitmap.GetPixel(x, y))) dark++;
        }
        return dark / Math.Max(1.0, total);
    }

    private static double DarkRatioOnColumn(Bitmap bitmap, int x, int yStart, int yEnd)
    {
        int top = Math.Clamp(yStart, 0, bitmap.Height - 1);
        int bottom = Math.Clamp(yEnd, top, bitmap.Height - 1);
        int dark = 0;
        int total = 0;
        for (int y = top; y <= bottom; y++)
        {
            total++;
            if (IsDark(bitmap.GetPixel(x, y))) dark++;
        }
        return dark / Math.Max(1.0, total);
    }

    private static int HorizontalOverlap(int aStart, int aEnd, int bStart, int bEnd)
    {
        int left = Math.Max(aStart, bStart);
        int right = Math.Min(aEnd, bEnd);
        return Math.Max(0, right - left + 1);
    }

    private static int Median(List<int> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        return values[values.Count / 2];
    }

    private static bool IsTickFill(Color c) => IsGreenFill(c) || IsRedFill(c);

    private static bool IsGreenFill(Color c)
    {
        // RuneLite at very low window opacity still preserves colour dominance,
        // but the absolute channel value can be as low as ~25 instead of 255.
        // Use relative dominance rather than a high brightness threshold.
        int strongestOther = Math.Max(c.R, c.B);
        return c.G >= 12 && c.G >= strongestOther + 5 && c.G >= strongestOther * 1.45;
    }

    private static bool IsRedFill(Color c)
    {
        // Same low-opacity handling as green.  This keeps the darkened red segment
        // usable while avoiding grey/black scenery and text.
        int strongestOther = Math.Max(c.G, c.B);
        return c.R >= 12 && c.R >= strongestOther + 5 && c.R >= strongestOther * 1.45;
    }

    private static bool IsValidLatchedRect(Bitmap bitmap, Rectangle innerRect, int sampleY)
    {
        if (innerRect.Width < 20) return false;
        if (innerRect.Left < 0 || innerRect.Right > bitmap.Width) return false;
        if (sampleY < 0 || sampleY >= bitmap.Height) return false;

        int tickFill = 0;
        for (int x = innerRect.Left; x < innerRect.Right; x++)
        {
            if (IsTickFill(bitmap.GetPixel(x, sampleY)))
                tickFill++;
        }

        double ratio = tickFill / Math.Max(1.0, innerRect.Width);
        return ratio >= 0.45;
    }

    private static bool TryAnalyzeLatchedLine(Bitmap bitmap, Rectangle innerRect, int sampleY, double? expectedPhase, out LineDetection detection)
    {
        detection = default;

        int xStart = Math.Clamp(innerRect.Left, 0, bitmap.Width - 1);
        int xEnd = Math.Clamp(innerRect.Right - 1, xStart, bitmap.Width - 1);
        int width = xEnd - xStart + 1;
        if (width < 20) return false;

        List<DarkRun> runs = FindDarkRuns(bitmap, sampleY, xStart, xEnd);
        if (runs.Count == 0) return false;

        double bestScore = 0;
        LineDetection best = default;
        int maxMarkerWidth = Math.Max(8, width / 18);

        foreach (DarkRun run in runs)
        {
            int centerX = (run.Start + run.End) / 2;
            double phase = (centerX - xStart) / Math.Max(1.0, width - 1.0);

            if (phase < 0.015 || phase > 0.985) continue;
            if (run.Width > maxMarkerWidth) continue;

            double score = 1.0;

            if (run.Width > 4)
                score *= Math.Max(0.45, 1.0 - ((run.Width - 4) / (double)Math.Max(4, maxMarkerWidth)));

            double boundaryDistance = Math.Abs(phase - RedBoundaryPhase);
            if (!expectedPhase.HasValue && boundaryDistance < 0.055)
                score *= 0.25;

            if (expectedPhase.HasValue)
            {
                double distance = CircularDistance(phase, expectedPhase.Value);
                double match = 1.0 - Math.Min(0.85, distance / 0.22);
                score *= match;
            }
            else
            {
                score *= 1.0 - Math.Min(0.25, phase * 0.18);
            }

            if (score > bestScore)
            {
                double confidence = Math.Clamp(0.20 + (score * 0.78), 0, 0.98);
                bestScore = score;
                best = new LineDetection(centerX, phase, confidence, run.Width);
            }
        }

        if (bestScore <= 0) return false;
        detection = best;
        return true;
    }

    private static List<DarkRun> FindDarkRuns(Bitmap bitmap, int y, int xStart, int xEnd)
    {
        List<DarkRun> runs = new();
        int x = Math.Clamp(xStart, 0, bitmap.Width - 1);
        int endLimit = Math.Clamp(xEnd, x, bitmap.Width - 1);

        while (x <= endLimit)
        {
            if (!IsDark(bitmap.GetPixel(x, y)))
            {
                x++;
                continue;
            }

            int start = x;
            while (x <= endLimit && IsDark(bitmap.GetPixel(x, y)))
                x++;

            runs.Add(new DarkRun(start, x - 1));
        }

        return runs;
    }

    private static double NonDarkRatio(Bitmap bitmap, int y, int xStart, int xEnd)
    {
        if (xEnd < xStart) return 0;

        int total = 0;
        int nonDark = 0;
        for (int x = xStart; x <= xEnd; x++)
        {
            total++;
            if (!IsDark(bitmap.GetPixel(x, y)))
                nonDark++;
        }

        return nonDark / Math.Max(1.0, total);
    }

    private static bool IsDark(Color c)
    {
        // Do not classify darkened low-opacity green/red fill as black.  At 10%
        // opacity the fill can be almost black by brightness, so darkness must be
        // low-saturation/black-like rather than simply "low RGB".
        if (IsGreenFill(c) || IsRedFill(c)) return false;

        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        int min = Math.Min(c.R, Math.Min(c.G, c.B));
        int spread = max - min;

        return max <= 42 && spread <= 18;
    }

    private static double CircularDistance(double a, double b)
    {
        double distance = Math.Abs(a - b);
        return Math.Min(distance, 1.0 - distance);
    }

    private static double WrapPhase(double value)
    {
        value %= 1.0;
        if (value < 0) value += 1.0;
        return value;
    }

    private readonly record struct TickBarCandidate(Rectangle InnerRect, Rectangle OuterRect, int SampleY, double Score);

    private readonly record struct RowColorRun(int Y, int Start, int End, int GreenPixels, int RedPixels, int TickPixels, double FillRatio)
    {
        public int Width => End - Start + 1;
    }

    private readonly record struct DarkRun(int Start, int End)
    {
        public int Width => End - Start + 1;
    }

    private readonly record struct LineDetection(int LineX, double Phase, double Confidence, int Width);
}
