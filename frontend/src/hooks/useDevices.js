import { useState, useEffect, useCallback, useRef } from "react";
import { fetchDevices } from "../services/api";

/**
 * useDevices
 * Fetches the device registry from REST and keeps it fresh.
 *
 * - Polls every REFRESH_INTERVAL_MS while the window is focused
 * - Also refreshes when a new device appears in the SignalR device map
 *   (caller passes latestDeviceIds to trigger a re-fetch)
 * - Exposes a manual refresh function for the "refresh" button
 *
 * @param {Set<string>} knownDeviceIds - device IDs seen via SignalR;
 *        when a new ID appears here, trigger a fresh REST fetch
 */
export function useDevices(knownDeviceIds = new Set()) {
  const REFRESH_INTERVAL_MS = 30_000;  // poll every 30 seconds

  const [devices,  setDevices]  = useState([]);
  const [loading,  setLoading]  = useState(true);
  const [error,    setError]    = useState(null);

  const knownRef    = useRef(new Set());
  const intervalRef = useRef(null);

  const load = useCallback(async () => {
    try {
      setError(null);
      const data = await fetchDevices();
      setDevices(data ?? []);
    } catch (err) {
      console.error("[useDevices] fetch failed:", err);
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, []);

  // Initial load
  useEffect(() => { load(); }, [load]);

  // Polling while window focused
  useEffect(() => {
    const start = () => {
      intervalRef.current = setInterval(load, REFRESH_INTERVAL_MS);
    };
    const stop = () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };

    start();
    window.addEventListener("focus",  start);
    window.addEventListener("blur",   stop);
    return () => {
      stop();
      window.removeEventListener("focus",  start);
      window.removeEventListener("blur",   stop);
    };
  }, [load]);

  // Re-fetch when a new device appears via SignalR
  useEffect(() => {
    let hasNew = false;
    for (const id of knownDeviceIds) {
      if (!knownRef.current.has(id)) {
        hasNew = true;
        knownRef.current.add(id);
      }
    }
    if (hasNew) load();
  }, [knownDeviceIds, load]);

  return { devices, loading, error, refresh: load };
}
