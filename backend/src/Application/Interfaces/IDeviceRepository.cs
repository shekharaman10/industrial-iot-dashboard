using IotDashboard.Domain.Entities;

namespace IotDashboard.Application.Interfaces;

/// <summary>
/// Persists and queries the device registry.
/// Implementation: PostgreSQL via Dapper (Infrastructure layer).
///
/// Why separate from ISensorRepository?
///   Devices are relational entities with FK relationships to alerts.
///   Sensor readings are time-series. Different access patterns,
///   different storage engines, different interfaces.
/// </summary>
public interface IDeviceRepository
{
    /// <summary>
    /// Insert or update a device record.
    /// Called on every telemetry heartbeat by DeviceService.
    /// Must be idempotent — safe to call at 2 Hz per device.
    /// </summary>
    Task UpsertAsync(Device device, CancellationToken ct = default);

    /// <summary>Get a single device by its ID, or null if not registered.</summary>
    Task<Device?> GetByIdAsync(string deviceId, CancellationToken ct = default);

    /// <summary>Get all registered devices ordered by last-seen descending.</summary>
    Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default);
}
