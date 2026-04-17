import { useState, useEffect, useCallback, useRef } from "react";
import { useSignalR } from "./useSignalR";
import { fetchHistory, fetchAlerts } from "../services/api";
import { MAX_LIVE_POINTS, MAX_ALERTS_IN_MEMORY } from "../utils/constants";

/**
 * useSensorData
 * Single source of truth for all sensor data in the dashboard.
 *
 * Combines:
 *   - SignalR live stream → liveReadings ring buffer + devices map
 *   - REST history fetch  → historical chart data on device/window change
 *   - Alert stream        → prepended to alerts array (max 100 in memory)
 *
 * @param {string|null} selectedDeviceId  — active device
 * @param {number}      historyMinutes    — lookback window for history mode
 */
export function useSensorData(selectedDeviceId, historyMinutes = 60) {
  const { status, on } = useSignalR();

  const [liveReadings, setLiveReadings] = useState([]);
  const [history,      setHistory]      = useState([]);
  const [alerts,       setAlerts]       = useState([]);
  const [devices,      setDevices]      = useState({});   // id → latest frame
  const [loading,      setLoading]      = useState(false);

  // Stable ref for selectedDeviceId so SignalR callbacks don't go stale
  const selectedRef = useRef(selectedDeviceId);
  useEffect(() => { selectedRef.current = selectedDeviceId; }, [selectedDeviceId]);

  // ── Live telemetry ─────────────────────────────────────────────────────────
  useEffect(() => {
    on("TelemetryReceived", (frame) => {
      // Always update per-device map (powers DeviceCard grid)
      setDevices((prev) => ({ ...prev, [frame.deviceId]: frame }));

      // Only append to live ring for selected device
      if (frame.deviceId !== selectedRef.current) return;

      setLiveReadings((prev) => {
        const next = [...prev, { ...frame, _ts: Date.now() }];
        return next.length > MAX_LIVE_POINTS ? next.slice(-MAX_LIVE_POINTS) : next;
      });
    });
  }, [on]);

  // ── Live alerts ────────────────────────────────────────────────────────────
  useEffect(() => {
    on("AlertReceived", (alert) => {
      setAlerts((prev) => [alert, ...prev].slice(0, MAX_ALERTS_IN_MEMORY));
    });
  }, [on]);

  // ── Load history + initial alerts on device/window change ─────────────────
  useEffect(() => {
    if (!selectedDeviceId) return;

    setLiveReadings([]);   // clear live buffer on device switch
    setLoading(true);

    Promise.all([
      fetchHistory(selectedDeviceId, historyMinutes),
      fetchAlerts(50),
    ])
      .then(([hist, alertData]) => {
        setHistory(hist ?? []);
        setAlerts(alertData ?? []);
      })
      .catch((err) => {
        console.error("[useSensorData] fetch failed:", err);
        setHistory([]);
      })
      .finally(() => setLoading(false));
  }, [selectedDeviceId, historyMinutes]);

  // ── Acknowledge alert ──────────────────────────────────────────────────────
  const ackAlert = useCallback(async (alertId) => {
    const { acknowledgeAlert } = await import("../services/api");
    await acknowledgeAlert(alertId);
    setAlerts((prev) =>
      prev.map((a) => a.id === alertId ? { ...a, acknowledged: true } : a)
    );
  }, []);

  return {
    connectionStatus : status,
    liveReadings,
    history,
    alerts,
    devices,
    loading,
    ackAlert,
  };
}
