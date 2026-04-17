using IotDashboard.Domain.Enums;

namespace IotDashboard.Domain.Entities;

/// <summary>
/// Immutable domain entity representing one telemetry frame from an edge device.
/// All nullable fields are optional depending on which sensors are online.
/// </summary>
public sealed record SensorReading
{
    public Guid            Id           { get; init; } = Guid.NewGuid();
    public required string DeviceId     { get; init; }
    public required string Location     { get; init; }
    public DateTimeOffset  Timestamp    { get; init; } = DateTimeOffset.UtcNow;
    public uint            SequenceNum  { get; init; }
    public int             SchemaVersion{ get; init; } = 1;

    // Vibration axes (m/s²)
    public float? AccelX      { get; init; }
    public float? AccelY      { get; init; }
    public float? AccelZ      { get; init; }
    public float? GyroX       { get; init; }
    public float? GyroY       { get; init; }
    public float? GyroZ       { get; init; }
    public float? VibRms      { get; init; }   // Primary maintenance metric

    // Environment
    public float? TemperatureC{ get; init; }
    public float? Humidity     { get; init; }

    public bool HasVibration    => VibRms.HasValue;
    public bool HasTemperature  => TemperatureC.HasValue;
}
