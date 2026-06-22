using OSRSAgilityOverlay.Models;

namespace OSRSAgilityOverlay.Services;

public sealed class MarkerSequenceEngine
{
    public const int ClickTimingTicksBeforeReady = 2;
    public const int ClickTimingTicksAfterReady = 3;
    public const double MinimumTickSyncCorrectionConfidence = 0.70;
    public const double MaximumAcceptedTickSyncCorrectionTicks = 0.14;
    public const double MaximumAppliedTickSyncCorrectionTicks = 0.08333333333333333;
    public const int TickSyncTrackingTicksBeforeReady = 4;
    public const double MinimumTickSyncEdgeConfidence = 0.80;
    public const double MaximumAcceptedEdgeCorrectionTicks = 0.49;

    private readonly Dictionary<int, int?> _clickedTicks = new();
    private readonly Dictionary<int, int> _targetTicks = new();

    public OverlayConfig Config { get; private set; }
    public TickClock Clock { get; }
    public int CurrentIndex { get; private set; }
    public bool LapRunning { get; private set; }
    public DateTime LapStartedAt { get; private set; }
    public DateTime ReadyAt { get; private set; }
    public DateTime PerfectUntil { get; private set; }
    public bool IsReadyPrompt { get; private set; }
    public double? LastClickSliderPosition { get; private set; }
    public DateTime LastClickSliderUntil { get; private set; } = DateTime.MinValue;
    public bool TickSyncAttemptedForCurrentMarker { get; private set; }

    public IReadOnlyDictionary<int, int?> ClickedTicks => _clickedTicks;
    public IReadOnlyDictionary<int, int> TargetTicks => _targetTicks;

    private double ClickTimingWindowTicks => ClickTimingTicksBeforeReady + ClickTimingTicksAfterReady;

    public MarkerSequenceEngine(OverlayConfig config, TickClock clock)
    {
        Config = config;
        Clock = clock;
        ApplyConfig(config, DateTime.Now);
    }

    public void ApplyConfig(OverlayConfig config, DateTime now)
    {
        ApplyConfigWithoutReset(config);
        ResetToMarkerOneReady(now);
    }

    public void ApplyConfigWithoutReset(OverlayConfig config)
    {
        Config = config;
        Clock.TickSeconds = Math.Max(0.1, config.TickSeconds);
        Clock.OffsetSeconds = config.TickOffsetSeconds;
        CurrentIndex = Math.Clamp(CurrentIndex, 0, Math.Max(0, config.Markers.Count - 1));
        ResetTimingDebug();
    }

    public void ResetToMarkerOneReady(DateTime now)
    {
        CurrentIndex = 0;
        LapRunning = false;
        LapStartedAt = now;
        ResetTimingDebug();
        ArmReadyPrompt(now);
    }

    public int TargetTickForMarker(int markerIndex)
    {
        if (markerIndex < 0 || markerIndex >= Config.Markers.Count) return 0;

        if (markerIndex == 0 && LapRunning)
            return Config.BestPossibleTicks;

        int target = 0;
        for (int i = 1; i <= markerIndex; i++)
            target += Math.Max(0, Config.Markers[i].DelayTicks);

        return target;
    }

    public MarkerState GetState(DateTime now)
    {
        if (Config.Markers.Count == 0)
            return new MarkerState(0, MarkerVisualState.Perfect, now, now.AddSeconds(Clock.TickSeconds), 0, 0, 0);

        if (IsReadyPrompt)
            return new MarkerState(CurrentIndex, MarkerVisualState.Perfect, ReadyAt, PerfectUntil, 0, 0, 1);

        MarkerVisualState visual;
        int remaining = 0;
        int late = 0;
        double progress = 0;

        if (now < ReadyAt)
        {
            visual = MarkerVisualState.Waiting;
            remaining = Math.Max(1, (int)Math.Ceiling((ReadyAt - now).TotalSeconds / Clock.TickSeconds));
        }
        else if (now < PerfectUntil)
        {
            visual = MarkerVisualState.Perfect;
            progress = Math.Clamp((now - ReadyAt).TotalSeconds / Clock.TickSeconds, 0, 1);
        }
        else
        {
            visual = MarkerVisualState.Late;
            late = Math.Max(1, (int)Math.Floor((now - ReadyAt).TotalSeconds / Clock.TickSeconds));
        }

        return new MarkerState(CurrentIndex, visual, ReadyAt, PerfectUntil, remaining, late, progress);
    }

