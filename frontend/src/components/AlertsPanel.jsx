import { useState } from "react";
import { useTheme } from "../context/ThemeContext";

const TYPE_LABELS = {
  VibrationSpike     : "Vibration Spike",
  VibrationSustained : "Sustained Vibration",
  ThermalOverheat    : "Thermal Overload",
  HumidityOutOfRange : "Humidity OOR",
  RateOfChangeSpike  : "Rate-of-Change Spike",
  SensorFault        : "Sensor Fault",
};

// Text-char icons instead of emoji for professional SCADA look
const SEVERITY_ICONS = {
  Fault    : "⊗",
  Critical : "●",
  Warning  : "▲",
  Info     : "ℹ",
};

function AlertRow({ alert, onAcknowledge, T }) {
  const time       = new Date(alert.timestamp).toLocaleTimeString();
  const typeLabel  = TYPE_LABELS[alert.type] ?? alert.type;

  const sevColor = {
    Fault    : T.fault,
    Critical : T.critical,
    Warning  : T.warning,
    Info     : T.info,
  }[alert.severity] ?? T.info;

  const sevBg = {
    Fault    : T.name === "dark" ? "#1c0a0a" : "#fde8e8",
    Critical : T.name === "dark" ? "#1c0f0a" : "#fdeee8",
    Warning  : T.name === "dark" ? "#1c160a" : "#fdf7e8",
    Info     : T.name === "dark" ? "#0a1220" : "#e8f4fd",
  }[alert.severity] ?? T.bg2;

  const sevBorder = {
    Fault    : T.name === "dark" ? "#5c1818" : "#f5a0a0",
    Critical : T.name === "dark" ? "#5c2c18" : "#f5c0a0",
    Warning  : T.name === "dark" ? "#5c4a18" : "#f5e0a0",
    Info     : T.name === "dark" ? "#18385c" : "#a0c8f5",
  }[alert.severity] ?? T.border;

  const icon = SEVERITY_ICONS[alert.severity] ?? "ℹ";
  const sevLabel = alert.severity?.toUpperCase() ?? "INFO";

  return (
    <div style={{
      background   : sevBg,
      border       : `1px solid ${sevBorder}`,
      borderLeft   : `3px solid ${sevColor}`,
      borderRadius : 4,
      padding      : "10px 14px",
      marginBottom : 6,
      opacity      : alert.acknowledged ? 0.5 : 1,
      transition   : "opacity 0.3s, background 0.2s",
      display      : "grid",
      gridTemplateColumns: "auto 1fr auto",
      gap          : "0 12px",
      alignItems   : "start",
    }}>
      {/* Severity badge */}
      <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 4, minWidth: 36 }}>
        <span style={{ fontSize: 16, color: sevColor, fontFamily: "IBM Plex Mono", lineHeight: 1 }}>
          {icon}
        </span>
        <span style={{
          fontSize: 9, fontFamily: "IBM Plex Mono", fontWeight: 700,
          color: sevColor, letterSpacing: "0.08em",
        }}>
          {sevLabel}
        </span>
      </div>

      {/* Content */}
      <div>
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 3, flexWrap: "wrap" }}>
          <span style={{ color: T.text1, fontSize: 11, fontFamily: "IBM Plex Mono" }}>
            {alert.deviceId}
          </span>
          <span style={{
            background: T.bg2, color: sevColor, border: `1px solid ${sevBorder}`,
            borderRadius: 2, padding: "0 5px", fontSize: 9, fontFamily: "IBM Plex Mono",
            letterSpacing: "0.06em",
          }}>
            {typeLabel}
          </span>
          <span style={{ color: T.text2, fontSize: 10, fontFamily: "IBM Plex Mono", marginLeft: "auto" }}>
            {time}
          </span>
        </div>
        <p style={{
          color: T.text0, fontSize: 12, margin: 0,
          fontFamily: "IBM Plex Mono", lineHeight: 1.5,
        }}>
          {alert.message}
        </p>
        <div style={{ display: "flex", gap: 16, marginTop: 5 }}>
          <AlertMetric label="Value"    value={alert.value?.toFixed(4)}      color={sevColor}  T={T} />
          <AlertMetric label="Baseline" value={alert.threshold?.toFixed(4)}  color={T.text2}   T={T} />
          <AlertMetric label="Z-score"  value={alert.zScore?.toFixed(2)+"σ"} color={T.accent}  T={T} />
        </div>
      </div>

      {/* ACK button */}
      <div>
        {!alert.acknowledged && (
          <button
            onClick={() => onAcknowledge(alert.id)}
            style={{
              background  : "transparent",
              border      : `1px solid ${T.border}`,
              borderRadius: 4,
              color       : T.text2,
              cursor      : "pointer",
              fontSize    : 10,
              fontFamily  : "IBM Plex Mono",
              padding     : "4px 8px",
              whiteSpace  : "nowrap",
              transition  : "all 0.15s",
            }}
            onMouseEnter={(e) => {
              e.currentTarget.style.borderColor = T.accent;
              e.currentTarget.style.color = T.accent;
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.borderColor = T.border;
              e.currentTarget.style.color = T.text2;
            }}
          >
            ACK
          </button>
        )}
        {alert.acknowledged && (
          <span style={{ fontSize: 10, color: T.text2, fontFamily: "IBM Plex Mono" }}>✓ acked</span>
        )}
      </div>
    </div>
  );
}

