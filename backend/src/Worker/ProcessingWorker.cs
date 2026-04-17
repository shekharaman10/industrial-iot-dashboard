using System.Threading.Channels;
using IotDashboard.Api.Hubs;
using IotDashboard.Application.Interfaces;
using IotDashboard.Application.Services;
using IotDashboard.Domain.Entities;
using IotDashboard.Domain.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IotDashboard.Worker;

/// <summary>
/// Stage 2 of the processing pipeline — the "brain" of the backend.
///
/// For each SensorReading:
///   1. Run analytics (per-metric z-score evaluation)
///   2. Persist raw reading to InfluxDB
///   3. If anomaly detected → raise alert via AlertService → save to PostgreSQL
///   4. Broadcast reading + analysis metadata to all SignalR clients
///
/// All steps run concurrently where possible (Task.WhenAll).
/// DB write failure does NOT suppress SignalR broadcast (best-effort delivery
/// to dashboard even if persistence is temporarily unavailable).
/// </summary>
public sealed class ProcessingWorker : BackgroundService
{
    private readonly ILogger<ProcessingWorker>   _logger;
    private readonly ChannelReader<SensorReading> _channel;
    private readonly IAnalyticsEngine            _engine;
    private readonly ISensorRepository           _sensorRepo;
    private readonly AlertService                _alertSvc;
    private readonly IHubContext<SensorHub>      _hub;

    public ProcessingWorker(
        ILogger<ProcessingWorker> logger,
        ChannelReader<SensorReading> channel,
        IAnalyticsEngine engine,
        ISensorRepository sensorRepo,
        AlertService alertSvc,
        IHubContext<SensorHub> hub)
    {
        _logger     = logger;
        _channel    = channel;
        _engine     = engine;
        _sensorRepo = sensorRepo;
        _alertSvc   = alertSvc;
        _hub        = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ProcessingWorker] Started");

        await foreach (var reading in _channel.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(reading, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "[ProcessingWorker] Unhandled error for device {Device}", reading.DeviceId);
            }
        }

        _logger.LogInformation("[ProcessingWorker] Stopped");
    }

    private async Task ProcessAsync(SensorReading reading, CancellationToken ct)
    {
        // ── 1. Analytics ────────────────────────────────────────────────────
        AnalysisResult? vibAnalysis  = null;
        AnalysisResult? tempAnalysis = null;

        if (reading.HasVibration)
            vibAnalysis = _engine.Evaluate(reading.DeviceId, reading.VibRms!.Value, SensorType.VibrationRms);

        if (reading.HasTemperature)
            tempAnalysis = _engine.Evaluate(reading.DeviceId, reading.TemperatureC!.Value, SensorType.TemperatureCelsius);

        // ── 2. Persist (fire and log error — do not block broadcast) ────────
        _ = _sensorRepo.WriteAsync(reading, ct)
            .ContinueWith(t => _logger.LogError(t.Exception,
                "[ProcessingWorker] InfluxDB write failed for {Device}", reading.DeviceId),
                TaskContinuationOptions.OnlyOnFaulted);

        // ── 3. Alert evaluation ──────────────────────────────────────────────
        Alert? vibAlert  = null;
        Alert? tempAlert = null;

        if (vibAnalysis is not null)
            vibAlert = await _alertSvc.RaiseIfNeededAsync(
                reading.DeviceId, SensorType.VibrationRms, vibAnalysis,
                reading.VibRms!.Value, ct);

        if (tempAnalysis is not null)
            tempAlert = await _alertSvc.RaiseIfNeededAsync(
                reading.DeviceId, SensorType.TemperatureCelsius, tempAnalysis,
                reading.TemperatureC!.Value, ct);

        // ── 4. SignalR broadcast ─────────────────────────────────────────────
        var broadcastTasks = new List<Task>(3)
        {
            _hub.Clients.All.SendAsync("TelemetryReceived", BuildTelemetryDto(
                reading, vibAnalysis, tempAnalysis), ct),
        };

        if (vibAlert  is not null)
            broadcastTasks.Add(_hub.Clients.All.SendAsync("AlertReceived", MapAlert(vibAlert),  ct));
        if (tempAlert is not null)
            broadcastTasks.Add(_hub.Clients.All.SendAsync("AlertReceived", MapAlert(tempAlert), ct));

        await Task.WhenAll(broadcastTasks);
    }

    // ── DTO builders (anonymous types — fast, no extra alloc) ───────────────

    private static object BuildTelemetryDto(
        SensorReading reading,
        AnalysisResult? vibResult,
        AnalysisResult? tempResult) => new
    {
        deviceId    = reading.DeviceId,
        location    = reading.Location,
        timestamp   = reading.Timestamp,
        seq         = reading.SequenceNum,
        vibration   = reading.VibRms,
        accelX      = reading.AccelX,
        accelY      = reading.AccelY,
        accelZ      = reading.AccelZ,
        temperature = reading.TemperatureC,
        humidity    = reading.Humidity,
        analysis = new
        {
            vibration = vibResult is null ? null : (object)new
            {
                isAnomaly  = vibResult.IsAnomaly,
                zScore     = vibResult.ZScore,
                movingAvg  = vibResult.MovingAverage,
                stdDev     = vibResult.StandardDeviation,
                baseline   = vibResult.Baseline,
                severity   = vibResult.Severity?.ToString(),
                reason     = vibResult.AnomalyReason,
            },
            temperature = tempResult is null ? null : (object)new
            {
                isAnomaly  = tempResult.IsAnomaly,
                zScore     = tempResult.ZScore,
                movingAvg  = tempResult.MovingAverage,
                stdDev     = tempResult.StandardDeviation,
                baseline   = tempResult.Baseline,
                severity   = tempResult.Severity?.ToString(),
                reason     = tempResult.AnomalyReason,
            },
        }
    };

    private static object MapAlert(Alert a) => new
    {
        id         = a.Id,
        deviceId   = a.DeviceId,
        severity   = a.Severity.ToString(),
        type       = a.Type.ToString(),
        message    = a.Message,
        value      = a.MeasuredValue,
        threshold  = a.ThresholdValue,
        zScore     = a.ZScore,
        timestamp  = a.Timestamp,
    };
}
