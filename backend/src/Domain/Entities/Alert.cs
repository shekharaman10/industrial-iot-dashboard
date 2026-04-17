namespace IotDashboard.Domain.Entities;

/// <summary>
/// Domain entity representing an anomaly event raised by the analytics engine.
/// Stored in PostgreSQL (relational) because alerts need foreign-key joins,
/// full-text search, and acknowledgment workflows — not suited to InfluxDB.
/// </summary>
public sealed class Alert
{
    public Guid           Id              { get; init; } = Guid.NewGuid();
    public required string DeviceId       { get; init; }
    public AlertSeverity  Severity        { get; init; }
    public AlertType      Type            { get; init; }
    public required string Message        { get; init; }
    public double         MeasuredValue   { get; init; }
    public double         ThresholdValue  { get; init; }
    public double         ZScore          { get; init; }
    public DateTimeOffset Timestamp       { get; init; } = DateTimeOffset.UtcNow;
    public bool           Acknowledged    { get; set; }  = false;
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string?        AcknowledgedBy  { get; set; }
}

public enum AlertSeverity { Info = 0, Warning = 1, Critical = 2, Fault = 3 }

public enum AlertType
{
    VibrationSpike,
    VibrationSustained,
    ThermalOverheat,
    HumidityOutOfRange,
    RateOfChangeSpike,
    SensorFault,
}
