using OSRSAgilityOverlay.Models;

namespace OSRSAgilityOverlay.Input;

public static class ClickDetector
{
    public static bool IsInside(Point cursorScreen, Marker marker, Rectangle targetRect, bool anchorToWindow, int extraRadius)
    {
        Point markerScreen = anchorToWindow && !targetRect.IsEmpty
            ? new Point(targetRect.Left + marker.X, targetRect.Top + marker.Y)
            : new Point(marker.X, marker.Y);

        int radius = marker.Radius + Math.Max(0, extraRadius);
        double dx = cursorScreen.X - markerScreen.X;
        double dy = cursorScreen.Y - markerScreen.Y;

        return (dx * dx + dy * dy) <= (radius * radius);
    }
}