    public ClickResult TryClick(DateTime now)
    {
        if (Config.Markers.Count == 0)
            return new ClickResult(false, false, false, 0, 0, 0, 0);

        CurrentIndex = Math.Clamp(CurrentIndex, 0, Config.Markers.Count - 1);

        MarkerState state = GetState(now);
        double msBeforeReady = Math.Max(0, (ReadyAt - now).TotalMilliseconds);
        int effectiveQueueMs = Math.Max(0, Config.EffectiveEarlyClickQueueMs);
        bool queuedEarly = state.VisualState == MarkerVisualState.Waiting
            && !IsReadyPrompt
            && msBeforeReady <= effectiveQueueMs;

        if (state.VisualState == MarkerVisualState.Waiting && !queuedEarly)
            return new ClickResult(false, false, false, CurrentIndex, 0, 0, 0, false, msBeforeReady);

        int clickedMarker = CurrentIndex;
        bool perfect = state.VisualState == MarkerVisualState.Perfect || queuedEarly;

        DateTime actionTime = ActionTimeForAcceptedClick(now, state, queuedEarly);

        if (clickedMarker == 0 && !LapRunning)
        {
            LapRunning = true;
            LapStartedAt = actionTime;
            ResetTimingDebug();
        }

        int targetTick = clickedMarker == 0
            ? 0
            : (_targetTicks.TryGetValue(clickedMarker, out int stored) ? stored : TargetTickForMarker(clickedMarker));

        int hitTick = perfect
            ? targetTick
            : targetTick + state.LateTicks;

        _clickedTicks[clickedMarker] = hitTick;
        _targetTicks[clickedMarker] = targetTick;

        double sliderPosition = TimingSliderPosition(now);
        LastClickSliderPosition = sliderPosition;
        LastClickSliderUntil = now.AddSeconds(Clock.TickSeconds * 3);

        // v45 clock-only tick sync:
        // the following marker is scheduled from the game action tick determined
        // above. Normal perfect/queued clicks use this marker's ReadyAt boundary;
        // late clicks use ReadyAt plus the displayed late ticks.
        IsReadyPrompt = false;
        CurrentIndex = (CurrentIndex + 1) % Config.Markers.Count;
        ScheduleAfterClick(actionTime);

        return new ClickResult(true, perfect, !perfect, clickedMarker, hitTick, targetTick, sliderPosition, queuedEarly, msBeforeReady);
    }


    public DateTime ActionTimeForAcceptedClick(DateTime now, MarkerState state, bool queuedEarly)
    {
        // v45: once a marker has a known READY tick, do not re-snap normal
        // perfect clicks to the closest external clock boundary. If the user clicks
        // 250-350ms after READY, closest-boundary snapping can choose the next tick
        // and schedule the following marker one whole tick late.
        //
        // For active countdown markers:
        //   queued early  -> the marker's READY tick
        //   perfect       -> the marker's READY tick
        //   late          -> READY tick + displayed late ticks
        //
        // Ready prompts are different: their ReadyAt is just when the prompt was
        // armed, not a game-tick boundary, so those still use the external clock if
        // available.
        if (!IsReadyPrompt)
        {
            if (queuedEarly || state.VisualState == MarkerVisualState.Perfect)
                return ReadyAt;

            if (state.VisualState == MarkerVisualState.Late)
                return ReadyAt.AddSeconds(Math.Max(1, state.LateTicks) * Clock.TickSeconds);
        }

        return Clock.ActionTimeForClick(now);
    }

    public bool CheckLapCompleted(DateTime now)
    {
        if (CurrentIndex != 0 || !LapRunning) return false;
        if (now < ReadyAt) return false;

        LapRunning = false;
        return true;
    }

