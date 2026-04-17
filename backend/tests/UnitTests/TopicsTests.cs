using IotDashboard.Shared.Constants;
using Xunit;

namespace IotDashboard.Tests.UnitTests;

/// <summary>
/// Tests for Topics static helper — critical because topic strings are the
/// contract between firmware and backend. A typo here breaks the entire pipeline.
/// </summary>
public sealed class TopicsTests
{
    [Theory]
    [InlineData("sensors/unit-01/telemetry", "unit-01")]
    [InlineData("sensors/assembly-b-02/telemetry", "assembly-b-02")]
    [InlineData("sensors/device-with-dashes-123/telemetry", "device-with-dashes-123")]
    public void ExtractDeviceId_ReturnsDeviceId_ForValidTopic(string topic, string expected)
    {
        var result = Topics.ExtractDeviceId(topic);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("sensors/unit-01/status")]          // wrong segment
    [InlineData("devices/unit-01/commands")]         // wrong root
    [InlineData("unit-01/telemetry")]                // missing root segment
    [InlineData("sensors/unit-01")]                  // missing trailing segment
    [InlineData("")]                                 // empty
    public void ExtractDeviceId_ReturnsNull_ForInvalidTopic(string topic)
    {
        var result = Topics.ExtractDeviceId(topic);
        Assert.Null(result);
    }

    [Fact]
    public void Telemetry_BuildsCorrectTopic()
    {
        Assert.Equal("sensors/unit-01/telemetry", Topics.Telemetry("unit-01"));
        Assert.Equal("sensors/assembly-line-a/telemetry", Topics.Telemetry("assembly-line-a"));
    }

    [Fact]
    public void Command_BuildsCorrectTopic()
    {
        Assert.Equal("devices/unit-01/commands", Topics.Command("unit-01"));
    }

    [Fact]
    public void TelemetryPattern_ContainsWildcard()
    {
        Assert.Contains("+", Topics.TelemetryPattern);
        Assert.Contains("telemetry", Topics.TelemetryPattern);
    }
}
