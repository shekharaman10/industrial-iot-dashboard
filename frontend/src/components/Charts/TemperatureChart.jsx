import {
  LineChart, Line, XAxis, YAxis, CartesianGrid,
  Tooltip, ReferenceLine, ResponsiveContainer, Legend
} from "recharts";
import { useMemo } from "react";
import { useTheme } from "../../context/ThemeContext";

/**
 * TemperatureChart
 * Dual-axis line chart for temperature (°C) and humidity (%).
 * Anomaly frames are highlighted with a colored dot on the temperature line.
 */
export default function TemperatureChart({ data = [], tempWarningThreshold = 70 }) {
  const { T } = useTheme();

  const TEMP_COLOR    = T.temp;
  const HUMID_COLOR   = T.humid;
  const ANOMALY_COLOR = T.anomaly;

  const chartData = useMemo(() =>
    data.map((r) => ({
      time        : new Date(r.timestamp || r.ts).toLocaleTimeString(),
      temperature : r.temperature != null ? +r.temperature.toFixed(1) : null,
      humidity    : r.humidity    != null ? +r.humidity.toFixed(1)    : null,
      anomaly     : r.analysis?.temperature?.isAnomaly ? r.temperature : null,
    }))
  , [data]);

  const AnomalyDot = ({ cx, cy, payload }) => {
    if (!payload.anomaly) return null;
    return (
      <circle
        cx={cx} cy={cy} r={5}
        fill={ANOMALY_COLOR}
        stroke="#fff"
        strokeWidth={1.5}
        style={{ filter: `drop-shadow(0 0 4px ${ANOMALY_COLOR})` }}
      />
    );
  };

  const CustomTooltip = ({ active, payload, label }) => {
    if (!active || !payload?.length) return null;
    return (
      <div style={{
        background: T.bg3,
        border: `1px solid ${T.border}`,
        borderRadius: 4, padding: "8px 12px",
        fontFamily: "'IBM Plex Mono', monospace", fontSize: 12, color: T.text1,
      }}>
        <p style={{ color: T.text2, marginBottom: 4 }}>{label}</p>
        {payload.map((p) => (
          <p key={p.dataKey} style={{ color: p.color }}>
            {p.name}: {p.value}{p.dataKey === "temperature" ? " °C" : " %"}
          </p>
        ))}
      </div>
    );
  };

  return (
    <ResponsiveContainer width="100%" height={240}>
      <LineChart data={chartData} margin={{ top: 8, right: 16, bottom: 0, left: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke={T.chartGrid} />
        <XAxis
          dataKey="time"
          tick={{ fill: T.chartTick, fontSize: 10, fontFamily: "IBM Plex Mono" }}
          tickLine={false}
          interval="preserveStartEnd"
        />
        <YAxis
          yAxisId="temp"
          orientation="left"
          tick={{ fill: T.chartTick, fontSize: 10, fontFamily: "IBM Plex Mono" }}
          tickLine={false} axisLine={false}
          domain={[0, 120]}
          tickFormatter={(v) => `${v}°`}
        />
        <YAxis
          yAxisId="humid"
          orientation="right"
          tick={{ fill: T.chartTick, fontSize: 10, fontFamily: "IBM Plex Mono" }}
          tickLine={false} axisLine={false}
          domain={[0, 100]}
          tickFormatter={(v) => `${v}%`}
        />

        {tempWarningThreshold && (
          <ReferenceLine
            yAxisId="temp"
            y={tempWarningThreshold}
            stroke={T.warning}
            strokeDasharray="6 3"
            label={{ value: `warn ${tempWarningThreshold}°C`, fill: T.warning, fontSize: 10 }}
          />
        )}

        <Tooltip content={<CustomTooltip />} />
        <Legend wrapperStyle={{ fontSize: 11, fontFamily: "IBM Plex Mono", color: T.text2 }} />

        <Line
          yAxisId="temp"
          type="monotone"
          dataKey="temperature"
          name="Temperature (°C)"
          stroke={TEMP_COLOR}
          strokeWidth={1.5}
          dot={<AnomalyDot />}
          activeDot={{ r: 4, fill: TEMP_COLOR }}
          connectNulls
          isAnimationActive={false}
        />
        <Line
          yAxisId="humid"
          type="monotone"
          dataKey="humidity"
          name="Humidity (%)"
          stroke={HUMID_COLOR}
          strokeWidth={1}
          strokeDasharray="4 2"
          dot={false}
          connectNulls
          isAnimationActive={false}
        />
      </LineChart>
    </ResponsiveContainer>
  );
}
