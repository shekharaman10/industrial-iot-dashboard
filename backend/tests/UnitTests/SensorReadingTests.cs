using IotDashboard.Domain.Entities;
using Xunit;

namespace IotDashboard.Tests.UnitTests;

/// <summary>
/// Tests for SensorReading domain entity computed properties and invariants.
/// Pure unit tests — no external dependencies.
/// </summary>
public sealed class SensorReadingTests
{
    [Fact]
    public void HasVibration_IsTrue_WhenVibRmsSet()
    {
        var r = new SensorReading { DeviceId = "d1", Location = "L", VibRms = 9.81f };
        Assert.True(r.HasVibration);
    }

    [Fact]
    public void HasVibration_IsFalse_WhenVibRmsNull()
    {
        var r = new SensorReading { DeviceId = "d1", Location = "L", VibRms = null };
        Assert.False(r.HasVibration);
    }

    [Fact]
    public void HasTemperature_IsTrue_WhenTempSet()
    {
        var r = new SensorReading { DeviceId = "d1", Location = "L", TemperatureC = 42.0f };
        Assert.True(r.HasTemperature);
    }

    [Fact]
    public void HasTemperature_IsFalse_WhenTempNull()
    {
        var r = new SensorReading { DeviceId = "d1", Location = "L", TemperatureC = null };
        Assert.False(r.HasTemperature);
    }

    [Fact]
    public void SensorReading_DefaultTimestamp_IsUtc()
    {
        var r    = new SensorReading { DeviceId = "d1", Location = "L" };
        var diff = DateTimeOffset.UtcNow - r.Timestamp;
        Assert.True(diff.TotalSeconds < 2, "Default Timestamp should be close to UtcNow");
    }

    [Fact]
    public void SensorReading_DefaultId_IsNotEmpty()
    {
        var r = new SensorReading { DeviceId = "d1", Location = "L" };
        Assert.NotEqual(Guid.Empty, r.Id);
    }

    [Fact]
    public void SensorReading_TwoInstances_HaveDifferentIds()
    {
        var r1 = new SensorReading { DeviceId = "d1", Location = "L" };
        var r2 = new SensorReading { DeviceId = "d1", Location = "L" };
        Assert.NotEqual(r1.Id, r2.Id);
    }

    [Fact]
    public void Device_IsStale_WhenLastSeenOver30s()
    {
        var d = new IotDashboard.Domain.Entities.Device
        {
            Id          = "unit-01",
            Location    = "A",
            LastSeenUtc = DateTimeOffset.UtcNow.AddSeconds(-31),
        };
        Assert.True(d.IsStale);
    }

    [Fact]
    public void Device_IsNotStale_WhenLastSeenRecent()
    {
        var d = new IotDashboard.Domain.Entities.Device
        {
            Id          = "unit-01",
            Location    = "A",
            LastSeenUtc = DateTimeOffset.UtcNow.AddSeconds(-10),
        };
        Assert.False(d.IsStale);
    }

    [Fact]
    public void Alert_DefaultAcknowledged_IsFalse()
    {
        var a = new IotDashboard.Domain.Entities.Alert
        {
            DeviceId = "unit-01",
            Message  = "Test",
            Severity = IotDashboard.Domain.Entities.AlertSeverity.Warning,
            Type     = IotDashboard.Domain.Entities.AlertType.VibrationSpike,
        };
        Assert.False(a.Acknowledged);
        Assert.Null(a.AcknowledgedAt);
        Assert.Null(a.AcknowledgedBy);
    }
}
