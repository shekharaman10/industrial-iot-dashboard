using IotDashboard.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IotDashboard.Api.Controllers;

/// <summary>
/// Sensor time-series data endpoints.
///
/// Write path: sensor data enters exclusively through the MQTT pipeline.
/// Read path:  queries InfluxDB for historical time-series data.
///
/// Route: /api/sensors
/// </summary>
[ApiController]
[Route("api/sensors")]
[Produces("application/json")]
public sealed class SensorsController : ControllerBase
{
    private readonly ISensorRepository _sensorRepo;
    private readonly ILogger<SensorsController> _logger;

    public SensorsController(ISensorRepository sensorRepo, ILogger<SensorsController> logger)
    {
        _sensorRepo = sensorRepo;
        _logger     = logger;
    }

    /// <summary>
    /// List all device IDs that have written sensor data to InfluxDB.
    /// Sourced from InfluxDB tag values — may differ slightly from the
    /// PostgreSQL device registry (devices table) if a device wrote data
    /// before its status message arrived.
    /// </summary>
    [HttpGet("devices")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveDeviceIds(CancellationToken ct)
    {
        var ids = await _sensorRepo.GetActiveDeviceIdsAsync(ct);
        return Ok(ids);
    }

    /// <summary>
    /// Time-series sensor readings for one device within a rolling window.
    /// Returned in ascending timestamp order (oldest first).
    /// </summary>
    /// <param name="deviceId">Device identifier (e.g. "unit-01").</param>
    /// <param name="minutes">Lookback window in minutes (1–1440). Default: 60.</param>
    [HttpGet("{deviceId}/history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetHistory(
        string deviceId,
        [FromQuery] int minutes = 60,
        CancellationToken ct = default)
    {
        if (minutes is < 1 or > 1440)
            return BadRequest(new { error = "minutes must be between 1 and 1440." });

        var readings = await _sensorRepo.QueryAsync(deviceId, TimeSpan.FromMinutes(minutes), ct);

        return Ok(readings.Select(r => new
        {
            timestamp   = r.Timestamp,
            seq         = r.SequenceNum,
            vibration   = r.VibRms,
            accelX      = r.AccelX,
            accelY      = r.AccelY,
            accelZ      = r.AccelZ,
            gyroX       = r.GyroX,
            gyroY       = r.GyroY,
            gyroZ       = r.GyroZ,
            temperature = r.TemperatureC,
            humidity    = r.Humidity,
        }));
    }
}
