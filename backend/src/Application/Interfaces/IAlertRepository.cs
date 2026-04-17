using IotDashboard.Domain.Entities;

namespace IotDashboard.Application.Interfaces;

/// <summary>
/// Persist and query anomaly alerts.
///
/// Implementation: <see cref="Infrastructure.Persistence.Postgres.PostgresAlertRepository"/>
///
/// Why PostgreSQL (not InfluxDB)?
///   Alerts need UPDATE (acknowledgement), JOIN to devices, and
///   filtered queries by severity/device. InfluxDB is append-only
///   and doesn't support these relational access patterns.
/// </summary>
public interface IAlertRepository
{
    /// <summary>
    /// Persist a new alert. Called by <see cref="Application.Services.AlertService"/>
    /// after passing deduplication cooldown check.
    /// </summary>
    Task SaveAsync(Alert alert, CancellationToken ct = default);

    /// <summary>
    /// Most recent N alerts across all devices, newest first.
    /// Default limit 50 matches the dashboard's initial load.
    /// </summary>
    Task<IReadOnlyList<Alert>> GetRecentAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Alerts for a specific device, newest first.
    /// Used by the device-detail panel in the dashboard.
    /// </summary>
    Task<IReadOnlyList<Alert>> GetByDeviceAsync(
        string deviceId,
        int    limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Mark an alert acknowledged. Sets acknowledged=true,
    /// acknowledged_at=UTC now, acknowledged_by=operator name.
    /// </summary>
    Task AcknowledgeAsync(Guid alertId, string acknowledgedBy, CancellationToken ct = default);

    /// <summary>
    /// Count of unacknowledged alerts — used in the dashboard header badge.
    /// </summary>
    Task<int> CountUnacknowledgedAsync(CancellationToken ct = default);
}
