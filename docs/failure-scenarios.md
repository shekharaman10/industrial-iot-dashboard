# Failure Scenarios & Mitigations

This document describes every failure mode the system was designed for, how it manifests, and how the code handles it. Engineers reviewing this project should be able to trace each mitigation to a specific file and line.

---

## 1. Edge Device Failures

### 1.1 MQTT Broker Unreachable

**Symptom:** ESP32 cannot connect on boot or broker restarts.

**Mitigation:**
- `main.cpp`: exponential back-off reconnect loop (`gReconnDelay = min(delay * 2, 60000)`)
- Reconnect delay starts at 2s, doubles each attempt, caps at 60s
- Watchdog timer (`WDT_TIMEOUT_S = 15`) resets ESP32 if WiFi/MQTT stack deadlocks entirely
- Last-Will-Testament configured at connect time — broker publishes `status: offline` when TCP drops, so backend knows immediately

**Data impact:** Readings during broker downtime are lost. Mitigation: for critical applications, add local SQLite write-through on ESP32 with a replay queue on reconnect.

---

### 1.2 Sensor Hardware Failure (MPU6050 / DHT22 Dead)

**Symptom:** I2C bus NAK, `begin()` returns false, or DHT22 returns `NaN` continuously.

**Mitigation:**
- `vibration_sensor.cpp:begin()` logs failure and sets `_online = false`
- `main.cpp:loop()` checks `gLatestVib.valid` before publishing
- If both sensors fail: "Both sensors invalid — skipping publish" is logged; no garbage data is sent
- Backend: `MqttMessageParser` logs warning if both `vibration` and `temperature` fields are null but still accepts partial frames

**Data impact:** Gaps appear in InfluxDB time-series. Dashboard shows `—` for missing metrics.

---

### 1.3 JSON Serialisation Failure on Edge

**Symptom:** `serializeJson()` returns 0 (StaticJsonDocument allocation failure).

**Mitigation:**
- `telemetry_formatter.cpp`: checks return value of `serializeJson()`; returns empty string on failure
- `main.cpp:publishTelemetry()`: checks `payload.isEmpty()` before calling `publish()`, skips gracefully

---

### 1.4 WiFi Dropout During Operation

**Symptom:** `WiFi.status() != WL_CONNECTED` mid-session.

**Mitigation:**
- `PubSubClient.loop()` detects broken TCP connection
- `mqttClient.connected()` check every loop iteration triggers reconnect path
- WiFi stack on ESP32 attempts auto-reconnect at OS level (enabled by default with `WIFI_STA` mode)

---

## 2. Broker Failures

### 2.1 Mosquitto Restart / Crash

**Symptom:** Active MQTT connections drop; LWT messages published by broker for all clients.

**Mitigation:**
- Backend `MqttSubscriberService`: `onclose` handler triggers reconnect loop with exponential back-off
- `CleanSession = false` in backend client options: QoS-1 messages queued during downtime are delivered on reconnect
- LWT from edge devices triggers `DeviceService.MarkOfflineAsync()` updating device status in PostgreSQL

**Data impact:** Messages published during broker restart window are lost unless QoS-1 was used end-to-end. Current firmware uses QoS-0 (fire-and-forget) for throughput; upgrade to QoS-1 if data loss is unacceptable.

---

## 3. Backend Pipeline Failures

### 3.1 InfluxDB Write Failure

**Symptom:** InfluxDB returns 5xx or is temporarily unreachable.

**Mitigation:**
- `ProcessingWorker.cs`: InfluxDB write is fire-and-forget (`_ = sensorRepo.WriteAsync(...).ContinueWith(...)`)
- Write failures are logged as errors but do NOT block SignalR broadcast
- This means the dashboard continues to show real-time data even when persistence is degraded
- For production: add a retry queue (e.g., Polly `RetryAsync`) wrapping the write call

**Data impact:** Readings during InfluxDB downtime are lost from history but visible live on dashboard.

---

### 3.2 Channel Backpressure (Processing Falls Behind MQTT Ingest)

**Symptom:** MQTT messages arrive faster than `ProcessingWorker` can handle them.

