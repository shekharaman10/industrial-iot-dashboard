import { memo, useMemo } from "react";
import { formatVib, formatTemp, formatRelativeTime } from "../utils/formatters.js";
import { useTheme } from "../context/ThemeContext";

const SEVERITY_ORDER = { Fault: 4, Critical: 3, Warning: 2, Info: 1 };

/**
 * DeviceCard
 * Compact instrument-tag style card showing one device's latest status.
 * Wrapped in React.memo — only re-renders when its specific device's frame changes.
 *
 * Props:
 *   device      : { id, location, status, firmware, lastSeen, isStale }
 *   latestFrame : latest TelemetryReceived payload for this device (may be null)
 *   isSelected  : bool — highlight as active
 *   onSelect    : () => void
 */
const DeviceCard = memo(function DeviceCard({ device, latestFrame, isSelected, onSelect }) {
  const { T } = useTheme();

  const statusColor = {
    Online  : T.online,
    Offline : T.offline,
    Degraded: T.stale,
    Unknown : T.text2,
  }[device.status] ?? T.text2;

  const vibAnomaly  = latestFrame?.analysis?.vibration?.isAnomaly;
  const tempAnomaly = latestFrame?.analysis?.temperature?.isAnomaly;

  const worstSeverity = useMemo(() => {
    const sev = [
      latestFrame?.analysis?.vibration?.severity,
      latestFrame?.analysis?.temperature?.severity,
    ].filter(Boolean);
    return sev.sort((a, b) => (SEVERITY_ORDER[b] ?? 0) - (SEVERITY_ORDER[a] ?? 0))[0] ?? null;
  }, [latestFrame?.analysis?.vibration?.severity, latestFrame?.analysis?.temperature?.severity]);

  const sevColor = worstSeverity ? {
    Fault    : T.fault,
    Critical : T.critical,
    Warning  : T.warning,
    Info     : T.info,
  }[worstSeverity] ?? T.info : null;

  return (
    <button
      onClick={onSelect}
      aria-pressed={isSelected}
      aria-label={`Device ${device.id} — ${device.status}${worstSeverity ? `, ${worstSeverity} alert` : ""}`}
      style={{
        background    : isSelected ? T.bg3 : T.bg1,
        border        : `1px solid ${isSelected ? T.borderAccent : T.border}`,
        borderLeft    : `3px solid ${statusColor}`,
        borderTop     : worstSeverity ? `2px solid ${sevColor}` : `2px solid transparent`,
        borderRadius  : 4,
        cursor        : "pointer",
        padding       : "11px 13px",
        textAlign     : "left",
        width         : "100%",
        boxShadow     : isSelected ? T.shadowSm : "none",
        transition    : "all 0.2s",
      }}
    >
      {/* Header row */}
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6 }}>
        <span style={{
          width: 8, height: 8, borderRadius: "50%",
          background: statusColor,
          boxShadow : device.status === "Online" ? `0 0 5px ${statusColor}` : "none",
          flexShrink: 0,
          animation: device.status === "Online" ? "led-pulse 2s infinite" : "none",
        }} />
        <span style={{
          fontFamily: "IBM Plex Mono", fontSize: 12,
          fontWeight: 700, color: isSelected ? T.text0 : T.text1,
          letterSpacing: "0.04em",
        }}>
          {device.id}
        </span>
        {device.isStale && (
          <span style={{
            fontSize: 8, fontFamily: "IBM Plex Mono", color: T.stale,
            background: T.name === "dark" ? "#1c160a" : "#fdf7e8",
            border: `1px solid ${T.stale}60`,
            borderRadius: 3, padding: "0 4px",
          }}>
            STALE
          </span>
        )}
        {worstSeverity && (
          <span style={{
            fontSize: 9, fontFamily: "IBM Plex Mono",
            color: sevColor,
            background: T.bg1,
            border: `1px solid ${sevColor}40`,
            borderRadius: 3, padding: "0 4px",
            marginLeft: "auto",
          }}>
            {worstSeverity.toUpperCase()}
          </span>
        )}
      </div>

      {/* Location */}
      <div style={{
        fontSize: 10, fontFamily: "IBM Plex Mono", color: T.text2, marginBottom: 8,
        overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
      }}>
        {device.location || "—"}
      </div>

      {/* Live values */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 6 }}>
        <MetricCell
          label="VIBRATION"
          value={formatVib(latestFrame?.vibration)}
          anomaly={vibAnomaly}
          T={T}
        />
        <MetricCell
          label="TEMP"
          value={formatTemp(latestFrame?.temperature)}
          anomaly={tempAnomaly}
          T={T}
        />
      </div>

      {/* Footer */}
      <div style={{
        marginTop: 8, fontSize: 9, fontFamily: "IBM Plex Mono",
        color: T.text2, display: "flex", justifyContent: "space-between",
      }}>
        <span>fw: {device.firmware || "—"}</span>
        <span>{formatRelativeTime(device.lastSeen)}</span>
      </div>
    </button>
  );
});

export default DeviceCard;

function MetricCell({ label, value, anomaly, T }) {
  return (
    <div style={{
      background: T.bg2,
      borderRadius: 3,
      padding: "5px 8px",
      border: `1px solid ${anomaly ? T.fault + "80" : T.border}`,
      transition: "border-color 0.2s",
    }}>
      <div style={{ fontSize: 8, fontFamily: "IBM Plex Mono", color: T.text2, marginBottom: 2, letterSpacing: "0.08em" }}>
        {label}
      </div>
      <div style={{
        fontSize: 11, fontFamily: "IBM Plex Mono",
        fontWeight: 700,
        color: anomaly ? T.fault : T.text1,
        transition: "color 0.2s",
        fontVariantNumeric: "tabular-nums",
      }}>
        {value}
      </div>
    </div>
  );
}
