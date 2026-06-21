using System.Drawing;
using OSRSAgilityOverlay.Input;
using OSRSAgilityOverlay.Models;
using Xunit;

namespace OSRSAgilityOverlay.Tests;

public sealed class ClickDetectorTests
{
    [Fact]
    public void ClickInside_UsesRadiusPlusExtra()
    {
        var marker = new Marker { X = 100, Y = 100, Radius = 16 };

        bool inside = ClickDetector.IsInside(
            new Point(125, 100),
            marker,
            Rectangle.Empty,
            anchorToWindow: false,
            extraRadius: 10);

        Assert.True(inside);
    }

    [Fact]
    public void ClickOutside_ReturnsFalse()
    {
        var marker = new Marker { X = 100, Y = 100, Radius = 16 };

        bool inside = ClickDetector.IsInside(
            new Point(200, 100),
            marker,
            Rectangle.Empty,
            anchorToWindow: false,
            extraRadius: 10);

        Assert.False(inside);
    }
}
