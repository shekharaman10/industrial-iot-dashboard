using IotDashboard.Application.Interfaces;
using IotDashboard.Application.Services;
using IotDashboard.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace IotDashboard.Api.Controllers;

/// <summary>
/// Device registry endpoints.
///
/// Device records are created automatically when a device first sends telemetry
/// (via DeviceService.RegisterHeartbeatAsync). These endpoints are read-only
/// because write operations happen through the MQTT pipeline, not HTTP.
///
/// Route: /api/devices
/// </summary>
[ApiController]
[Route("api/devices")]
[Produces("application/json")]
public sealed class DevicesController : ControllerBase
{
    private readonly DeviceService     _deviceSvc;
    private readonly IAlertRepository  _alertRepo;
    private readonly IAnalyticsEngine  _analytics;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(
        DeviceService    deviceSvc,
        IAlertRepository alertRepo,
        IAnalyticsEngine analytics,
        ILogger<DevicesController> logger)
    {
        _deviceSvc = deviceSvc;
        _alertRepo  = alertRepo;
        _analytics  = analytics;
        _logger     = logger;
    }

    /// <summary>
    /// List all registered devices with current status.
    /// </summary>
    /// <returns>Array of device summaries ordered by last-seen descending.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var devices = await _deviceSvc.GetAllAsync(ct);
        return Ok(devices.Select(MapDeviceDto));
    }

    /// <summary>
    /// Get a single device by ID.
    /// </summary>
    [HttpGet("{deviceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string deviceId, CancellationToken ct)
    {
        var device = await _deviceSvc.GetByIdAsync(deviceId, ct);
        if (device is null)
            return NotFound(new { error = $"Device '{deviceId}' not found." });

        return Ok(MapDeviceDto(device));
    }

    /// <summary>
    /// Get all alerts for a specific device.
    /// Combines unacknowledged + recent acknowledged alerts.
    /// </summary>
    [HttpGet("{deviceId}/alerts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeviceAlerts(
        string deviceId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > 500)
            return BadRequest(new { error = "limit must be between 1 and 500." });

        var alerts = await _alertRepo.GetByDeviceAsync(deviceId, limit, ct);
        return Ok(alerts);
    }

    /// <summary>
    /// Reset the analytics engine baseline for a specific device.
    /// Use this after maintenance — the engine will re-establish baseline
    /// from the next 300 samples (~2.5 minutes at 2 Hz).
    /// </summary>
    [HttpPost("{deviceId}/reset-analytics")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetAnalytics(string deviceId, CancellationToken ct)
    {
        var device = await _deviceSvc.GetByIdAsync(deviceId, ct);
        if (device is null)
            return NotFound(new { error = $"Device '{deviceId}' not found." });

        _analytics.ResetDevice(deviceId);
        _logger.LogInformation(
            "[DevicesController] Analytics baseline reset for device {DeviceId}", deviceId);

        return NoContent();
    }

    // ── DTO mapping ────────────────────────────────────────────────────────────

    private static object MapDeviceDto(Device d) => new
    {
        id              = d.Id,
        location        = d.Location,
        status          = d.Status.ToString(),
        firmware        = d.FirmwareVersion,
        lastSeen        = d.LastSeenUtc,
        registeredAt    = d.RegisteredUtc,
        isStale         = d.IsStale,
        uptimeSeconds   = (DateTimeOffset.UtcNow - d.RegisteredUtc).TotalSeconds,
    };
}
