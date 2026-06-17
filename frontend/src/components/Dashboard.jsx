import { useState, useEffect, useCallback, Component } from "react";
import VibrationChart    from "./Charts/VibrationChart";
import TemperatureChart  from "./Charts/TemperatureChart";
import AlertsPanel       from "./AlertsPanel";
import DeviceCard        from "./DeviceCard";
import AnalysisOverlay   from "./AnalysisOverlay";
import { useSensorData } from "../hooks/useSensorData";
import { fetchDevices }  from "../services/api";
import { formatZScore } from "../utils/formatters";
import { TEMP_WARNING_THRESHOLD, HISTORY_WINDOWS } from "../utils/constants";
import { useTheme } from "../context/ThemeContext";

// ─── Error Boundary ───────────────────────────────────────────────────────────
class ChartErrorBoundary extends Component {
  constructor(props) {
    super(props);
    this.state = { hasError: false, T: props.T };
  }
  static getDerivedStateFromError() { return { hasError: true }; }
  componentDidCatch(err) { console.error("[ChartErrorBoundary]", err); }
  render() {
    const T = this.props.T;
    if (this.state.hasError) {
      return (
        <div style={{
          height: 240, display: "flex", alignItems: "center", justifyContent: "center",
          background: T.bg2, borderRadius: 4, color: T.text2,
          fontFamily: "IBM Plex Mono", fontSize: 12,
        }}>
          Chart failed to render. Check console for details.
        </div>
      );
    }
    return this.props.children;
  }
}

// ─── Sub-components ───────────────────────────────────────────────────────────

function ConnectionBadge({ status, T }) {
  const color = {
    Connected    : T.online,
    Reconnecting : T.stale,
    Disconnected : T.fault,
  }[status] ?? T.text2;

  return (
    <div
      role="status"
      aria-label={`Connection status: ${status}`}
      aria-live="polite"
      style={{ display: "flex", alignItems: "center", gap: 6 }}
    >
      <span style={{
        display: "inline-block", width: 7, height: 7, borderRadius: "50%",
        background: color,
        boxShadow: status === "Connected" ? `0 0 6px ${color}` : "none",
        animation: status === "Connected" ? "led-pulse 2s infinite" : "none",
      }} />
      <span style={{ fontFamily: "IBM Plex Mono", fontSize: 11, color, letterSpacing: "0.06em" }}>
        {status.toUpperCase()}
      </span>
    </div>
  );
}

function MetricCard({ label, value, unit, color, sublabel, anomaly, T }) {
  const cardColor = color ?? T.accent;
  return (
    <div
      role={anomaly ? "alert" : "status"}
      aria-label={`${label}: ${value ?? "no data"}${unit ? " " + unit : ""}${anomaly ? ", anomaly detected" : ""}`}
      style={{
        background: T.bg2,
        border: `1px solid ${anomaly ? T.fault + "80" : T.border}`,
        borderTop: `2px solid ${anomaly ? T.fault : cardColor}`,
        borderRadius: 4, padding: "14px 18px",
        minWidth: 148,
        transition: "border-color 0.2s, background 0.2s",
      }}
    >
      {anomaly && (
        <div style={{
          fontSize: 9, fontFamily: "IBM Plex Mono", color: T.fault,
          letterSpacing: "0.08em", marginBottom: 4,
          animation: "blink 1s step-start infinite",
        }} aria-hidden="true">
          ⚠ ANOMALY
        </div>
      )}
      <div style={{
        fontSize: 9, fontFamily: "IBM Plex Mono", color: T.text2,
        letterSpacing: "0.1em", marginBottom: 6, textTransform: "uppercase",
      }}>
        {label}
      </div>
      <div style={{ display: "flex", alignItems: "baseline", gap: 4 }}>
        <span style={{
          fontFamily: "IBM Plex Mono", fontWeight: 700, fontSize: 24,
          color: anomaly ? T.fault : cardColor, transition: "color 0.2s",
        }}>
          {value ?? "—"}
        </span>
        {unit && <span style={{ fontFamily: "IBM Plex Mono", fontSize: 11, color: T.text2 }}>{unit}</span>}
      </div>
      {sublabel && (
        <div style={{ fontSize: 10, fontFamily: "IBM Plex Mono", color: T.text2, marginTop: 4 }}>
          {sublabel}
        </div>
      )}
    </div>
  );
}

