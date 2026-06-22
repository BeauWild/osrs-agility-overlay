namespace OSRSAgilityOverlay.Models;

public sealed class OverlayConfig
{
    public string TargetWindowTitleContains { get; set; } = "RuneLite";
    public bool AnchorToRuneLiteWindow { get; set; } = true;
    public int OverlayOpacity { get; set; } = 100;
    public int MarkerNumberSize { get; set; } = 12;
    public double TickSeconds { get; set; } = 0.6;
    public int GlobalClickExtraRadius { get; set; } = 10;
    public int WorldLagMs { get; set; } = 28;
    public int QueueSafetyMs { get; set; } = 15;
    public int EffectiveEarlyClickQueueMs => Math.Max(0, WorldLagMs) + Math.Max(0, QueueSafetyMs);
    public double TickOffsetSeconds { get; set; } = 0.0;
    public bool ShowInfoOverlay { get; set; } = true;
    public bool MinimalMode { get; set; } = false;
    public bool DebugTimingLogEnabled { get; set; } = false;

    // Optional visual OSRS tick sync. When enabled, the app samples this screen area
    // and uses the moving tick line to keep marker countdowns aligned to the game tick display.
    public bool TickSyncEnabled { get; set; } = false;
    public bool TickSyncSinglePixelMode { get; set; } = true;
    public bool TickSyncAreaRelativeToRuneLite { get; set; } = true;
    public int TickSyncAreaX { get; set; } = 0;
    public int TickSyncAreaY { get; set; } = 0;
    public int TickSyncAreaWidth { get; set; } = 0;
    public int TickSyncAreaHeight { get; set; } = 0;

    public List<Marker> Markers { get; set; } = new();

    public bool HasTickSyncArea => TickSyncAreaWidth >= 1 && TickSyncAreaHeight >= 1;
    public int MarkerTickTotal => Markers.Sum(m => Math.Max(0, m.DelayTicks));
    public int BestPossibleTicks => Math.Max(0, MarkerTickTotal);
    public double BestPossibleSeconds => BestPossibleTicks * TickSeconds;
}