    public void ScheduleCurrent(DateTime now)
    {
        IsReadyPrompt = false;

        if (Config.Markers.Count == 0)
        {
            ReadyAt = now;
            PerfectUntil = now.AddSeconds(Clock.TickSeconds);
            return;
        }

        if (CurrentIndex == 0 && !LapRunning)
        {
            ArmReadyPrompt(now);
            return;
        }

        ScheduleCountdown(now);
        ClearLastClickSlider();
    }

    public void SelectMarker(int index, DateTime now)
    {
        if (index < 0 || index >= Config.Markers.Count) return;
        CurrentIndex = index;
        ScheduleCurrent(now);
    }

    public void SelectMarkerReady(int index, DateTime now)
    {
        if (index < 0 || index >= Config.Markers.Count) return;

        CurrentIndex = index;
        LapRunning = false;
        LapStartedAt = now;
        ResetTimingDebug();
        ArmReadyPrompt(now);
    }

    public double TimingSliderPosition(DateTime now)
    {
        if (Config.Markers.Count == 0) return 0;

        DateTime start = ReadyAt.AddSeconds(-ClickTimingTicksBeforeReady * Clock.TickSeconds);
        double elapsedTicks = (now - start).TotalSeconds / Clock.TickSeconds;
        return Math.Clamp(elapsedTicks / ClickTimingWindowTicks, 0, 1);
    }

    public bool HasRecentClick(DateTime now) => LastClickSliderPosition.HasValue && now <= LastClickSliderUntil;

    public DateTime MarkerTimingSliderStartsAt => ReadyAt.AddSeconds(-ClickTimingTicksBeforeReady * Clock.TickSeconds);

    public bool ShouldShowMarkerTimingSlider(DateTime now)
    {
        if (Config.Markers.Count == 0 || IsReadyPrompt) return false;

        DateTime showFrom = MarkerTimingSliderStartsAt;
        DateTime showUntil = ReadyAt.AddSeconds(ClickTimingTicksAfterReady * Clock.TickSeconds);
        return now >= showFrom && now <= showUntil;
    }

    public bool ShouldUseMinimalMarkerPreview(DateTime now)
    {
        if (!Config.MinimalMode || Config.Markers.Count == 0 || IsReadyPrompt) return false;

        MarkerState state = GetState(now);
        if (state.VisualState != MarkerVisualState.Waiting) return false;

        double secondsUntilReady = (ReadyAt - now).TotalSeconds;
        return secondsUntilReady > (3 * Clock.TickSeconds);
    }

    public bool ShouldTrackReadyPromptTickSync()
    {
        // Keep the external clock warm while waiting to start a lap, including marker 1
        // after a completed/partial route has wrapped back around.  This updates only
        // TickClock; it never moves the marker countdown.
        return Config.Markers.Count > 0 && (IsReadyPrompt || (CurrentIndex == 0 && !LapRunning));
    }

    public bool ShouldTrackTickSync(DateTime now)
    {
        if (Config.Markers.Count == 0 || IsReadyPrompt) return false;
        if (TickSyncAttemptedForCurrentMarker) return false;
        if (now >= MarkerTimingSliderStartsAt) return false;

        DateTime trackFrom = ReadyAt.AddSeconds(-(TickSyncTrackingTicksBeforeReady * Clock.TickSeconds));
        return now >= trackFrom;
    }

    public bool ShouldAttemptOneShotTickSync(DateTime now)
    {
        if (Config.Markers.Count == 0 || IsReadyPrompt) return false;
        if (TickSyncAttemptedForCurrentMarker) return false;
        if (now >= ReadyAt) return false;

        return now >= MarkerTimingSliderStartsAt;
    }

    public void MarkOneShotTickSyncAttempted()
    {
        TickSyncAttemptedForCurrentMarker = true;
    }

