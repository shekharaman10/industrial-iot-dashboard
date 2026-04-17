namespace IotDashboard.Infrastructure.Persistence.Postgres;

/// <summary>
/// PostgreSQL connection options.
/// Bound from appsettings.json section "Postgres".
/// Shared by all Postgres repositories (DeviceRepository, PostgresAlertRepository).
/// </summary>
public sealed class PostgresOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}
