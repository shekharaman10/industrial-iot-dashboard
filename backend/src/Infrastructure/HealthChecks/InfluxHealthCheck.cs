using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IotDashboard.Infrastructure.HealthChecks;

public sealed class InfluxHealthCheck : IHealthCheck
{
    private readonly string _url;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public InfluxHealthCheck(string url) => _url = url.TrimEnd('/');

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetAsync($"{_url}/health", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("InfluxDB reachable")
                : HealthCheckResult.Unhealthy($"InfluxDB returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("InfluxDB unreachable", ex);
        }
    }
}
