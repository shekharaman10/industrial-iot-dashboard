import { useEffect, useRef, useState, useCallback } from "react";
import * as signalR from "@microsoft/signalr";

const SIGNALR_URL = import.meta.env.VITE_SIGNALR_URL || "http://localhost:8080/hubs/sensors";

/**
 * useSignalR
 * Manages a SignalR HubConnection lifecycle.
 *
 * Features:
 *  - JWT bearer token passed as ?access_token= query param (required by backend)
 *  - Automatic reconnect with exponential back-off (built into SignalR client)
 *  - Connection state exposed to UI (Connecting / Connected / Reconnecting / Disconnected)
 *  - Typed event subscription via `on(event, handler)`
 *  - Cleanup on unmount
 *
 * Token:
 *  The backend requires a JWT. Obtain one from POST /api/auth/token and store it
 *  in sessionStorage under the key "iot_token". This hook reads it on every
 *  (re)connect via the accessTokenFactory callback, so token refreshes are
 *  picked up automatically without remounting.
 *
 * @returns { connection, status, on }
 */
export function useSignalR() {
  const connectionRef = useRef(null);
  const handlersRef   = useRef({});          // event → handler (stable refs)
  const [status, setStatus] = useState("Disconnected");

  useEffect(() => {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(SIGNALR_URL, {
        // Read the token at connection/reconnection time so refreshes are automatic
        accessTokenFactory: () => sessionStorage.getItem("iot_token") ?? "",
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])  // retry schedule (ms)
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    conn.onreconnecting(() => setStatus("Reconnecting"));
    conn.onreconnected(() => {
      setStatus("Connected");
      // Re-register all handlers after reconnect to ensure no stale closures
      Object.entries(handlersRef.current).forEach(([event, handler]) => {
        conn.off(event);
        conn.on(event, handler);
      });
    });
    conn.onclose((err) => {
      setStatus("Disconnected");
      if (err) console.error("[SignalR] Connection closed with error:", err);
    });

    // Register any handlers already registered before the connection was built
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

    return () => { conn.stop(); };
  }, []);

  /**
   * Subscribe to a SignalR event.
   * Re-registrations are idempotent (same handler replaces previous for that event).
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