function SectionLabel({ icon, children, count, T }) {
  return (
    <div style={{
      display: "flex", alignItems: "center", gap: 8, marginBottom: 12,
    }}>
      {icon && (
        <span style={{
          fontFamily: "IBM Plex Mono", fontSize: 12, color: T.brand,
        }}>{icon}</span>
      )}
      <span style={{
        fontSize: 10, fontFamily: "IBM Plex Mono", fontWeight: 700,
        color: T.text2, letterSpacing: "0.15em", textTransform: "uppercase",
      }}>
        {children}
      </span>
      {count != null && (
        <span style={{
          fontSize: 9, fontFamily: "IBM Plex Mono",
          color: T.accent, border: `1px solid ${T.accent}40`,
          borderRadius: 3, padding: "0 5px",
        }}>
          {count}
        </span>
      )}
      <span style={{ flex: 1, height: 1, background: T.border }} />
    </div>
  );
}

function TabButton({ active, onClick, children, label, T }) {
  return (
    <button
      onClick={onClick}
      aria-pressed={active}
      aria-label={label}
      style={{
        background: active ? T.bg3 : "transparent",
        border: `1px solid ${active ? T.accent : T.border}`,
        borderRadius: 3, color: active ? T.accent : T.text2,
        cursor: "pointer", fontFamily: "IBM Plex Mono",
        fontSize: 10, padding: "5px 14px",
        letterSpacing: "0.08em", textTransform: "uppercase",
        transition: "all 0.2s",
      }}
    >
      {children}
    </button>
  );
}

function Skeleton({ height, T }) {
  return (
    <div
      role="progressbar"
      aria-label="Loading chart data"
      style={{
        height, background: T.bg3, borderRadius: 4,
        animation: "fade-in 1.5s ease-in-out infinite alternate",
      }}
    />
  );
}

// ─── Main Dashboard ────────────────────────────────────────────────────────────

