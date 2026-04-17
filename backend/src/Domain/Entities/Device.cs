namespace IotDashboard.Domain.Entities;

/// <summary>
/// Tracks metadata and live status for each connected edge device.
/// Persisted to PostgreSQL; updated on MQTT status messages.
/// </summary>
public sealed class Device
{
    public string        Id              { get; set; } = string.Empty;
    public string        Location        { get; set; } = string.Empty;
    public string        FirmwareVersion { get; set; } = string.Empty;
    public DeviceStatus  Status          { get; set; } = DeviceStatus.Unknown;
    public DateTimeOffset LastSeenUtc    { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset RegisteredUtc  { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>True if device has not sent telemetry in > 30 seconds.</summary>
    public bool IsStale => (DateTimeOffset.UtcNow - LastSeenUtc).TotalSeconds > 30;
}

public enum DeviceStatus { Unknown, Online, Offline, Degraded }
