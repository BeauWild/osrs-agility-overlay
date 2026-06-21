namespace OSRSAgilityOverlay.Services;

public sealed class TickClock
{
    public const double ExternalSyncFreshSeconds = 12.0; // diagnostic only; the internal clock stays alive after the first good lock

    private const double StableCorrectionDeadbandSeconds = 0.015;
    private const double StableCorrectionSoftLimitSeconds = 0.060;
    private const double StableCorrectionSuspiciousSeconds = 0.140;
    private const double StableCorrectionMaxStepSeconds = 0.002;
    private const double StableCorrectionMaxTotalFromLockSeconds = 0.030;

    private double _baselineOffsetSeconds;
    private double _cumulativeAutoCorrectionSeconds;
    private int _sameDirectionCorrectionCount;
    private int _lastCorrectionDirection;

    public double TickSeconds { get; set; } = 0.6;
    public double OffsetSeconds { get; set; } = 0.0;
    public bool ExternalSyncLocked { get; private set; }
    public DateTime LastExternalSyncAt { get; private set; } = DateTime.MinValue;
    public double ExternalSyncConfidence { get; private set; }
    public double LastObservedSyncCorrectionSeconds { get; private set; }
    public double LastAppliedSyncCorrectionSeconds { get; private set; }
    public string LastSyncAdjustmentText { get; private set; } = string.Empty;

    public DateTime TargetTime(DateTime lapStart, int targetTick)
    {
        return lapStart.AddSeconds((targetTick * TickSeconds) - ScheduleOffsetSeconds);
    }

    public double ScheduleOffsetSeconds => ExternalSyncLocked ? 0.0 : OffsetSeconds;

    public int ElapsedTicks(DateTime lapStart, DateTime now)
    {
        return Math.Max(0, (int)Math.Round((now - lapStart).TotalSeconds / TickSeconds));
    }

    public double Phase(DateTime now, DateTime? sessionStart = null)
    {
        double seconds = sessionStart.HasValue
            ? (now - sessionStart.Value).TotalSeconds + OffsetSeconds
            : (now - DateTime.Today).TotalSeconds + OffsetSeconds;

        double phase = seconds % TickSeconds;
        if (phase < 0) phase += TickSeconds;
        return phase / TickSeconds;
    }

    public DateTime TickBoundaryAtOrBefore(DateTime now)
    {
        double phaseSeconds = Phase(now) * TickSeconds;
        if (phaseSeconds < 0.001 || phaseSeconds >= TickSeconds - 0.001)
            phaseSeconds = 0;

        return now.AddSeconds(-phaseSeconds);
    }

    public DateTime ActionTimeForClick(DateTime now)
    {
        return IsExternalSyncFresh(now) ? ClosestTickBoundaryTo(now) : now;
    }

    public DateTime ClosestTickBoundaryTo(DateTime now)
    {
        DateTime previous = TickBoundaryAtOrBefore(now);
        DateTime next = previous.AddSeconds(Math.Max(0.1, TickSeconds));

        double msAfterPrevious = (now - previous).TotalMilliseconds;
        double msBeforeNext = (next - now).TotalMilliseconds;
        return msAfterPrevious <= msBeforeNext ? previous : next;
    }

    public DateTime NextTickBoundaryAfter(DateTime now)
    {
        double tickSeconds = Math.Max(0.1, TickSeconds);
        double phaseSeconds = Phase(now) * tickSeconds;
        double secondsUntilNext = tickSeconds - phaseSeconds;

        if (phaseSeconds <= 0.001 || secondsUntilNext >= tickSeconds)
            secondsUntilNext = tickSeconds;
        if (secondsUntilNext < 0)
            secondsUntilNext += tickSeconds;

        return now.AddSeconds(secondsUntilNext);
    }

    public bool IsExternalSyncFresh(DateTime now)
    {
        return ExternalSyncLocked;
    }

