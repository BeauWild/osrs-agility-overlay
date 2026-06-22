using OSRSAgilityOverlay.Models;
using OSRSAgilityOverlay.Services;

namespace OSRSAgilityOverlay.Rendering;

public sealed class OverlayRenderer
{
    public int DrawInfoBox(Graphics g, int timingBoxWidth, OverlayConfig config, StatsService stats, MarkerSequenceEngine engine, DateTime now)
    {
        string[] lines =
        [
            $"Total: {RenderHelpers.FormatClock(stats.Stats.TotalSeconds(now))} | Lost: {RenderHelpers.FormatClock(stats.Stats.LostSeconds)}",
            $"Best: {config.BestPossibleSeconds:0.0}s x{stats.Stats.BestCount} | Last: {RenderHelpers.FormatSeconds(stats.Stats.LastLapSeconds)}",
            $"Perfect total/highest/current: {stats.Stats.PerfectTotal}/{stats.Stats.HighestCombo}/{stats.Stats.PerfectCombo}",
            $"Lap: {RenderHelpers.FormatSeconds(stats.Stats.CurrentLapSeconds(now))}"
        ];

        using Font font = new(FontFamily.GenericSansSerif, 10, FontStyle.Bold);
        using SolidBrush bg = new(Color.FromArgb(170, Color.Black));
        using SolidBrush fg = new(Color.White);

        SizeF lineSize = g.MeasureString("Ag", font);
        float width = Math.Max(300, lines.Max(line => g.MeasureString(line, font).Width) + 28);
        float height = (lineSize.Height * lines.Length) + 18;

        float x = timingBoxWidth + 12;
        RectangleF rect = new(x, 12, width, height);
        g.FillRectangle(bg, rect);

        for (int i = 0; i < lines.Length; i++)
            g.DrawString(lines[i], font, fg, x + 12, 18 + (i * lineSize.Height));

        return (int)Math.Ceiling(x + width);
    }

    public void DrawTickSyncStatus(Graphics g, int infoBoxRight, TickSyncService tickSync, DateTime now)
    {
        string edgeAge = tickSync.LastTrackedBoundaryAt == DateTime.MinValue
            ? "--"
            : $"{Math.Max(0, (now - tickSync.LastTrackedBoundaryAt).TotalMilliseconds):0}ms";

        string sampleAge = tickSync.LastSampleAt == DateTime.MinValue
            ? "--"
            : $"{Math.Max(0, (now - tickSync.LastSampleAt).TotalMilliseconds):0}ms";

        string text = $"Tick: {tickSync.Status} | conf {tickSync.Confidence * 100:0}%";
        string detail = tickSync.IsLocked
            ? $"edge {edgeAge} | sample {sampleAge}"
            : "set/select tick pixel";

        if (!string.IsNullOrWhiteSpace(tickSync.LastTrackerText))
            detail += $" | {ShortenTickText(tickSync.LastTrackerText)}";

        using Font font = new(FontFamily.GenericSansSerif, 9, FontStyle.Bold);
        using SolidBrush bg = new(Color.FromArgb(170, Color.Black));
        using SolidBrush fg = new(tickSync.IsLocked ? Color.FromArgb(80, 255, 80) : Color.Orange);
        using SolidBrush detailFg = new(Color.White);

        SizeF textSize = g.MeasureString(text, font);
        SizeF detailSize = g.MeasureString(detail, font);
        float width = Math.Max(textSize.Width, detailSize.Width) + 18;
        float height = textSize.Height + detailSize.Height + 14;
        float x = infoBoxRight + 10;
        float y = 12;

        RectangleF rect = new(x, y, width, height);
        g.FillRectangle(bg, rect);
        g.DrawString(text, font, fg, x + 9, y + 6);
        g.DrawString(detail, font, detailFg, x + 9, y + 6 + textSize.Height);
    }

    private static string ShortenTickText(string text)
    {
        if (text.Contains("pixel verified", StringComparison.OrdinalIgnoreCase)) return "pixel verified";
        if (text.Contains("pixel flip", StringComparison.OrdinalIgnoreCase)) return "pixel flip";
        if (text.Contains("pixel unreadable", StringComparison.OrdinalIgnoreCase)) return "pixel unreadable";
        if (text.Contains("blue", StringComparison.OrdinalIgnoreCase)) return "pixel blue";
        if (text.Contains("white", StringComparison.OrdinalIgnoreCase)) return "pixel white";
        return text.Length <= 32 ? text : text[..32];
    }

