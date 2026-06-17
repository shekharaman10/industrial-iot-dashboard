const BASE = import.meta.env.VITE_API_URL || "http://localhost:8080";

async function request(path, options = {}) {
  const token = sessionStorage.getItem("iot_token");
  const res = await fetch(`${BASE}${path}`, {
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
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
export const fetchDevices = (signal) => request("/api/devices", { signal });

// Returns time-series history for one device
export const fetchHistory = (deviceId, minutes = 60, signal) =>
  request(`/api/sensors/${deviceId}/history?minutes=${minutes}`, { signal });

// Returns recent alerts (pass `before` ISO timestamp for cursor pagination)
export const fetchAlerts = (limit = 50, signal, before = null) => {
  const params = new URLSearchParams({ limit });
  if (before) params.set("before", before);
  return request(`/api/alerts?${params}`, { signal });
};

// Acknowledge one alert
export const acknowledgeAlert = (alertId, by = "dashboard") =>
  request(`/api/alerts/${alertId}/acknowledge?acknowledgedBy=${by}`, { method: "POST" });

// Exchange an API key for a JWT token
export const getToken = (apiKey) =>
  request("/api/auth/token", {
    method: "POST",
    body: JSON.stringify({ apiKey }),
  });