    public void SetExternalPhase(DateTime observedAt, double phase, double confidence)
    {
        phase = Math.Clamp(phase, 0, 0.999999);
        confidence = Math.Clamp(confidence, 0, 1);

        double observedPhaseSeconds = phase * TickSeconds;
        double currentSeconds = (observedAt - DateTime.Today).TotalSeconds;
        double observedOffset = NormalizeOffset(observedPhaseSeconds - currentSeconds);

        if (!ExternalSyncLocked)
        {
            OffsetSeconds = observedOffset;
            _baselineOffsetSeconds = observedOffset;
            _cumulativeAutoCorrectionSeconds = 0;
            ExternalSyncLocked = confidence >= 0.35;
            LastExternalSyncAt = observedAt;
            ExternalSyncConfidence = confidence;
            LastObservedSyncCorrectionSeconds = 0;
            LastAppliedSyncCorrectionSeconds = 0;
            LastSyncAdjustmentText = $"lock {OffsetSeconds * 1000.0:+0;-0;0}ms";
            _sameDirectionCorrectionCount = 0;
            _lastCorrectionDirection = 0;
            return;
        }

        double wantedCorrection = NormalizeOffset(observedOffset - OffsetSeconds);
        LastObservedSyncCorrectionSeconds = wantedCorrection;
        LastAppliedSyncCorrectionSeconds = 0;
        ExternalSyncLocked = true;
        LastExternalSyncAt = observedAt;
        ExternalSyncConfidence = confidence;

        if (confidence < 0.80)
        {
            LastSyncAdjustmentText = $"low conf ignored {wantedCorrection * 1000.0:+0;-0;0}ms";
            return;
        }

        double absWanted = Math.Abs(wantedCorrection);
        if (absWanted <= StableCorrectionDeadbandSeconds)
        {
            LastSyncAdjustmentText = $"stable {wantedCorrection * 1000.0:+0;-0;0}ms";
            _sameDirectionCorrectionCount = 0;
            _lastCorrectionDirection = 0;
            return;
        }

        int direction = wantedCorrection > 0 ? 1 : -1;
        if (direction == _lastCorrectionDirection)
            _sameDirectionCorrectionCount++;
        else
        {
            _lastCorrectionDirection = direction;
            _sameDirectionCorrectionCount = 1;
        }

        if (absWanted > StableCorrectionSuspiciousSeconds)
        {
            LastSyncAdjustmentText = $"suspicious ignored {wantedCorrection * 1000.0:+0;-0;0}ms";
            return;
        }

        bool mediumCorrection = absWanted > StableCorrectionSoftLimitSeconds;
        if (mediumCorrection && _sameDirectionCorrectionCount < 3)
        {
            LastSyncAdjustmentText = $"hold {wantedCorrection * 1000.0:+0;-0;0}ms ({_sameDirectionCorrectionCount}/3)";
            return;
        }

        double applied = Math.Clamp(wantedCorrection * 0.10, -StableCorrectionMaxStepSeconds, StableCorrectionMaxStepSeconds);
        double newCumulative = Math.Clamp(_cumulativeAutoCorrectionSeconds + applied,
            -StableCorrectionMaxTotalFromLockSeconds,
             StableCorrectionMaxTotalFromLockSeconds);

        if (Math.Abs(newCumulative - _cumulativeAutoCorrectionSeconds) < 0.000001)
        {
            LastSyncAdjustmentText = $"cap reached {(_cumulativeAutoCorrectionSeconds * 1000.0):+0;-0;0}ms";
            return;
        }

        _cumulativeAutoCorrectionSeconds = newCumulative;
        OffsetSeconds = NormalizeOffset(_baselineOffsetSeconds + _cumulativeAutoCorrectionSeconds);
        LastAppliedSyncCorrectionSeconds = applied;
        LastSyncAdjustmentText = mediumCorrection
            ? $"slow adjust {applied * 1000.0:+0;-0;0}ms (total {_cumulativeAutoCorrectionSeconds * 1000.0:+0;-0;0}ms)"
            : $"tiny adjust {applied * 1000.0:+0;-0;0}ms (total {_cumulativeAutoCorrectionSeconds * 1000.0:+0;-0;0}ms)";
    }

    public void ClearExternalSync()
    {
        ExternalSyncLocked = false;
        ExternalSyncConfidence = 0;
        LastExternalSyncAt = DateTime.MinValue;
        LastObservedSyncCorrectionSeconds = 0;
        LastAppliedSyncCorrectionSeconds = 0;
        LastSyncAdjustmentText = string.Empty;
        _sameDirectionCorrectionCount = 0;
        _lastCorrectionDirection = 0;
        _baselineOffsetSeconds = OffsetSeconds;
        _cumulativeAutoCorrectionSeconds = 0;
    }

    public void Nudge(double seconds)
    {
        OffsetSeconds = NormalizeOffset(OffsetSeconds + seconds);
        ClearExternalSync();
    }

    private double NormalizeOffset(double value)
    {
        double tick = Math.Max(0.1, TickSeconds);
        value %= tick;
        if (value > tick / 2) value -= tick;
        if (value < -tick / 2) value += tick;
        return value;
    }
}
