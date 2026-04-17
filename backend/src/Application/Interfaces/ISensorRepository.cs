using IotDashboard.Domain.Entities;

namespace IotDashboard.Application.Interfaces;

/// <summary>
/// Write and query time-series sensor readings.
///
/// Implementation: <see cref="Infrastructure.Persistence.InfluxDb.InfluxDbSensorRepository"/>
///
/// Design note: This interface exposes only what the application layer needs.
/// InfluxDB-specific features (continuous queries, downsampling tasks, retention
/// policies) are configured directly on the DB, not surfaced here.
/// </summary>
public interface ISensorRepository
{
    /// <summary>
    /// Persist one sensor reading frame.
    /// Internally writes separate measurements per sensor type
    /// (vibration, temperature) tagged with deviceId and location.
    /// </summary>
    Task WriteAsync(SensorReading reading, CancellationToken ct = default);

    /// <summary>
    /// Query historical readings for a single device within a time window.
    /// Returned in ascending timestamp order.
    /// </summary>
    Task<IReadOnlyList<SensorReading>> QueryAsync(
        string   deviceId,
        TimeSpan window,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all deviceIds that have written data to InfluxDB.
    /// Used by the dashboard to populate the device selector on initial load.
    /// </summary>
    Task<IReadOnlyList<string>> GetActiveDeviceIdsAsync(CancellationToken ct = default);
}
