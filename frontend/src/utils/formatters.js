/**
 * formatters.js
 * Pure utility functions for formatting sensor values, timestamps,
 * and status labels consistently across all components.
 *
 * Rules:
 *  - All exports are pure functions (no side effects)
 *  - Never throw — return a safe fallback string on bad input
 *  - All numeric outputs are rounded (no floating-point artifacts)
 */

/** Format vibration RMS to 4 decimal places with unit. */
export function formatVib(value) {
  if (value == null || isNaN(value)) return "—";
  return `${parseFloat(value).toFixed(4)} m/s²`;
}

/** Format temperature to 1 decimal place with unit. */
export function formatTemp(value) {
  if (value == null || isNaN(value)) return "—";
  return `${parseFloat(value).toFixed(1)} °C`;
}

/** Format humidity to 1 decimal place with unit. */
export function formatHumidity(value) {
  if (value == null || isNaN(value)) return "—";
  return `${parseFloat(value).toFixed(1)} %`;
}

/** Format Z-score to 2 decimal places with sigma symbol. */
export function formatZScore(value) {
  if (value == null || isNaN(value)) return "—";
  return `${parseFloat(value).toFixed(2)}σ`;
}

/** Format a Date or ISO string as HH:MM:SS. */
export function formatTime(ts) {
  if (!ts) return "—";
  try {
    return new Date(ts).toLocaleTimeString([], { hour12: false });
  } catch {
    return "—";
  }
}

/** Format a Date or ISO string as relative time ("2 min ago", "just now"). */
export function formatRelativeTime(ts) {
  if (!ts) return "—";
  const diffMs = Date.now() - new Date(ts).getTime();
  if (isNaN(diffMs)) return "—";

  const s = Math.floor(diffMs / 1000);
  if (s < 10)  return "just now";
  if (s < 60)  return `${s}s ago`;
  const m = Math.floor(s / 60);
  if (m < 60)  return `${m}m ago`;
  const h = Math.floor(m / 60);
  return `${h}h ago`;
}

/** Severity → display label mapping. */
export const SEVERITY_LABELS = {
  Fault   : "FAULT",
  Critical: "CRITICAL",
  Warning : "WARNING",
  Info    : "INFO",
};

/** Severity → CSS color (matches AlertsPanel palette). */
export const SEVERITY_COLORS = {
  Fault   : "#ef4444",
  Critical: "#f97316",
  Warning : "#f59e0b",
  Info    : "#60a5fa",
};

/** Alert type → readable label. */
export const ALERT_TYPE_LABELS = {
  VibrationSpike     : "Vibration Spike",
  VibrationSustained : "Sustained Vibration",
  ThermalOverheat    : "Thermal Overload",
  HumidityOutOfRange : "Humidity OOR",
  RateOfChangeSpike  : "Rate-of-Change Spike",
  SensorFault        : "Sensor Fault",
};

/** Device status → color. */
export const STATUS_COLORS = {
  Online  : "#22c55e",
  Offline : "#ef4444",
  Degraded: "#f59e0b",
  Unknown : "#475569",
};

/** Truncate a string to maxLen characters with ellipsis. */
export function truncate(str, maxLen = 30) {
  if (!str) return "";
  return str.length > maxLen ? str.slice(0, maxLen - 1) + "…" : str;
}
