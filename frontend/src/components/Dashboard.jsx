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

// ─── Design tokens ────────────────────────────────────────────────────────────
const C = {
  bg0   : "#060b12",
  bg1   : "#0a1019",
  bg2   : "#0f1923",
  border: "#132030",
  accent: "#f59e0b",
  text0 : "#e2e8f0",
  text1 : "#94a3b8",
  text2 : "#475569",
};

// ─── Error Boundary ───────────────────────────────────────────────────────────
class ChartErrorBoundary extends Component {
  constructor(props) {
    super(props);
    this.state = { hasError: false };
  }
  static getDerivedStateFromError() { return { hasError: true }; }
  componentDidCatch(err) { console.error("[ChartErrorBoundary]", err); }
  render() {
    if (this.state.hasError) {
      return (
        <div style={{
          height: 240, display: "flex", alignItems: "center", justifyContent: "center",
          background: "#0f1923", borderRadius: 4, color: "#475569",
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

function ConnectionBadge({ status }) {
  const color = { Connected: "#22c55e", Reconnecting: "#f59e0b", Disconnected: "#ef4444" }[status] ?? C.text2;
  return (
    <div
      role="status"
      aria-label={`Connection status: ${status}`}
      aria-live="polite"
      style={{ display: "flex", alignItems: "center", gap: 6 }}
    >
      <span style={{
        display: "inline-block", width: 7, height: 7, borderRadius: "50%",
        background: color, boxShadow: status === "Connected" ? `0 0 6px ${color}` : "none",
        animation: status === "Connected" ? "pulse 2s infinite" : "none",
      }} />
      <span style={{ fontFamily: "IBM Plex Mono", fontSize: 11, color, letterSpacing: "0.06em" }}>
        {status.toUpperCase()}
      </span>
    </div>
  );
}

function MetricCard({ label, value, unit, color = C.accent, sublabel, anomaly }) {
  return (
    <div
      role={anomaly ? "alert" : "status"}
      aria-label={`${label}: ${value ?? "no data"}${unit ? " " + unit : ""}${anomaly ? ", anomaly detected" : ""}`}
      style={{
        background: C.bg1,
        border: `1px solid ${anomaly ? "#7f1d1d" : C.border}`,
        borderTop: `2px solid ${anomaly ? "#ef4444" : color}`,
        borderRadius: 6, padding: "14px 18px",
        minWidth: 140, transition: "border-color 0.3s",
      }}
    >
      {anomaly && (
        <div style={{
          fontSize: 9, fontFamily: "IBM Plex Mono", color: "#ef4444",
          letterSpacing: "0.08em", marginBottom: 4,
          animation: "blink 1s step-start infinite",
        }} aria-hidden="true">
          ⚠ ANOMALY DETECTED
        </div>
      )}
      <div style={{ fontSize: 10, fontFamily: "IBM Plex Mono", color: C.text2, letterSpacing: "0.1em", marginBottom: 6 }}>
        {label.toUpperCase()}
      </div>
      <div style={{ display: "flex", alignItems: "baseline", gap: 4 }}>
        <span style={{
          fontFamily: "IBM Plex Mono", fontWeight: 700, fontSize: 24,
          color: anomaly ? "#ef4444" : color, transition: "color 0.3s",
        }}>
          {value ?? "—"}
        </span>
        {unit && <span style={{ fontFamily: "IBM Plex Mono", fontSize: 11, color: C.text2 }}>{unit}</span>}
      </div>
      {sublabel && (
        <div style={{ fontSize: 10, fontFamily: "IBM Plex Mono", color: C.text2, marginTop: 4 }}>
          {sublabel}
        </div>
      )}
    </div>
  );
}

function SectionHeader({ children }) {
  return (
    <div style={{
      fontSize: 10, fontFamily: "IBM Plex Mono", fontWeight: 700,
      color: C.text2, letterSpacing: "0.15em", textTransform: "uppercase",
      marginBottom: 10, display: "flex", alignItems: "center", gap: 8,
    }}>
      <span style={{ flex: 1, height: 1, background: C.border }} />
      {children}
      <span style={{ flex: 1, height: 1, background: C.border }} />
    </div>
  );
}

function TabButton({ active, onClick, children, label }) {
  return (
    <button
      onClick={onClick}
      aria-pressed={active}
      aria-label={label}
      style={{
        background: active ? C.bg2 : "transparent",
        border: `1px solid ${active ? C.accent : C.border}`,
        borderRadius: 4, color: active ? C.accent : C.text2,
        cursor: "pointer", fontFamily: "IBM Plex Mono",
        fontSize: 10, padding: "5px 14px",
        letterSpacing: "0.08em", textTransform: "uppercase",
      }}
    >
      {children}
    </button>
  );
}

// ─── Main Dashboard ────────────────────────────────────────────────────────────

export default function Dashboard() {
  const [deviceList,   setDeviceList]   = useState([]);
  const [selectedId,   setSelectedId]   = useState(null);
  const [viewMode,     setViewMode]     = useState("live");   // "live" | "history"
  const [historyMins,  setHistoryMins]  = useState(60);
  const [showAnalysis, setShowAnalysis] = useState(true);

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

  return (
    <div
      style={{ background: C.bg0, minHeight: "100vh", color: C.text0, fontFamily: "'IBM Plex Sans', sans-serif" }}
    >
      <style>{`
        @import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;700&family=IBM+Plex+Sans:wght@400;500;600&display=swap');
        * { box-sizing: border-box; margin: 0; padding: 0; }
        ::-webkit-scrollbar { width: 4px; }
        ::-webkit-scrollbar-thumb { background: #1e3a4a; border-radius: 2px; }
        @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:.4} }
        @keyframes blink { 0%,100%{opacity:1} 50%{opacity:0} }
      `}</style>

      {/* ── Top Bar ───────────────────────────────────────────────────── */}
      <header
        role="banner"
        aria-label="Industrial IoT Monitor"
        style={{
          background: C.bg1, borderBottom: `1px solid ${C.border}`,
          padding: "12px 28px", display: "flex", alignItems: "center",
          gap: 20, position: "sticky", top: 0, zIndex: 50,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 10, flex: 1 }}>
          <div style={{
            width: 28, height: 28, borderRadius: 6,
            background: `linear-gradient(135deg, ${C.accent} 0%, #92400e 100%)`,
            display: "flex", alignItems: "center", justifyContent: "center",
            fontSize: 14, fontWeight: 700, color: "#000",
          }} aria-hidden="true">⬡</div>
          <div>
            <div style={{ fontSize: 13, fontWeight: 600, letterSpacing: "0.02em" }}>
              INDUSTRIAL IoT MONITOR
            </div>
            <div style={{ fontSize: 9, fontFamily: "IBM Plex Mono", color: C.text2, letterSpacing: "0.1em" }}>
              PREDICTIVE MAINTENANCE SYSTEM v1.2.0
            </div>
          </div>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: 18 }}>
          {unackedCount > 0 && (
            <div
              role="alert"
              aria-live="assertive"
              style={{
                background: "#1c0a0a", border: "1px solid #7f1d1d",
                borderRadius: 4, padding: "4px 10px",
                fontSize: 11, fontFamily: "IBM Plex Mono", color: "#ef4444",
                animation: "blink 2s step-start infinite",
              }}
            >
              ⚠ {unackedCount} UNACKED ALERT{unackedCount > 1 ? "S" : ""}
            </div>
          )}
          <ConnectionBadge status={connectionStatus} />
          <div style={{ fontSize: 10, fontFamily: "IBM Plex Mono", color: C.text2 }} aria-hidden="true">
            {new Date().toLocaleString()}
          </div>
        </div>
      </header>

      <main style={{ padding: "20px 28px", display: "grid", gap: 16 }}>

        {/* ── Device Grid ──────────────────────────────────────────── */}
        <section aria-label="Devices" style={{ background: C.bg1, border: `1px solid ${C.border}`, borderRadius: 6, padding: "14px 18px" }}>
          <SectionHeader>DEVICES ({deviceList.length})</SectionHeader>
          {deviceList.length === 0
            ? <div style={{ fontSize: 12, fontFamily: "IBM Plex Mono", color: C.text2 }}>
                No devices registered. Start the simulator or connect ESP32.
              </div>
            : (
              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(220px, 1fr))", gap: 10 }}>
                {deviceList.map((d) => (
                  <DeviceCard
                    key={d.id}
                    device={d}
                    latestFrame={devices[d.id]}
                    isSelected={d.id === selectedId}
                    onSelect={() => setSelectedId(d.id)}
                  />
                ))}
              </div>
            )
          }
        </section>

        {/* ── Selected Device Detail ────────────────────────────────── */}
        {selectedId && (
          <div style={{ display: "grid", gridTemplateColumns: "1fr 360px", gap: 16 }}>

            {/* LEFT COLUMN */}
            <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>

              {/* Metric cards */}
              <section aria-label="Live metrics" style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
                <MetricCard
                  label="Vibration RMS"
                  value={latest?.vibration?.toFixed(4) ?? "—"}
                  unit="m/s²"
                  color={C.accent}
                  anomaly={vibAnalysis?.isAnomaly}
                  sublabel={vibAnalysis
                    ? `Z=${formatZScore(vibAnalysis.zScore)}  avg=${vibAnalysis.movingAvg?.toFixed(3)}`
                    : "Awaiting data…"}
                />
                <MetricCard
                  label="Temperature"
                  value={latest?.temperature?.toFixed(1) ?? "—"}
                  unit="°C"
                  color="#34d399"
                  anomaly={tempAnalysis?.isAnomaly}
                  sublabel={`Humidity: ${latest?.humidity?.toFixed(1) ?? "—"} %`}
                />
                <MetricCard
                  label="Sequence"
                  value={latest?.seq ?? "—"}
                  color="#60a5fa"
                  sublabel={latest?.location ?? "—"}
                />
                <MetricCard
                  label="Buffer"
                  value={liveReadings.length}
                  unit="pts"
                  color="#a78bfa"
                  sublabel="live ring (120 max)"
                />
              </section>

              {/* Analytics overlay */}
              {showAnalysis && (vibAnalysis || tempAnalysis) && (
                <AnalysisOverlay
                  vibAnalysis={vibAnalysis}
                  tempAnalysis={tempAnalysis}
                />
              )}

              {/* View controls */}
              <div role="toolbar" aria-label="View controls" style={{ display: "flex", gap: 6, alignItems: "center", flexWrap: "wrap" }}>
                <TabButton active={viewMode === "live"} onClick={() => setViewMode("live")} label="Switch to live view">
                  ● Live
                </TabButton>
                {HISTORY_WINDOWS.map((w) => (
                  <TabButton
                    key={w.minutes}
                    active={viewMode === "history" && historyMins === w.minutes}
                    onClick={() => { setViewMode("history"); setHistoryMins(w.minutes); }}
                    label={`View ${w.label} history`}
                  >
                    ⏱ {w.label}
                  </TabButton>
                ))}
                <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: 6 }}>
                  <button
                    onClick={() => setShowAnalysis((v) => !v)}
                    aria-pressed={showAnalysis}
                    aria-label={showAnalysis ? "Hide analysis overlay" : "Show analysis overlay"}
                    style={{
                      background: showAnalysis ? C.bg2 : "transparent",
                      border: `1px solid ${C.border}`, borderRadius: 4,
                      color: C.text2, cursor: "pointer",
                      fontFamily: "IBM Plex Mono", fontSize: 10, padding: "5px 10px",
                    }}
                  >
                    {showAnalysis ? "Hide" : "Show"} Analysis
                  </button>
                </div>
              </div>

              {/* Vibration Chart */}
              <section
                aria-label="Vibration RMS chart"
                style={{
                  background: C.bg1,
                  border: `1px solid ${vibAnalysis?.isAnomaly ? "#7f1d1d" : C.border}`,
                  borderRadius: 6, padding: "16px 18px",
                  transition: "border-color 0.4s",
                }}
              >
                <SectionHeader>VIBRATION RMS (m/s²)</SectionHeader>
                {loading
                  ? <Skeleton height={240} />
                  : (
                    <ChartErrorBoundary>
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
                  background: C.bg1,
                  border: `1px solid ${tempAnalysis?.isAnomaly ? "#7f1d1d" : C.border}`,
                  borderRadius: 6, padding: "16px 18px",
                  transition: "border-color 0.4s",
                }}
              >
                <SectionHeader>TEMPERATURE (°C) + HUMIDITY (%)</SectionHeader>
                {loading
                  ? <Skeleton height={240} />
                  : (
                    <ChartErrorBoundary>
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
                background: C.bg1, border: `1px solid ${C.border}`,
                borderRadius: 6, padding: "16px 18px",
                display: "flex", flexDirection: "column",
                maxHeight: "calc(100vh - 160px)", overflow: "hidden",
              }}
            >
              <SectionHeader>ALERTS</SectionHeader>
              <AlertsPanel alerts={alerts} onAcknowledge={ackAlert} />
            </section>
          </div>
        )}
      </main>
    </div>
  );
}

function Skeleton({ height }) {
  return (
    <div
      role="progressbar"
      aria-label="Loading chart data"
      style={{
        height, background: "#0f1923", borderRadius: 4,
        animation: "pulse 1.5s ease-in-out infinite",
      }}
    />
  );
}
