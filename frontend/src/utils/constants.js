/**
 * constants.js
 * Application-wide frontend constants.
 * All environment-sensitive values come from import.meta.env (Vite).
 */

export const API_BASE     = import.meta.env.VITE_API_URL     || "http://localhost:8080";
export const SIGNALR_URL  = import.meta.env.VITE_SIGNALR_URL || "http://localhost:8080/hubs/sensors";

/** Live data ring buffer size (frames per device). */
export const MAX_LIVE_POINTS = 120;   // 60 seconds at 2 Hz

/** History lookback window options shown in the dashboard toggle. */
export const HISTORY_WINDOWS = [
  { label: "30 min",  minutes: 30  },
  { label: "1 hour",  minutes: 60  },
  { label: "4 hours", minutes: 240 },
  { label: "24 hours",minutes: 1440},
];

/** Temperature threshold for warning reference line on chart (°C). */
export const TEMP_WARNING_THRESHOLD = 70;

/** SignalR reconnect schedule (ms). */
export const SIGNALR_RETRY_SCHEDULE = [0, 2000, 5000, 10000, 30000];

/** Severity ordering for filter tabs (lowest → highest). */
export const SEVERITY_ORDER = ["Info", "Warning", "Critical", "Fault"];

/** Maximum alerts kept in React state. */
export const MAX_ALERTS_IN_MEMORY = 100;

/** Device is considered stale if last seen > this many seconds ago. */
export const STALE_THRESHOLD_SECONDS = 30;
