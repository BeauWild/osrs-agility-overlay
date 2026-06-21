using OSRSAgilityOverlay.Models;
using OSRSAgilityOverlay.Services;

namespace OSRSAgilityOverlay.Rendering;

public static class ClickTimingBarRenderer
{
    public static void Draw(Graphics g, Rectangle rect, MarkerSequenceEngine engine, OverlayConfig config, DateTime now, bool drawLabels, bool drawRecentClick = true)
    {
        using SolidBrush bg = new(Color.FromArgb(170, Color.Black));
        g.FillRectangle(bg, rect);

        int barX = rect.X + 10;
        int barY = rect.Y + (drawLabels ? 18 : 8);
        int barW = Math.Max(20, rect.Width - 20);
        int barH = 10;
        int segments = MarkerSequenceEngine.ClickTimingTicksBeforeReady + MarkerSequenceEngine.ClickTimingTicksAfterReady;
        float segmentW = barW / (float)segments;

        using SolidBrush red = new(Color.FromArgb(180, Color.Red));
        using SolidBrush green = new(Color.FromArgb(190, Color.Lime));

        for (int i = 0; i < segments; i++)
        {
            int x = barX + (int)Math.Round(i * segmentW);
            int nextX = i == segments - 1 ? barX + barW : barX + (int)Math.Round((i + 1) * segmentW);
            bool perfectSegment = i == MarkerSequenceEngine.ClickTimingTicksBeforeReady;
            g.FillRectangle(perfectSegment ? green : red, x, barY, Math.Max(1, nextX - x), barH);
        }

        using Pen outline = new(Color.White, 1);
        g.DrawRectangle(outline, barX, barY, barW, barH);

        int currentX = barX + (int)Math.Round(engine.TimingSliderPosition(now) * barW);
        using Pen cursorPen = new(Color.White, 3);
        g.DrawLine(cursorPen, currentX, barY - 6, currentX, barY + barH + 6);

        if (drawRecentClick && engine.HasRecentClick(now) && engine.LastClickSliderPosition.HasValue)
        {
            int clickX = barX + (int)Math.Round(engine.LastClickSliderPosition.Value * barW);
            using Pen clickPen = new(Color.Black, 3);
            g.DrawLine(clickPen, clickX, barY - 8, clickX, barY + barH + 8);
        }

        if (!drawLabels) return;

        using Font font = new(FontFamily.GenericSansSerif, 8, FontStyle.Bold);
        using SolidBrush fg = new(Color.White);

        for (int i = 0; i < segments; i++)
        {
            int tick = i - MarkerSequenceEngine.ClickTimingTicksBeforeReady;
            string label = tick > 0 ? $"+{tick}" : tick.ToString();
            float labelCenter = barX + (i + 0.5f) * segmentW;
            SizeF size = g.MeasureString(label, font);
            g.DrawString(label, font, fg, labelCenter - size.Width / 2, rect.Y + 2);
        }
    }
}
