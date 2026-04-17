using IotDashboard.Application.Services;
using IotDashboard.Domain.Entities;
using IotDashboard.Domain.Enums;
using Xunit;

namespace IotDashboard.Tests.UnitTests;

public sealed class AnalyticsEngineTests
{
    private readonly AnalyticsEngine _engine = new();

    [Fact]
    public void Evaluate_ReturnsNoAnomaly_WhenInsufficientData()
    {
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
        // Use large consistent noise so the window stddev is meaningful
        // and a small test value doesn't cross the 1.5σ threshold
        var rng = new Random(42);
        for (int i = 0; i < 99; i++)
            // ±0.5 noise gives stddev ~0.29; 9.82 is within 1.5σ of 9.81
            _engine.Evaluate("device-stable",
                9.81f + (float)(rng.NextDouble() - 0.5),
                SensorType.VibrationRms);

        // Value within normal range — should NOT be anomaly
        var result = _engine.Evaluate("device-stable", 9.82f, SensorType.VibrationRms);

        Assert.False(result.IsAnomaly);
    }

    [Fact]
    public void Evaluate_DetectsAnomaly_OnLargeSpike()
    {
        // Warm up with tight consistent values
        WarmUp("device-spike", 9.81f, 50);

        // 98.1 is 10× the baseline — clearly anomalous
        // Rate-of-change fires first (>40% jump) → Critical
        var result = _engine.Evaluate("device-spike", 98.1f, SensorType.VibrationRms);

        Assert.True(result.IsAnomaly);
        Assert.NotNull(result.Severity);
        // ROC spike fires as Critical; Z-score would be Fault
        // Either is acceptable — just confirm anomaly is detected
        Assert.True(result.Severity == AlertSeverity.Critical ||
                    result.Severity == AlertSeverity.Fault);
    }

    [Fact]
    public void Evaluate_DetectsCritical_OnRateOfChangeSpike()
    {
        WarmUp("device-roc", 9.81f, 20);

        // Establish last value
        _engine.Evaluate("device-roc", 9.81f, SensorType.VibrationRms);

        // 60% jump in one step → rate-of-change spike → Critical
        var result = _engine.Evaluate("device-roc", 15.7f, SensorType.VibrationRms);

        Assert.True(result.IsAnomaly);
        Assert.Equal(AlertSeverity.Critical, result.Severity);
        Assert.Contains("Rate-of-change", result.AnomalyReason);
    }

    [Fact]
    public void Evaluate_DetectsWarning_WhenZScoreBetween1point5And2point5()
    {
        // Use large noise so we can control exactly where the spike lands
        var rng = new Random(0);
        for (int i = 0; i < 59; i++)
            _engine.Evaluate("device-warn",
                9.81f + (float)(rng.NextDouble() - 0.5),   // ±0.5 → stddev ~0.29
                SensorType.VibrationRms);

        // Get current stats via one more evaluate
        var baseline = _engine.Evaluate("device-warn", 9.81f, SensorType.VibrationRms);
        double sigma = baseline.StandardDeviation;

        // Inject exactly 1.8σ above mean (within Warning band 1.5–2.5)
        float warnValue = (float)(baseline.MovingAverage + 1.8 * sigma);

        var result = _engine.Evaluate("device-warn", warnValue, SensorType.VibrationRms);

        Assert.True(result.IsAnomaly);
        Assert.Equal(AlertSeverity.Warning, result.Severity);
    }

    [Fact]
    public void Evaluate_IsIsolatedPerDevice()
    {
        WarmUp("device-a", 9.81f, 50);
        WarmUp("device-b", 5.00f, 50);

        // Big spike on device-a only
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

        // After reset, first sample — SampleCount must be 1
        var result = _engine.Evaluate("device-reset", 9.81f, SensorType.VibrationRms);
        Assert.Equal(1, result.SampleCount);
    }

    [Fact]
    public void Evaluate_IsThreadSafe()
    {
        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            var deviceId = $"device-thread-{i}";
            for (int j = 0; j < 200; j++)
                _engine.Evaluate(deviceId, 9.81f + j * 0.001f, SensorType.VibrationRms);
        }));

        var ex = Record.Exception(() => Task.WhenAll(tasks).Wait());
        Assert.Null(ex);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void WarmUp(string deviceId, float baseValue, int samples)
    {
        var rng = new Random(0);
        for (int i = 0; i < samples; i++)
            _engine.Evaluate(deviceId,
                baseValue + (float)(rng.NextDouble() * 0.02 - 0.01),
                SensorType.VibrationRms);
    }
}
