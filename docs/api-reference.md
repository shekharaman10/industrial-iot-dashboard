# API Reference

Base URL: `http://localhost:8080`  
Interactive docs: `http://localhost:8080/swagger`  
Real-time hub: `ws://localhost:8080/hubs/sensors`

---

## REST Endpoints

### Sensors

#### `GET /api/sensors/devices`
Returns all device IDs that have written to InfluxDB (may differ from the device registry if a device wrote data before its status message arrived).

**Response `200 OK`:**
```json
["unit-01", "unit-02", "unit-03"]
```

---

#### `GET /api/sensors/{deviceId}/history`

Returns time-series sensor readings for one device.

**Query parameters:**

| Parameter | Type    | Default | Description                      |
|-----------|---------|---------|----------------------------------|
| `minutes` | integer | `60`    | Lookback window (1–1440)         |

**Response `200 OK`:**
```json
[
  {
    "timestamp":   "2024-01-15T14:30:00Z",
    "seq":         1042,
    "vibration":   9.8142,
    "accelX":      0.1023,
    "accelY":      0.0512,
    "accelZ":      9.8099,
    "gyroX":       0.0012,
    "gyroY":      -0.0004,
    "gyroZ":       0.0007,
    "temperature": 42.3,
    "humidity":    61.2
  }
]
```

**Response `400 Bad Request`:**
```json
{ "error": "minutes must be between 1 and 1440." }
```

---

### Devices

#### `GET /api/devices`
Lists all registered devices with current status.

**Response `200 OK`:**
```json
[
  {
    "id":            "unit-01",
    "location":      "Assembly-Line-A",
    "status":        "Online",
    "firmware":      "1.2.0",
    "lastSeen":      "2024-01-15T14:30:01Z",
    "registeredAt":  "2024-01-01T08:00:00Z",
    "isStale":       false,
    "uptimeSeconds": 1234567.89
  }
]
```

---

#### `GET /api/devices/{deviceId}`
Get a single device.

**Response `404 Not Found`:**
```json
{ "error": "Device 'unit-99' not found." }
```

---

#### `GET /api/devices/{deviceId}/alerts`
Recent alerts for a specific device.

**Query parameters:** `limit` (default 50, max 500)

---

#### `POST /api/devices/{deviceId}/reset-analytics`
Resets the in-memory analytics baseline for a device. Use after maintenance so the engine re-establishes "normal" from fresh readings.

**Response `204 No Content`** (success)  
**Response `404 Not Found`** (device not in registry)

---

### Alerts

#### `GET /api/alerts`
Recent alerts across all devices, newest first.

**Query parameters:** `limit` (default 50, max 500)

**Response `200 OK`:**
```json
[
  {
    "id":             "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "deviceId":       "unit-01",
    "severity":       "Critical",
    "type":           "VibrationSpike",
    "message":        "Z-score 3.2σ ≥ 2.5σ — significant deviation from baseline.",
    "measuredValue":  22.341,
    "thresholdValue": 9.812,
    "zScore":         3.2,
    "timestamp":      "2024-01-15T14:30:00Z",
    "acknowledged":   false,
    "acknowledgedAt": null,
    "acknowledgedBy": null
  }
]
```

---

#### `GET /api/alerts/unacknowledged/count`
Returns the count of unacknowledged alerts. Used by the dashboard header badge.

**Response `200 OK`:**
```json
{ "count": 3 }
```

---

#### `POST /api/alerts/{alertId}/acknowledge`
Mark an alert as acknowledged.

**Query parameters:** `acknowledgedBy` (default `"dashboard"`)

**Response `204 No Content`** (success)

---

#### `POST /api/alerts/acknowledge-all`
Bulk acknowledge all unacknowledged alerts.

**Query parameters:** `acknowledgedBy` (default `"dashboard"`)

**Response `200 OK`:**
```json
{ "acknowledged": 7 }
```

---

### System

#### `GET /health`
Full health check: InfluxDB + PostgreSQL + channel pipeline.

