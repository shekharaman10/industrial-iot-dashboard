using System.Text.Json.Serialization;

namespace IotDashboard.Application.Models;

/// <summary>
/// Strongly-typed representation of the JSON payload sent by edge firmware.
/// This is the schema contract — changes here require a matching firmware update.
/// Maps to SCHEMA_VERSION 1 defined in telemetry_formatter.h.
/// </summary>
public sealed class TelemetryMessage
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("firmware")]
    public string Firmware { get; set; } = string.Empty;

    [JsonPropertyName("seq")]
    public uint Seq { get; set; }

    /// <summary>
    /// Device-local millis() timestamp. Used for sequence gap detection.
    /// Replace with UTC epoch once NTP is added to firmware.
    /// </summary>
    [JsonPropertyName("ts_ms")]
    public ulong TsMs { get; set; }

    [JsonPropertyName("vibration")]
    public VibrationPayload? Vibration { get; set; }

    [JsonPropertyName("temperature")]
    public TemperaturePayload? Temperature { get; set; }
}

public sealed class VibrationPayload
{
    [JsonPropertyName("accel_x")] public float AccelX { get; set; }
    [JsonPropertyName("accel_y")] public float AccelY { get; set; }
    [JsonPropertyName("accel_z")] public float AccelZ { get; set; }
    [JsonPropertyName("gyro_x")]  public float GyroX  { get; set; }
    [JsonPropertyName("gyro_y")]  public float GyroY  { get; set; }
    [JsonPropertyName("gyro_z")]  public float GyroZ  { get; set; }
    [JsonPropertyName("rms")]     public float Rms     { get; set; }
}

public sealed class TemperaturePayload
{
    [JsonPropertyName("celsius")]  public float Celsius  { get; set; }
    [JsonPropertyName("humidity")] public float Humidity { get; set; }
}
