using OSRSAgilityOverlay.Services;

namespace OSRSAgilityOverlay.Forms;

public sealed class TickSyncPanel : Control
{
    private readonly TickClock _clock;
    private readonly Func<DateTime?> _sessionStartProvider;

    public TickSyncPanel(TickClock clock, Func<DateTime?> sessionStartProvider)
    {
        _clock = clock;
        _sessionStartProvider = sessionStartProvider;
        DoubleBuffered = true;
        BackColor = Color.Black;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        int margin = 3;
        Rectangle rect = new(margin, margin, Math.Max(10, Width - margin * 2 - 1), Math.Max(10, Height - margin * 2 - 1));

        int greenW = (int)Math.Round(rect.Width * 0.83);
        int redW = rect.Width - greenW;

        using SolidBrush green = new(Color.Lime);
        using SolidBrush red = new(Color.Red);
        using Pen border = new(Color.Black, 2);

        g.FillRectangle(green, rect.Left, rect.Top, greenW, rect.Height);
        g.FillRectangle(red, rect.Left + greenW, rect.Top, redW, rect.Height);
        g.DrawRectangle(border, rect);

        // This panel displays the external OSRS/RuneLite tick clock. Do not anchor
        // it to the lap/session start, otherwise the visual bar can look out of
        // phase even while the marker engine is correctly locked.
        double phase = _clock.Phase(DateTime.Now);
        int x = rect.Left + (int)Math.Round(phase * rect.Width);

        using Pen whiteLine = new(Color.White, 5);
        using Pen blackLine = new(Color.Black, 3);
        g.DrawLine(whiteLine, x, rect.Top - 1, x, rect.Bottom + 1);
        g.DrawLine(blackLine, x, rect.Top - 1, x, rect.Bottom + 1);

        using Font font = new(FontFamily.GenericSansSerif, 9, FontStyle.Bold);
        string label = $"{phase * _clock.TickSeconds:0.00}s / {_clock.TickSeconds:0.00}s";
        SizeF size = g.MeasureString(label, font);

        using SolidBrush shadow = new(Color.Black);
        using SolidBrush text = new(Color.White);
        float tx = rect.Left + 8;
        float ty = rect.Top + (rect.Height - size.Height) / 2;
        g.DrawString(label, font, shadow, tx + 1, ty + 1);
        g.DrawString(label, font, text, tx, ty);
    }
}
