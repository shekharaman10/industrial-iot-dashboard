using IotDashboard.Application.Interfaces;
using IotDashboard.Application.Models;
using IotDashboard.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace IotDashboard.Application.Services;

/// <summary>
/// Manages the device registry: upserts device records on first contact,
/// updates last-seen timestamps, and handles online/offline status transitions
/// triggered by MQTT Last-Will-Testament messages.
/// </summary>
public sealed class DeviceService
{
    private readonly IDeviceRepository      _repo;
    private readonly ILogger<DeviceService> _logger;

    // In-process cache; TTL refresh on every telemetry frame
    private readonly Dictionary<string, Device> _cache = new();
    private readonly SemaphoreSlim              _lock  = new(1, 1);

    public DeviceService(IDeviceRepository repo, ILogger<DeviceService> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    /// <summary>
    /// Called by IngestionWorker on every telemetry frame.
    /// Upserts device record and refreshes last-seen + firmware version.
    /// </summary>
    public async Task RegisterHeartbeatAsync(TelemetryMessage msg, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!_cache.TryGetValue(msg.DeviceId, out var device))
            {
                device = new Device
                {
                    Id             = msg.DeviceId,
                    Location       = msg.Location,
                    FirmwareVersion= msg.Firmware,
                    RegisteredUtc  = DateTimeOffset.UtcNow,
                };
                _logger.LogInformation("[DeviceService] New device registered: {DeviceId}", msg.DeviceId);
            }

            device.Status          = DeviceStatus.Online;
            device.LastSeenUtc     = DateTimeOffset.UtcNow;
            device.FirmwareVersion = msg.Firmware;

            _cache[msg.DeviceId] = device;
            await _repo.UpsertAsync(device, ct);
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Called when an MQTT Last-Will or explicit offline status is received.
    /// </summary>
    public async Task MarkOfflineAsync(string deviceId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(deviceId, out var device))
            {
                device.Status = DeviceStatus.Offline;
                await _repo.UpsertAsync(device, ct);
                _logger.LogWarning("[DeviceService] Device went offline: {DeviceId}", deviceId);
            }
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default)
        => await _repo.GetAllAsync(ct);

    public async Task<Device?> GetByIdAsync(string deviceId, CancellationToken ct = default)
        => await _repo.GetByIdAsync(deviceId, ct);
}
