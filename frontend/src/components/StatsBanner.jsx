import { useMemo } from "react";
import { SEVERITY_COLORS } from "../utils/formatters";

/**
 * StatsBanner
 * Horizontal bar showing aggregate stats across all devices and alerts.
 * Updates in real time from the SignalR device map and alert list.
 *
 * Props:
 *   devices    : Record<deviceId, latestFrame>  — from useSensorData
 *   alerts     : Alert[]                         — from useSensorData
 *   deviceList : Device[]                        — from useDevices REST list
 */
export default function StatsBanner({ devices = {}, alerts = [], deviceList = [] }) {
  const stats = useMemo(() => {
    const online  = deviceList.filter((d) => d.status === "Online").length;
    const stale   = deviceList.filter((d) => d.isStale).length;
    const offline = deviceList.filter((d) => d.status === "Offline").length;

    const unacked = alerts.filter((a) => !a.acknowledged);
    const faults   = unacked.filter((a) => a.severity === "Fault").length;
    const critical = unacked.filter((a) => a.severity === "Critical").length;
    const warnings = unacked.filter((a) => a.severity === "Warning").length;

    // Highest active vibration across all live devices
    const vibValues = Object.values(devices)
      .map((f) => f.vibration)
      .filter((v) => v != null);
    const maxVib = vibValues.length ? Math.max(...vibValues) : null;

    return { online, stale, offline, faults, critical, warnings, maxVib };
  }, [devices, alerts, deviceList]);

  return (
    <div style={{
      background  : "#060e18",
      borderBottom: "1px solid #0d1f2d",
      padding     : "6px 28px",
      display     : "flex",
      alignItems  : "center",
      gap         : 24,
      fontSize    : 10,
      fontFamily  : "IBM Plex Mono",
      overflowX   : "auto",
    }}>
      {/* Device status */}
      <StatGroup label="DEVICES">
        <Pip color="#22c55e" value={stats.online}  label="online"  />
        <Pip color="#f59e0b" value={stats.stale}   label="stale"   />
        <Pip color="#475569" value={stats.offline} label="offline" />
      </StatGroup>

      <Divider />

      {/* Alert counts */}
      <StatGroup label="ACTIVE ALERTS">
        {stats.faults   > 0 && <Pip color={SEVERITY_COLORS.Fault}    value={stats.faults}   label="fault"    />}
        {stats.critical > 0 && <Pip color={SEVERITY_COLORS.Critical} value={stats.critical} label="critical" />}
        {stats.warnings > 0 && <Pip color={SEVERITY_COLORS.Warning}  value={stats.warnings} label="warning"  />}
        {stats.faults + stats.critical + stats.warnings === 0 && (
          <span style={{ color: "#22c55e" }}>All clear</span>
        )}
      </StatGroup>

      <Divider />

      {/* Peak vibration */}
      <StatGroup label="PEAK VIB">
        <span style={{
          color: stats.maxVib != null && stats.maxVib > 15 ? "#ef4444" : "#94a3b8",
          fontWeight: 700,
        }}>
          {stats.maxVib != null ? `${stats.maxVib.toFixed(3)} m/s²` : "—"}
        </span>
      </StatGroup>

      {/* Spacer + timestamp */}
      <span style={{ marginLeft: "auto", color: "#334155" }}>
        {new Date().toLocaleTimeString([], { hour12: false })}
      </span>
    </div>
  );
}

function StatGroup({ label, children }) {
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
      <span style={{ color: "#334155", letterSpacing: "0.1em" }}>{label}:</span>
      <div style={{ display: "flex", gap: 10 }}>{children}</div>
    </div>
  );
}

function Pip({ color, value, label }) {
  return (
    <span style={{ display: "flex", alignItems: "center", gap: 4 }}>
      <span style={{
        width: 6, height: 6, borderRadius: "50%",
        background: color, display: "inline-block",
        boxShadow: `0 0 4px ${color}60`,
      }} />
      <span style={{ color, fontWeight: 700 }}>{value}</span>
      <span style={{ color: "#475569" }}>{label}</span>
    </span>
  );
}

function Divider() {
  return <span style={{ width: 1, height: 14, background: "#132030" }} />;
}
