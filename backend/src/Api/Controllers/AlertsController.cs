using IotDashboard.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IotDashboard.Api.Controllers;

/// <summary>
/// Alert management endpoints.
///
/// Alerts are created exclusively by the analytics pipeline (ProcessingWorker).
/// This controller provides read access and the acknowledgement workflow only.
///
/// Route: /api/alerts
/// </summary>
[ApiController]
[Route("api/alerts")]
[Produces("application/json")]
public sealed class AlertsController : ControllerBase
{
    private readonly IAlertRepository _repo;
    private readonly ILogger<AlertsController> _logger;

    public AlertsController(IAlertRepository repo, ILogger<AlertsController> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    /// <summary>
    /// Get recent alerts across all devices, newest first.
    /// </summary>
    /// <param name="limit">Max number of alerts to return (1–500, default 50).</param>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRecent(
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > 500)
            return BadRequest(new { error = "limit must be between 1 and 500." });

        var alerts = await _repo.GetRecentAsync(limit, ct);
        return Ok(alerts);
    }

    /// <summary>
    /// Count of currently unacknowledged alerts.
    /// Used by the dashboard header badge without fetching all alert data.
    /// </summary>
    [HttpGet("unacknowledged/count")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnacknowledgedCount(CancellationToken ct)
    {
        var count = await _repo.CountUnacknowledgedAsync(ct);
        return Ok(new { count });
    }

    /// <summary>
    /// Acknowledge an alert — marks it as seen by an operator.
    /// Acknowledged alerts remain visible but are visually dimmed in the dashboard.
    /// </summary>
    /// <param name="alertId">The alert's UUID.</param>
    /// <param name="acknowledgedBy">Operator name or identifier (default: "dashboard").</param>
    [HttpPost("{alertId:guid}/acknowledge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Acknowledge(
        Guid alertId,
        [FromQuery] string acknowledgedBy = "dashboard",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(acknowledgedBy))
            return BadRequest(new { error = "acknowledgedBy cannot be empty." });

        await _repo.AcknowledgeAsync(alertId, acknowledgedBy, ct);
        _logger.LogInformation(
            "[AlertsController] Alert {AlertId} acknowledged by {By}", alertId, acknowledgedBy);

        return NoContent();
    }

    /// <summary>
    /// Bulk acknowledge — marks all unacknowledged alerts as seen.
    /// Useful for clearing the dashboard after maintenance.
    /// </summary>
    [HttpPost("acknowledge-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AcknowledgeAll(
        [FromQuery] string acknowledgedBy = "dashboard",
        CancellationToken ct = default)
    {
        // Fetch all unacknowledged alerts and ack them individually
        // In production, replace with a single bulk SQL UPDATE for efficiency
        var unacked = await _repo.GetRecentAsync(500, ct);
        var toAck   = unacked.Where(a => !a.Acknowledged).ToList();

        var tasks = toAck.Select(a => _repo.AcknowledgeAsync(a.Id, acknowledgedBy, ct));
        await Task.WhenAll(tasks);

        _logger.LogInformation(
            "[AlertsController] Bulk acknowledged {Count} alerts by {By}",
            toAck.Count, acknowledgedBy);

        return Ok(new { acknowledged = toAck.Count });
    }
}
