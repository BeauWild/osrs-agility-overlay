namespace OSRSAgilityOverlay.Models;

public sealed record MarkerState(
    int MarkerIndex,
    MarkerVisualState VisualState,
    DateTime ReadyAt,
    DateTime PerfectUntil,
    int RemainingTicks,
    int LateTicks,
    double PerfectProgress);
