using OSRSAgilityOverlay.Models;

namespace OSRSAgilityOverlay.Services;

public sealed class StatsService
{
    public LapStats Stats { get; } = new();
    private DateTime _nextLostAt = DateTime.MaxValue;

    public void Reset(DateTime now)
    {
        Stats.Reset(now);
        _nextLostAt = DateTime.MaxValue;
    }

    public void OnLapStarted(DateTime now)
    {
        if (!Stats.SessionStarted)
        {
            Stats.SessionStarted = true;
            Stats.SessionStartedAt = now;
        }

        Stats.LapRunning = true;
        Stats.LapStartedAt = now;
        Stats.CurrentLapClean = true;
        _nextLostAt = DateTime.MaxValue;
    }

    public void OnClick(ClickResult click)
    {
        if (!click.Accepted) return;

        if (click.Perfect)
        {
            Stats.PerfectCombo++;
            Stats.PerfectTotal++;
            Stats.HighestCombo = Math.Max(Stats.HighestCombo, Stats.PerfectCombo);
        }
        else
        {
            Stats.PerfectCombo = 0;
            Stats.CurrentLapClean = false;
        }
    }

    public void OnLapCompleted(DateTime now)
    {
        if (!Stats.LapRunning) return;

        Stats.LastLapSeconds = Math.Max(0, (now - Stats.LapStartedAt).TotalSeconds);

        if (Stats.CurrentLapClean)
            Stats.BestCount++;

        Stats.LapRunning = false;
        Stats.CurrentLapClean = true;
        _nextLostAt = DateTime.MaxValue;
    }

    public void CancelCurrentLap(bool resetCombo)
    {
        Stats.LapRunning = false;
        Stats.CurrentLapClean = true;
        _nextLostAt = DateTime.MaxValue;

        if (resetCombo)
            Stats.PerfectCombo = 0;
    }

    public void TrackLostTime(DateTime now, DateTime perfectUntil, double tickSeconds, bool paused, bool editMode)
    {
        if (!Stats.SessionStarted || paused || editMode) return;
        if (now < perfectUntil) return;

        if (_nextLostAt == DateTime.MaxValue)
            _nextLostAt = perfectUntil;

        while (now >= _nextLostAt)
        {
            Stats.LostSeconds += tickSeconds;
            _nextLostAt = _nextLostAt.AddSeconds(tickSeconds);
        }
    }

    public void ResetLostTicker()
    {
        _nextLostAt = DateTime.MaxValue;
    }
}
