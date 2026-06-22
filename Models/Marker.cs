namespace OSRSAgilityOverlay.Models;

public sealed class Marker
{
    public string Name { get; set; } = "Marker";
    public int X { get; set; }
    public int Y { get; set; }
    public int Radius { get; set; } = 16;
    public int DelayTicks { get; set; } = 4;

    public Marker Clone() => new()
    {
        Name = Name,
        X = X,
        Y = Y,
        Radius = Radius,
        DelayTicks = DelayTicks
    };
}
