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
      style={{
        display: "flex", alignItems: "center", gap: 6,
        background: `${color}12`,
        border: `1px solid ${color}35`,
        borderRadius: 4, padding: "3px 10px",
      }}
    >
      <span style={{
        display: "inline-block", width: 7, height: 7, borderRadius: "50%",
        background: color,
        boxShadow: status === "Connected" ? `0 0 6px ${color}` : "none",
        animation: status === "Connected"
          ? "led-pulse 2s infinite"
          : status === "Reconnecting"
          ? "spin 1.2s linear infinite"
          : "none",
        flexShrink: 0,
      }} />
      <span style={{
        fontFamily: "IBM Plex Mono", fontSize: 10, color,
        letterSpacing: "0.08em", fontWeight: 600,
      }}>
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
        background: T.bg1,
        border: `1px solid ${anomaly ? T.fault + "60" : T.border}`,
        borderTop: `3px solid ${anomaly ? T.fault : cardColor}`,
        borderRadius: 4, padding: "12px 16px",
        minWidth: 150,
        transition: "border-color 0.25s, background 0.25s",
        boxShadow: anomaly ? `0 0 0 1px ${T.fault}20` : "none",
      }}
    >
      <div style={{
        display: "flex", justifyContent: "space-between", alignItems: "center",
        marginBottom: 8,
      }}>
        <div style={{
          fontSize: 9, fontFamily: "IBM Plex Mono", color: T.text2,
          letterSpacing: "0.12em", textTransform: "uppercase",
        }}>
          {label}
        </div>
        {anomaly && (
          <div style={{
            fontSize: 8, fontFamily: "IBM Plex Mono", color: T.fault,
            letterSpacing: "0.08em", animation: "blink 1s step-start infinite",
          }} aria-hidden="true">⚠ ANOM</div>
        )}
      </div>
      <div style={{ display: "flex", alignItems: "baseline", gap: 4 }}>
        <span style={{
          fontFamily: "IBM Plex Mono", fontWeight: 700, fontSize: 22,
          color: anomaly ? T.fault : cardColor, transition: "color 0.25s",
          fontVariantNumeric: "tabular-nums",
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
      <span style={{
        display: "inline-block", width: 3, height: 14,
        background: T.brand, borderRadius: 2, flexShrink: 0,
      }} aria-hidden="true" />
      {icon && (
        <span style={{
          fontFamily: "IBM Plex Mono", fontSize: 11, color: T.brand,
        }}>{icon}</span>
      )}
      <span style={{
        fontSize: 10, fontFamily: "IBM Plex Mono", fontWeight: 700,
        color: T.text1, letterSpacing: "0.12em", textTransform: "uppercase",
      }}>
        {children}
      </span>
      {count != null && (
        <span style={{
          fontSize: 9, fontFamily: "IBM Plex Mono",
          color: T.accent, background: `${T.accent}18`,
          border: `1px solid ${T.accent}40`,
          borderRadius: 10, padding: "0 6px",
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
        height, background: T.bg2, borderRadius: 4,
        position: "relative", overflow: "hidden",
      }}
    >
      <div style={{
        position: "absolute", inset: 0,
        background: `linear-gradient(90deg, transparent 0%, ${T.border}40 50%, transparent 100%)`,
        animation: "shimmer 1.6s ease-in-out infinite",
        backgroundSize: "200% 100%",
      }} />
    </div>
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
        @import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500;700&family=IBM+Plex+Sans:wght@400;500;600;700&display=swap');
        *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
        html, body { background: ${T.bg0}; }
        button { outline: none; font-family: inherit; }
        ::-webkit-scrollbar { width: 5px; height: 5px; }
        ::-webkit-scrollbar-track { background: ${T.bg0}; }
        ::-webkit-scrollbar-thumb { background: ${T.border}; border-radius: 3px; }
        ::-webkit-scrollbar-thumb:hover { background: ${T.text2}; }
        @keyframes led-pulse { 0%,100%{opacity:1;box-shadow:0 0 4px currentColor} 50%{opacity:.65;box-shadow:none} }
        @keyframes blink { 0%,49%{opacity:1} 50%,100%{opacity:0} }
        @keyframes shimmer { 0%{opacity:.3} 50%{opacity:.6} 100%{opacity:.3} }
        @keyframes card-in { from{opacity:0;transform:translateY(5px)} to{opacity:1;transform:translateY(0)} }
        @keyframes spin { from{transform:rotate(0deg)} to{transform:rotate(360deg)} }
      `}</style>

      {/* ── HEADER ──────────────────────────────────────────────────────── */}
      <header
        role="banner"
        aria-label="Industrial IoT Dashboard"
        style={{
          background: T.bg1,
          borderBottom: `1px solid ${T.border}`,
          borderLeft: `4px solid ${T.brand}`,
          padding: "0 24px",
          height: 54,
          display: "flex", alignItems: "center",
          gap: 20, position: "sticky", top: 0, zIndex: 100,
          boxShadow: T.shadowSm,
          transition: "background 0.25s, border-color 0.25s",
        }}
      >
        {/* Left: Logo + Title */}
        <div style={{ display: "flex", alignItems: "center", gap: 12, flex: 1 }}>
          <div style={{
            width: 32, height: 32, borderRadius: 6,
            background: `linear-gradient(135deg, ${T.brand}, ${T.accent})`,
            display: "flex", alignItems: "center", justifyContent: "center",
            fontSize: 15, color: "#fff", fontWeight: 700, flexShrink: 0,
            boxShadow: `0 0 10px ${T.brand}44`,
          }} aria-hidden="true">⬡</div>
          <div>
            <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
              <span style={{
                fontFamily: "'IBM Plex Sans', sans-serif", fontWeight: 700,
                fontSize: 15, color: T.text0, letterSpacing: "0.05em",
              }}>
                IIoT<span style={{ color: T.brand }}> Monitor</span>
              </span>
              <span style={{
                fontSize: 9, fontFamily: "IBM Plex Mono",
                color: T.accent, background: `${T.accent}18`,
                border: `1px solid ${T.accent}40`,
                borderRadius: 3, padding: "1px 6px",
                letterSpacing: "0.06em",
              }}>v2.1.0</span>
            </div>
            <div style={{
              fontSize: 9, fontFamily: "IBM Plex Mono", color: T.text2,
              letterSpacing: "0.1em", marginTop: 1,
            }}>
              PREDICTIVE MAINTENANCE SYSTEM
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

          {/* Theme toggle */}
          <button
            onClick={toggleTheme}
            aria-label={isDark ? "Switch to light mode" : "Switch to dark mode"}
            title={isDark ? "Switch to light mode" : "Switch to dark mode"}
            style={{
              width: 34, height: 34, borderRadius: "50%",
              border: `1px solid ${T.border}`,
              background: T.bg2,
              cursor: "pointer",
              display: "flex", alignItems: "center", justifyContent: "center",
              fontSize: 15,
              color: T.text1,
              transition: "all 0.2s",
              flexShrink: 0,
            }}
            onMouseEnter={(e) => {
              e.currentTarget.style.borderColor = T.brand;
              e.currentTarget.style.color = T.brand;
              e.currentTarget.style.background = `${T.brand}18`;
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.borderColor = T.border;
              e.currentTarget.style.color = T.text1;
              e.currentTarget.style.background = T.bg2;
            }}
          >
            {isDark ? "☀" : "☽"}
          </button>
        </div>
      </header>

      {/* ── STATUS RIBBON ────────────────────────────────────────────────── */}
      <div style={{
        background: T.bg1,
        borderBottom: `1px solid ${T.border}`,
        padding: "5px 24px",
        display: "flex", alignItems: "center", gap: 14,
        fontSize: 10, fontFamily: "IBM Plex Mono",
        transition: "background 0.25s",
        letterSpacing: "0.05em",
      }}>
        <span style={{ color: T.text2, fontSize: 9 }}>NODES</span>
        <RibbonDot color={T.online}  label={`${onlineCount} Online`}  T={T} />
        <RibbonDot color={T.stale}   label={`${staleCount} Stale`}    T={T} />
        <RibbonDot color={T.offline} label={`${offlineCount} Offline`} T={T} />
        <RibbonSep T={T} />
        <span style={{ color: T.text2, fontSize: 9 }}>ALERTS</span>
        {faultCount === 0 && critCount === 0 && warnCount === 0 ? (
          <span style={{ color: T.online, fontWeight: 700, fontSize: 9 }}>● ALL NOMINAL</span>
        ) : (
          <>
            {faultCount > 0 && <RibbonDot color={T.fault}    label={`${faultCount} Fault`}    T={T} />}
            {critCount  > 0 && <RibbonDot color={T.critical} label={`${critCount} Critical`}  T={T} />}
            {warnCount  > 0 && <RibbonDot color={T.warning}  label={`${warnCount} Warning`}   T={T} />}
          </>
        )}
        <RibbonSep T={T} />
        <span style={{ color: T.text2, fontSize: 9 }}>PEAK VIB</span>
        <span style={{ color: T.vib, fontVariantNumeric: "tabular-nums" }}>
          {peakVib}{peakVib !== "—" ? " m/s²" : ""}
        </span>
        <div style={{ flex: 1 }} />
        <span style={{ color: T.text2, fontSize: 9 }}>
          {now.toLocaleDateString()} {now.toLocaleTimeString()}
        </span>
      </div>

      <main style={{ padding: "16px 24px", display: "grid", gap: 14 }}>

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
            <div style={{
              display: "flex", alignItems: "center", gap: 14,
              padding: "16px 20px",
              background: T.bg2, borderRadius: 4, border: `1px dashed ${T.border}`,
            }}>
              <span style={{
                fontSize: 22, opacity: 0.35, animation: "shimmer 2s infinite",
              }}>⬡</span>
              <div>
                <div style={{
                  fontSize: 11, fontFamily: "IBM Plex Mono", color: T.text1,
                  marginBottom: 3,
                }}>
                  No equipment nodes registered
                </div>
                <div style={{ fontSize: 10, fontFamily: "IBM Plex Mono", color: T.text2 }}>
                  Run the sensor simulator or connect an ESP32 device to begin.
                </div>
              </div>
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
          <div style={{ display: "grid", gridTemplateColumns: "1fr 360px", gap: 14 }}>

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
    <span style={{ display: "flex", alignItems: "center", gap: 5 }}>
      <span style={{
        width: 6, height: 6, borderRadius: "50%",
        background: color, flexShrink: 0,
        boxShadow: `0 0 4px ${color}80`,
      }} />
      <span style={{ color: T.text1, fontSize: 10 }}>{label}</span>
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
