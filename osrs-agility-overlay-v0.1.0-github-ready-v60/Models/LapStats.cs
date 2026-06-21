namespace OSRSAgilityOverlay.Models;

public sealed class LapStats
{
    public bool SessionStarted { get; set; }
    public bool LapRunning { get; set; }
    public DateTime SessionStartedAt { get; set; }
    public DateTime LapStartedAt { get; set; }
    public double LastLapSeconds { get; set; }
    public int BestCount { get; set; }
    public int PerfectCombo { get; set; }
    public int PerfectTotal { get; set; }
    public int HighestCombo { get; set; }
    public double LostSeconds { get; set; }
    public bool CurrentLapClean { get; set; } = true;

    public double CurrentLapSeconds(DateTime now) => LapRunning ? Math.Max(0, (now - LapStartedAt).TotalSeconds) : 0;
    public double TotalSeconds(DateTime now) => SessionStarted ? Math.Max(0, (now - SessionStartedAt).TotalSeconds) : 0;

    public void Reset(DateTime now)
    {
        SessionStarted = false;
        LapRunning = false;
        SessionStartedAt = now;
        LapStartedAt = now;
        LastLapSeconds = 0;
        BestCount = 0;
        PerfectCombo = 0;
        PerfectTotal = 0;
        HighestCombo = 0;
        LostSeconds = 0;
        CurrentLapClean = true;
    }
}
