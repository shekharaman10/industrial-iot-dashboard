using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using IotDashboard.Application.Interfaces;
using IotDashboard.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IotDashboard.Infrastructure.Persistence.InfluxDb;

public sealed class InfluxDbOptions
{
    public string Url    { get; set; } = "http://localhost:8086";
    public string Token  { get; set; } = string.Empty;
    public string Org    { get; set; } = "iot-org";
    public string Bucket { get; set; } = "sensor-readings";
}

/// <summary>
/// Time-series persistence backed by InfluxDB 2.x.
///
/// Write strategy:
///   Each SensorReading is written as two optional measurements:
///     - "vibration"   (when HasVibration)
///     - "temperature" (when HasTemperature)
///   Tags are indexed (deviceId, location); fields are the values.
///
/// Query strategy (Flux):
///   - Range filter → tag filter → pivot → return rows
///   Downsampling tasks (continuous queries in InfluxDB) should be configured
///   separately for 1-hour and 1-day aggregates.
/// </summary>
public sealed class InfluxDbSensorRepository : ISensorRepository
{
    private readonly InfluxDBClient  _client;
    private readonly InfluxDbOptions _opts;
    private readonly ILogger<InfluxDbSensorRepository> _logger;

    public InfluxDbSensorRepository(
        IOptions<InfluxDbOptions> opts,
        ILogger<InfluxDbSensorRepository> logger)
    {
        _opts   = opts.Value;
        _logger = logger;
        _client = new InfluxDBClient(_opts.Url, _opts.Token);
    }

    public async Task WriteAsync(SensorReading r, CancellationToken ct = default)
    {
        var writeApi = _client.GetWriteApiAsync();
        var points = new List<PointData>(2);

        if (r.HasVibration)
        {
            points.Add(PointData
                .Measurement("vibration")
                .Tag("device_id", r.DeviceId)
                .Tag("location",  r.Location)
                .Field("rms",     (double)r.VibRms!)
                .Field("accel_x", (double)(r.AccelX ?? 0f))
                .Field("accel_y", (double)(r.AccelY ?? 0f))
                .Field("accel_z", (double)(r.AccelZ ?? 0f))
                .Field("gyro_x",  (double)(r.GyroX  ?? 0f))
                .Field("gyro_y",  (double)(r.GyroY  ?? 0f))
                .Field("gyro_z",  (double)(r.GyroZ  ?? 0f))
                .Field("seq",     (long)r.SequenceNum)
                .Timestamp(r.Timestamp, WritePrecision.Ms));
        }

        if (r.HasTemperature)
        {
            points.Add(PointData
                .Measurement("temperature")
                .Tag("device_id", r.DeviceId)
                .Tag("location",  r.Location)
                .Field("celsius",  (double)r.TemperatureC!)
                .Field("humidity", (double)(r.Humidity ?? 0f))
                .Timestamp(r.Timestamp, WritePrecision.Ms));
        }

        if (points.Count == 0) return;

        try
        {
            await writeApi.WritePointsAsync(points, _opts.Bucket, _opts.Org, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[InfluxDB] Write failed for device {DeviceId}", r.DeviceId);
            throw;   // Let caller (ProcessingWorker) decide retry strategy
        }
    }

    public async Task<IReadOnlyList<SensorReading>> QueryAsync(
        string deviceId, TimeSpan window, CancellationToken ct = default)
    {
        var queryApi = _client.GetQueryApi();
        int minutes  = (int)Math.Ceiling(window.TotalMinutes);

        // Flux query: pivot both measurements so each timestamp = one row
        var flux = $@"
            vib = from(bucket: ""{_opts.Bucket}"")
              |> range(start: -{minutes}m)
              |> filter(fn: (r) => r._measurement == ""vibration"")
              |> filter(fn: (r) => r.device_id == ""{deviceId}"")
              |> pivot(rowKey: [""_time""], columnKey: [""_field""], valueColumn: ""_value"")

            temp = from(bucket: ""{_opts.Bucket}"")
              |> range(start: -{minutes}m)
              |> filter(fn: (r) => r._measurement == ""temperature"")
              |> filter(fn: (r) => r.device_id == ""{deviceId}"")
              |> pivot(rowKey: [""_time""], columnKey: [""_field""], valueColumn: ""_value"")

            join(tables: {{vib: vib, temp: temp}}, on: [""_time"", ""device_id""])
        ";

        var tables   = await queryApi.QueryAsync(flux, _opts.Org);
        var readings = new List<SensorReading>();

        foreach (var table in tables)
        foreach (var record in table.Records)
        {
            readings.Add(new SensorReading
            {
                DeviceId     = deviceId,
                Location     = record.GetValueByKey("location")?.ToString() ?? "",
                Timestamp    = record.GetTime()?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                VibRms       = GetField<float>(record, "rms"),
                AccelX       = GetField<float>(record, "accel_x"),
                AccelY       = GetField<float>(record, "accel_y"),
                AccelZ       = GetField<float>(record, "accel_z"),
                TemperatureC = GetField<float>(record, "celsius"),
                Humidity     = GetField<float>(record, "humidity"),
            });
        }

        return readings.OrderBy(r => r.Timestamp).ToList();
    }

    public async Task<IReadOnlyList<string>> GetActiveDeviceIdsAsync(CancellationToken ct = default)
    {
        var queryApi = _client.GetQueryApi();
        var flux = $@"
            import ""influxdata/influxdb/schema""
            schema.tagValues(bucket: ""{_opts.Bucket}"", tag: ""device_id"")
        ";
        var tables = await queryApi.QueryAsync(flux, _opts.Org);
        return tables
            .SelectMany(t => t.Records)
            .Select(r => r.GetValue()?.ToString() ?? "")
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    private static T? GetField<T>(
        InfluxDB.Client.Core.Flux.Domain.FluxRecord record,
        string field) where T : struct
    {
        try
        {
            var v = record.GetValueByKey(field);
            return v is null ? null : (T)Convert.ChangeType(v, typeof(T));
        }
        catch { return null; }
    }
}
