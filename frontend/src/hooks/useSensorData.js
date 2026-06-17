import { useState, useEffect, useCallback, useRef } from "react";
import { useSignalR } from "./useSignalR";
import { fetchHistory, fetchAlerts, acknowledgeAlert } from "../services/api";
import { MAX_LIVE_POINTS, MAX_ALERTS_IN_MEMORY } from "../utils/constants";

/**
 * useSensorData
 * Single source of truth for all sensor data in the dashboard.
 *
 * Combines:
 *   - SignalR live stream → liveReadings circular buffer + devices map
 *   - REST history fetch  → historical chart data on device/window change
 *   - Alert stream        → prepended to alerts array (max 100 in memory)
 *
 * @param {string|null} selectedDeviceId  — active device
 * @param {number}      historyMinutes    — lookback window for history mode
 */
export function useSensorData(selectedDeviceId, historyMinutes = 60) {
  const { status, on } = useSignalR();

  // Circular ring buffer stored in a ref — avoids allocating a new array on
  // every 2 Hz tick. Exposed as state only when the buffer changes length.
  const ringRef   = useRef(new Array(MAX_LIVE_POINTS).fill(null));
  const headRef   = useRef(0);   // next write position
  const sizeRef   = useRef(0);   // number of valid entries

  const [liveReadings, setLiveReadings] = useState([]);
  const [history,      setHistory]      = useState([]);
  const [alerts,       setAlerts]       = useState([]);
  const [devices,      setDevices]      = useState({});   // id → latest frame
  const [loading,      setLoading]      = useState(false);

  // Stable ref for selectedDeviceId so SignalR callbacks don't go stale
  const selectedRef = useRef(selectedDeviceId);
  useEffect(() => { selectedRef.current = selectedDeviceId; }, [selectedDeviceId]);

  // Helper: read the ring buffer out in insertion order
  const drainRing = useCallback(() => {
    const ring = ringRef.current;
    const size = sizeRef.current;
    if (size === 0) return [];
    const head = headRef.current;
    const result = new Array(size);
    for (let i = 0; i < size; i++) {
      result[i] = ring[(head - size + i + MAX_LIVE_POINTS) % MAX_LIVE_POINTS];
    }
    return result;
  }, []);

  // ── Live telemetry ─────────────────────────────────────────────────────────
  useEffect(() => {
    on("TelemetryReceived", (frame) => {
      // Always update per-device map (powers DeviceCard grid)
      setDevices((prev) => ({ ...prev, [frame.deviceId]: frame }));

      // Only append to live ring for selected device
      if (frame.deviceId !== selectedRef.current) return;

      // Write into the ring buffer (overwrites oldest entry when full)
      ringRef.current[headRef.current] = { ...frame, _ts: Date.now() };
      headRef.current = (headRef.current + 1) % MAX_LIVE_POINTS;
      if (sizeRef.current < MAX_LIVE_POINTS) sizeRef.current++;

      // Trigger re-render with a stable-identity snapshot
      setLiveReadings(drainRing());
    });
  }, [on, drainRing]);

  // ── Live alerts ────────────────────────────────────────────────────────────
  useEffect(() => {
    on("AlertReceived", (alert) => {
      setAlerts((prev) => [alert, ...prev].slice(0, MAX_ALERTS_IN_MEMORY));
    });
  }, [on]);

  // ── Load history + initial alerts on device/window change ─────────────────
  useEffect(() => {
    if (!selectedDeviceId) return;

    // Reset ring buffer on device switch
    ringRef.current.fill(null);
    headRef.current = 0;
    sizeRef.current = 0;
    setLiveReadings([]);
    setLoading(true);

    // AbortController cancels in-flight fetches if the component unmounts or
    // the selected device changes before the response arrives.
    const controller = new AbortController();
    const { signal } = controller;

    Promise.all([
      fetchHistory(selectedDeviceId, historyMinutes, signal),
      fetchAlerts(50, signal),
    ])
      .then(([hist, alertData]) => {
        setHistory(hist ?? []);
        setAlerts(alertData ?? []);
      })
      .catch((err) => {
        if (err.name === "AbortError") return;   // unmounted — ignore
        console.error("[useSensorData] fetch failed:", err);
        setHistory([]);
      })
      .finally(() => {
        if (!signal.aborted) setLoading(false);
      });

    return () => controller.abort();
  }, [selectedDeviceId, historyMinutes]);

  // ── Acknowledge alert ──────────────────────────────────────────────────────
  const ackAlert = useCallback(async (alertId) => {
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
