using System.Threading.Channels;
using IotDashboard.Application.Models;
using IotDashboard.Application.Services;
using IotDashboard.Domain.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IotDashboard.Worker;

/// <summary>
/// Stage 1 of the processing pipeline.
///
/// Consumes raw TelemetryMessage objects from the bounded Channel
/// (written by MqttSubscriberService) and:
///   1. Maps them to SensorReading domain entities
///   2. Updates the device registry (heartbeat)
///   3. Writes mapped readings to the ProcessingChannel
///
/// Having a separate IngestionWorker means:
///   - If DB is slow, MQTT ingestion still buffers up to channel capacity
///   - Mapping/validation logic is isolated from connectivity logic
///   - Workers can be scaled independently in a future multi-process setup
/// </summary>
public sealed class IngestionWorker : BackgroundService
{
    private readonly ILogger<IngestionWorker>          _logger;
    private readonly ChannelReader<TelemetryMessage>   _inbound;
    private readonly ChannelWriter<SensorReading>      _outbound;
    private readonly DeviceService                     _deviceSvc;

    public IngestionWorker(
        ILogger<IngestionWorker> logger,
        ChannelReader<TelemetryMessage> inbound,
        ChannelWriter<SensorReading> outbound,
        DeviceService deviceSvc)
    {
        _logger    = logger;
        _inbound   = inbound;
        _outbound  = outbound;
        _deviceSvc = deviceSvc;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[IngestionWorker] Started");

        await foreach (var msg in _inbound.ReadAllAsync(stoppingToken))
        {
            try
            {
                // 1. Register device heartbeat (non-blocking upsert)
                _ = _deviceSvc.RegisterHeartbeatAsync(msg, stoppingToken);

                // 2. Map to domain entity
                var reading = MapToReading(msg);

                // 3. Forward to processing stage
                await _outbound.WriteAsync(reading, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "[IngestionWorker] Failed to ingest message from {Device}", msg.DeviceId);
            }
        }

        _logger.LogInformation("[IngestionWorker] Stopped");
    }

    private static SensorReading MapToReading(TelemetryMessage msg) => new()
    {
        DeviceId      = msg.DeviceId,
        Location      = msg.Location,
        Timestamp     = DateTimeOffset.UtcNow,
        SequenceNum   = msg.Seq,
        SchemaVersion = msg.SchemaVersion,

        VibRms        = msg.Vibration?.Rms,
        AccelX        = msg.Vibration?.AccelX,
        AccelY        = msg.Vibration?.AccelY,
        AccelZ        = msg.Vibration?.AccelZ,
        GyroX         = msg.Vibration?.GyroX,
        GyroY         = msg.Vibration?.GyroY,
        GyroZ         = msg.Vibration?.GyroZ,

        TemperatureC  = msg.Temperature?.Celsius,
        Humidity      = msg.Temperature?.Humidity,
    };
}
