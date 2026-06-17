import { formatZScore } from "../utils/formatters.js";
import { useTheme } from "../context/ThemeContext";

/**
 * AnalysisOverlay
 * Shows analytics metadata for the currently selected device:
 *   - Z-score gauge (visual bar)
 *   - Moving average vs baseline comparison
 *   - Sample count (baseline establishment progress)
 *
 * Props:
 *   vibAnalysis  : analysis object from latest TelemetryReceived frame
 *   tempAnalysis : analysis object from latest TelemetryReceived frame
 */
export default function AnalysisOverlay({ vibAnalysis, tempAnalysis }) {
  const { T } = useTheme();

  if (!vibAnalysis && !tempAnalysis) return null;

  const SEVERITY_BAR = { Warning: T.warning, Critical: T.critical, Fault: T.fault };

  return (
    <div style={{
      display: "grid",
      gridTemplateColumns: vibAnalysis && tempAnalysis ? "1fr 1fr" : "1fr",
      gap: 10,
    }}>
      {vibAnalysis  && (
        <AnalysisCard
          label="Vibration"
          analysis={vibAnalysis}
          color={T.vib}
          T={T}
          SEVERITY_BAR={SEVERITY_BAR}
        />
      )}
      {tempAnalysis && (
        <AnalysisCard
          label="Temperature"
          analysis={tempAnalysis}
          color={T.temp}
          T={T}
          SEVERITY_BAR={SEVERITY_BAR}
        />
      )}
    </div>
  );
}

function AnalysisCard({ label, analysis, color, T, SEVERITY_BAR }) {
  const z      = analysis.zScore     ?? 0;
  const avg    = analysis.movingAvg  ?? 0;
  const base   = analysis.baseline   ?? 0;
  const stddev = analysis.stdDev     ?? 0;
  const n      = analysis.sampleCount ?? 0;
  const sev    = analysis.severity;

  // Z-score bar: 0–4σ range, capped at 100%
  const zPct = Math.min((z / 4) * 100, 100);
  const barColor = SEVERITY_BAR[sev] ?? color;

  return (
    <div style={{
      background: T.bg1,
      border: `1px solid ${sev ? SEVERITY_BAR[sev] + "40" : T.border}`,
      borderRadius: 6,
      padding: "10px 12px",
      transition: "border-color 0.2s",
    }}>
      {/* Label + anomaly badge */}
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 8 }}>
        <span style={{
          fontSize: 10, fontFamily: "IBM Plex Mono", fontWeight: 700,
          color: T.text2, letterSpacing: "0.1em",
        }}>
          {label.toUpperCase()}
        </span>
        {sev && (
          <span style={{
            fontSize: 9, fontFamily: "IBM Plex Mono",
            color: SEVERITY_BAR[sev],
            border: `1px solid ${SEVERITY_BAR[sev]}60`,
            borderRadius: 3, padding: "1px 5px",
            animation: sev === "Fault" ? "blink 1s step-start infinite" : "none",
          }}>
            {sev.toUpperCase()}
          </span>
        )}
      </div>

      {/* Z-score gauge */}
      <div style={{ marginBottom: 8 }}>
        <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 3 }}>
          <span style={{ fontSize: 9, fontFamily: "IBM Plex Mono", color: T.text2 }}>
            Z-SCORE
          </span>
          <span style={{ fontSize: 11, fontFamily: "IBM Plex Mono", fontWeight: 700, color: barColor }}>
            {formatZScore(z)}
          </span>
        </div>
        <div style={{ height: 4, background: T.bg0, borderRadius: 2 }}>
          <div style={{
            height: "100%", width: `${zPct}%`,
            background: barColor,
            borderRadius: 2,
            transition: "width 0.4s ease, background 0.3s",
          }} />
        </div>
        {/* Reference lines at 1.5σ, 2.5σ, 3.5σ */}
        <div style={{ position: "relative", height: 8 }}>
          {[1.5, 2.5, 3.5].map((threshold) => (
            <div key={threshold} style={{
              position: "absolute",
              left: `${Math.min((threshold / 4) * 100, 100)}%`,
              top: 0, width: 1, height: 8,
              background: T.border,
            }} />
          ))}
        </div>
      </div>

      {/* Stats table */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "2px 10px" }}>
        <Stat label="Avg"      value={avg.toFixed(3)}    T={T} />
        <Stat label="Baseline" value={base.toFixed(3)}   T={T} />
        <Stat label="Std Dev"  value={stddev.toFixed(3)} T={T} />
        <Stat label="Samples"  value={n >= 300 ? `${n} ✓` : `${n}/300`} T={T} />
      </div>
    </div>
  );
}

function Stat({ label, value, T }) {
  return (
    <>
      <span style={{ fontSize: 9, fontFamily: "IBM Plex Mono", color: T.text2 }}>
        {label}
      </span>
      <span style={{ fontSize: 10, fontFamily: "IBM Plex Mono", color: T.text1, textAlign: "right" }}>
        {value}
      </span>
    </>
  );
}
