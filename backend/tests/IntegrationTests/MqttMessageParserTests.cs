using System.Text;
using System.Text.Json;
using IotDashboard.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IotDashboard.Tests.IntegrationTests;

/// <summary>
/// Integration tests for MqttMessageParser.
/// No broker needed — tests raw byte-array → TelemetryMessage parsing.
/// These run in CI without any Docker dependencies.
/// </summary>
public sealed class MqttMessageParserTests
{
    private readonly MqttMessageParser _parser =
        new(NullLogger<MqttMessageParser>.Instance);

    private static ReadOnlySpan<byte> Bytes(string json) =>
        Encoding.UTF8.GetBytes(json);

    [Fact]
    public void Parse_ValidFullPayload_ReturnsMessage()
    {
        var json = """
        {
            "schema_version": 1,
            "device_id": "unit-01",
            "location": "Assembly-Line-A",
            "firmware": "1.2.0",
            "seq": 42,
            "ts_ms": 1234567890,
            "vibration": {
                "accel_x": 0.1234,
                "accel_y": 0.2345,
                "accel_z": 9.7821,
                "gyro_x": 0.0012,
                "gyro_y": -0.0003,
                "gyro_z": 0.0007,
                "rms": 9.7906
            },
            "temperature": {
                "celsius": 42.3,
                "humidity": 61.2
            }
        }
        """;

        var result = _parser.Parse(Bytes(json), "sensors/unit-01/telemetry");

        Assert.NotNull(result);
        Assert.Equal("unit-01",           result.DeviceId);
        Assert.Equal("Assembly-Line-A",   result.Location);
        Assert.Equal(1,                   result.SchemaVersion);
        Assert.Equal(42u,                 result.Seq);
        Assert.NotNull(result.Vibration);
        Assert.Equal(9.7906f,             result.Vibration!.Rms,    precision: 3);
        Assert.NotNull(result.Temperature);
        Assert.Equal(42.3f,               result.Temperature!.Celsius, precision: 1);
    }

    [Fact]
    public void Parse_VibrationOnly_ReturnsMessageWithNullTemperature()
    {
        var json = """
        {
            "schema_version": 1,
            "device_id": "unit-02",
            "location": "Assembly-Line-B",
            "firmware": "1.2.0",
            "seq": 1,
            "ts_ms": 0,
            "vibration": { "accel_x": 0.1, "accel_y": 0.2, "accel_z": 9.8, "gyro_x": 0, "gyro_y": 0, "gyro_z": 0, "rms": 9.8 }
        }
        """;

        var result = _parser.Parse(Bytes(json), "sensors/unit-02/telemetry");

        Assert.NotNull(result);
        Assert.NotNull(result.Vibration);
        Assert.Null(result.Temperature);
    }

    [Fact]
    public void Parse_EmptyPayload_ReturnsNull()
    {
        var result = _parser.Parse(ReadOnlySpan<byte>.Empty, "sensors/unit-01/telemetry");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        var result = _parser.Parse(Bytes("{ this is not json }"), "sensors/unit-01/telemetry");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_WrongSchemaVersion_ReturnsNull()
    {
        var json = """
        {
            "schema_version": 99,
            "device_id": "unit-01",
            "location": "A",
            "firmware": "9.9.9",
            "seq": 1,
            "ts_ms": 0
        }
        """;

        var result = _parser.Parse(Bytes(json), "sensors/unit-01/telemetry");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_MissingDeviceId_ReturnsNull()
    {
        var json = """
        {
            "schema_version": 1,
            "device_id": "",
            "location": "A",
            "firmware": "1.0.0",
            "seq": 1,
            "ts_ms": 0
        }
        """;

        var result = _parser.Parse(Bytes(json), "sensors//telemetry");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_TrailingCommaAllowed()
    {
        // ArduinoJson sometimes emits trailing commas — must be tolerant
        var json = """
        {
            "schema_version": 1,
            "device_id": "unit-01",
            "location": "A",
            "firmware": "1.2.0",
            "seq": 5,
            "ts_ms": 0,
        }
        """;

        // AllowTrailingCommas is set in parser options — should succeed
        var result = _parser.Parse(Bytes(json), "sensors/unit-01/telemetry");
        Assert.NotNull(result);
    }
}
