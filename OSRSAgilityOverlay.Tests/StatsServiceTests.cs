using OSRSAgilityOverlay.Models;
using OSRSAgilityOverlay.Services;
using Xunit;

namespace OSRSAgilityOverlay.Tests;

public sealed class StatsServiceTests
{
    [Fact]
    public void PerfectClick_IncrementsTotalComboAndHighestCombo()
    {
        var stats = new StatsService();

        stats.OnClick(new ClickResult(true, true, false, 0, 0, 0, 0.5));
        stats.OnClick(new ClickResult(true, true, false, 1, 9, 9, 0.5));

        Assert.Equal(2, stats.Stats.PerfectTotal);
        Assert.Equal(2, stats.Stats.PerfectCombo);
        Assert.Equal(2, stats.Stats.HighestCombo);
    }

    [Fact]
    public void LateClick_ResetsComboAndMarksLapDirty()
    {
        var stats = new StatsService();
        stats.OnClick(new ClickResult(true, true, false, 0, 0, 0, 0.5));

        stats.OnClick(new ClickResult(true, false, true, 1, 10, 9, 0.9));

        Assert.Equal(1, stats.Stats.PerfectTotal);
        Assert.Equal(0, stats.Stats.PerfectCombo);
        Assert.False(stats.Stats.CurrentLapClean);
    }

    [Fact]
    public void LostTime_IncrementsInTickChunks()
    {
        var stats = new StatsService();
        DateTime start = DateTime.UtcNow;
        stats.OnLapStarted(start);

        DateTime perfectUntil = start.AddSeconds(1.2);

        stats.TrackLostTime(start.AddSeconds(1.21), perfectUntil, 0.6, false, false);
        Assert.Equal(0.6, stats.Stats.LostSeconds, 5);

        stats.TrackLostTime(start.AddSeconds(1.81), perfectUntil, 0.6, false, false);
        Assert.Equal(1.2, stats.Stats.LostSeconds, 5);
    }

    [Fact]
    public void CancelCurrentLap_StopsLapWithoutCountingBest()
    {
        var stats = new StatsService();
        DateTime start = DateTime.UtcNow;

        stats.OnLapStarted(start);
        stats.OnClick(new ClickResult(true, true, false, 0, 0, 0, 0.5));
        stats.CancelCurrentLap(resetCombo: true);
        stats.OnLapCompleted(start.AddSeconds(10));

        Assert.False(stats.Stats.LapRunning);
        Assert.Equal(0, stats.Stats.BestCount);
        Assert.Equal(0, stats.Stats.PerfectCombo);
        Assert.Equal(0, stats.Stats.LastLapSeconds);
    }

}
