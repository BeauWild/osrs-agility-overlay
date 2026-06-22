namespace OSRSAgilityOverlay.Services;

public readonly record struct TickSyncCorrectionResult(
    bool DidApply,
    bool WasClamped,
    double RequestedSeconds,
    double AppliedSeconds,
    double Confidence,
    string Status,
    string Detail)
{
    public static TickSyncCorrectionResult Applied(double requestedSeconds, double appliedSeconds, double confidence, bool wasClamped, string detail)
    {
        return new TickSyncCorrectionResult(true, wasClamped, requestedSeconds, appliedSeconds, confidence, wasClamped ? "CLAMPED" : "LOCKED", detail);
    }

    public static TickSyncCorrectionResult Ignored(double requestedSeconds, double appliedSeconds, double confidence, string status, string detail)
    {
        return new TickSyncCorrectionResult(false, false, requestedSeconds, appliedSeconds, confidence, status, detail);
    }

    public string UiAdjustmentText
    {
        get
        {
            if (DidApply && WasClamped)
                return $"clamped {AppliedSeconds:+0.000;-0.000;0.000}s (wanted {RequestedSeconds:+0.000;-0.000;0.000}s)";

            if (DidApply)
                return $"adjust {AppliedSeconds:+0.000;-0.000;0.000}s";

            if (Status == "LOW CONF")
                return $"low conf {Confidence * 100:0}% ignored {RequestedSeconds:+0.000;-0.000;0.000}s";

            if (Status == "SUSPICIOUS")
                return $"suspicious ignored {RequestedSeconds:+0.000;-0.000;0.000}s";

            if (Status == "NO EDGE")
                return "no edge - no correction";

            return $"ignored {RequestedSeconds:+0.000;-0.000;0.000}s";
        }
    }
}
