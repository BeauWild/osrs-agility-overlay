using System.Drawing;
using System.Globalization;
using System.Text;
using OSRSAgilityOverlay.Models;

namespace OSRSAgilityOverlay.Services;

public sealed class TimingDebugLogger : IDisposable
{
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private DateTime _startedAt;
    private DateTime _lastFrameAt = DateTime.MinValue;
    private string _appVersion = string.Empty;
    private DateTime _lastFlushAt = DateTime.MinValue;

    public bool Enabled { get; private set; }
    public string LogDirectory { get; private set; } = string.Empty;
    public string LogPath { get; private set; } = string.Empty;

    public void Configure(bool enabled, string appVersion)
    {
        Enabled = enabled;
        _appVersion = appVersion;

        if (!Enabled)
        {
            Dispose();
            return;
        }

        EnsureStarted();
    }

    public bool ShouldLogFrame(DateTime now, MarkerSequenceEngine sequence)
    {
        if (!Enabled) return false;
        if ((now - _lastFrameAt).TotalMilliseconds < 100) return false;

        _lastFrameAt = now;

        if (sequence.Config.Markers.Count == 0) return false;
        if (sequence.IsReadyPrompt) return true;
        if (sequence.ShouldShowMarkerTimingSlider(now)) return true;

        // Log the normal countdown too, but at a slower cadence so the CSV does not
        // become uselessly massive.  This still gives enough timer samples to see
        // if the app is drifting by half/whole ticks between clicks.
        return now.Millisecond % 500 < 120;
    }

