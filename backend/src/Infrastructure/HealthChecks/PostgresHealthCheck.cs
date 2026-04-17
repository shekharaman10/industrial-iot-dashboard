using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace IotDashboard.Infrastructure.HealthChecks;

public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public PostgresHealthCheck(string connectionString) => _connectionString = connectionString;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL unreachable", ex);
        }
    }
}
