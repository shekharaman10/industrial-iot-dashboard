const BASE = import.meta.env.VITE_API_URL || "http://localhost:8080";

async function request(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ detail: res.statusText }));
    throw new Error(err.detail || `HTTP ${res.status}`);
  }
  if (res.status === 204) return null;
  return res.json();
}

// Returns full device objects {id, location, status, firmware, lastSeen, isStale}
export const fetchDevices = () => request("/api/devices");

// Returns time-series history for one device
export const fetchHistory = (deviceId, minutes = 60) =>
  request(`/api/sensors/${deviceId}/history?minutes=${minutes}`);

// Returns recent alerts
export const fetchAlerts = (limit = 50) =>
  request(`/api/alerts?limit=${limit}`);

// Acknowledge one alert
export const acknowledgeAlert = (alertId, by = "dashboard") =>
  request(`/api/alerts/${alertId}/acknowledge?acknowledgedBy=${by}`, { method: "POST" });