**Response `200 OK`:**
```json
{
  "status": "Healthy",
  "results": {
    "influxdb": { "status": "Healthy" },
    "postgres":  { "status": "Healthy" },
    "channel-pipeline": {
      "status": "Healthy",
      "data": {
        "mqtt_channel_depth":       12,
        "processing_channel_depth": 8,
        "capacity":                 2000,
        "mqtt_pct":                 "0.6%",
        "processing_pct":           "0.4%"
      }
    }
  }
}
```

**Response `503 Service Unavailable`** when any check fails.

---

#### `GET /health/ready`
Readiness probe (subset of checks tagged "ready"). Used by Docker HEALTHCHECK and Kubernetes readiness probes.

---

## SignalR Hub

**URL:** `ws://localhost:8080/hubs/sensors`  
**Client library:** `@microsoft/signalr`

### Connection

```javascript
import * as signalR from "@microsoft/signalr";

const conn = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:8080/hubs/sensors")
  .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
  .build();

await conn.start();
```

---

### Server → Client Events

#### `TelemetryReceived`
Fired for every sensor reading that passes through the pipeline (~2 Hz per device).

```typescript
conn.on("TelemetryReceived", (frame: TelemetryFrame) => { ... });

interface TelemetryFrame {
  deviceId:    string;
  location:    string;
  timestamp:   string;      // ISO 8601 UTC
  seq:         number;
  vibration:   number|null; // RMS magnitude (m/s²)
  accelX:      number|null;
  accelY:      number|null;
  accelZ:      number|null;
  temperature: number|null; // Celsius
  humidity:    number|null; // Percent
  analysis: {
    vibration?: {
      isAnomaly:  boolean;
      zScore:     number;
      movingAvg:  number;
      stdDev:     number;
      baseline:   number;
      severity:   "Warning"|"Critical"|"Fault"|null;
      reason:     string|null;
    };
    temperature?: { /* same shape */ };
  };
}
```

#### `AlertReceived`
Fired when an anomaly crosses a threshold and passes the 30-second deduplication cooldown.

```typescript
conn.on("AlertReceived", (alert: AlertEvent) => { ... });

interface AlertEvent {
  id:        string;   // UUID
  deviceId:  string;
  severity:  "Info"|"Warning"|"Critical"|"Fault";
  type:      "VibrationSpike"|"VibrationSustained"|"ThermalOverheat"|
             "HumidityOutOfRange"|"RateOfChangeSpike"|"SensorFault";
  message:   string;
  value:     number;
  threshold: number;
  zScore:    number;
  timestamp: string;
}
```

---

### Client → Server Methods

#### `SubscribeToDevice(deviceId: string)`
Join a device-scoped SignalR group to receive updates for a specific device only. Useful when the dashboard is open to a specific device detail view.

```javascript
await conn.invoke("SubscribeToDevice", "unit-01");
```

#### `UnsubscribeFromDevice(deviceId: string)`
Leave the device-scoped group.

---

## MQTT Topics

For reference — the backend subscribes to these topics from Mosquitto.

| Topic | Direction | QoS | Description |
|---|---|---|---|
| `sensors/+/telemetry` | Edge → Backend | 0 | Sensor data (2 Hz) |
| `sensors/+/status` | Edge → Backend | 1 | Online/offline (retained) |
| `devices/+/commands` | Backend → Edge | 1 | OTA commands, resets |
| `backend/status` | Backend | 1 | Backend online/offline (retained) |

---

## Error Responses

All error responses follow RFC 7807 Problem Details:

```json
{
  "type":   "https://tools.ietf.org/html/rfc7807",
  "title":  "ArgumentException",
  "status": 400,
  "detail": "minutes must be between 1 and 1440."
}
```

In development (`ASPNETCORE_ENVIRONMENT=Development`), the `trace` field includes the full stack trace.

---

## Rate Limits

No rate limiting is implemented in the current version. All endpoints are intended for internal dashboard use. Add rate limiting via `AspNetCoreRateLimit` or an API gateway (e.g., Nginx) before exposing publicly.
