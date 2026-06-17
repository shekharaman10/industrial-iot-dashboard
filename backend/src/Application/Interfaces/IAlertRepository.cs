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
    /// Pass <paramref name="before"/> as a cursor for stable keyset pagination
    /// (avoids OFFSET drift when new alerts arrive between pages).
    /// </summary>
    Task<IReadOnlyList<Alert>> GetRecentAsync(
        int              limit  = 50,
        DateTimeOffset?  before = null,
        CancellationToken ct    = default);

    /// <summary>
    /// Alerts for a specific device, newest first.
    /// Pass <paramref name="before"/> as a cursor for keyset pagination.
    /// </summary>
    Task<IReadOnlyList<Alert>> GetByDeviceAsync(
        string           deviceId,
        int              limit  = 50,
        DateTimeOffset?  before = null,
        CancellationToken ct    = default);

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
