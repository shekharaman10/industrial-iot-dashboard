import { useState } from "react";

const SEVERITY_CONFIG = {
  Fault    : { color: "#ef4444", bg: "#1c0a0a", border: "#7f1d1d", icon: "⛔", label: "FAULT"    },
  Critical : { color: "#f97316", bg: "#1c0f0a", border: "#7c2d12", icon: "🔴", label: "CRITICAL" },
  Warning  : { color: "#f59e0b", bg: "#1c160a", border: "#78350f", icon: "🟡", label: "WARNING"  },
  Info     : { color: "#60a5fa", bg: "#0a1220", border: "#1e3a5f", icon: "🔵", label: "INFO"     },
};

const TYPE_LABELS = {
  VibrationSpike     : "Vibration Spike",
  VibrationSustained : "Sustained Vibration",
  ThermalOverheat    : "Thermal Overload",
  HumidityOutOfRange : "Humidity OOR",
  RateOfChangeSpike  : "Rate-of-Change Spike",
  SensorFault        : "Sensor Fault",
};

function AlertRow({ alert, onAcknowledge }) {
  const cfg        = SEVERITY_CONFIG[alert.severity] ?? SEVERITY_CONFIG.Info;
  const time       = new Date(alert.timestamp).toLocaleTimeString();
  const typeLabel  = TYPE_LABELS[alert.type] ?? alert.type;

  return (
    <div style={{
      background   : cfg.bg,
      border       : `1px solid ${cfg.border}`,
      borderLeft   : `3px solid ${cfg.color}`,
      borderRadius : 4,
      padding      : "10px 14px",
      marginBottom : 6,
      opacity      : alert.acknowledged ? 0.45 : 1,
      transition   : "opacity 0.3s",
      display      : "grid",
      gridTemplateColumns: "auto 1fr auto",
      gap          : "0 12px",
      alignItems   : "start",
    }}>
      {/* Severity badge */}
      <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 4 }}>
        <span style={{ fontSize: 16 }}>{cfg.icon}</span>
        <span style={{
          fontSize: 9, fontFamily: "IBM Plex Mono", fontWeight: 700,
          color: cfg.color, letterSpacing: "0.08em",
        }}>
          {cfg.label}
        </span>
      </div>

      {/* Content */}
      <div>
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 3 }}>
          <span style={{ color: "#94a3b8", fontSize: 11, fontFamily: "IBM Plex Mono" }}>
            {alert.deviceId}
          </span>
          <span style={{
            background: "#0f1923", color: cfg.color, border: `1px solid ${cfg.border}`,
            borderRadius: 2, padding: "0 5px", fontSize: 9, fontFamily: "IBM Plex Mono",
            letterSpacing: "0.06em",
          }}>
            {typeLabel}
          </span>
          <span style={{ color: "#334155", fontSize: 10, fontFamily: "IBM Plex Mono", marginLeft: "auto" }}>
            {time}
          </span>
        </div>
        <p style={{
          color: "#cbd5e1", fontSize: 12, margin: 0,
          fontFamily: "IBM Plex Mono", lineHeight: 1.5,
        }}>
          {alert.message}
        </p>
        <div style={{ display: "flex", gap: 16, marginTop: 5 }}>
          <Metric label="Value"    value={alert.value?.toFixed(4)}     color={cfg.color} />
          <Metric label="Baseline" value={alert.threshold?.toFixed(4)} color="#475569"   />
          <Metric label="Z-score"  value={alert.zScore?.toFixed(2)+"σ"}color="#60a5fa"   />
        </div>
      </div>

      {/* ACK button */}
      <div>
        {!alert.acknowledged && (
          <button
            onClick={() => onAcknowledge(alert.id)}
            style={{
              background  : "transparent",
              border      : "1px solid #1e3a4a",
              borderRadius: 4,
              color       : "#475569",
              cursor      : "pointer",
              fontSize    : 10,
              fontFamily  : "IBM Plex Mono",
              padding     : "4px 8px",
              whiteSpace  : "nowrap",
              transition  : "all 0.15s",
            }}
            onMouseEnter={(e) => { e.currentTarget.style.borderColor = "#60a5fa"; e.currentTarget.style.color = "#60a5fa"; }}
            onMouseLeave={(e) => { e.currentTarget.style.borderColor = "#1e3a4a"; e.currentTarget.style.color = "#475569"; }}
          >
            ACK
          </button>
        )}
        {alert.acknowledged && (
          <span style={{ fontSize: 10, color: "#334155", fontFamily: "IBM Plex Mono" }}>✓ acked</span>
        )}
      </div>
    </div>
  );
}

function Metric({ label, value, color }) {
  return (
    <span style={{ fontSize: 10, fontFamily: "IBM Plex Mono", color: "#475569" }}>
      {label}: <span style={{ color }}>{value ?? "—"}</span>
    </span>
  );
}

/**
 * AlertsPanel
 * Scrollable alert feed with severity filter tabs and acknowledgement.
 */
export default function AlertsPanel({ alerts = [], onAcknowledge }) {
  const [filter, setFilter] = useState("All");
  const severities = ["All", "Fault", "Critical", "Warning", "Info"];

  const filtered = filter === "All"
    ? alerts
    : alerts.filter((a) => a.severity === filter);

  const countFor = (sev) => sev === "All"
    ? alerts.filter((a) => !a.acknowledged).length
    : alerts.filter((a) => a.severity === sev && !a.acknowledged).length;

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
      {/* Filter tabs */}
      <div style={{
        display: "flex", gap: 4, marginBottom: 12,
        borderBottom: "1px solid #0f1923", paddingBottom: 10,
      }}>
        {severities.map((sev) => {
          const cfg   = SEVERITY_CONFIG[sev] ?? { color: "#60a5fa" };
          const count = countFor(sev);
          const active= filter === sev;
          return (
            <button
              key={sev}
              onClick={() => setFilter(sev)}
              style={{
                background  : active ? "#0f1923" : "transparent",
                border      : `1px solid ${active ? cfg.color ?? "#60a5fa" : "#1e2d3d"}`,
                borderRadius: 4,
                color       : active ? cfg.color ?? "#60a5fa" : "#475569",
                cursor      : "pointer",
                fontSize    : 10,
                fontFamily  : "IBM Plex Mono",
                padding     : "4px 10px",
                display     : "flex",
                alignItems  : "center",
                gap         : 6,
              }}
            >
              {sev}
              {count > 0 && (
                <span style={{
                  background: cfg.color ?? "#60a5fa",
                  color: "#000", borderRadius: 8,
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
            textAlign: "center", color: "#334155",
            fontFamily: "IBM Plex Mono", fontSize: 12, marginTop: 40,
          }}>
            No {filter === "All" ? "" : filter.toLowerCase()} alerts
          </div>
        )}
        {filtered.map((a) => (
          <AlertRow key={a.id} alert={a} onAcknowledge={onAcknowledge} />
        ))}
      </div>
    </div>
  );
}
