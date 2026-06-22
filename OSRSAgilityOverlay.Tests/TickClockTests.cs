using System.Drawing;
using OSRSAgilityOverlay.Services;
using Xunit;

namespace OSRSAgilityOverlay.Tests;

public sealed class TickClockTests
{
    [Fact]
    public void SetExternalPhase_LocksClockPhase()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime now = DateTime.UtcNow;

        clock.SetExternalPhase(now, 0.5, 0.95);

        Assert.True(clock.IsExternalSyncFresh(now));
        Assert.InRange(clock.Phase(now), 0.499, 0.501);
    }

    [Fact]
    public void ActionTimeForClick_WhenLocked_ReturnsClosestTickBoundary()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime now = DateTime.UtcNow;

        clock.SetExternalPhase(now, 0.25, 0.95);

        DateTime actionTime = clock.ActionTimeForClick(now);
        Assert.InRange(Math.Abs((actionTime - now.AddSeconds(-0.15)).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void ActionTimeForClick_WhenUnlocked_ReturnsClickTime()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime now = DateTime.UtcNow;

        Assert.Equal(now, clock.ActionTimeForClick(now));
    }

    [Fact]
    public void ActionTimeForClick_ReusesTrackedTickClockAcrossMarkerDelay()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime boundary = DateTime.Today.AddSeconds(100);

        clock.SetExternalPhase(boundary, 0, 0.95);
        DateTime click = boundary.AddSeconds(1.962); // 162ms after the 1.8s boundary

        Assert.True(clock.IsExternalSyncFresh(click));

        DateTime actionTime = clock.ActionTimeForClick(click);
        Assert.InRange(Math.Abs((actionTime - boundary.AddSeconds(1.8)).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void ActionTimeForClick_ClickLateInTick_MapsForwardToNearestBoundary()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime boundary = DateTime.Today.AddSeconds(100);

        clock.SetExternalPhase(boundary, 0, 0.95);
        DateTime click = boundary.AddSeconds(0.402);

        DateTime actionTime = clock.ActionTimeForClick(click);
        Assert.InRange(Math.Abs((actionTime - boundary.AddSeconds(0.6)).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void ClosestTickBoundaryTo_NormalAfterReadyClick_DoesNotAddHalfTick()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime boundary = DateTime.Today.AddSeconds(100);

        clock.SetExternalPhase(boundary, 0, 0.95);
        DateTime click = boundary.AddSeconds(0.120);

        DateTime actionTime = clock.ActionTimeForClick(click);
        Assert.InRange(Math.Abs((actionTime - boundary).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void NextTickBoundaryAfter_WhenExactlyOnBoundary_ReturnsFollowingTick()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime boundary = DateTime.Today.AddSeconds(100);

        clock.SetExternalPhase(boundary, 0, 0.95);

        DateTime actionTime = clock.NextTickBoundaryAfter(boundary);
        Assert.InRange(Math.Abs((actionTime - boundary.AddSeconds(0.6)).TotalMilliseconds), 0, 2);
    }
    [Fact]
    public void TickBoundaryAtOrBefore_UsesExternalPhase()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime now = DateTime.Today.AddSeconds(100);

        clock.SetExternalPhase(now, 0.25, 0.95);

        DateTime boundary = clock.TickBoundaryAtOrBefore(now);
        Assert.InRange(Math.Abs((boundary - now.AddSeconds(-0.15)).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void TickSyncAnalyzer_LatchesBarBordersAndUsesInnerLinePhase()
    {
        using Bitmap bitmap = new(220, 44);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(70, 70, 70));

        using SolidBrush black = new(Color.Black);
        using SolidBrush green = new(Color.Lime);
        using SolidBrush red = new(Color.Red);

        // Oversized selected area with the real tick box inside it.
        g.FillRectangle(black, 18, 12, 4, 20);       // left outer border
        g.FillRectangle(black, 198, 12, 4, 20);      // right outer border
        g.FillRectangle(green, 22, 13, 142, 18);
        g.FillRectangle(red, 166, 13, 32, 18);
        g.FillRectangle(black, 164, 12, 3, 20);      // static green/red split
        g.FillRectangle(black, 63, 12, 3, 20);       // moving tick marker

        Assert.True(TickSyncService.TryAnalyzeTickBarBitmapForTest(bitmap, null, out double phase, out double confidence, out Rectangle innerRect, out int lineX));
        Assert.Equal(22, innerRect.Left);
        Assert.Equal(198, innerRect.Right);
        Assert.InRange(lineX, 63, 65);
        Assert.InRange(phase, 0.22, 0.25);
        Assert.InRange(confidence, 0.70, 1.0);
    }

    [Fact]
    public void TickSyncAnalyzer_ExpectedPhaseBeatsStaticBoundary()
    {
        using Bitmap bitmap = new(220, 44);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(70, 70, 70));

        using SolidBrush black = new(Color.Black);
        using SolidBrush green = new(Color.Lime);
        using SolidBrush red = new(Color.Red);

        g.FillRectangle(black, 18, 12, 4, 20);
        g.FillRectangle(black, 198, 12, 4, 20);
        g.FillRectangle(green, 22, 13, 142, 18);
        g.FillRectangle(red, 166, 13, 32, 18);
        g.FillRectangle(black, 164, 12, 3, 20);      // static boundary near 0.81
        g.FillRectangle(black, 121, 12, 3, 20);      // moving marker near expected phase

        Assert.True(TickSyncService.TryAnalyzeTickBarBitmapForTest(bitmap, 0.56, out double phase, out double confidence, out _, out int lineX));
        Assert.InRange(lineX, 121, 123);
        Assert.InRange(phase, 0.55, 0.58);
        Assert.InRange(confidence, 0.65, 1.0);
    }

    [Fact]
    public void TickSyncAnalyzer_DetectsLowOpacityTickBar()
    {
        using Bitmap bitmap = new(220, 44);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(10, 10, 10));

        using SolidBrush black = new(Color.Black);
        using SolidBrush dimGreen = new(Color.FromArgb(0, 25, 0));
        using SolidBrush dimRed = new(Color.FromArgb(25, 0, 0));

        g.FillRectangle(black, 18, 12, 4, 20);
        g.FillRectangle(black, 198, 12, 4, 20);
        g.FillRectangle(dimGreen, 22, 13, 142, 18);
        g.FillRectangle(dimRed, 166, 13, 32, 18);
        g.FillRectangle(black, 164, 12, 3, 20);
        g.FillRectangle(black, 88, 12, 3, 20);

        Assert.True(TickSyncService.TryAnalyzeTickBarBitmapForTest(bitmap, null, out double phase, out double confidence, out Rectangle innerRect, out int lineX));
        Assert.Equal(22, innerRect.Left);
        Assert.Equal(198, innerRect.Right);
        Assert.InRange(lineX, 88, 90);
        Assert.InRange(phase, 0.36, 0.40);
        Assert.InRange(confidence, 0.65, 1.0);
    }

    [Fact]
    public void SetExternalPhase_WhenLocked_IgnoresTinyVisualNoise()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime boundary = DateTime.Today.AddSeconds(100);

        clock.SetExternalPhase(boundary, 0, 0.95);
        double offsetBefore = clock.OffsetSeconds;

        clock.SetExternalPhase(boundary.AddMilliseconds(10), 0, 0.95);

        Assert.Equal(0, clock.LastAppliedSyncCorrectionSeconds, precision: 6);
        Assert.Equal(offsetBefore, clock.OffsetSeconds, precision: 6);
        Assert.Contains("stable", clock.LastSyncAdjustmentText);
    }

    [Fact]
    public void SetExternalPhase_WhenLocked_AppliesOnlyTinySmallCorrections()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime boundary = DateTime.Today.AddSeconds(100);

        clock.SetExternalPhase(boundary, 0, 0.95);
        clock.SetExternalPhase(boundary.AddMilliseconds(40), 0, 0.95);

        Assert.InRange(Math.Abs(clock.LastObservedSyncCorrectionSeconds * 1000.0), 35, 45);
        Assert.InRange(Math.Abs(clock.LastAppliedSyncCorrectionSeconds * 1000.0), 0.1, 3.1);
        Assert.Contains("tiny adjust", clock.LastSyncAdjustmentText);
    }

    [Fact]
    public void SetExternalPhase_WhenLocked_HoldsMediumCorrectionsUntilRepeated()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime boundary = DateTime.Today.AddSeconds(100);

        clock.SetExternalPhase(boundary, 0, 0.95);

        clock.SetExternalPhase(boundary.AddSeconds(0.6).AddMilliseconds(100), 0, 0.95);
        Assert.Equal(0, clock.LastAppliedSyncCorrectionSeconds, precision: 6);
        Assert.Contains("hold", clock.LastSyncAdjustmentText);

        clock.SetExternalPhase(boundary.AddSeconds(1.2).AddMilliseconds(100), 0, 0.95);
        Assert.Equal(0, clock.LastAppliedSyncCorrectionSeconds, precision: 6);
        Assert.Contains("hold", clock.LastSyncAdjustmentText);

        clock.SetExternalPhase(boundary.AddSeconds(1.8).AddMilliseconds(100), 0, 0.95);
        Assert.InRange(Math.Abs(clock.LastAppliedSyncCorrectionSeconds * 1000.0), 0.1, 6.1);
        Assert.Contains("slow adjust", clock.LastSyncAdjustmentText);
    }

    [Fact]
    public void SetExternalPhase_WhenLocked_IgnoresSuspiciousLargeCorrections()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime boundary = DateTime.Today.AddSeconds(100);

        clock.SetExternalPhase(boundary, 0, 0.95);
        double offsetBefore = clock.OffsetSeconds;

        clock.SetExternalPhase(boundary.AddMilliseconds(220), 0, 0.95);

        Assert.Equal(0, clock.LastAppliedSyncCorrectionSeconds, precision: 6);
        Assert.Equal(offsetBefore, clock.OffsetSeconds, precision: 6);
        Assert.Contains("suspicious ignored", clock.LastSyncAdjustmentText);
    }

    [Fact]
    public void ActionTimeForClick_ReusesInternalClockAfterLongVisualGap()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime boundary = DateTime.Today.AddSeconds(100);

        clock.SetExternalPhase(boundary, 0, 0.95);

        DateTime click = boundary.AddSeconds(120.120);
        Assert.True(clock.IsExternalSyncFresh(click));

        DateTime actionTime = clock.ActionTimeForClick(click);
        Assert.InRange(Math.Abs((actionTime - boundary.AddSeconds(120.0)).TotalMilliseconds), 0, 2);
    }

    [Fact]
    public void SetExternalPhase_WhenLockedAfterLongGap_DoesNotHardRelock()
    {
        TickClock clock = new() { TickSeconds = 0.6 };
        DateTime boundary = DateTime.Today.AddSeconds(100);

        clock.SetExternalPhase(boundary, 0, 0.95);
        double offsetBefore = clock.OffsetSeconds;

        clock.SetExternalPhase(boundary.AddSeconds(120).AddMilliseconds(100), 0, 0.95);

        Assert.True(clock.ExternalSyncLocked);
        Assert.False(clock.LastSyncAdjustmentText.Contains("lock", StringComparison.OrdinalIgnoreCase));
        Assert.InRange(Math.Abs((clock.OffsetSeconds - offsetBefore) * 1000.0), 0, 7);
    }

}
