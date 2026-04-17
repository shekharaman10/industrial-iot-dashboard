# Data Flow

## End-to-End Message Lifecycle

```
ESP32 (edge)
  │
  │  Every 500ms:
  │    1. MPU6050 → raw accel/gyro samples
  │    2. Kalman filter (per axis) → filtered values
  │    3. RMS magnitude computed: √(x²+y²+z²)
  │  Every 3000ms:
  │    4. DHT22 → filtered temperature + humidity
  │    5. TelemetryFormatter::build() → JSON (schema_version:1)
  │
  ▼ MQTT PUBLISH  (QoS-0, topic: sensors/unit-01/telemetry)
  
Mosquitto Broker
  │
  ▼ MQTT FORWARD  (QoS-1 if CleanSession=false)

MqttSubscriberService (.NET)
  │  ApplicationMessageReceivedAsync callback
  │  1. MqttMessageParser.Parse() → validate schema_version + device_id
  │  2. ChannelWriter<TelemetryMessage>.WriteAsync() → blocks on backpressure
  │
  ▼ System.Threading.Channel (capacity: 2000)

IngestionWorker (.NET)
  │  Reads from Channel
  │  1. DeviceService.RegisterHeartbeatAsync() → upsert PostgreSQL devices table
  │  2. Map TelemetryMessage → SensorReading (domain entity)
  │  3. ChannelWriter<SensorReading>.WriteAsync()
  │
  ▼ System.Threading.Channel (capacity: 2000)

ProcessingWorker (.NET)
  │  Reads from Channel
  │  1. AnalyticsEngine.Evaluate(deviceId, vibRms, VibrationRms)
  │     → rolling window → Z-score → rate-of-change → AnalysisResult
  │  2. AnalyticsEngine.Evaluate(deviceId, temp, TemperatureCelsius)
  │  3. ISensorRepository.WriteAsync(reading) → InfluxDB (fire-and-forget)
  │  4. AlertService.RaiseIfNeededAsync() → if anomaly:
  │       → IAlertRepository.SaveAsync(alert) → PostgreSQL
  │  5. IHubContext<SensorHub>.Clients.All.SendAsync("TelemetryReceived", dto)
  │  6. IHubContext<SensorHub>.Clients.All.SendAsync("AlertReceived", alert) [if anomaly]
  │
  ├── InfluxDB: vibration{device_id, location} + temperature{device_id, location}
  │
  └── SignalR WebSocket

React Dashboard
  │  useSignalR.on("TelemetryReceived")
  │    → useSensorData.setDevices() [latest frame per device]
  │    → useSensorData.setLiveReadings() [ring buffer, 120 frames]
  │    → VibrationChart re-renders (isAnimationActive=false for perf)
  │    → TemperatureChart re-renders
  │    → MetricCards update
  │
  └── useSignalR.on("AlertReceived")
        → useSensorData.setAlerts() [prepend, keep 100]
        → AlertsPanel re-renders
```

---

## REST API Flows

### GET /api/sensors/devices
```
Browser → GET /api/sensors/devices
  → SensorsController.GetDevices()
  → DeviceService.GetAllAsync()
  → DeviceRepository (Dapper, PostgreSQL)
  → SELECT * FROM devices ORDER BY last_seen_utc DESC
  → JSON array [{id, location, status, firmware, lastSeen, isStale}]
```

### GET /api/sensors/{deviceId}/history?minutes=60
```
Browser → GET /api/sensors/unit-01/history?minutes=60
  → SensorsController.GetHistory()
  → ISensorRepository.QueryAsync("unit-01", 60min)
  → InfluxDbSensorRepository (Flux query)
  → Joins vibration + temperature measurements on timestamp
  → JSON array [{timestamp, vibration, accelX..Z, temperature, humidity}]
  → VibrationChart/TemperatureChart render historical view
```

### POST /api/alerts/{alertId}/acknowledge
```
Dashboard ACK button click
  → api.js acknowledgeAlert(alertId)
  → POST /api/alerts/{id}/acknowledge?acknowledgedBy=dashboard
  → AlertsController.Acknowledge()
  → IAlertRepository.AcknowledgeAsync()
  → UPDATE alerts SET acknowledged=true WHERE id=?
  → 204 No Content
  → useSensorData.setAlerts() optimistic update (acknowledged=true locally)
```

---

## MQTT Status Flow (Last-Will-Testament)

```
ESP32 connects →
  gMqtt.connect(..., lwt="{status:offline}", topic="sensors/unit-01/status") →
  gMqtt.publish("sensors/unit-01/status", "{status:online}", retain=true)

ESP32 loses power / WiFi drops →
  Mosquitto broker publishes LWT: sensors/unit-01/status → {status:offline}

MqttSubscriberService receives LWT →
  (Future: subscribe to sensors/+/status)
  → DeviceService.MarkOfflineAsync("unit-01")
  → PostgreSQL UPDATE devices SET status='Offline'
  → SignalR: broadcast DeviceStatusChanged
```

---

## Analytics Decision Flow (per sample)

```
ProcessingWorker receives SensorReading{deviceId="unit-01", vibRms=14.7}

AnalyticsEngine.Evaluate("unit-01", 14.7, VibrationRms):
  key = "unit-01:VibrationRms"
  state = _states[key]  // thread-locked

  lastValue = 9.82 (previous)
  delta = |14.7 - 9.82| / 9.82 = 0.497 → rateSpike = true (> 0.40)

  window.Enqueue(14.7) → [9.78, 9.81, 9.79, ..., 14.7]  (60 samples)

  avg    = 9.87 (influenced by spike)
  stddev = 0.61
  zScore = |14.7 - 9.87| / 0.61 = 7.9

  Classify(zScore=7.9, rateSpike=true):
    → rateSpike wins first check
    → return (isAnomaly=true, Critical, "Rate-of-change spike: >40% in one step")

  return AnalysisResult(IsAnomaly=true, ZScore=7.9, Severity=Critical, ...)

AlertService.RaiseIfNeededAsync("unit-01", VibrationRms, result, 14.7):
  dedupKey = "unit-01:RateOfChangeSpike"
  lastAlerted["unit-01:RateOfChangeSpike"] = null → no cooldown
  Create Alert{Severity=Critical, ZScore=7.9, Message="Rate-of-change..."}
  → PostgreSQL INSERT alerts
  → _lastAlerted[dedupKey] = now

SignalR broadcast:
  "TelemetryReceived" → all dashboard clients
  "AlertReceived"     → all dashboard clients
```