    public TickSyncCorrectionResult ApplyOneShotTickSyncCorrection(DateTime now, double observedPhase, double confidence)
    {
        if (Config.Markers.Count == 0 || IsReadyPrompt)
            return TickSyncCorrectionResult.Ignored(0, 0, confidence, "IGNORED", "No active marker to correct");

        double tickSeconds = Math.Max(0.1, Clock.TickSeconds);
        observedPhase = Math.Clamp(observedPhase, 0, 0.999999);
        confidence = Math.Clamp(confidence, 0, 1);

        DateTime originalReadyAt = ReadyAt;
        DateTime nearestAlignedReadyAt = NearestTickAlignedTime(originalReadyAt, now, observedPhase, tickSeconds);
        double requestedSeconds = (nearestAlignedReadyAt - originalReadyAt).TotalSeconds;

        if (confidence < MinimumTickSyncCorrectionConfidence)
            return TickSyncCorrectionResult.Ignored(requestedSeconds, 0, confidence, "LOW CONF", $"ignored {requestedSeconds:+0.000;-0.000;0.000}s, confidence {confidence * 100:0}%");

        double maxAcceptedSeconds = tickSeconds * MaximumAcceptedTickSyncCorrectionTicks;
        if (Math.Abs(requestedSeconds) > maxAcceptedSeconds)
            return TickSyncCorrectionResult.Ignored(requestedSeconds, 0, confidence, "SUSPICIOUS", $"ignored {requestedSeconds:+0.000;-0.000;0.000}s, larger than {maxAcceptedSeconds:0.000}s limit");

        double maxAppliedSeconds = tickSeconds * MaximumAppliedTickSyncCorrectionTicks;
        double appliedSeconds = Math.Clamp(requestedSeconds, -maxAppliedSeconds, maxAppliedSeconds);
        bool clamped = Math.Abs(appliedSeconds - requestedSeconds) > 0.001;

        ReadyAt = originalReadyAt.AddSeconds(appliedSeconds);
        PerfectUntil = ReadyAt.AddSeconds(tickSeconds);

        if (LapRunning)
            LapStartedAt = ReadyAt.AddSeconds(-(TargetTickForMarker(CurrentIndex) * tickSeconds));

        // Keep click action scheduling aligned with the corrected marker boundary, not with
        // the raw observed phase if the correction was clamped.
        Clock.SetExternalPhase(now, PhaseRelativeToBoundary(now, ReadyAt, tickSeconds), confidence);

        string detail = clamped
            ? $"clamped {appliedSeconds:+0.000;-0.000;0.000}s, wanted {requestedSeconds:+0.000;-0.000;0.000}s"
            : $"adjust {appliedSeconds:+0.000;-0.000;0.000}s";

        return TickSyncCorrectionResult.Applied(requestedSeconds, appliedSeconds, confidence, clamped, detail);
    }

    public TickSyncCorrectionResult ApplyTrackedTickBoundaryCorrection(DateTime now, DateTime boundaryAt, double confidence)
    {
        if (Config.Markers.Count == 0 || IsReadyPrompt)
            return TickSyncCorrectionResult.Ignored(0, 0, confidence, "IGNORED", "No active marker to correct");

        if (boundaryAt == DateTime.MinValue)
            return TickSyncCorrectionResult.Ignored(0, 0, confidence, "NO EDGE", "No tracked tick edge available");

        double tickSeconds = Math.Max(0.1, Clock.TickSeconds);
        confidence = Math.Clamp(confidence, 0, 1);

        DateTime originalReadyAt = ReadyAt;
        DateTime nearestAlignedReadyAt = NearestTickAlignedTimeFromBoundary(originalReadyAt, boundaryAt, tickSeconds);
        double requestedSeconds = (nearestAlignedReadyAt - originalReadyAt).TotalSeconds;

        if (confidence < MinimumTickSyncEdgeConfidence)
            return TickSyncCorrectionResult.Ignored(requestedSeconds, 0, confidence, "LOW CONF", $"edge ignored {requestedSeconds:+0.000;-0.000;0.000}s, confidence {confidence * 100:0}%");

        double maxAcceptedSeconds = tickSeconds * MaximumAcceptedEdgeCorrectionTicks;
        if (Math.Abs(requestedSeconds) > maxAcceptedSeconds)
            return TickSyncCorrectionResult.Ignored(requestedSeconds, 0, confidence, "SUSPICIOUS", $"edge ignored {requestedSeconds:+0.000;-0.000;0.000}s, larger than {maxAcceptedSeconds:0.000}s limit");

        ReadyAt = nearestAlignedReadyAt;
        PerfectUntil = ReadyAt.AddSeconds(tickSeconds);

        if (LapRunning)
            LapStartedAt = ReadyAt.AddSeconds(-(TargetTickForMarker(CurrentIndex) * tickSeconds));

        Clock.SetExternalPhase(boundaryAt, 0, confidence);

        string detail = Math.Abs(requestedSeconds) < 0.001
            ? "edge aligned, no adjustment"
            : $"edge adjust {requestedSeconds:+0.000;-0.000;0.000}s";

        return TickSyncCorrectionResult.Applied(requestedSeconds, requestedSeconds, confidence, false, detail);
    }

