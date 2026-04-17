import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid,
  Tooltip, ReferenceLine, ResponsiveContainer, Legend
} from "recharts";
import { useMemo } from "react";

const ANOMALY_COLOR = "#ef4444";
const NORMAL_COLOR  = "#f59e0b";
const AVG_COLOR     = "#60a5fa";

/**
 * VibrationChart
 * Renders the vibration RMS time-series with:
 *  - Live or historical data (same prop shape)
 *  - Anomaly overlay: dots turn red when isAnomaly = true
 *  - Moving average line (from backend analysis)
 *  - Baseline reference line
 *
 * Props:
 *   data        : SensorReading[] with optional analysis metadata
 *   showAvg     : bool — overlay moving average line
 *   baselineRef : number | null — draw a horizontal reference line
 */
export default function VibrationChart({ data = [], showAvg = true, baselineRef = null }) {
  const chartData = useMemo(() =>
    data.map((r) => ({
      time    : new Date(r.timestamp || r.ts).toLocaleTimeString(),
      rms     : r.vibration != null ? +r.vibration.toFixed(4) : null,
      movingAvg: r.analysis?.vibration?.movingAvg
                  ? +r.analysis.vibration.movingAvg.toFixed(4)
                  : null,
      anomaly : r.analysis?.vibration?.isAnomaly ? r.vibration : null,
    }))
  , [data]);

  // Custom dot: red on anomaly, transparent otherwise
  const CustomDot = ({ cx, cy, payload }) => {
    if (!payload.anomaly) return null;
    return (
      <circle
        cx={cx} cy={cy} r={5}
        fill={ANOMALY_COLOR}
        stroke="#fff"
        strokeWidth={1.5}
        style={{ filter: "drop-shadow(0 0 4px #ef4444)" }}
      />
    );
  };

  const CustomTooltip = ({ active, payload, label }) => {
    if (!active || !payload?.length) return null;
    const frame = payload[0]?.payload;
    return (
      <div style={{
        background: "#0f1923",
        border: "1px solid #1e3a4a",
        borderRadius: 4,
        padding: "8px 12px",
        fontFamily: "'IBM Plex Mono', monospace",
        fontSize: 12,
        color: "#cbd5e1",
      }}>
        <p style={{ color: "#64748b", marginBottom: 4 }}>{label}</p>
        {frame.rms      != null && <p style={{ color: NORMAL_COLOR }}>RMS: {frame.rms} m/s²</p>}
        {frame.movingAvg!= null && <p style={{ color: AVG_COLOR   }}>Avg: {frame.movingAvg} m/s²</p>}
        {frame.anomaly  != null && <p style={{ color: ANOMALY_COLOR, fontWeight: 700 }}>⚠ ANOMALY</p>}
      </div>
    );
  };

  return (
    <ResponsiveContainer width="100%" height={240}>
      <AreaChart data={chartData} margin={{ top: 8, right: 16, bottom: 0, left: 0 }}>
        <defs>
          <linearGradient id="vibGrad" x1="0" y1="0" x2="0" y2="1">
            <stop offset="5%"  stopColor={NORMAL_COLOR} stopOpacity={0.18} />
            <stop offset="95%" stopColor={NORMAL_COLOR} stopOpacity={0}    />
          </linearGradient>
        </defs>

        <CartesianGrid strokeDasharray="3 3" stroke="#1e3a4a" />

        <XAxis
          dataKey="time"
          tick={{ fill: "#475569", fontSize: 10, fontFamily: "IBM Plex Mono" }}
          tickLine={false}
          interval="preserveStartEnd"
        />
        <YAxis
          tick={{ fill: "#475569", fontSize: 10, fontFamily: "IBM Plex Mono" }}
          tickLine={false}
          axisLine={false}
          domain={["auto", "auto"]}
          tickFormatter={(v) => `${v.toFixed(2)}`}
        />

        {baselineRef && (
          <ReferenceLine
            y={baselineRef}
            stroke="#334155"
            strokeDasharray="6 3"
            label={{ value: "baseline", fill: "#475569", fontSize: 10 }}
          />
        )}

        <Tooltip content={<CustomTooltip />} />
        <Legend
          wrapperStyle={{ fontSize: 11, fontFamily: "IBM Plex Mono", color: "#64748b" }}
        />

        <Area
          type="monotone"
          dataKey="rms"
          name="Vibration RMS (m/s²)"
          stroke={NORMAL_COLOR}
          strokeWidth={1.5}
          fill="url(#vibGrad)"
          dot={<CustomDot />}
          activeDot={{ r: 4, fill: NORMAL_COLOR }}
          connectNulls
          isAnimationActive={false}   // disable for real-time performance
        />

        {showAvg && (
          <Area
            type="monotone"
            dataKey="movingAvg"
            name="Moving Average"
            stroke={AVG_COLOR}
            strokeWidth={1}
            strokeDasharray="4 2"
            fill="none"
            dot={false}
            connectNulls
            isAnimationActive={false}
          />
        )}
      </AreaChart>
    </ResponsiveContainer>
  );
}