    public void Log(
        string eventName,
        DateTime now,
        MarkerSequenceEngine sequence,
        TickClock clock,
        TickSyncService tickSync,
        StatsService stats,
        string notes = "",
        Point? mouse = null,
        bool? inside = null,
        ClickResult? result = null,
        DateTime? actionTime = null,
        int? markerIndexOverride = null,
        MarkerState? stateOverride = null,
        DateTime? readyAtBefore = null,
        int? nextMarkerIndex = null,
        int? nextDelayTicks = null,
        DateTime? nextReadyAt = null)
    {
        if (!Enabled) return;

        try
        {
            EnsureStarted();
            if (_writer == null) return;

            MarkerState state = stateOverride ?? sequence.GetState(now);
            int markerIndex = markerIndexOverride ?? state.MarkerIndex;
            DateTime readyAt = readyAtBefore ?? state.ReadyAt;
            DateTime perfectUntil = sequence.PerfectUntil;

            double tickPhase = clock.Phase(now);
            DateTime previousBoundary = clock.TickBoundaryAtOrBefore(now);
            DateTime nextBoundary = clock.NextTickBoundaryAfter(now);
            double msSincePreviousBoundary = (now - previousBoundary).TotalMilliseconds;
            double msUntilNextBoundary = (nextBoundary - now).TotalMilliseconds;
            double msToReady = (readyAt - now).TotalMilliseconds;
            double msSinceReady = (now - readyAt).TotalMilliseconds;
            double externalAgeMs = clock.LastExternalSyncAt == DateTime.MinValue ? double.NaN : (now - clock.LastExternalSyncAt).TotalMilliseconds;
            double edgeAgeMs = tickSync.LastTrackedBoundaryAt == DateTime.MinValue ? double.NaN : (now - tickSync.LastTrackedBoundaryAt).TotalMilliseconds;
            double sampleAgeMs = tickSync.LastSampleAt == DateTime.MinValue ? double.NaN : (now - tickSync.LastSampleAt).TotalMilliseconds;
            int currentDelayTicks = markerIndex >= 0 && markerIndex < sequence.Config.Markers.Count
                ? sequence.Config.Markers[markerIndex].DelayTicks
                : 0;

            double actionDeltaMs = actionTime.HasValue ? (actionTime.Value - now).TotalMilliseconds : double.NaN;
            double nextMsToReady = nextReadyAt.HasValue ? (nextReadyAt.Value - now).TotalMilliseconds : double.NaN;

            var values = new List<string>
            {
                now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                now.ToString("O", CultureInfo.InvariantCulture),
                ((now - _startedAt).TotalMilliseconds).ToString("0.000", CultureInfo.InvariantCulture),
                _appVersion,
                eventName,
                (markerIndex + 1).ToString(CultureInfo.InvariantCulture),
                currentDelayTicks.ToString(CultureInfo.InvariantCulture),
                state.VisualState.ToString(),
                sequence.LapRunning ? "1" : "0",
                sequence.IsReadyPrompt ? "1" : "0",
                stats.Stats.LapRunning ? "1" : "0",
                FormatDate(now),
                FormatDate(readyAt),
                FormatDate(perfectUntil),
                FormatNumber(msToReady),
                FormatNumber(msSinceReady),
                state.RemainingTicks.ToString(CultureInfo.InvariantCulture),
                state.LateTicks.ToString(CultureInfo.InvariantCulture),
                FormatNumber(tickPhase),
                FormatNumber(tickPhase * clock.TickSeconds * 1000.0),
                FormatDate(previousBoundary),
                FormatDate(nextBoundary),
                FormatNumber(msSincePreviousBoundary),
                FormatNumber(msUntilNextBoundary),
                clock.ExternalSyncLocked ? "1" : "0",
                clock.IsExternalSyncFresh(now) ? "1" : "0",
                FormatNumber(externalAgeMs),
                FormatNumber(clock.ExternalSyncConfidence * 100.0),
                FormatNumber(clock.OffsetSeconds * 1000.0),
                FormatNumber(clock.LastObservedSyncCorrectionSeconds * 1000.0),
                FormatNumber(clock.LastAppliedSyncCorrectionSeconds * 1000.0),
                clock.LastSyncAdjustmentText,
                tickSync.Status,
                tickSync.Detail,
                FormatNumber(tickSync.LastPhase),
                tickSync.LastLineX?.ToString(CultureInfo.InvariantCulture) ?? "",
                FormatNumber(tickSync.Confidence * 100.0),
                FormatNumber(tickSync.LastCaptureMilliseconds),
                FormatNumber(tickSync.LastScanMilliseconds),
                FormatDate(tickSync.LastSampleAt),
                FormatNumber(sampleAgeMs),
                FormatDate(tickSync.LastTrackedBoundaryAt),
                FormatNumber(edgeAgeMs),
                FormatNumber(tickSync.LastTrackedBoundaryConfidence * 100.0),
                mouse?.X.ToString(CultureInfo.InvariantCulture) ?? "",
                mouse?.Y.ToString(CultureInfo.InvariantCulture) ?? "",
                inside.HasValue ? (inside.Value ? "1" : "0") : "",
                result?.Accepted == true ? "1" : result == null ? "" : "0",
                result?.Perfect == true ? "1" : result == null ? "" : "0",
                result?.Late == true ? "1" : result == null ? "" : "0",
                result?.QueuedEarly == true ? "1" : result == null ? "" : "0",
                result != null ? FormatNumber(result.MsBeforeReady) : "",
                sequence.Config.WorldLagMs.ToString(CultureInfo.InvariantCulture),
                sequence.Config.QueueSafetyMs.ToString(CultureInfo.InvariantCulture),
                sequence.Config.EffectiveEarlyClickQueueMs.ToString(CultureInfo.InvariantCulture),
                result?.HitTick.ToString(CultureInfo.InvariantCulture) ?? "",
                result?.TargetTick.ToString(CultureInfo.InvariantCulture) ?? "",
                result != null ? FormatNumber(result.SliderPosition) : "",
                actionTime.HasValue ? FormatDate(actionTime.Value) : "",
                FormatNumber(actionDeltaMs),
                nextMarkerIndex.HasValue ? (nextMarkerIndex.Value + 1).ToString(CultureInfo.InvariantCulture) : "",
                nextDelayTicks?.ToString(CultureInfo.InvariantCulture) ?? "",
                nextReadyAt.HasValue ? FormatDate(nextReadyAt.Value) : "",
                FormatNumber(nextMsToReady),
                FormatDate(sequence.LapStartedAt),
                FormatDate(stats.Stats.LapStartedAt),
                FormatNumber(stats.Stats.CurrentLapSeconds(now) * 1000.0),
                FormatNumber(stats.Stats.TotalSeconds(now) * 1000.0),
                FormatNumber(stats.Stats.LostSeconds * 1000.0),
                notes
            };

            lock (_lock)
            {
                _writer.WriteLine(ToCsv(values));

                // Flushing every row made the test build noticeably laggy while the
                // tick tracker was sampling at ~20 Hz. Keep the log safe enough for
                // testing by flushing important events immediately and normal samples
                // about once per second.
                bool important = eventName.Contains("CLICK", StringComparison.OrdinalIgnoreCase)
                    || eventName.Contains("LAP", StringComparison.OrdinalIgnoreCase)
                    || eventName.Contains("APP_CLOSE", StringComparison.OrdinalIgnoreCase);

                if (important || (now - _lastFlushAt).TotalSeconds >= 1.0)
                {
                    _writer.Flush();
                    _lastFlushAt = now;
                }
            }
        }
        catch
        {
            // Logging must never break the overlay while testing.
        }
    }

