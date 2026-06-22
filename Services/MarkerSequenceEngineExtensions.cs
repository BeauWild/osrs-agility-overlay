namespace OSRSAgilityOverlay.Services;

public static class MarkerSequenceEngineExtensions
{
    public static double RemainingSeconds(this MarkerSequenceEngine engine, DateTime now)
    {
        return Math.Max(0, (engine.ReadyAt - now).TotalSeconds);
    }
}