    private static DateTime NearestTickAlignedTimeFromBoundary(DateTime targetTime, DateTime boundaryAt, double tickSeconds)
    {
        double ticksFromBoundary = (targetTime - boundaryAt).TotalSeconds / tickSeconds;
        double nearestTick = Math.Round(ticksFromBoundary);
        return boundaryAt.AddSeconds(nearestTick * tickSeconds);
    }

    private static DateTime NearestTickAlignedTime(DateTime targetTime, DateTime observedAt, double observedPhase, double tickSeconds)
    {
        DateTime observedBoundary = observedAt.AddSeconds(-(observedPhase * tickSeconds));
        double ticksFromBoundary = (targetTime - observedBoundary).TotalSeconds / tickSeconds;
        double nearestTick = Math.Round(ticksFromBoundary);
        return observedBoundary.AddSeconds(nearestTick * tickSeconds);
    }

    private static double PhaseRelativeToBoundary(DateTime now, DateTime boundaryAlignedTime, double tickSeconds)
    {
        double phaseSeconds = (now - boundaryAlignedTime).TotalSeconds % tickSeconds;
        if (phaseSeconds < 0) phaseSeconds += tickSeconds;
        return phaseSeconds / tickSeconds;
    }

    private void ScheduleAfterClick(DateTime actionTime)
    {
        IsReadyPrompt = false;

        if (Config.Markers.Count == 0)
        {
            ReadyAt = actionTime;
            PerfectUntil = actionTime.AddSeconds(Clock.TickSeconds);
            return;
        }

        ScheduleCountdownFromActionTick(actionTime);
    }

    private void ScheduleCountdown(DateTime now)
    {
        // Manual editor selection/fallback scheduling still starts from the supplied
        // time, but normal gameplay uses ScheduleCountdownFromActionTick() after
        // TryClick() has snapped the mouse click to the OSRS action tick.
        ScheduleCountdownFromActionTick(now);
    }

    private void ScheduleCountdownFromActionTick(DateTime actionTime)
    {
        int delayTicks = Config.Markers.Count == 0 ? 0 : Math.Max(0, Config.Markers[CurrentIndex].DelayTicks);
        ReadyAt = Clock.TargetTime(actionTime, delayTicks);
        PerfectUntil = ReadyAt.AddSeconds(Clock.TickSeconds);
        TickSyncAttemptedForCurrentMarker = false;
    }

    private void ArmReadyPrompt(DateTime now)
    {
        ReadyAt = now;
        PerfectUntil = DateTime.MaxValue;
        IsReadyPrompt = true;
        TickSyncAttemptedForCurrentMarker = false;
        ClearLastClickSlider();
    }

    private void ClearLastClickSlider()
    {
        LastClickSliderPosition = null;
        LastClickSliderUntil = DateTime.MinValue;
    }

    public void ResetTimingDebug()
    {
        _clickedTicks.Clear();
        _targetTicks.Clear();

        for (int i = 0; i < Config.Markers.Count; i++)
        {
            _clickedTicks[i] = null;
            _targetTicks[i] = i == 0 ? 0 : TargetTickForMarker(i);
        }

        ClearLastClickSlider();
    }

    public List<TimingDebugRow> GetTimingRows()
    {
        var rows = new List<TimingDebugRow>();
        for (int i = 0; i < Config.Markers.Count; i++)
        {
            _clickedTicks.TryGetValue(i, out int? hit);
            int target = _targetTicks.TryGetValue(i, out int t) ? t : (i == 0 ? 0 : TargetTickForMarker(i));
            rows.Add(new TimingDebugRow(i + 1, hit, target));
        }
        return rows;
    }
}
