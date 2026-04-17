import { useEffect, useRef, useState, useCallback } from "react";
import * as signalR from "@microsoft/signalr";

const SIGNALR_URL = import.meta.env.VITE_SIGNALR_URL || "http://localhost:8080/hubs/sensors";

/**
 * useSignalR
 * Manages a SignalR HubConnection lifecycle.
 *
 * Features:
 *  - Automatic reconnect with exponential back-off (built into SignalR client)
 *  - Connection state exposed to UI (Connecting / Connected / Reconnecting / Disconnected)
 *  - Typed event subscription via `on(event, handler)`
 *  - Cleanup on unmount
 *
 * @returns { connection, status, on }
 */
export function useSignalR() {
  const connectionRef = useRef(null);
  const handlersRef   = useRef({});          // event → handler (stable refs)
  const [status, setStatus] = useState("Disconnected");

  useEffect(() => {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(SIGNALR_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])  // retry schedule (ms)
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    conn.onreconnecting(() => setStatus("Reconnecting"));
    conn.onreconnected(() => setStatus("Connected"));
    conn.onclose(() => setStatus("Disconnected"));

    // Re-register all handlers after reconnect
    Object.entries(handlersRef.current).forEach(([event, handler]) => {
      conn.on(event, handler);
    });

    conn.start()
      .then(() => setStatus("Connected"))
      .catch((err) => {
        console.error("[SignalR] Initial connect failed:", err);
        setStatus("Disconnected");
      });

    connectionRef.current = conn;

    return () => {
      conn.stop();
    };
  }, []);

  /**
   * Subscribe to a SignalR event.
   * Re-registrations are idempotent (same handler replaces previous).
   */
  const on = useCallback((event, handler) => {
    handlersRef.current[event] = handler;
    if (connectionRef.current) {
      connectionRef.current.off(event);
      connectionRef.current.on(event, handler);
    }
  }, []);

  return { connection: connectionRef.current, status, on };
}
