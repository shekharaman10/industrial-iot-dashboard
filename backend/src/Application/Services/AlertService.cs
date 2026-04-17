using IotDashboard.Application.Interfaces;
using IotDashboard.Domain.Entities;
using IotDashboard.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IotDashboard.Application.Services;

/// <summary>
/// Converts AnalysisResult anomalies into Alert domain entities and persists them.
///
/// DEDUPLICATION:
///   Suppresses duplicate alerts for the same (device, alertType) within
///   ALERT_COOLDOWN_SECONDS. This prevents alert storms during sustained faults.
///   Example: a bearing fault at 2 Hz would generate 120 alerts/minute without this.
///   With 30s cooldown: maximum 2 alerts/minute per metric per device.
///
/// COOLDOWN RESET:
///   Cooldown state is in-memory. On backend restart the cooldown resets,
///   so one alert fires immediately after restart even if one was recently saved.
///   Acceptable trade-off — a restart is operationally significant anyway.
/// </summary>
public sealed class AlertService
{
    private const int ALERT_COOLDOWN_SECONDS = 30;

    private readonly IAlertRepository              _repo;
    private readonly ILogger<AlertService>         _logger;
    private readonly Dictionary<string, DateTimeOffset> _lastAlerted = new();
    private readonly SemaphoreSlim                      _lock         = new(1, 1);

    public AlertService(IAlertRepository repo, ILogger<AlertService> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    /// <summary>
    /// Evaluate an analysis result and raise an alert if warranted.
    /// Returns the persisted Alert, or null if suppressed by cooldown.
    /// </summary>
    public async Task<Alert?> RaiseIfNeededAsync(
        string         deviceId,
        SensorType     metric,
        AnalysisResult result,
        float          measuredValue,
        CancellationToken ct = default)
    {
        if (!result.IsAnomaly || result.Severity is null)
            return null;

        var alertType = MapToAlertType(metric, result);
        var dedupKey  = $"{deviceId}:{alertType}";

        await _lock.WaitAsync(ct);
        try
        {
            if (_lastAlerted.TryGetValue(dedupKey, out var lastTime))
            {
                double elapsed = (DateTimeOffset.UtcNow - lastTime).TotalSeconds;
                if (elapsed < ALERT_COOLDOWN_SECONDS)
                {
                    _logger.LogDebug(
                        "[AlertService] Suppressed {Type} for {Device} " +
                        "(cooldown {Elapsed:F0}s / {Limit}s)",
                        alertType, deviceId, elapsed, ALERT_COOLDOWN_SECONDS);
                    return null;
                }
            }

            var alert = new Alert
            {
                DeviceId       = deviceId,
                Severity       = result.Severity.Value,
                Type           = alertType,
                Message        = result.AnomalyReason ?? "Anomaly detected",
                MeasuredValue  = measuredValue,
                ThresholdValue = result.Baseline,
                ZScore         = result.ZScore,
                Timestamp      = DateTimeOffset.UtcNow,
            };

            await _repo.SaveAsync(alert, ct);
            _lastAlerted[dedupKey] = alert.Timestamp;

            _logger.LogWarning(
                "[Alert] {Severity} {Type} | Device={Device} Z={ZScore:F2} " +
                "Value={Value:F4} Baseline={Baseline:F4} | {Message}",
                alert.Severity, alert.Type, deviceId,
                result.ZScore, measuredValue, result.Baseline, alert.Message);

            return alert;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static AlertType MapToAlertType(SensorType metric, AnalysisResult result)
    {
        return metric switch
        {
            SensorType.VibrationRms when result.AnomalyReason?.Contains("Rate-of-change") == true
                => AlertType.RateOfChangeSpike,
            SensorType.VibrationRms
                => AlertType.VibrationSpike,
            SensorType.TemperatureCelsius
                => AlertType.ThermalOverheat,
            SensorType.HumidityPercent
                => AlertType.HumidityOutOfRange,
            _   => AlertType.VibrationSpike,
        };
    }
}
