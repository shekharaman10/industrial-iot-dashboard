using System.Text;
using System.Text.Json;
using IotDashboard.Application.Models;
using Microsoft.Extensions.Logging;

namespace IotDashboard.Infrastructure.Messaging;

/// <summary>
/// Responsible ONLY for deserialising raw MQTT byte payloads into
/// strongly-typed TelemetryMessage objects.
///
/// Isolation rationale:
///   - MqttSubscriberService handles connectivity, threading, and back-off.
///   - This class handles the data contract. Swapping JSON for Protobuf/MsgPack
///     changes only this file.
///   - Unit-testable without a broker connection.
///
/// Validation rules:
///   - Rejects unknown schema_version (future-proofing)
///   - Rejects empty device_id
///   - Warns on missing vibration AND temperature (partial data is accepted)
/// </summary>
public sealed class MqttMessageParser
{
    private const int SUPPORTED_SCHEMA_VERSION = 1;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas         = true,
    };

    private readonly ILogger<MqttMessageParser> _logger;

    public MqttMessageParser(ILogger<MqttMessageParser> logger) => _logger = logger;

    /// <summary>
    /// Parse a raw MQTT payload.
    /// Returns null if the payload is malformed or fails validation.
    /// </summary>
    public TelemetryMessage? Parse(ReadOnlySpan<byte> payload, string topic)
    {
        if (payload.IsEmpty)
        {
            _logger.LogWarning("[Parser] Empty payload on topic {Topic}", topic);
            return null;
        }

        TelemetryMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<TelemetryMessage>(payload, _jsonOpts);
        }
        catch (JsonException ex)
        {
            // Log first 200 chars of bad payload for debugging
            var preview = Encoding.UTF8.GetString(payload[..Math.Min(200, payload.Length)]);
            _logger.LogError(ex, "[Parser] JSON parse error on {Topic}: {Preview}", topic, preview);
            return null;
        }

        if (msg is null) return null;

        return Validate(msg, topic) ? msg : null;
    }

    private bool Validate(TelemetryMessage msg, string topic)
    {
        if (msg.SchemaVersion != SUPPORTED_SCHEMA_VERSION)
        {
            _logger.LogError(
                "[Parser] Unsupported schema_version={Version} on {Topic}. Expected {Expected}.",
                msg.SchemaVersion, topic, SUPPORTED_SCHEMA_VERSION);
            return false;
        }

        if (string.IsNullOrWhiteSpace(msg.DeviceId))
        {
            _logger.LogError("[Parser] Missing device_id on {Topic}", topic);
            return false;
        }

        if (msg.Vibration is null && msg.Temperature is null)
        {
            _logger.LogWarning("[Parser] No sensor data in payload from {Device}", msg.DeviceId);
            // Still valid — device may be in partial-sensor mode
        }

        return true;
    }
}
