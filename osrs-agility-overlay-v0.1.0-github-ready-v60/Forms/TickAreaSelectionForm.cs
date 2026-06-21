namespace OSRSAgilityOverlay.Forms;

public sealed class TickAreaSelectionForm : Form
{
    public Rectangle SelectedRectangle { get; private set; } = Rectangle.Empty;

    public TickAreaSelectionForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Bounds = SystemInformation.VirtualScreen;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Opacity = 0.28;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Focus();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            Hide();
            Close();
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        Point screen = PointToScreen(e.Location);
        SelectedRectangle = new Rectangle(screen.X, screen.Y, 1, 1);
        DialogResult = DialogResult.OK;
        Hide();
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Hide();
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;

        using Font font = new(FontFamily.GenericSansSerif, 16, FontStyle.Bold);
        using SolidBrush text = new(Color.White);
        using SolidBrush shadow = new(Color.Black);
        string msg = "Click a single pixel inside the blue/white tick circle plugin. Pick a spot that turns blue then white every tick. Right-click or Esc cancels.";
        PointF msgPoint = new(24, 24);
        g.DrawString(msg, font, shadow, msgPoint.X + 2, msgPoint.Y + 2);
        g.DrawString(msg, font, text, msgPoint);
    }
}