**Mitigation:**
- Both channels use `BoundedChannelOptions(capacity: 2000, FullMode: Wait)`
- `Wait` mode causes `MqttSubscriberService.HandleMessageAsync` to block (not drop)
- This provides natural backpressure all the way back to the MQTT broker (TCP receive window fills → broker slows QoS-1 flow)
- `AnalyticsEngine` is O(n) per sample where n = window size (60) — very fast; backpressure should not occur under normal load

**Observable signal:** If channel is consistently full, backend logs will show `[IngestionWorker]` falling behind. Add metrics via `System.Diagnostics.Metrics` for production monitoring.

---

### 3.3 PostgreSQL Unavailable

**Symptom:** Alert saves and device registry upserts fail.

**Mitigation:**
- `AlertService.RaiseIfNeededAsync()` logs error and continues (alert not persisted but was broadcast via SignalR)
- `DeviceService.RegisterHeartbeatAsync()` is fire-and-forget from `IngestionWorker` — failure does not block the pipeline
- `docker-compose.yml`: backend has `depends_on: postgres: condition: service_healthy` — won't start until Postgres is ready

---

### 3.4 SignalR Client Disconnect (Dashboard Tab Closed)

**Symptom:** Browser closes WebSocket connection.

**Mitigation:**
- `SensorHub.OnDisconnectedAsync()` cleans up group memberships automatically (SignalR manages this)
- Broadcasts via `_hub.Clients.All.SendAsync()` silently skip disconnected clients
- No explicit cleanup needed; no resource leak

---

## 4. Frontend Failures

### 4.1 Backend API Unreachable on Load

**Symptom:** `fetchDevices()` or `fetchAlerts()` fails on mount.

**Mitigation:**
- `api.js`: all requests throw on non-2xx response
- `useSensorData`: errors are caught in `.catch(console.error)` — dashboard renders empty state rather than crashing
- Dashboard shows "No devices registered" message rather than blank or error screen

---

### 4.2 SignalR Reconnect Loop

**Symptom:** Backend restarts while dashboard is open.

**Mitigation:**
- `useSignalR.js`: configured with `withAutomaticReconnect([0, 2000, 5000, 10000, 30000])`
- Connection status is displayed in the header (`RECONNECTING` badge)
- Live chart buffer (`liveReadings`) is preserved in React state during reconnect
- After reconnect, data stream resumes automatically; no page reload required

---

## 5. Data Quality Failures

### 5.1 Sensor Noise Causing False Anomaly Alerts

**Symptom:** MPU6050 produces occasional noisy spikes; Z-score triggers false alerts.

**Mitigation:**
- **Edge**: Kalman filter with tuned `R = 0.5` (high measurement noise tolerance) removes single-sample spikes before they reach MQTT
- **Backend**: `AnalyticsEngine` uses a 60-sample rolling window; a single noisy reading shifts Z-score only minimally
- **Backend**: `AlertService` has a 30-second cooldown per device+type — even if a false positive slips through, it won't spam the alert panel

---

### 5.2 Dropped Sequence Numbers

**Symptom:** Sequence counter jumps (e.g., seq=100, seq=103 — two packets dropped).

**Mitigation:**
- `IngestionWorker` currently maps `msg.Seq` to `SensorReading.SequenceNum` but does not validate gaps
- **Improvement path**: Add gap detection in `ProcessingWorker` — log warning when `current.seq - previous.seq > 1`; raise `SensorFault` alert if gap exceeds 10 consecutive missed readings

---

### 5.3 Clock Skew (Edge millis() vs Server UTC)

**Symptom:** `ts_ms` in firmware payload is `millis()` (device uptime, not wall clock). After reboot, timestamps reset to 0.

**Mitigation (current):**
- `IngestionWorker.MapToReading()` replaces device timestamp with server-side `DateTimeOffset.UtcNow`
- This sacrifices sub-millisecond precision for correctness

**Production fix:** Add NTP client to ESP32 firmware (`configTime()` call after WiFi connects); send Unix epoch milliseconds instead of `millis()`. Update `TelemetryMessage.TsMs` handling to use the device timestamp directly.
