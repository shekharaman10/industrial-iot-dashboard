using System.Collections.Concurrent;
using IotDashboard.Application.Interfaces;
using IotDashboard.Domain.Entities;
using IotDashboard.Domain.Enums;

namespace IotDashboard.Application.Services;

/// <summary>
/// Stateful rolling-window anomaly detection engine.
///
/// Algorithm overview:
///   1. Maintain a sliding window of the last WINDOW_SIZE readings per (device, metric).
///   2. Compute moving average (μ) and sample standard deviation (σ) over the window.
///   3. Compute Z-score: z = |value − μ| / σ
///   4. Classify severity based on z-score thresholds.
///   5. Additionally detect rate-of-change spikes regardless of z-score.
///
/// Baseline:
///   The first BASELINE_SAMPLES readings establish a "normal" operating baseline
///   stored separately from the rolling window. This baseline is used in alert
///   messages to give operators context ("value is 2.4× normal baseline").
///
/// Thread safety:
///   State per (deviceId, metric) is locked independently, so concurrent
///   reads from different devices do not block each other.
///
/// Tuning parameters (adjust per deployment environment):
///   WINDOW_SIZE       — larger = more stable but slower to detect drift
///   WARNING_SIGMA     — lower = more alerts (noisy); higher = fewer (misses)
///   ROC_THRESHOLD_PCT — 40% change in one step; tune per machinery type
/// </summary>
public sealed class AnalyticsEngine : IAnalyticsEngine
{
    // ── Tuneable constants ───────────────────────────────────────────────────
    private const int    WINDOW_SIZE       = 60;    // rolling window depth
    private const int    BASELINE_SAMPLES  = 300;   // ~15 min at 2 Hz to set baseline
    private const double WARNING_SIGMA     = 1.5;
    private const double CRITICAL_SIGMA    = 2.5;
    private const double FAULT_SIGMA       = 3.5;
    private const double ROC_THRESHOLD_PCT = 0.40;  // 40 % single-step jump

    private readonly ConcurrentDictionary<string, MetricState> _states = new();

    public AnalysisResult Evaluate(string deviceId, float value, SensorType metric)
    {
        var key   = BuildKey(deviceId, metric);
        var state = _states.GetOrAdd(key, _ => new MetricState());

        lock (state)
        {
            bool rateSpike = DetectRateSpike(state, value);

            // Update window
            state.Window.Enqueue(value);
            if (state.Window.Count > WINDOW_SIZE)
                state.Window.Dequeue();

            state.LastValue = value;
            state.TotalSamples++;

            // Need at least 10 samples for meaningful statistics
            if (state.TotalSamples < 10)
                return NotEnoughData(value);

            (double avg, double stddev) = ComputeStats(state.Window);

            // Snapshot baseline after initial warm-up period
            if (state.TotalSamples == BASELINE_SAMPLES)
            {
                state.BaselineAvg    = avg;
                state.BaselineStdDev = stddev;
            }

            double zScore   = stddev > 1e-6 ? Math.Abs((value - avg) / stddev) : 0.0;
            double baseline = state.BaselineAvg > 0 ? state.BaselineAvg : avg;

            var (isAnomaly, severity, reason) = Classify(zScore, rateSpike, value, baseline);

            return new AnalysisResult(
                IsAnomaly        : isAnomaly,
                ZScore           : Math.Round(zScore, 3),
                MovingAverage    : Math.Round(avg,    3),
                StandardDeviation: Math.Round(stddev, 3),
                Baseline         : Math.Round(baseline,3),
                SampleCount      : state.TotalSamples,
                Severity         : severity,
                AnomalyReason    : reason
            );
        }
    }

    public void ResetDevice(string deviceId)
    {
        var toRemove = _states.Keys
            .Where(k => k.StartsWith(deviceId + ":", StringComparison.Ordinal))
            .ToList();

        foreach (var k in toRemove)
            _states.TryRemove(k, out _);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string BuildKey(string deviceId, SensorType metric) =>
        $"{deviceId}:{metric}";

    private static bool DetectRateSpike(MetricState state, float value)
    {
        if (!state.LastValue.HasValue || Math.Abs(state.LastValue.Value) < 1e-6f)
            return false;

        double delta = Math.Abs(value - state.LastValue.Value) / Math.Abs(state.LastValue.Value);
        return delta > ROC_THRESHOLD_PCT;
    }

    private static (double avg, double stddev) ComputeStats(Queue<float> window)
    {
        if (window.Count == 0) return (0, 0);

        double sum = 0;
        foreach (var v in window) sum += v;
        double avg = sum / window.Count;

        if (window.Count < 2) return (avg, 0);

        double sumSq = 0;
        foreach (var v in window) sumSq += (v - avg) * (v - avg);
        double stddev = Math.Sqrt(sumSq / (window.Count - 1));   // Bessel's correction

        return (avg, stddev);
    }

    private static (bool isAnomaly, AlertSeverity? severity, string? reason)
        Classify(double zScore, bool rateSpike, double value, double baseline)
    {
        if (rateSpike)
            return (true, AlertSeverity.Critical,
                $"Rate-of-change spike: >{ROC_THRESHOLD_PCT * 100:F0}% change in one step.");

        if (zScore >= FAULT_SIGMA)
            return (true, AlertSeverity.Fault,
                $"Z-score {zScore:F2}σ ≥ {FAULT_SIGMA}σ — potential equipment failure. " +
                $"Value {value:F3} is {value / baseline:F1}× baseline.");

        if (zScore >= CRITICAL_SIGMA)
            return (true, AlertSeverity.Critical,
                $"Z-score {zScore:F2}σ ≥ {CRITICAL_SIGMA}σ — significant deviation.");

        if (zScore >= WARNING_SIGMA)
            return (true, AlertSeverity.Warning,
                $"Z-score {zScore:F2}σ ≥ {WARNING_SIGMA}σ — elevated reading.");

        return (false, null, null);
    }

    private static AnalysisResult NotEnoughData(float value) =>
        new(false, 0, value, 0, 0, 0, null, null);

    // ── Inner state class ─────────────────────────────────────────────────────
    private sealed class MetricState
    {
        public Queue<float> Window       { get; } = new(WINDOW_SIZE);
        public float?       LastValue    { get; set; }
        public int          TotalSamples { get; set; }
        public double       BaselineAvg  { get; set; }
        public double       BaselineStdDev { get; set; }
    }
}
