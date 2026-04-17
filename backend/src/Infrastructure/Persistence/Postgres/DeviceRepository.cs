using Dapper;
using IotDashboard.Application.Interfaces;
using IotDashboard.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IotDashboard.Infrastructure.Persistence.Postgres;

/// <summary>
/// PostgreSQL-backed device registry using Dapper.
/// Queries devices table (see infra/postgres/init.sql).
///
/// Why Dapper over EF Core?
///   Devices table has 6 columns and 3 simple queries.
///   EF Core adds migration tooling and change-tracking overhead
///   that's unnecessary at this scale and query complexity.
/// </summary>
public sealed class DeviceRepository : IDeviceRepository
{
    private readonly string _connStr;
    private readonly ILogger<DeviceRepository> _logger;

    public DeviceRepository(
        IOptions<PostgresOptions> opts,
        ILogger<DeviceRepository> logger)
    {
        _connStr = opts.Value.ConnectionString;
        _logger  = logger;
    }

    public async Task UpsertAsync(Device device, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO devices (id, location, firmware_version, status, last_seen_utc, registered_utc)
            VALUES (@Id, @Location, @FirmwareVersion, @Status, @LastSeenUtc, @RegisteredUtc)
            ON CONFLICT (id) DO UPDATE SET
                location         = EXCLUDED.location,
                firmware_version = EXCLUDED.firmware_version,
                status           = EXCLUDED.status,
                last_seen_utc    = EXCLUDED.last_seen_utc;
        ";

        try
        {
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync(sql, new
            {
                device.Id,
                device.Location,
                device.FirmwareVersion,
                Status        = device.Status.ToString(),
                device.LastSeenUtc,
                device.RegisteredUtc,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DeviceRepo] Upsert failed for device {DeviceId}", device.Id);
            throw;
        }
    }

    public async Task<Device?> GetByIdAsync(string deviceId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, location, firmware_version AS FirmwareVersion,
                   status, last_seen_utc AS LastSeenUtc, registered_utc AS RegisteredUtc
            FROM devices WHERE id = @Id LIMIT 1;
        ";
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<DeviceRow>(sql, new { Id = deviceId });
        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, location, firmware_version AS FirmwareVersion,
                   status, last_seen_utc AS LastSeenUtc, registered_utc AS RegisteredUtc
            FROM devices ORDER BY last_seen_utc DESC;
        ";
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<DeviceRow>(sql);
        return rows.Select(MapRow).ToList();
    }

    private static Device MapRow(DeviceRow r) => new()
    {
        Id              = r.Id,
        Location        = r.Location,
        FirmwareVersion = r.FirmwareVersion,
        Status          = Enum.TryParse<DeviceStatus>(r.Status, out var s) ? s : DeviceStatus.Unknown,
        LastSeenUtc     = r.LastSeenUtc,
        RegisteredUtc   = r.RegisteredUtc,
    };

    private sealed class DeviceRow
    {
        public string         Id              { get; set; } = "";
        public string         Location        { get; set; } = "";
        public string         FirmwareVersion { get; set; } = "";
        public string         Status          { get; set; } = "Unknown";
        public DateTimeOffset LastSeenUtc     { get; set; }
        public DateTimeOffset RegisteredUtc   { get; set; }
    }
}
