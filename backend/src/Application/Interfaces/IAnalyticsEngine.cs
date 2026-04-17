using IotDashboard.Domain.Entities;
using IotDashboard.Domain.Enums;

namespace IotDashboard.Application.Interfaces;

/// <summary>
/// Stateful per-device analytics engine.
///
/// Implementation: <see cref="Application.Services.AnalyticsEngine"/>
///
/// CONTRACT:
///   - Engine maintains a rolling window of readings per (deviceId, metric).
///   - State is in-process memory — resets on restart (acceptable trade-off;
///     the baseline re-establishes within ~2.5 minutes at 2 Hz).
///   - Thread-safe: concurrent Evaluate() calls for different devices
///     do not block each other (ConcurrentDictionary + per-key lock).
///   - Returns a deterministic result: same input sequence always produces
///     the same output. No randomness, no external dependencies.
///
/// ANOMALY DETECTION ALGORITHM:
///   1. Maintain 60-sample FIFO rolling window per (deviceId, metric)
///   2. Compute sample mean (μ) and stddev (σ, Bessel-corrected)
///   3. Z-score = |value − μ| / σ
///   4. Classify: ≥1.5σ=Warning, ≥2.5σ=Critical, ≥3.5σ=Fault
///   5. Additionally detect rate-of-change spike (>40% single-step)
/// </summary>
public interface IAnalyticsEngine
{
    /// <summary>
    /// Feed one measurement and return a statistical summary.
    /// Returns NotEnoughData result (IsAnomaly=false) for the first 10 samples
    /// while the window bootstraps.
    /// </summary>
    AnalysisResult Evaluate(string deviceId, float value, SensorType metric);

    /// <summary>
    /// Wipe all state for a device.
    /// Called when a device reconnects after a long outage so stale baseline
    /// does not generate false anomalies.
    /// </summary>
    void ResetDevice(string deviceId);
}

/// <summary>
/// Immutable result from a single analytics evaluation pass.
/// All numeric fields rounded to 3 decimal places by the engine.
/// </summary>
public sealed record AnalysisResult(
    bool          IsAnomaly,
    double        ZScore,
    double        MovingAverage,
    double        StandardDeviation,
    double        Baseline,
    int           SampleCount,
    AlertSeverity? Severity,
    string?       AnomalyReason
);