    public int DrawTimingBox(Graphics g, MarkerSequenceEngine engine, OverlayConfig config, double lastLapSeconds)
    {
        using Font font = new(FontFamily.GenericSansSerif, 9, FontStyle.Bold);
        using SolidBrush bg = new(Color.FromArgb(175, Color.DarkGreen));
        using SolidBrush fg = new(Color.White);
        using SolidBrush ok = new(Color.Lime);
        using SolidBrush bad = new(Color.OrangeRed);

        int width = MeasureTimingBoxWidth(g, engine, config, font);
        int lineHeight = (int)Math.Ceiling(g.MeasureString("Ag", font).Height);
        int rows = config.Markers.Count + 2;
        int height = (lineHeight * rows) + 10;

        g.FillRectangle(bg, new Rectangle(0, 0, width, height));

        int y = 6;
        g.DrawString("M  hit/target diff", font, fg, 8, y);
        y += lineHeight;

        foreach (TimingDebugRow row in engine.GetTimingRows())
        {
            int diff = row.Diff ?? 0;
            string diffText = row.Diff.HasValue ? diff.ToString("+0;-0;0") : "";
            string text = $"{row.MarkerNumber}: {(row.HitTick.HasValue ? row.HitTick.Value.ToString() : "-")}/{row.TargetTick} {diffText}";
            Brush brush = !row.HitTick.HasValue ? fg : (diff == 0 ? ok : bad);
            g.DrawString(text, font, brush, 8, y);
            y += lineHeight;
        }

        string lapRow = $"last lap: -/{config.BestPossibleTicks}";
        if (lastLapSeconds > 0)
        {
            int lastTicks = (int)Math.Round(lastLapSeconds / config.TickSeconds);
            int diff = lastTicks - config.BestPossibleTicks;
            lapRow = $"last lap: {lastTicks}/{config.BestPossibleTicks} {diff.ToString("+0;-0;0")}";
        }

        g.DrawString(lapRow, font, fg, 8, y);
        return width;
    }

    public int MeasureTimingBoxWidth(Graphics g, MarkerSequenceEngine engine, OverlayConfig config, Font? font = null)
    {
        bool dispose = font == null;
        font ??= new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold);

        float width = g.MeasureString("M  hit/target diff", font).Width;

        foreach (TimingDebugRow row in engine.GetTimingRows())
        {
            string diffText = row.Diff.HasValue ? row.Diff.Value.ToString("+0;-0;0") : "";
            string text = $"{row.MarkerNumber}: {(row.HitTick.HasValue ? row.HitTick.Value.ToString() : "-")}/{row.TargetTick} {diffText}";
            width = Math.Max(width, g.MeasureString(text, font).Width);
        }

        width = Math.Max(width, g.MeasureString($"last lap: -/{config.BestPossibleTicks}", font).Width);

        if (dispose) font.Dispose();
        return (int)Math.Ceiling(width + 18);
    }

    public void DrawTickSlider(Graphics g, int timingBoxWidth, MarkerSequenceEngine engine, OverlayConfig config, DateTime now)
    {
        int x = timingBoxWidth + 12;
        int y = 154;
        int width = 320;
        int height = 42;

        ClickTimingBarRenderer.Draw(g, new Rectangle(x, y, width, height), engine, config, now, drawLabels: true, drawRecentClick: true);
    }

    public void DrawTopBanner(Graphics g, string text, int x = 12, int y = 12)
    {
        using Font font = new(FontFamily.GenericSansSerif, 12, FontStyle.Bold);
        using SolidBrush bg = new(Color.FromArgb(165, Color.Black));
        using SolidBrush fg = new(Color.White);

        SizeF size = g.MeasureString(text, font);
        RectangleF rect = new(x, y, size.Width + 16, size.Height + 10);
        g.FillRectangle(bg, rect);
        g.DrawString(text, font, fg, x + 8, y + 5);
    }
}
