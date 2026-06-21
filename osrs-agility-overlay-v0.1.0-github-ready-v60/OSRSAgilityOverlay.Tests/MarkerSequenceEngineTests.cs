using OSRSAgilityOverlay.Models;
using OSRSAgilityOverlay.Services;
using Xunit;

namespace OSRSAgilityOverlay.Tests;

public sealed class MarkerSequenceEngineTests
{
    private static (MarkerSequenceEngine Engine, OverlayConfig Config) CreateEngine()
    {
        var config = new OverlayConfig
        {
            TickSeconds = 0.6,
            Markers =
            [
                new Marker { Name = "M1", DelayTicks = 18, Radius = 16 },
                new Marker { Name = "M2", DelayTicks = 9, Radius = 16 },
                new Marker { Name = "M3", DelayTicks = 14, Radius = 16 },
                new Marker { Name = "M4", DelayTicks = 10, Radius = 16 }
            ]
        };

        var clock = new TickClock { TickSeconds = config.TickSeconds };
        return (new MarkerSequenceEngine(config, clock), config);
    }

    [Fact]
    public void Reset_MarkerOneIsReadyImmediately()
    {
        var (engine, _) = CreateEngine();
        DateTime now = DateTime.UtcNow;

        engine.ResetToMarkerOneReady(now);
        MarkerState state = engine.GetState(now);

        Assert.True(engine.IsReadyPrompt);
        Assert.Equal(0, engine.CurrentIndex);
        Assert.Equal(MarkerVisualState.Perfect, state.VisualState);
    }

    [Fact]
    public void EarlyClick_DoesNotAdvance()
    {
        var (engine, _) = CreateEngine();
        DateTime start = DateTime.UtcNow;

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start); // marker 1 accepted
        int before = engine.CurrentIndex;

        ClickResult result = engine.TryClick(start.AddSeconds(1)); // marker 2 not ready