function AlertMetric({ label, value, color, T }) {
  return (
    <span style={{ fontSize: 10, fontFamily: "IBM Plex Mono", color: T.text2 }}>
      {label}: <span style={{ color }}>{value ?? "—"}</span>
    </span>
  );
}

/**
 * AlertsPanel
 * Scrollable alert feed with severity filter tabs and acknowledgement.
 */
export default function AlertsPanel({ alerts = [], onAcknowledge }) {
  const { T } = useTheme();
  const [filter, setFilter] = useState("All");
  const severities = ["All", "Fault", "Critical", "Warning", "Info"];

  const filtered = filter === "All"
    ? alerts
    : alerts.filter((a) => a.severity === filter);

  const countFor = (sev) => sev === "All"
    ? alerts.filter((a) => !a.acknowledged).length
    : alerts.filter((a) => a.severity === sev && !a.acknowledged).length;

  const getSevColor = (sev) => ({
    Fault    : T.fault,
    Critical : T.critical,
    Warning  : T.warning,
    Info     : T.info,
  }[sev] ?? T.accent);

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
      {/* Filter tabs */}
      <div style={{
        display: "flex", gap: 4, marginBottom: 12,
        borderBottom: `1px solid ${T.border}`, paddingBottom: 10,
        flexWrap: "wrap",
      }}>
        {severities.map((sev) => {
          const sevColor = getSevColor(sev);
          const count = countFor(sev);
          const active = filter === sev;
          return (
            <button
              key={sev}
              onClick={() => setFilter(sev)}
              style={{
                background  : active ? (sev === "All" ? T.accent : getSevColor(sev)) + (T.name === "dark" ? "22" : "18") : "transparent",
                border      : `1px solid ${active ? (sev === "All" ? T.accent : sevColor) : T.border}`,
                borderRadius: 4,
                color       : active ? (sev === "All" ? T.accent : sevColor) : T.text2,
                cursor      : "pointer",
                fontSize    : 10,
                fontFamily  : "IBM Plex Mono",
                padding     : "4px 10px",
                display     : "flex",
                alignItems  : "center",
                gap         : 6,
                transition  : "all 0.15s",
              }}
            >
              {sev}
              {count > 0 && (
                <span style={{
                  background: sev === "All" ? T.accent : sevColor,
                  color: "#fff", borderRadius: 8,
                  fontSize: 9, fontWeight: 700,
                  padding: "0 5px", lineHeight: "14px",
                }}>
                  {count}
                </span>
              )}
            </button>
          );
        })}
      </div>

      {/* Alert list */}
      <div style={{ overflowY: "auto", flex: 1 }}>
        {filtered.length === 0 && (
          <div style={{
            textAlign: "center", color: T.text2,
            fontFamily: "IBM Plex Mono", fontSize: 12, marginTop: 40,
          }}>
            No {filter === "All" ? "" : filter.toLowerCase()} alerts
          </div>
        )}
        {filtered.map((a) => (
          <AlertRow key={a.id} alert={a} onAcknowledge={onAcknowledge} T={T} />
        ))}
      </div>
    </div>
  );
}
