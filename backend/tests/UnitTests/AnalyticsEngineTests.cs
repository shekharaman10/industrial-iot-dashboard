using IotDashboard.Application.Services;
using IotDashboard.Domain.Entities;
using IotDashboard.Domain.Enums;
using Xunit;

namespace IotDashboard.Tests.UnitTests;

/// <summary>
/// Unit tests for AnalyticsEngine.
/// These are pure logic tests — no external dependencies, no mocks needed.
/// </summary>
public sealed class AnalyticsEngineTests
{
    private readonly AnalyticsEngine _engine = new();

    [Fact]
    public void Evaluate_ReturnsNoAnomaly_WhenInsufficientData()
    {
        // Feed only 5 samples — engine needs ≥ 10 before evaluating
        for (int i = 0; i < 5; i++)
        {
            var result = _engine.Evaluate("device-1", 9.8f, SensorType.VibrationRms);
            Assert.False(result.IsAnomaly);
            Assert.Null(result.Severity);
        }
    }

    [Fact]
    public void Evaluate_ReturnsNoAnomaly_ForStableSignal()
    {
        // Feed 100 readings with tiny Gaussian noise
        var rng = new Random(42);
        for (int i = 0; i < 99; i++)
            _engine.Evaluate("device-stable", 9.81f + (float)(rng.NextDouble() * 0.01 - 0.005), SensorType.VibrationRms);

        // Last reading within normal range
        var result = _engine.Evaluate("device-stable", 9.82f, SensorType.VibrationRms);

        Assert.False(result.IsAnomaly);
        Assert.True(result.ZScore < 1.5, $"Expected Z < 1.5, got {result.ZScore}");
    }

    [Fact]
    public void Evaluate_DetectsWarning_OnMildDeviation()
    {
        WarmUp("device-warn", 9.81f, 50);

        // Inject a reading 2σ above normal
        double stddev = EstimateStdDev(9.81f, 50);
        float spike = 9.81f + (float)(2.0 * stddev);

        var result = _engine.Evaluate("device-warn", spike, SensorType.VibrationRms);

        Assert.True(result.IsAnomaly);
        Assert.Equal(AlertSeverity.Warning, result.Severity);
    }

    [Fact]
    public void Evaluate_DetectsFault_OnExtremeSpike()
    {
        WarmUp("device-fault", 9.81f, 50);

        // Inject an extreme outlier (10× normal)
        var result = _engine.Evaluate("device-fault", 98.1f, SensorType.VibrationRms);

        Assert.True(result.IsAnomaly);
        Assert.Equal(AlertSeverity.Fault, result.Severity);
        Assert.True(result.ZScore > 3.0, $"Expected Z > 3.0, got {result.ZScore}");
    }

    [Fact]
    public void Evaluate_DetectsRateOfChangeSpike()
    {
        WarmUp("device-roc", 9.81f, 20);

        // Inject value 60% higher than previous (exceeds 40% ROC threshold)
        _engine.Evaluate("device-roc", 9.81f, SensorType.VibrationRms);  // last value = 9.81
        var result = _engine.Evaluate("device-roc", 15.7f, SensorType.VibrationRms);  // ~60% jump

        Assert.True(result.IsAnomaly);
        Assert.Equal(AlertSeverity.Critical, result.Severity);
        Assert.Contains("Rate-of-change", result.AnomalyReason);
    }

    [Fact]
    public void Evaluate_IsIsolatedPerDevice()
    {
        WarmUp("device-a", 9.81f, 50);
        WarmUp("device-b", 5.00f, 50);

        // Spike only device-a
        var resultA = _engine.Evaluate("device-a", 98.1f, SensorType.VibrationRms);
        var resultB = _engine.Evaluate("device-b", 5.01f, SensorType.VibrationRms);

        Assert.True(resultA.IsAnomaly,  "device-a should detect anomaly");
        Assert.False(resultB.IsAnomaly, "device-b should not be affected");
    }

    [Fact]
    public void ResetDevice_ClearsState()
    {
        WarmUp("device-reset", 9.81f, 50);

        _engine.ResetDevice("device-reset");

        // After reset, engine should not have enough data to evaluate
        var result = _engine.Evaluate("device-reset", 9.81f, SensorType.VibrationRms);
        Assert.Equal(1, result.SampleCount);
    }

    [Fact]
    public void Evaluate_IsThreadSafe()
    {
        // Simulate concurrent readings from multiple devices
        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            var deviceId = $"device-thread-{i}";
            for (int j = 0; j < 200; j++)
                _engine.Evaluate(deviceId, 9.81f + j * 0.001f, SensorType.VibrationRms);
        }));

        // Should not throw
        var ex = Record.Exception(() => Task.WhenAll(tasks).Wait());
        Assert.Null(ex);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void WarmUp(string deviceId, float baseValue, int samples)
    {
        var rng = new Random(0);
        for (int i = 0; i < samples; i++)
            _engine.Evaluate(deviceId, baseValue + (float)(rng.NextDouble() * 0.02 - 0.01), SensorType.VibrationRms);
    }

    private static double EstimateStdDev(float baseValue, int samples)
    {
        // Rough estimate for noise width used in WarmUp
        return baseValue * 0.001;  // ~0.1% variation
    }
}