        Assert.False(result.Accepted);
        Assert.Equal(before, engine.CurrentIndex);
    }

    [Fact]
    public void LateClick_ResyncsFutureMarkerSchedule()
    {
        var (engine, _) = CreateEngine();
        DateTime start = DateTime.UtcNow;

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start); // marker 1, lap start

        // Marker 2 target = 9 ticks = 5.4s. Click 1 displayed tick late at 6.1.
        // v45 schedules the next marker from the displayed late action tick
        // (ReadyAt + 1 tick), not from the sub-tick mouse-down time.
        DateTime lateMarker2Click = start.AddSeconds(6.1);
        DateTime marker2Ready = engine.ReadyAt;
        engine.TryClick(lateMarker2Click);

        Assert.Equal(2, engine.CurrentIndex); // marker 3
        Assert.Equal(marker2Ready.AddSeconds(1 * 0.6).AddSeconds(14 * 0.6), engine.ReadyAt);
    }

    [Fact]
    public void LateClick_RecordsCorrectLateTick_NotOneExtra()
    {
        var (engine, config) = CreateEngine();
        DateTime start = DateTime.UtcNow;

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start); // marker 1, schedules marker 2

        DateTime oneTickLate = engine.ReadyAt.AddSeconds(config.TickSeconds + 0.01);
        ClickResult result = engine.TryClick(oneTickLate);

        Assert.True(result.Accepted);
        Assert.True(result.Late);
        Assert.Equal(1, result.HitTick - result.TargetTick);
    }

    [Fact]
    public void PerfectClick_RecordsTargetTick_NotMinusOne()
    {
        var (engine, _) = CreateEngine();
        DateTime start = DateTime.UtcNow;

        engine.ResetToMarkerOneReady(start);
        ClickResult result = engine.TryClick(start);

        Assert.True(result.Accepted);
        Assert.True(result.Perfect);
        Assert.Equal(result.TargetTick, result.HitTick);
    }

    [Fact]
    public void TryClick_PerfectClick_DoesNotThrowAndReturnsSlider()
    {
        var (engine, _) = CreateEngine();
        DateTime start = DateTime.UtcNow;

        engine.ResetToMarkerOneReady(start);

        ClickResult result = engine.TryClick(start);

        Assert.True(result.Accepted);
        Assert.InRange(result.SliderPosition, 0, 1);
    }

    [Fact]
    public void LastClickSlider_HoldsClickedPositionForThreeTicks()
    {
        var (engine, config) = CreateEngine();
        DateTime start = DateTime.UtcNow;

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start);

        Assert.True(engine.HasRecentClick(start.AddSeconds(config.TickSeconds * 2.9)));
        Assert.False(engine.HasRecentClick(start.AddSeconds(config.TickSeconds * 3.1)));
    }

    [Fact]
    public void VeryLateClick_ResyncsNextMarkerCountdown()
    {
        var (engine, _) = CreateEngine();
        DateTime start = DateTime.UtcNow;

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start); // marker 1, lap start

        DateTime marker2Ready = engine.ReadyAt;
        DateTime veryLateMarker2Click = start.AddSeconds(200);
        ClickResult result = engine.TryClick(veryLateMarker2Click);

        Assert.True(result.Accepted);
        Assert.True(result.Late);
        Assert.Equal(2, engine.CurrentIndex); // marker 3

        int displayedLateTicks = result.HitTick - result.TargetTick;
        DateTime expectedMarker3Ready = marker2Ready
            .AddSeconds(displayedLateTicks * 0.6)
            .AddSeconds(14 * 0.6);
        Assert.Equal(expectedMarker3Ready, engine.ReadyAt);
    }

    [Fact]
    public void ReadyPrompt_StaysReadyUntilFirstClick()
    {
        var (engine, _) = CreateEngine();
        DateTime start = DateTime.UtcNow;

        engine.ResetToMarkerOneReady(start);
        MarkerState state = engine.GetState(start.AddSeconds(30));

        Assert.True(engine.IsReadyPrompt);
        Assert.Equal(0, engine.CurrentIndex);
        Assert.Equal(MarkerVisualState.Perfect, state.VisualState);
        Assert.Equal(0, state.LateTicks);
    }

    [Fact]
    public void SelectMarkerReady_MakesAnyMarkerReadyImmediately()
    {
        var (engine, _) = CreateEngine();
        DateTime now = DateTime.UtcNow;

        engine.SelectMarkerReady(2, now);
        MarkerState state = engine.GetState(now);

        Assert.True(engine.IsReadyPrompt);
        Assert.False(engine.LapRunning);
        Assert.Equal(2, engine.CurrentIndex);
        Assert.Equal(MarkerVisualState.Perfect, state.VisualState);
    }

    [Fact]
    public void PartialRouteClick_DoesNotStartLap_AndNextMarkerCountsFromActualClick()
    {
        var (engine, _) = CreateEngine();
        DateTime clickMarker3At = DateTime.UtcNow;

        engine.SelectMarkerReady(2, clickMarker3At); // start halfway on marker 3
        ClickResult result = engine.TryClick(clickMarker3At);

        Assert.True(result.Accepted);
        Assert.True(result.Perfect);
        Assert.False(engine.LapRunning);
        Assert.False(engine.IsReadyPrompt);
        Assert.Equal(3, engine.CurrentIndex); // marker 4
        Assert.Equal(clickMarker3At.AddSeconds(10 * 0.6), engine.ReadyAt);
    }

    [Fact]
    public void PartialRouteWrapToMarkerOne_CountsDownInsteadOfReadyPrompt()
    {
        var (engine, config) = CreateEngine();
        DateTime marker4Click = DateTime.UtcNow;

        engine.SelectMarkerReady(3, marker4Click);
        engine.TryClick(marker4Click); // marker 4, wraps to marker 1 countdown

        Assert.Equal(0, engine.CurrentIndex);
        Assert.False(engine.IsReadyPrompt);
        Assert.False(engine.LapRunning);
        Assert.Equal(marker4Click.AddSeconds(18 * config.TickSeconds), engine.ReadyAt);

        MarkerState state = engine.GetState(marker4Click);
        Assert.Equal(MarkerVisualState.Waiting, state.VisualState);
        Assert.Equal(18, state.RemainingTicks);
    }

    [Fact]
    public void PartialRouteMarkerOneClick_StartsRealLapAfterCountdown()
    {
        var (engine, config) = CreateEngine();
        DateTime marker4Click = DateTime.UtcNow;

        engine.SelectMarkerReady(3, marker4Click);
        engine.TryClick(marker4Click); // marker 4, wraps to marker 1 countdown

        DateTime marker1Click = engine.ReadyAt;
        ClickResult result = engine.TryClick(marker1Click);

        Assert.True(result.Accepted);
        Assert.True(result.Perfect);
        Assert.True(engine.LapRunning);
        Assert.Equal(1, engine.CurrentIndex);
        Assert.Equal(marker1Click.AddSeconds(9 * config.TickSeconds), engine.ReadyAt);
    }

    [Fact]
    public void PartialRouteWrapToMarkerOne_DoesNotCompleteLap()
    {
        var (engine, _) = CreateEngine();
        DateTime marker3Click = DateTime.UtcNow;

        engine.SelectMarkerReady(2, marker3Click);
        engine.TryClick(marker3Click); // marker 3, then marker 4 countdown

        DateTime marker4Click = engine.ReadyAt;
        engine.TryClick(marker4Click); // marker 4, wraps to marker 1 countdown

        Assert.Equal(0, engine.CurrentIndex);
        Assert.False(engine.IsReadyPrompt);
        Assert.False(engine.LapRunning);
        Assert.False(engine.CheckLapCompleted(marker4Click));
    }


    [Fact]
    public void ExternalTickSync_ClickSchedulesFromClosestGameTickBoundary()
    {
        var (engine, config) = CreateEngine();
        DateTime click = DateTime.UtcNow;

        engine.Clock.SetExternalPhase(click, 0.5, 0.95); // halfway through the visible OSRS tick
        engine.ResetToMarkerOneReady(click);

        ClickResult result = engine.TryClick(click);

        DateTime actionTick = engine.Clock.ActionTimeForClick(click);
        Assert.True(result.Accepted);
        Assert.True(engine.LapRunning);
        Assert.InRange(Math.Abs((engine.LapStartedAt - actionTick).TotalMilliseconds), 0, 2);
        Assert.InRange(Math.Abs((engine.ReadyAt - actionTick.AddSeconds(9 * config.TickSeconds)).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void ExternalTickSync_PartialRouteUsesClosestGameTickBoundaryButDoesNotStartLap()
    {
        var (engine, config) = CreateEngine();
        DateTime clickMarker3At = DateTime.UtcNow;

        engine.Clock.SetExternalPhase(clickMarker3At, 0.25, 0.95);
        engine.SelectMarkerReady(2, clickMarker3At);
        ClickResult result = engine.TryClick(clickMarker3At);

        DateTime actionTick = engine.Clock.ActionTimeForClick(clickMarker3At);
        Assert.True(result.Accepted);
        Assert.False(engine.LapRunning);
        Assert.Equal(3, engine.CurrentIndex);
        Assert.InRange(Math.Abs((engine.ReadyAt - actionTick.AddSeconds(10 * config.TickSeconds)).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void ClockOnlyTickSync_PerfectMarkerSchedulesNextFromReadyTick()
    {
        var (engine, config) = CreateEngine();
        DateTime clickMarker1 = DateTime.Today.AddSeconds(100);

        engine.Clock.SetExternalPhase(clickMarker1, 0.50, 0.95);
        engine.ResetToMarkerOneReady(clickMarker1);
        engine.TryClick(clickMarker1);

        DateTime marker2Ready = engine.ReadyAt;
        DateTime clickMarker2 = marker2Ready.AddSeconds(0.40);
        engine.Clock.SetExternalPhase(clickMarker2, 0.40 / config.TickSeconds, 0.95);
        ClickResult result = engine.TryClick(clickMarker2);

        DateTime expectedMarker3Ready = marker2Ready.AddSeconds(14 * config.TickSeconds);

        Assert.True(result.Accepted);
        Assert.True(result.Perfect);
        Assert.Equal(2, engine.CurrentIndex);
        Assert.InRange(Math.Abs((engine.ReadyAt - expectedMarker3Ready).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void OneShotTickSync_OnlyBecomesDueTwoTicksBeforeReady()
    {
        var (engine, config) = CreateEngine();
        DateTime start = DateTime.UtcNow;

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start); // marker 1, marker 2 scheduled 9 ticks later

        DateTime dueAt = engine.ReadyAt.AddSeconds(-MarkerSequenceEngine.ClickTimingTicksBeforeReady * config.TickSeconds);

        Assert.False(engine.ShouldAttemptOneShotTickSync(dueAt.AddMilliseconds(-1)));
        Assert.True(engine.ShouldAttemptOneShotTickSync(dueAt));

        engine.MarkOneShotTickSyncAttempted();
        Assert.False(engine.ShouldAttemptOneShotTickSync(dueAt.AddMilliseconds(25)));
    }

    [Fact]
    public void OneShotTickSync_AppliesSmallNearestAlignedCorrection()
    {
        var (engine, config) = CreateEngine();
        DateTime start = DateTime.Today.AddSeconds(200);

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start); // marker 1, marker 2 scheduled 9 ticks later

        DateTime originalReady = engine.ReadyAt;
        DateTime checkAt = originalReady.AddSeconds(-MarkerSequenceEngine.ClickTimingTicksBeforeReady * config.TickSeconds);

        TickSyncCorrectionResult correction = engine.ApplyOneShotTickSyncCorrection(checkAt, observedPhase: 0.05, confidence: 0.95);
        DateTime expectedReady = originalReady.AddSeconds(-0.03);

        Assert.True(correction.DidApply);
        Assert.False(correction.WasClamped);
        Assert.InRange(Math.Abs(correction.AppliedSeconds + 0.03), 0, 0.002);
        Assert.InRange(Math.Abs((engine.ReadyAt - expectedReady).TotalMilliseconds), 0, 2);
        Assert.InRange(Math.Abs((engine.PerfectUntil - expectedReady.AddSeconds(config.TickSeconds)).TotalMilliseconds), 0, 2);
        Assert.InRange(Math.Abs((engine.LapStartedAt - expectedReady.AddSeconds(-9 * config.TickSeconds)).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void OneShotTickSync_ClampsMediumCorrectionInsteadOfJumpingTimer()
    {
        var (engine, config) = CreateEngine();
        DateTime start = DateTime.Today.AddSeconds(200);

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start);

        DateTime originalReady = engine.ReadyAt;
        DateTime checkAt = originalReady.AddSeconds(-MarkerSequenceEngine.ClickTimingTicksBeforeReady * config.TickSeconds);

        TickSyncCorrectionResult correction = engine.ApplyOneShotTickSyncCorrection(checkAt, observedPhase: 0.10, confidence: 0.95);
        double maxApplied = config.TickSeconds * MarkerSequenceEngine.MaximumAppliedTickSyncCorrectionTicks;

        Assert.True(correction.DidApply);
        Assert.True(correction.WasClamped);
        Assert.InRange(Math.Abs(correction.RequestedSeconds + 0.06), 0, 0.002);
        Assert.InRange(Math.Abs(correction.AppliedSeconds + maxApplied), 0, 0.002);
        Assert.InRange(Math.Abs((engine.ReadyAt - originalReady.AddSeconds(-maxApplied)).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void OneShotTickSync_IgnoresLowConfidenceCorrection()
    {
        var (engine, config) = CreateEngine();
        DateTime start = DateTime.Today.AddSeconds(200);

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start);

        DateTime originalReady = engine.ReadyAt;
        DateTime checkAt = originalReady.AddSeconds(-MarkerSequenceEngine.ClickTimingTicksBeforeReady * config.TickSeconds);

        TickSyncCorrectionResult correction = engine.ApplyOneShotTickSyncCorrection(checkAt, observedPhase: 0.10, confidence: 0.49);

        Assert.False(correction.DidApply);
        Assert.Equal(originalReady, engine.ReadyAt);
        Assert.False(engine.Clock.IsExternalSyncFresh(checkAt));
    }

    [Fact]
    public void OneShotTickSync_IgnoresTooLargeCorrection()
    {
        var (engine, config) = CreateEngine();
        DateTime start = DateTime.Today.AddSeconds(200);

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start);

        DateTime originalReady = engine.ReadyAt;
        DateTime checkAt = originalReady.AddSeconds(-MarkerSequenceEngine.ClickTimingTicksBeforeReady * config.TickSeconds);

        TickSyncCorrectionResult correction = engine.ApplyOneShotTickSyncCorrection(checkAt, observedPhase: 0.25, confidence: 0.95);

        Assert.False(correction.DidApply);
        Assert.Equal(originalReady, engine.ReadyAt);
        Assert.InRange(Math.Abs(correction.RequestedSeconds), config.TickSeconds * MarkerSequenceEngine.MaximumAcceptedTickSyncCorrectionTicks, config.TickSeconds / 2);
    }


    [Fact]
    public void OneShotTickSync_UsesCaptureSampleTimeInsteadOfOldTimerTickTime()
    {
        var (engine, config) = CreateEngine();
        DateTime start = DateTime.Today.AddSeconds(200);

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start);

        DateTime originalReady = engine.ReadyAt;
        DateTime checkAt = originalReady.AddSeconds(-MarkerSequenceEngine.ClickTimingTicksBeforeReady * config.TickSeconds);
        DateTime sampleAt = checkAt.AddSeconds(0.10);
        double observedPhaseAtSample = 0.10 / config.TickSeconds;

        TickSyncCorrectionResult correction = engine.ApplyOneShotTickSyncCorrection(sampleAt, observedPhaseAtSample, confidence: 0.95);

        Assert.True(correction.DidApply);
        Assert.False(correction.WasClamped);
        Assert.InRange(Math.Abs(correction.RequestedSeconds), 0, 0.002);
        Assert.InRange(Math.Abs((engine.ReadyAt - originalReady).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void OneShotTickSync_DoesNotRunForReadyPrompt()
    {
        var (engine, _) = CreateEngine();
        DateTime start = DateTime.UtcNow;

        engine.ResetToMarkerOneReady(start);

        Assert.True(engine.IsReadyPrompt);
        Assert.False(engine.ShouldAttemptOneShotTickSync(start.AddSeconds(20)));
    }


    [Fact]
    public void ReadyPromptTickSync_IsAllowedSoMarkerOneCanStartFromGameTick()
    {
        var (engine, _) = CreateEngine();
        DateTime start = DateTime.UtcNow;

        engine.ResetToMarkerOneReady(start);

        Assert.True(engine.IsReadyPrompt);
        Assert.True(engine.ShouldTrackReadyPromptTickSync());
    }

    [Fact]
    public void PrestartTickLock_MarkerOneClickSchedulesMarkerTwoFromNextGameTick()
    {
        var (engine, config) = CreateEngine();
        DateTime click = DateTime.Today.AddSeconds(100);

        engine.ResetToMarkerOneReady(click);
        engine.Clock.SetExternalPhase(click, 0.70, 0.95);

        ClickResult result = engine.TryClick(click);

        DateTime expectedActionTick = click.AddSeconds(config.TickSeconds * 0.30);
        Assert.True(result.Accepted);
        Assert.True(engine.LapRunning);
        Assert.InRange(Math.Abs((engine.LapStartedAt - expectedActionTick).TotalMilliseconds), 0, 2);
        Assert.InRange(Math.Abs((engine.ReadyAt - expectedActionTick.AddSeconds(9 * config.TickSeconds)).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void ReadyPromptTickSync_PartialStartUsesGameTickWithoutStartingLap()
    {
        var (engine, config) = CreateEngine();
        DateTime click = DateTime.Today.AddSeconds(200);

        engine.SelectMarkerReady(2, click);
        engine.Clock.SetExternalPhase(click, 0.40, 0.95);

        ClickResult result = engine.TryClick(click);

        DateTime expectedActionTick = engine.Clock.ActionTimeForClick(click);
        Assert.True(result.Accepted);
        Assert.False(engine.LapRunning);
        Assert.Equal(3, engine.CurrentIndex);
        Assert.InRange(Math.Abs((engine.ReadyAt - expectedActionTick.AddSeconds(10 * config.TickSeconds)).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void WorldLagWindow_AcceptsClickJustBeforeReadyAsQueuedPerfect()
    {
        var (engine, config) = CreateEngine();
        config.WorldLagMs = 35;
        DateTime start = DateTime.Today.AddSeconds(300);

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start); // marker 1, marker 2 countdown

        DateTime marker2Ready = engine.ReadyAt;
        DateTime earlyClick = marker2Ready.AddMilliseconds(-28);
        ClickResult result = engine.TryClick(earlyClick);

        Assert.True(result.Accepted);
        Assert.True(result.Perfect);
        Assert.True(result.QueuedEarly);
        Assert.InRange(result.MsBeforeReady, 27, 29);
        Assert.Equal(2, engine.CurrentIndex);
        Assert.InRange(Math.Abs((engine.ReadyAt - marker2Ready.AddSeconds(14 * config.TickSeconds)).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void WorldLagWindow_RejectsClickTooEarlyBeforeReady()
    {
        var (engine, config) = CreateEngine();
        config.WorldLagMs = 35;
        config.QueueSafetyMs = 15;
        DateTime start = DateTime.Today.AddSeconds(400);

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start);

        DateTime tooEarly = engine.ReadyAt.AddMilliseconds(-80);
        int beforeIndex = engine.CurrentIndex;
        ClickResult result = engine.TryClick(tooEarly);

        Assert.False(result.Accepted);
        Assert.False(result.QueuedEarly);
        Assert.Equal(beforeIndex, engine.CurrentIndex);
    }

    [Fact]
    public void QueueSafetyWindow_ExtendsWorldLagEarlyAcceptance()
    {
        var (engine, config) = CreateEngine();
        config.WorldLagMs = 31;
        config.QueueSafetyMs = 15;
        DateTime start = DateTime.Today.AddSeconds(450);

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start);

        DateTime marker2Ready = engine.ReadyAt;
        DateTime earlyClick = marker2Ready.AddMilliseconds(-42);
        ClickResult result = engine.TryClick(earlyClick);

        Assert.True(result.Accepted);
        Assert.True(result.QueuedEarly);
        Assert.InRange(result.MsBeforeReady, 41, 43);
    }

    [Fact]
    public void MinimalMode_PreviewRunsOnlyUntilThreeTicksBeforeReady()
    {
        var (engine, config) = CreateEngine();
        config.MinimalMode = true;
        DateTime start = DateTime.Today.AddSeconds(500);

        engine.ResetToMarkerOneReady(start);
        engine.TryClick(start);

        Assert.True(engine.ShouldUseMinimalMarkerPreview(engine.ReadyAt.AddSeconds(-(3 * config.TickSeconds) - 0.05)));
        Assert.False(engine.ShouldUseMinimalMarkerPreview(engine.ReadyAt.AddSeconds(-(3 * config.TickSeconds) + 0.01)));
    }


    [Fact]
    public void BestTime_UsesMarkerTicksOnly()
    {
        var (_, config) = CreateEngine();

        int markerTicks = 18 + 9 + 14 + 10;
        Assert.Equal(markerTicks, config.MarkerTickTotal);
        Assert.Equal(markerTicks, config.BestPossibleTicks);
        Assert.Equal(markerTicks * 0.6, config.BestPossibleSeconds, 5);
    }
}
