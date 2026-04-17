using IotDashboard.Application.Interfaces;
using IotDashboard.Application.Services;
using IotDashboard.Domain.Entities;
using IotDashboard.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IotDashboard.Tests.UnitTests;

/// <summary>
/// Unit tests for AlertService — specifically the deduplication cooldown logic.
/// Uses an in-memory fake repository; no DB connection needed.
/// </summary>
public sealed class AlertServiceTests
{
    private readonly FakeAlertRepository _repo   = new();
    private readonly AlertService        _svc;

    public AlertServiceTests()
    {
        _svc = new AlertService(_repo, NullLogger<AlertService>.Instance);
    }

    [Fact]
    public async Task RaiseIfNeeded_CreatesAlert_WhenAnomalyDetected()
    {
        var result = MakeAnomaly(AlertSeverity.Warning, "Z-score 2.1σ");

        var alert = await _svc.RaiseIfNeededAsync("unit-01", SensorType.VibrationRms, result, 14.5f);

        Assert.NotNull(alert);
        Assert.Equal("unit-01",          alert!.DeviceId);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        Assert.Equal(14.5f,              (float)alert.MeasuredValue, precision: 3);
        Assert.Equal(1, _repo.SavedCount);
    }

    [Fact]
    public async Task RaiseIfNeeded_ReturnsNull_WhenNoAnomaly()
    {
        var result = new AnalysisResult(false, 0.3, 9.81, 0.05, 9.81, 50, null, null);

        var alert = await _svc.RaiseIfNeededAsync("unit-02", SensorType.VibrationRms, result, 9.84f);

        Assert.Null(alert);
        Assert.Equal(0, _repo.SavedCount);
    }

    [Fact]
    public async Task RaiseIfNeeded_SuppressesDuplicate_WithinCooldown()
    {
        var result = MakeAnomaly(AlertSeverity.Critical, "Spike");

        // First call — should create alert
        var first = await _svc.RaiseIfNeededAsync("unit-01", SensorType.VibrationRms, result, 20f);
        Assert.NotNull(first);

        // Second call immediately after — same device + type, within 30s cooldown
        var second = await _svc.RaiseIfNeededAsync("unit-01", SensorType.VibrationRms, result, 21f);
        Assert.Null(second);   // suppressed

        Assert.Equal(1, _repo.SavedCount);   // only one persisted
    }

    [Fact]
    public async Task RaiseIfNeeded_AllowsDifferentDevices_Independently()
    {
        var result = MakeAnomaly(AlertSeverity.Warning, "Spike");

        var a1 = await _svc.RaiseIfNeededAsync("unit-01", SensorType.VibrationRms, result, 20f);
        var a2 = await _svc.RaiseIfNeededAsync("unit-02", SensorType.VibrationRms, result, 20f);

        Assert.NotNull(a1);
        Assert.NotNull(a2);
        Assert.Equal(2, _repo.SavedCount);
    }

    [Fact]
    public async Task RaiseIfNeeded_AllowsDifferentMetrics_SameDevice()
    {
        var result = MakeAnomaly(AlertSeverity.Warning, "Spike");

        var vib  = await _svc.RaiseIfNeededAsync("unit-01", SensorType.VibrationRms,     result, 20f);
        var temp = await _svc.RaiseIfNeededAsync("unit-01", SensorType.TemperatureCelsius, result, 80f);

        Assert.NotNull(vib);
        Assert.NotNull(temp);
        Assert.Equal(2, _repo.SavedCount);
    }

    [Fact]
    public async Task RaiseIfNeeded_MapsRateOfChange_ToCorrectAlertType()
    {
        var result = MakeAnomaly(AlertSeverity.Critical, "Rate-of-change spike: >40% in one step.");

        var alert = await _svc.RaiseIfNeededAsync("unit-01", SensorType.VibrationRms, result, 18f);

        Assert.NotNull(alert);
        Assert.Equal(AlertType.RateOfChangeSpike, alert!.Type);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static AnalysisResult MakeAnomaly(AlertSeverity sev, string reason) =>
        new(IsAnomaly: true, ZScore: 3.5, MovingAverage: 9.81,
            StandardDeviation: 0.5, Baseline: 9.81, SampleCount: 100,
            Severity: sev, AnomalyReason: reason);

    // ── Fake repository ─────────────────────────────────────────────────────
    private sealed class FakeAlertRepository : IAlertRepository
    {
        private readonly List<Alert> _alerts = [];
        public int SavedCount => _alerts.Count;

        public Task SaveAsync(Alert alert, CancellationToken ct = default)
        {
            _alerts.Add(alert);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Alert>> GetRecentAsync(int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Alert>>(_alerts.Take(limit).ToList());

        public Task<IReadOnlyList<Alert>> GetByDeviceAsync(string deviceId, int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Alert>>(_alerts.Where(a => a.DeviceId == deviceId).Take(limit).ToList());

        public Task AcknowledgeAsync(Guid alertId, string by, CancellationToken ct = default)
        {
            var a = _alerts.FirstOrDefault(x => x.Id == alertId);
            if (a != null) a.Acknowledged = true;
            return Task.CompletedTask;
        }

        public Task<int> CountUnacknowledgedAsync(CancellationToken ct = default)
            => Task.FromResult(_alerts.Count(a => !a.Acknowledged));
    }
}