export default function Dashboard() {
  const { T, isDark, toggleTheme } = useTheme();

  const [deviceList,   setDeviceList]   = useState([]);
  const [selectedId,   setSelectedId]   = useState(null);
  const [viewMode,     setViewMode]     = useState("live");   // "live" | "history"
  const [historyMins,  setHistoryMins]  = useState(60);
  const [showAnalysis, setShowAnalysis] = useState(true);
  const [now,          setNow]          = useState(new Date());

  // Clock tick
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(id);
  }, []);

  const loadDevices = useCallback(() => {
    fetchDevices()
      .then((devs) => {
        setDeviceList(devs);
        if (devs.length > 0 && !selectedId) setSelectedId(devs[0].id);
      })
      .catch(console.error);
  }, [selectedId]);

  useEffect(() => { loadDevices(); }, [loadDevices]);

  const {
    connectionStatus, liveReadings, history,
    alerts, devices, loading, ackAlert,
  } = useSensorData(selectedId, historyMins);

  const latest       = selectedId ? devices[selectedId] : null;
  const chartData    = viewMode === "live" ? liveReadings : history;
  const vibAnalysis  = latest?.analysis?.vibration;
  const tempAnalysis = latest?.analysis?.temperature;
  const unackedCount = alerts.filter((a) => !a.acknowledged).length;

  // Status ribbon counts
  const onlineCount  = deviceList.filter((d) => d.status === "Online").length;
  const staleCount   = deviceList.filter((d) => d.isStale).length;
  const offlineCount = deviceList.filter((d) => d.status === "Offline" || d.status === "Unknown").length;
  const faultCount   = alerts.filter((a) => a.severity === "Fault"    && !a.acknowledged).length;
  const critCount    = alerts.filter((a) => a.severity === "Critical"  && !a.acknowledged).length;
  const warnCount    = alerts.filter((a) => a.severity === "Warning"   && !a.acknowledged).length;
  const peakVib      = liveReadings.length > 0
    ? Math.max(...liveReadings.map((r) => r.vibration ?? 0)).toFixed(4)
    : "—";

  return (
    <div style={{
      background: T.bg0, minHeight: "100vh", color: T.text0,
      fontFamily: "'IBM Plex Sans', sans-serif",
      transition: "background 0.2s, color 0.2s",
    }}>
      <style>{`
        @import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;700&family=IBM+Plex+Sans:wght@400;500;600&display=swap');
        * { box-sizing: border-box; margin: 0; padding: 0; }
        ::-webkit-scrollbar { width: 4px; height: 4px; }
        ::-webkit-scrollbar-track { background: transparent; }
        ::-webkit-scrollbar-thumb { background: ${T.border}; border-radius: 2px; }
        @keyframes led-pulse { 0%,100%{opacity:1} 50%{opacity:.7} }
        @keyframes blink { 0%,100%{opacity:1} 50%{opacity:0} }
        @keyframes fade-in { 0%{opacity:.4} 100%{opacity:.7} }
        @keyframes card-in { from{opacity:0;transform:translateY(6px)} to{opacity:1;transform:translateY(0)} }
      `}</style>

      {/* ── HEADER ──────────────────────────────────────────────────────── */}
      <header
        role="banner"
        aria-label="Emerson Ovation IoT Dashboard"
        style={{
          background: T.bg1,
          borderBottom: `1px solid ${T.border}`,
          borderLeft: `4px solid ${T.brand}`,
          padding: "0 28px",
          height: 56,
          display: "flex", alignItems: "center",
          gap: 20, position: "sticky", top: 0, zIndex: 100,
          boxShadow: T.shadowSm,
          transition: "background 0.2s, border-color 0.2s",
        }}
      >
        {/* Left: Logo + Title */}
        <div style={{ display: "flex", alignItems: "center", gap: 10, flex: 1 }}>
          <div style={{
            width: 30, height: 30, borderRadius: 4,
            background: T.brand,
            display: "flex", alignItems: "center", justifyContent: "center",
            fontSize: 16, color: "#fff", fontWeight: 700, flexShrink: 0,
          }} aria-hidden="true">◈</div>
          <div>
            <div style={{ display: "flex", alignItems: "baseline", gap: 6 }}>
              <span style={{
                fontFamily: "'IBM Plex Sans', sans-serif", fontWeight: 600,
                fontSize: 14, color: T.brand, letterSpacing: "0.08em",
              }}>
                EMERSON
              </span>
              <span style={{
                fontFamily: "'IBM Plex Sans', sans-serif", fontWeight: 600,
                fontSize: 14, color: T.text0, letterSpacing: "0.04em",
              }}>
                OVATION™ IoT
              </span>
            </div>
            <div style={{
              fontSize: 10, fontFamily: "IBM Plex Mono", color: T.text2,
              letterSpacing: "0.08em",
            }}>
              Predictive Maintenance System v2.1.0
            </div>
          </div>
        </div>

        {/* Right: alerts + connection + time + theme toggle */}
        <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
          {unackedCount > 0 && (
            <div
              role="alert"
              aria-live="assertive"
              style={{
                background: T.name === "dark" ? "#1c0a0a" : "#fde8e8",
                border: `1px solid ${T.fault}60`,
                borderRadius: 4, padding: "4px 10px",
                fontSize: 11, fontFamily: "IBM Plex Mono", color: T.fault,
                animation: "blink 2s step-start infinite",
                letterSpacing: "0.06em",
              }}
            >
              ⚠ {unackedCount} UNACKED ALERT{unackedCount > 1 ? "S" : ""}
            </div>
          )}

          <ConnectionBadge status={connectionStatus} T={T} />

          <div style={{
            fontSize: 10, fontFamily: "IBM Plex Mono", color: T.text2,
            letterSpacing: "0.04em",
          }} aria-hidden="true">
            {now.toLocaleString()}
          </div>

          {/* Theme toggle */}
          <button
            onClick={toggleTheme}
            aria-label={isDark ? "Switch to light mode" : "Switch to dark mode"}
            title={isDark ? "Switch to light mode" : "Switch to dark mode"}
            style={{
              width: 36, height: 36, borderRadius: "50%",
              border: `1px solid ${T.border}`,
              background: "transparent",
              cursor: "pointer",
              display: "flex", alignItems: "center", justifyContent: "center",
              fontSize: 16,
              color: T.text1,
              transition: "all 0.2s",
            }}
            onMouseEnter={(e) => {
              e.currentTarget.style.borderColor = T.brand;
              e.currentTarget.style.color = T.brand;
              e.currentTarget.style.background = T.bg3;
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.borderColor = T.border;
              e.currentTarget.style.color = T.text1;
              e.currentTarget.style.background = "transparent";
            }}
          >
            {isDark ? "☀" : "☾"}
          </button>
        </div>
      </header>

      {/* ── STATUS RIBBON ────────────────────────────────────────────────── */}
      <div style={{
        background: T.bg1,
        borderBottom: `1px solid ${T.border}`,
        padding: "6px 28px",
        display: "flex", alignItems: "center", gap: 16,
        fontSize: 10, fontFamily: "IBM Plex Mono",
        transition: "background 0.2s",
      }}>
        {/* Device counts */}
        <span style={{ color: T.text2 }}>NODES:</span>
        <RibbonDot color={T.online}  label={`${onlineCount} Online`}  T={T} />
        <RibbonDot color={T.stale}   label={`${staleCount} Stale`}    T={T} />
        <RibbonDot color={T.offline} label={`${offlineCount} Offline`} T={T} />

        <RibbonSep T={T} />

        {/* Alert counts */}
        <span style={{ color: T.text2 }}>ALERTS:</span>
        {faultCount === 0 && critCount === 0 && warnCount === 0 ? (
          <span style={{ color: T.online, fontWeight: 700 }}>All nominal</span>
        ) : (
          <>
            {faultCount > 0 && <RibbonDot color={T.fault}    label={`${faultCount} Fault`}    T={T} />}
            {critCount  > 0 && <RibbonDot color={T.critical} label={`${critCount} Critical`}  T={T} />}
            {warnCount  > 0 && <RibbonDot color={T.warning}  label={`${warnCount} Warning`}   T={T} />}
          </>
        )}

        <RibbonSep T={T} />

        <span style={{ color: T.text2 }}>PEAK VIB:</span>
        <span style={{ color: T.text1 }}>{peakVib} {peakVib !== "—" ? "m/s²" : ""}</span>
      </div>

      <main style={{ padding: "18px 28px", display: "grid", gap: 16 }}>

        {/* ── EQUIPMENT NODES ──────────────────────────────────────────── */}
        <section
          aria-label="Equipment nodes"
          style={{
            background: T.bg1, border: `1px solid ${T.border}`,
            borderRadius: 4, padding: "14px 18px",
            transition: "background 0.2s, border-color 0.2s",
          }}
        >
          <SectionLabel icon="⬡" count={deviceList.length} T={T}>
            EQUIPMENT NODES
          </SectionLabel>

          {deviceList.length === 0 ? (
            <div style={{ fontSize: 12, fontFamily: "IBM Plex Mono", color: T.text2, padding: "8px 0" }}>
              No devices registered. Start the simulator or connect an ESP32.
            </div>
          ) : (
            <div style={{
              display: "flex", gap: 10, overflowX: "auto",
              paddingBottom: 4,
            }}>
              {deviceList.map((d) => (
                <div key={d.id} style={{ minWidth: 200, flexShrink: 0 }}>
                  <DeviceCard
                    device={d}
                    latestFrame={devices[d.id]}
                    isSelected={d.id === selectedId}
                    onSelect={() => setSelectedId(d.id)}
                  />
                </div>
              ))}
            </div>
          )}
        </section>

        {/* ── SELECTED DEVICE DETAIL ───────────────────────────────────── */}
        {selectedId && (
          <div style={{ display: "grid", gridTemplateColumns: "1fr 360px", gap: 16 }}>

            {/* LEFT COLUMN */}
            <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>

              {/* Metric cards row */}
              <section aria-label="Live metrics" style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
                <MetricCard
                  label="VIB RMS"
                  value={latest?.vibration?.toFixed(4) ?? "—"}
                  unit="m/s²"
                  color={T.vib}
                  anomaly={vibAnalysis?.isAnomaly}
                  sublabel={vibAnalysis
                    ? `Z=${formatZScore(vibAnalysis.zScore)}  avg=${vibAnalysis.movingAvg?.toFixed(3)}`
                    : "Awaiting data…"}
                  T={T}
                />
                <MetricCard
                  label="TEMPERATURE"
                  value={latest?.temperature?.toFixed(1) ?? "—"}
                  unit="°C"
                  color={T.temp}
                  anomaly={tempAnalysis?.isAnomaly}
                  sublabel={`Humidity: ${latest?.humidity?.toFixed(1) ?? "—"} %`}
                  T={T}
                />
                <MetricCard
                  label="SEQUENCE"
                  value={latest?.seq ?? "—"}
                  color={T.accent}
                  sublabel={latest?.location ?? "—"}
                  T={T}
                />
                <MetricCard
                  label="BUFFER"
                  value={liveReadings.length}
                  unit="pts"
                  color={T.humid}
                  sublabel="live ring (120 max)"
                  T={T}
                />
              </section>

              {/* Analytics overlay */}
              {showAnalysis && (vibAnalysis || tempAnalysis) && (
                <AnalysisOverlay
                  vibAnalysis={vibAnalysis}
                  tempAnalysis={tempAnalysis}
                />
              )}

              {/* Chart controls tab bar */}
              <div role="toolbar" aria-label="View controls" style={{ display: "flex", gap: 6, alignItems: "center", flexWrap: "wrap" }}>
                <TabButton
                  active={viewMode === "live"}
                  onClick={() => setViewMode("live")}
                  label="Switch to live view"
                  T={T}
                >
                  ● LIVE
                </TabButton>
                {HISTORY_WINDOWS.map((w) => (
                  <TabButton
                    key={w.minutes}
                    active={viewMode === "history" && historyMins === w.minutes}
                    onClick={() => { setViewMode("history"); setHistoryMins(w.minutes); }}
                    label={`View ${w.label} history`}
                    T={T}
                  >
                    {w.label.toUpperCase()}
                  </TabButton>
                ))}
                <div style={{ marginLeft: "auto" }}>
                  <button
                    onClick={() => setShowAnalysis((v) => !v)}
                    aria-pressed={showAnalysis}
                    aria-label={showAnalysis ? "Hide analysis overlay" : "Show analysis overlay"}
                    style={{
                      background: showAnalysis ? T.bg3 : "transparent",
                      border: `1px solid ${T.border}`, borderRadius: 3,
                      color: T.text2, cursor: "pointer",
                      fontFamily: "IBM Plex Mono", fontSize: 10, padding: "5px 10px",
                      letterSpacing: "0.06em", transition: "all 0.2s",
                    }}
                  >
                    {showAnalysis ? "HIDE ANALYSIS" : "SHOW ANALYSIS"}
                  </button>
                </div>
              </div>

              {/* Vibration Chart */}
              <section
                aria-label="Vibration RMS chart"
                style={{
                  background: T.bg1,
                  border: `1px solid ${vibAnalysis?.isAnomaly ? T.fault + "80" : T.border}`,
                  borderRadius: 4, padding: "16px 18px",
                  transition: "border-color 0.4s, background 0.2s",
                }}
              >
                <SectionLabel icon="◈" T={T}>VIBRATION RMS (m/s²)</SectionLabel>
                {loading
                  ? <Skeleton height={240} T={T} />
                  : (
                    <ChartErrorBoundary T={T}>
                      <VibrationChart
                        data={chartData}
                        showAvg
                        baselineRef={vibAnalysis?.baseline}
                      />
                    </ChartErrorBoundary>
                  )
                }
              </section>

              {/* Temperature Chart */}
              <section
                aria-label="Temperature and humidity chart"
                style={{
                  background: T.bg1,
                  border: `1px solid ${tempAnalysis?.isAnomaly ? T.fault + "80" : T.border}`,
                  borderRadius: 4, padding: "16px 18px",
                  transition: "border-color 0.4s, background 0.2s",
                }}
              >
                <SectionLabel icon="◈" T={T}>TEMPERATURE (°C) + HUMIDITY (%)</SectionLabel>
                {loading
                  ? <Skeleton height={240} T={T} />
                  : (
                    <ChartErrorBoundary T={T}>
                      <TemperatureChart
                        data={chartData}
                        tempWarningThreshold={TEMP_WARNING_THRESHOLD}
                      />
                    </ChartErrorBoundary>
                  )
                }
              </section>
            </div>

            {/* RIGHT COLUMN — Alerts */}
            <section
              aria-label="Alerts panel"
              aria-live="polite"
              style={{
                background: T.bg1, border: `1px solid ${T.border}`,
                borderRadius: 4, padding: "16px 18px",
                display: "flex", flexDirection: "column",
                maxHeight: "calc(100vh - 160px)", overflow: "hidden",
                transition: "background 0.2s, border-color 0.2s",
              }}
            >
              <SectionLabel icon="▲" T={T}>ALERTS</SectionLabel>
              <AlertsPanel alerts={alerts} onAcknowledge={ackAlert} />
            </section>
          </div>
        )}
      </main>
    </div>
  );
}

// ─── Ribbon helpers ───────────────────────────────────────────────────────────

function RibbonDot({ color, label, T }) {
  return (
    <span style={{ display: "flex", alignItems: "center", gap: 4 }}>
      <span style={{
        width: 6, height: 6, borderRadius: "50%",
        background: color, flexShrink: 0,
      }} />
      <span style={{ color: T.text1 }}>{label}</span>
    </span>
  );
}

function RibbonSep({ T }) {
  return (
    <span style={{
      width: 1, height: 12, background: T.border,
      display: "inline-block", flexShrink: 0,
    }} />
  );
}
