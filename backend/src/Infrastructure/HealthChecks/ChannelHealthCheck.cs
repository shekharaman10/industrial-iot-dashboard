using System.Threading.Channels;
using IotDashboard.Application.Models;
using IotDashboard.Domain.Entities;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IotDashboard.Infrastructure.HealthChecks;

/// <summary>
/// Health check that monitors the depth of both bounded channels.
///
/// WHY THIS MATTERS:
///   A channel near capacity means a consumer (IngestionWorker or ProcessingWorker)
///   is falling behind the producer. If fully saturated (2000/2000), new messages
///   will block the MQTT subscriber, eventually causing TCP receive-window fill
///   and broker-side flow control on QoS-1. This is intentional backpressure, but
///   ops should be alerted before it reaches saturation.
///
/// THRESHOLDS:
///   Healthy    : < 50% capacity (< 1000 items)
///   Degraded   : 50–80% capacity (1000–1600 items)  → investigate consumer latency
///   Unhealthy  : > 80% capacity (> 1600 items)      → consumer likely blocked or crashed
///
/// REGISTRATION in Program.cs:
///   builder.Services.AddHealthChecks()
///       .AddCheck&lt;ChannelHealthCheck&gt;("channel-pipeline", tags: ["ready"]);
/// </summary>
public sealed class ChannelHealthCheck : IHealthCheck
{
    private const int CAPACITY          = 2000;
    private const int DEGRADED_THRESHOLD = (int)(CAPACITY * 0.50);
    private const int UNHEALTHY_THRESHOLD= (int)(CAPACITY * 0.80);

    private readonly ChannelReader<TelemetryMessage> _mqttReader;
    private readonly ChannelReader<SensorReading>    _processingReader;

    public ChannelHealthCheck(
        ChannelReader<TelemetryMessage> mqttReader,
        ChannelReader<SensorReading>    processingReader)
    {
        _mqttReader       = mqttReader;
        _processingReader = processingReader;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken  cancellationToken = default)
    {
        // Channel.Count returns the current number of items waiting to be read
        int mqttDepth       = _mqttReader.Count;
        int processingDepth = _processingReader.Count;
        int maxDepth        = Math.Max(mqttDepth, processingDepth);

        var data = new Dictionary<string, object>
        {
            ["mqtt_channel_depth"]       = mqttDepth,
            ["processing_channel_depth"] = processingDepth,
            ["capacity"]                 = CAPACITY,
            ["mqtt_pct"]                 = $"{(double)mqttDepth / CAPACITY * 100:F1}%",
            ["processing_pct"]           = $"{(double)processingDepth / CAPACITY * 100:F1}%",
        };

        if (maxDepth >= UNHEALTHY_THRESHOLD)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Channel near saturation: mqtt={mqttDepth}, processing={processingDepth} " +
                $"(threshold={UNHEALTHY_THRESHOLD}). Consumer likely blocked or crashed.",
                data: data));
        }

        if (maxDepth >= DEGRADED_THRESHOLD)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Channel above 50% capacity: mqtt={mqttDepth}, processing={processingDepth}. " +
                "Investigate consumer processing latency.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Pipeline healthy: mqtt={mqttDepth}, processing={processingDepth}",
            data));
    }
}
