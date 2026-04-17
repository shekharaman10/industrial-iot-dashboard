import { formatVib, formatTemp, formatRelativeTime, STATUS_COLORS, SEVERITY_COLORS } from "../utils/formatters.js";

/**
 * DeviceCard
 * Compact card showing one device's latest status.
 * Used in a grid layout on the dashboard for multi-device overview.
 *
 * Props:
 *   device      : { id, location, status, firmware, lastSeen, isStale }
 *   latestFrame : latest TelemetryReceived payload for this device (may be null)
 *   isSelected  : bool — highlight as active
 *   onSelect    : () => void
 */
export default function DeviceCard({ device, latestFrame, isSelected, onSelect }) {
  const statusColor = STATUS_COLORS[device.status] ?? STATUS_COLORS.Unknown;
  const vibAnomaly  = latestFrame?.analysis?.vibration?.isAnomaly;
  const tempAnomaly = latestFrame?.analysis?.temperature?.isAnomaly;

  const worstSeverity = (() => {
    const sev = [
      latestFrame?.analysis?.vibration?.severity,
      latestFrame?.analysis?.temperature?.severity,
    ].filter(Boolean);
    const order = { Fault: 4, Critical: 3, Warning: 2, Info: 1 };
    return sev.sort((a, b) => (order[b] ?? 0) - (order[a] ?? 0))[0] ?? null;
  })();

  return (
    <button
      onClick={onSelect}
      style={{
        background    : isSelected ? "#0f1923" : "#0a1019",
        border        : `1px solid ${isSelected ? "#f59e0b" : worstSeverity ? SEVERITY_COLORS[worstSeverity] : "#132030"}`,
        borderRadius  : 8,
        cursor        : "pointer",
        padding       : "12px 14px",
        textAlign     : "left",
        width         : "100%",
        transition    : "border-color 0.3s, background 0.2s",
      }}
    >
      {/* Header row */}
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 8 }}>
        <span style={{
          width: 7, height: 7, borderRadius: "50%",
          background: statusColor,
          boxShadow : device.status === "Online" ? `0 0 5px ${statusColor}` : "none",
          flexShrink: 0,
        }} />
        <span style={{
          fontFamily: "IBM Plex Mono", fontSize: 12,
          fontWeight: 700, color: isSelected ? "#f59e0b" : "#94a3b8",
          letterSpacing: "0.04em",
        }}>
          {device.id}
        </span>
        {device.isStale && (
          <span style={{
            fontSize: 9, fontFamily: "IBM Plex Mono", color: "#f59e0b",
            background: "#1c160a", border: "1px solid #78350f",
            borderRadius: 3, padding: "0 4px",
          }}>
            STALE
          </span>
        )}
        {worstSeverity && (
          <span style={{
            fontSize: 9, fontFamily: "IBM Plex Mono",
            color: SEVERITY_COLORS[worstSeverity],
            background: "#0f1923",
            border: `1px solid ${SEVERITY_COLORS[worstSeverity]}40`,
            borderRadius: 3, padding: "0 4px",
            marginLeft: "auto",
          }}>
            {worstSeverity.toUpperCase()}
          </span>
        )}
      </div>

      {/* Location */}
      <div style={{ fontSize: 10, fontFamily: "IBM Plex Mono", color: "#475569", marginBottom: 8 }}>
        {device.location || "—"}
      </div>

      {/* Live values */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 6 }}>
        <Metric
          label="Vibration"
          value={formatVib(latestFrame?.vibration)}
          anomaly={vibAnomaly}
        />
        <Metric
          label="Temp"
          value={formatTemp(latestFrame?.temperature)}
          anomaly={tempAnomaly}
        />
      </div>

      {/* Footer */}
      <div style={{
        marginTop: 8, fontSize: 9, fontFamily: "IBM Plex Mono",
        color: "#334155", display: "flex", justifyContent: "space-between",
      }}>
        <span>fw: {device.firmware || "—"}</span>
        <span>{formatRelativeTime(device.lastSeen)}</span>
      </div>
    </button>
  );
}

function Metric({ label, value, anomaly }) {
  return (
    <div style={{
      background: "#060b12", borderRadius: 4,
      padding: "5px 8px",
      border: `1px solid ${anomaly ? "#7f1d1d" : "#0f1923"}`,
    }}>
      <div style={{ fontSize: 9, fontFamily: "IBM Plex Mono", color: "#334155", marginBottom: 2 }}>
        {label.toUpperCase()}
      </div>
      <div style={{
        fontSize: 11, fontFamily: "IBM Plex Mono",
        fontWeight: 700,
        color: anomaly ? "#ef4444" : "#94a3b8",
      }}>
        {value}
      </div>
    </div>
  );
}
