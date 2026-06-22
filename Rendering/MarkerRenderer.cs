using System.Drawing.Drawing2D;
using OSRSAgilityOverlay.Models;
using OSRSAgilityOverlay.Services;

namespace OSRSAgilityOverlay.Rendering;

public sealed class MarkerRenderer
{
    private string? _floatingText;
    private Point _floatingPoint;
    private DateTime _floatingUntil;

    public void ShowPerfect(Point point)
    {
        _floatingText = "Perfect!";
        _floatingPoint = point;
        _floatingUntil = DateTime.Now.AddSeconds(0.85);
    }

    public void DrawMarker(Graphics g, Marker marker, Point p, int index, bool active, bool showName, bool showTimer, bool editMode, bool lapRunning, MarkerSequenceEngine engine, OverlayConfig config, DateTime now, bool minimalPreview = false)
    {
        MarkerState state = engine.GetState(now);
        bool isCurrent = index == engine.CurrentIndex;
        int r = marker.Radius;

        Color markerColor;
        Color fillColor;

        if (!isCurrent)
        {
            markerColor = Color.Lime;
            fillColor = Color.FromArgb(45, Color.Lime);
        }
        else if (state.VisualState == MarkerVisualState.Waiting)
        {
            markerColor = Color.Orange;
            fillColor = Color.FromArgb(85, Color.Orange);
        }
        else if (state.VisualState == MarkerVisualState.Perfect)
        {
            markerColor = Color.Lime;
            fillColor = Color.FromArgb(180, Color.Lime);
        }
        else
        {
            bool flash = ((now.Millisecond / 180) % 2) == 0;
            markerColor = flash ? Color.Red : Color.DarkRed;
            fillColor = flash ? Color.FromArgb(175, Color.Red) : Color.FromArgb(105, Color.Red);
        }

        int clickRadius = r + Math.Max(0, config.GlobalClickExtraRadius);

        if (minimalPreview)
        {
            // Do not use pure Color.Lime here because the overlay TransparencyKey is Lime;
            // some systems will key it out and make the preview invisible.
            using Pen previewPen = new(Color.FromArgb(235, 80, 255, 80), 2);
            int previewRadius = Math.Max(3, r);
            g.DrawEllipse(previewPen, p.X - previewRadius, p.Y - previewRadius, previewRadius * 2, previewRadius * 2);
            return;
        }

        if (editMode)
        {
            using Pen clickPen = new(Color.FromArgb(130, Color.White), 1) { DashStyle = DashStyle.Dash };
            g.DrawEllipse(clickPen, p.X - clickRadius, p.Y - clickRadius, clickRadius * 2, clickRadius * 2);
        }

        if (active)
        {
            using Pen halo = new(Color.FromArgb(180, Color.White), 7);
            g.DrawEllipse(halo, p.X - r - 5, p.Y - r - 5, (r + 5) * 2, (r + 5) * 2);
        }

        Rectangle circleRect = new(p.X - r, p.Y - r, r * 2, r * 2);
        using Pen pen = new(markerColor, active ? 4 : 2);
        using SolidBrush baseFill = new(fillColor);

        if (isCurrent && state.VisualState == MarkerVisualState.Perfect)
        {
            g.DrawEllipse(pen, circleRect);

            int fillWidth = (int)Math.Round(circleRect.Width * state.PerfectProgress);
            if (fillWidth > 0)
            {
                GraphicsState saved = g.Save();
                using GraphicsPath path = new();
                path.AddEllipse(circleRect);
                g.SetClip(path, CombineMode.Intersect);
                using SolidBrush greenFill = new(Color.FromArgb(195, Color.Lime));
                g.FillRectangle(greenFill, circleRect.Left, circleRect.Top, fillWidth, circleRect.Height);
                g.Restore(saved);
            }

            g.DrawEllipse(pen, circleRect);
        }
        else
        {
            g.FillEllipse(baseFill, circleRect);
            g.DrawEllipse(pen, circleRect);
        }

        string mainText;
        if (isCurrent && state.VisualState == MarkerVisualState.Waiting && showTimer)
            mainText = state.RemainingTicks.ToString();
        else if (isCurrent && engine.IsReadyPrompt && showTimer)
            mainText = "READY";
        else if (isCurrent && state.VisualState == MarkerVisualState.Perfect && showTimer)
            mainText = "0";
        else if (isCurrent && state.VisualState == MarkerVisualState.Late && showTimer)
            mainText = $"+{state.LateTicks}";
        else
            mainText = (index + 1).ToString();

        float mainTextSize = string.Equals(mainText, "READY", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(7, config.MarkerNumberSize - 3)
            : config.MarkerNumberSize;

        using Font font = new(FontFamily.GenericSansSerif, mainTextSize, FontStyle.Bold);
        bool readyText = string.Equals(mainText, "READY", StringComparison.OrdinalIgnoreCase);
        Color textColor = readyText ? Color.FromArgb(80, 255, 80) : markerColor;
        using SolidBrush textBrush = new(textColor);
        SizeF size = g.MeasureString(mainText, font);
        float textX = p.X - size.Width / 2;
        float textY = p.Y - size.Height / 2;

        if (readyText)
        {
            using SolidBrush shadowBrush = new(Color.FromArgb(210, Color.Black));
            g.DrawString(mainText, font, shadowBrush, textX + 1, textY + 1);
        }

        g.DrawString(mainText, font, textBrush, textX, textY);

        if (isCurrent && showTimer && !editMode && !config.MinimalMode && engine.ShouldShowMarkerTimingSlider(now))
            DrawTimingBarUnderMarker(g, p, r, engine, config, now);

        if (_floatingText != null && now < _floatingUntil)
        {
            using Font perfectFont = new(FontFamily.GenericSansSerif, 12, FontStyle.Bold);
            using SolidBrush perfectBrush = new(Color.Lime);
            SizeF perfectSize = g.MeasureString(_floatingText, perfectFont);
            g.DrawString(_floatingText, perfectFont, perfectBrush, _floatingPoint.X - perfectSize.Width / 2, _floatingPoint.Y - r - 30);
        }

        if (showName)
        {
            using Font small = new(FontFamily.GenericSansSerif, 8, FontStyle.Bold);
            string label = $"{index + 1}. {marker.Name} | {marker.DelayTicks}t | r{marker.Radius}";
            SizeF labelSize = g.MeasureString(label, small);
            RectangleF bgRect = new(p.X + r + 4, p.Y - 10, labelSize.Width + 8, labelSize.Height + 4);
            using SolidBrush labelBg = new(Color.FromArgb(active ? 190 : 130, Color.Black));
            g.FillRectangle(labelBg, bgRect);
            g.DrawString(label, small, textBrush, bgRect.X + 4, bgRect.Y + 2);
        }
    }

    private static void DrawTimingBarUnderMarker(Graphics g, Point p, int radius, MarkerSequenceEngine engine, OverlayConfig config, DateTime now)
    {
        int width = Math.Max(150, radius * 7);
        int height = 26;
        int x = p.X - width / 2;
        int y = p.Y + radius + 10;

        // Under-marker timing bars are for the upcoming marker only. Do not draw the
        // held black click marker here, because for short-delay obstacles it can show
        // the previous obstacle's click timing for a tick before disappearing.
        ClickTimingBarRenderer.Draw(g, new Rectangle(x, y, width, height), engine, config, now, drawLabels: false, drawRecentClick: false);
    }
}
