using System.Collections.Concurrent;
using IotDashboard.Application.Interfaces;
using IotDashboard.Domain.Entities;
using IotDashboard.Domain.Enums;
using Microsoft.Extensions.Options;

namespace IotDashboard.Application.Services;

/// <summary>
/// Stateful rolling-window anomaly detection engine.
///
/// Algorithm overview:
///   1. Maintain a sliding window of the last WindowSize readings per (device, metric).
///   2. Compute moving average (μ) and sample standard deviation (σ) over the window.
///   3. Compute Z-score: z = |value − μ| / σ
///   4. Classify severity based on z-score thresholds.
///   5. Additionally detect rate-of-change spikes regardless of z-score.
///
/// Baseline:
///   The first BaselineSamples readings establish a "normal" operating baseline
///   stored separately from the rolling window. This baseline is used in alert
///   messages to give operators context ("value is 2.4× normal baseline").
///
/// Thread safety:
///   State per (deviceId, metric) is locked independently, so concurrent
///   reads from different devices do not block each other.
///
/// TTL eviction:
///   A background timer sweeps stale entries (no activity for StateEvictionSeconds).
///   Prevents unbounded memory growth when devices are decommissioned.
///
/// Tuning:
///   All parameters are read from IOptions&lt;AnalyticsEngineOptions&gt; and can be
///   changed via appsettings.json or environment variables without recompiling.
/// </summary>
public sealed class AnalyticsEngine : IAnalyticsEngine, IDisposable
{
    private readonly AnalyticsEngineOptions _opts;
    private readonly ConcurrentDictionary<string, MetricState> _states = new();
    private readonly Timer _evictionTimer;

    public AnalyticsEngine(IOptions<AnalyticsEngineOptions> opts)
    {
        _opts = opts.Value;
        // Sweep stale entries every half-eviction-period, minimum 60s
        var sweepInterval = TimeSpan.FromSeconds(Math.Max(60, _opts.StateEvictionSeconds / 2));
        _evictionTimer = new Timer(EvictStaleStates, null, sweepInterval, sweepInterval);
    }

    public AnalysisResult Evaluate(string deviceId, float value, SensorType metric)
    {
        var key   = BuildKey(deviceId, metric);
        var state = _states.GetOrAdd(key, _ => new MetricState());

        lock (state)
        {
            state.LastAccessedUtc = DateTimeOffset.UtcNow;

            bool rateSpike = DetectRateSpike(state, value);

            state.Window.Enqueue(value);
            if (state.Window.Count > _opts.WindowSize)
                state.Window.Dequeue();

            state.LastValue = value;
            state.TotalSamples++;

            if (state.TotalSamples < 10)
                return NotEnoughData(value);

            (double avg, double stddev) = ComputeStats(state.Window);

            if (state.TotalSamples == _opts.BaselineSamples)
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

    public void Dispose() => _evictionTimer.Dispose();

    // ── Private helpers ───────────────────────────────────────────────────────

    private void EvictStaleStates(object? state)
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-_opts.StateEvictionSeconds);
        foreach (var (key, metricState) in _states)
        {
            bool evict;
            lock (metricState) { evict = metricState.LastAccessedUtc < cutoff; }
            if (evict) _states.TryRemove(key, out MetricState? _dropped);
        }
    }

    private static string BuildKey(string deviceId, SensorType metric) =>
        $"{deviceId}:{metric}";

    private bool DetectRateSpike(MetricState state, float value)
    {
        if (!state.LastValue.HasValue || Math.Abs(state.LastValue.Value) < 1e-6f)
            return false;

        double delta = Math.Abs(value - state.LastValue.Value) / Math.Abs(state.LastValue.Value);
        return delta > _opts.RocThresholdPct;
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

    private (bool isAnomaly, AlertSeverity? severity, string? reason)
        Classify(double zScore, bool rateSpike, double value, double baseline)
    {
        if (rateSpike)
            return (true, AlertSeverity.Critical,
                $"Rate-of-change spike: >{_opts.RocThresholdPct * 100:F0}% change in one step.");

        if (zScore >= _opts.FaultSigma)
            return (true, AlertSeverity.Fault,
                $"Z-score {zScore:F2}σ ≥ {_opts.FaultSigma}σ — potential equipment failure. " +
                $"Value {value:F3} is {value / baseline:F1}× baseline.");

        if (zScore >= _opts.CriticalSigma)
            return (true, AlertSeverity.Critical,
                $"Z-score {zScore:F2}σ ≥ {_opts.CriticalSigma}σ — significant deviation.");

        if (zScore >= _opts.WarningSigma)
            return (true, AlertSeverity.Warning,
                $"Z-score {zScore:F2}σ ≥ {_opts.WarningSigma}σ — elevated reading.");

        return (false, null, null);
    }

    private static AnalysisResult NotEnoughData(float value) =>
        new(false, 0, value, 0, 0, 0, null, null);

    // ── Inner state class ─────────────────────────────────────────────────────
    private sealed class MetricState
    {
        public Queue<float>    Window           { get; } = new();
        public float?          LastValue        { get; set; }
        public int             TotalSamples     { get; set; }
        public double          BaselineAvg      { get; set; }
        public double          BaselineStdDev   { get; set; }
        public DateTimeOffset  LastAccessedUtc  { get; set; } = DateTimeOffset.UtcNow;
    }
}
