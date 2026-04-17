using Microsoft.AspNetCore.SignalR;

namespace IotDashboard.Api.Hubs;

/// <summary>
/// SignalR hub for real-time bidirectional communication with dashboard clients.
///
/// Server → Client events (pushed from ProcessingWorker):
///   "TelemetryReceived"  — new sensor frame with analytics overlay
///   "AlertReceived"      — anomaly alert
///   "DeviceStatusChanged"— device came online / went offline
///
/// Client → Server (methods below):
///   SubscribeToDevice    — join device-scoped group for filtered updates
///   UnsubscribeFromDevice
///
/// Groups:
///   "all"              — every connected client (default)
///   "device:{id}"      — clients subscribed to a specific device
///
/// Authentication:
///   Not wired here for brevity. Add [Authorize] attribute + JWT bearer
///   scheme when deploying to production.
/// </summary>
public sealed class SensorHub : Hub
{
    private readonly ILogger<SensorHub> _logger;

    public SensorHub(ILogger<SensorHub> logger) => _logger = logger;

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "all");
        _logger.LogDebug("[SignalR] Client connected: {Id}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("[SignalR] Client disconnected: {Id}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Subscribe to device-scoped updates only.</summary>
    public async Task SubscribeToDevice(string deviceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}");
        _logger.LogDebug("[SignalR] {Id} subscribed to device {DeviceId}",
            Context.ConnectionId, deviceId);
    }

    public async Task UnsubscribeFromDevice(string deviceId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"device:{deviceId}");
}
