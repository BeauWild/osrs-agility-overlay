namespace OSRSAgilityOverlay.Rendering;

public static class RenderHelpers
{
    public static string FormatSeconds(double seconds) => $"{Math.Max(0, seconds):0.0}s";

    public static string FormatClock(double seconds)
    {
        seconds = Math.Max(0, seconds);
        int totalSeconds = (int)Math.Floor(seconds);
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int wholeSeconds = totalSeconds % 60;

        return hours > 0
            ? $"{hours}:{minutes:00}:{wholeSeconds:00}"
            : $"{minutes}:{wholeSeconds:00}";
    }
}
