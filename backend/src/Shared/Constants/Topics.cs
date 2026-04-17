namespace IotDashboard.Shared.Constants;

/// <summary>
/// Single source of truth for MQTT topic patterns.
/// Mirrors the topic strings in the edge firmware's main.cpp.
/// Changing a topic here requires a matching firmware update.
/// </summary>
public static class Topics
{
    // sensors/{deviceId}/telemetry
    public const string TelemetryPattern  = "sensors/+/telemetry";
    public const string TelemetryTemplate = "sensors/{0}/telemetry";

    // sensors/{deviceId}/status
    public const string StatusPattern     = "sensors/+/status";
    public const string StatusTemplate    = "sensors/{0}/status";

    // devices/{deviceId}/commands (backend → edge)
    public const string CommandTemplate   = "devices/{0}/commands";

    // backend/status (for ops monitoring)
    public const string BackendStatus     = "backend/status";

    public static string Telemetry(string deviceId) =>
        string.Format(TelemetryTemplate, deviceId);

    public static string Command(string deviceId) =>
        string.Format(CommandTemplate, deviceId);

    /// <summary>
    /// Extract deviceId from a concrete telemetry topic like "sensors/unit-01/telemetry".
    /// Returns null if topic doesn't match the expected pattern.
    /// </summary>
    public static string? ExtractDeviceId(string topic)
    {
        // topic = "sensors/{deviceId}/telemetry"
        var parts = topic.Split('/');
        return parts.Length == 3 && parts[0] == "sensors" && parts[2] == "telemetry"
            ? parts[1]
            : null;
    }
}
