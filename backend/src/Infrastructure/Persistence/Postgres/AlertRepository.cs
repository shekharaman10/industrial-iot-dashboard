using Dapper;
using IotDashboard.Application.Interfaces;
using IotDashboard.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IotDashboard.Infrastructure.Persistence.Postgres;

/// <summary>
/// PostgreSQL-backed alert repository using Dapper.
///
/// Table: alerts (see infra/postgres/init.sql for full schema + indexes)
///
/// Key indexes:
///   idx_alerts_device_id  → device_id + timestamp DESC (per-device queries)
///   idx_alerts_severity   → severity + timestamp DESC (severity filter)
///   idx_alerts_unacked    → WHERE acknowledged=FALSE  (badge count)
///
/// All string enum values stored as TEXT (not integers) for readability
/// in raw SQL queries by ops team.
/// </summary>
public sealed class PostgresAlertRepository : IAlertRepository
{
    private readonly string _connStr;
    private readonly ILogger<PostgresAlertRepository> _logger;

    public PostgresAlertRepository(
        IOptions<PostgresOptions> opts,
        ILogger<PostgresAlertRepository> logger)
    {
        _connStr = opts.Value.ConnectionString;
        _logger  = logger;
    }

    public async Task SaveAsync(Alert alert, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO alerts
                (id, device_id, severity, type, message,
                 measured_value, threshold_value, z_score, timestamp, acknowledged)
            VALUES
                (@Id, @DeviceId, @Severity, @Type, @Message,
                 @MeasuredValue, @ThresholdValue, @ZScore, @Timestamp, @Acknowledged);
        ";

        try
        {
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync(sql, new
            {
                alert.Id,
                alert.DeviceId,
                Severity       = alert.Severity.ToString(),
                Type           = alert.Type.ToString(),
                alert.Message,
                alert.MeasuredValue,
                alert.ThresholdValue,
                alert.ZScore,
                alert.Timestamp,
                alert.Acknowledged,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AlertRepo] Failed to save alert {Id} for device {DeviceId}",
                alert.Id, alert.DeviceId);
            throw;
        }
    }

    public async Task<IReadOnlyList<Alert>> GetRecentAsync(
        int limit = 50, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, device_id AS DeviceId, severity, type, message,
                   measured_value AS MeasuredValue, threshold_value AS ThresholdValue,
                   z_score AS ZScore, timestamp, acknowledged,
                   acknowledged_at AS AcknowledgedAt, acknowledged_by AS AcknowledgedBy
            FROM alerts
            ORDER BY timestamp DESC
            LIMIT @Limit;
        ";

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<AlertRow>(sql, new { Limit = limit });
        return rows.Select(MapRow).ToList();
    }

    public async Task<IReadOnlyList<Alert>> GetByDeviceAsync(
        string deviceId, int limit = 50, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, device_id AS DeviceId, severity, type, message,
                   measured_value AS MeasuredValue, threshold_value AS ThresholdValue,
                   z_score AS ZScore, timestamp, acknowledged,
                   acknowledged_at AS AcknowledgedAt, acknowledged_by AS AcknowledgedBy
            FROM alerts
            WHERE device_id = @DeviceId
            ORDER BY timestamp DESC
            LIMIT @Limit;
        ";

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<AlertRow>(sql, new { DeviceId = deviceId, Limit = limit });
        return rows.Select(MapRow).ToList();
    }

    public async Task AcknowledgeAsync(Guid alertId, string acknowledgedBy, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE alerts
            SET acknowledged     = TRUE,
                acknowledged_at  = @Now,
                acknowledged_by  = @By
            WHERE id = @Id;
        ";

        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        int rows = await conn.ExecuteAsync(sql, new
        {
            Id  = alertId,
            Now = DateTimeOffset.UtcNow,
            By  = acknowledgedBy,
        });

        if (rows == 0)
            _logger.LogWarning("[AlertRepo] AcknowledgeAsync: alert {Id} not found", alertId);
    }

    public async Task<int> CountUnacknowledgedAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT COUNT(*) FROM alerts WHERE acknowledged = FALSE;";
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(sql);
    }

    // ── Private mapping ───────────────────────────────────────────────────────

    private static Alert MapRow(AlertRow r) => new()
    {
        Id             = r.Id,
        DeviceId       = r.DeviceId,
        Severity       = Enum.Parse<AlertSeverity>(r.Severity),
        Type           = Enum.Parse<AlertType>(r.Type),
        Message        = r.Message,
        MeasuredValue  = r.MeasuredValue,
        ThresholdValue = r.ThresholdValue,
        ZScore         = r.ZScore,
        Timestamp      = r.Timestamp,
        Acknowledged   = r.Acknowledged,
        AcknowledgedAt = r.AcknowledgedAt,
        AcknowledgedBy = r.AcknowledgedBy,
    };

    // Dapper flat mapping row
    private sealed class AlertRow
    {
        public Guid            Id              { get; set; }
        public string          DeviceId        { get; set; } = "";
        public string          Severity        { get; set; } = "";
        public string          Type            { get; set; } = "";
        public string          Message         { get; set; } = "";
        public double          MeasuredValue   { get; set; }
        public double          ThresholdValue  { get; set; }
        public double          ZScore          { get; set; }
        public DateTimeOffset  Timestamp       { get; set; }
        public bool            Acknowledged    { get; set; }
        public DateTimeOffset? AcknowledgedAt  { get; set; }
        public string?         AcknowledgedBy  { get; set; }
    }
}