    private void EnsureStarted()
    {
        if (_writer != null) return;

        _startedAt = DateTime.Now;
        LogDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(LogDirectory);
        LogPath = Path.Combine(LogDirectory, $"OverlayTimingLog_{_startedAt:yyyyMMdd_HHmmss}.csv");

        _writer = new StreamWriter(LogPath, append: false, Encoding.UTF8);
        _writer.WriteLine(ToCsv(HeaderColumns()));
        _writer.Flush();
    }

    private static IEnumerable<string> HeaderColumns() => new[]
    {
        "utc_time",
        "local_time",
        "elapsed_ms",
        "app_version",
        "event",
        "marker",
        "marker_delay_ticks",
        "state",
        "sequence_lap_running",
        "ready_prompt",
        "stats_lap_running",
        "now",
        "ready_at",
        "perfect_until",
        "ms_to_ready",
        "ms_since_ready",
        "remaining_ticks",
        "late_ticks",
        "clock_phase",
        "clock_phase_ms",
        "previous_tick_boundary",
        "next_tick_boundary",
        "ms_since_previous_tick",
        "ms_until_next_tick",
        "external_locked",
        "external_fresh",
        "external_age_ms",
        "external_conf_pct",
        "clock_offset_ms",
        "clock_sync_wanted_ms",
        "clock_sync_applied_ms",
        "clock_sync_filter",
        "ticksync_status",
        "ticksync_detail",
        "ticksync_phase",
        "ticksync_line_x",
        "ticksync_conf_pct",
        "capture_ms",
        "scan_ms",
        "last_sample_at",
        "sample_age_ms",
        "last_edge_at",
        "edge_age_ms",
        "edge_conf_pct",
        "mouse_x",
        "mouse_y",
        "inside_marker",
        "click_accepted",
        "click_perfect",
        "click_late",
        "click_queued_early",
        "click_ms_before_ready",
        "world_lag_ms",
        "queue_safety_ms",
        "effective_queue_ms",
        "hit_tick",
        "target_tick",
        "slider_position",
        "action_time",
        "action_delta_ms",
        "next_marker",
        "next_delay_ticks",
        "next_ready_at",
        "next_ms_to_ready",
        "sequence_lap_started_at",
        "stats_lap_started_at",
        "current_lap_ms",
        "total_session_ms",
        "lost_ms",
        "notes"
    };

    private static string FormatDate(DateTime dt)
    {
        return dt == DateTime.MinValue || dt == DateTime.MaxValue ? "" : dt.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string FormatNumber(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? "" : value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string ToCsv(IEnumerable<string> values)
    {
        return string.Join(",", values.Select(EscapeCsv));
    }

    private static string EscapeCsv(string value)
    {
        value ??= string.Empty;
        if (value.Contains('"')) value = value.Replace("\"", "\"\"");
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? $"\"{value}\"" : value;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }
}
