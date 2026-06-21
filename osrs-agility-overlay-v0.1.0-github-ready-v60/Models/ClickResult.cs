namespace OSRSAgilityOverlay.Models;

public sealed record ClickResult(
    bool Accepted,
    bool Perfect,
    bool Late,
    int MarkerIndex,
    int HitTick,
    int TargetTick,
    double SliderPosition,
    bool QueuedEarly = false,
    double MsBeforeReady = 0);
