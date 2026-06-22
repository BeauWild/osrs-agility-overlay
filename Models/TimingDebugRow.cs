namespace OSRSAgilityOverlay.Models;

public sealed record TimingDebugRow(int MarkerNumber, int? HitTick, int TargetTick)
{
    public int? Diff => HitTick.HasValue ? HitTick.Value - TargetTick : null;
}
