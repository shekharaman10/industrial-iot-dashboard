# Industrial IoT Dashboard

> Production-grade predictive maintenance system for industrial equipment.
> ESP32 edge devices → MQTT → .NET backend pipeline → React real-time dashboard.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Repository Structure](#2-repository-structure)
3. [Understanding Every Code File](#3-understanding-every-code-file)
4. [How Everything Connects](#4-how-everything-connects)
5. [Step-by-Step Execution Guide](#5-step-by-step-execution-guide)
6. [API Reference](#6-api-reference)
7. [Running Tests](#7-running-tests)
8. [Use Cases](#8-use-cases)
9. [Extending the System](#9-extending-the-system)

---

## 1. Architecture Overview

```
[ESP32 + Sensors]  →  [Mosquitto MQTT]  →  [.NET Backend]  →  [React Dashboard]
      C++                               C# (3 workers)         JavaScript/Recharts
  Kalman filter                        Channel pipeline         SignalR real-time
  Telemetry JSON               InfluxDB + PostgreSQL            REST + WebSocket
```

**Three-stage backend pipeline:**
```
MqttSubscriberService
  → Channel<TelemetryMessage>
  → IngestionWorker  (parse + device registry)
  → Channel<SensorReading>
  → ProcessingWorker (analytics + alerts + broadcast)
```

---

## 2. Repository Structure

```
industrial-iot-dashboard/
├── edge/firmware/                  ESP32 C++ firmware (PlatformIO)
│   ├── include/                    Header files (interfaces)
│   │   ├── kalman_filter.h         Kalman filter contract + tuning docs
│   │   ├── vibration_sensor.h      MPU6050 wrapper interface
│   │   ├── temperature_sensor.h    DHT22 wrapper interface
│   │   └── telemetry_formatter.h   JSON schema contract (schema_version:1)
│   ├── src/
│   │   ├── main.cpp                Entry point: WiFi, MQTT, sensor loop
│   │   ├── filters/kalman_filter.cpp       Prediction + update steps
│   │   ├── sensors/vibration_sensor.cpp    MPU6050 + 6x Kalman filters
│   │   ├── sensors/temperature_sensor.cpp  DHT22 + 2x Kalman filters
│   │   ├── sensors/telemetry_formatter.cpp JSON builder
│   │   └── communication/mqtt_client.cpp   MQTT wrapper (isolates networking)
│   └── platformio.ini              Build config (dev + release profiles)
│
├── backend/
│   ├── src/
│   │   ├── Domain/                 Pure domain logic — no framework deps
│   │   │   ├── Entities/
│   │   │   │   ├── SensorReading.cs  Immutable time-series record
│   │   │   │   ├── Device.cs         Device registry entity + IsStale prop
│   │   │   │   └── Alert.cs          Anomaly alert + acknowledgement state
│   │   │   └── Enums/SensorType.cs   VibrationRms, TemperatureCelsius, etc.
│   │   │
│   │   ├── Application/            Business logic — depends only on Domain
│   │   │   ├── Interfaces/
│   │   │   │   └── IInterfaces.cs    ISensorRepository, IAlertRepository,
│   │   │   │                         IAnalyticsEngine, IMessageBus
│   │   │   ├── Services/
│   │   │   │   ├── AnalyticsEngine.cs  Z-score + rate-of-change (stateful)
│   │   │   │   ├── AlertService.cs     Alert creation + 30s dedup cooldown
│   │   │   │   └── DeviceService.cs    Device heartbeat + online/offline
│   │   │   └── Models/
│   │   │       └── TelemetryMessage.cs JSON contract (mirrors firmware schema)
│   │   │
│   │   ├── Infrastructure/         External systems — implements Application interfaces
│   │   │   ├── Messaging/
│   │   │   │   ├── MqttSubscriberService.cs  MQTT broker connectivity
│   │   │   │   └── MqttMessageParser.cs      JSON deserialisation + validation
│   │   │   ├── Persistence/
│   │   │   │   ├── InfluxDb/InfluxDbSensorRepository.cs  Time-series writes + Flux
│   │   │   │   └── Postgres/DeviceRepository.cs          Dapper: devices + alerts
│   │   │   └── Logging/SerilogConfig.cs  JSON prod / coloured dev
│   │   │
│   │   ├── Worker/
│   │   │   ├── IngestionWorker.cs   Stage 1: MQTT message → SensorReading
│   │   │   └── ProcessingWorker.cs  Stage 2: analytics → DB → SignalR
│   │   │
│   │   ├── Api/
│   │   │   ├── Controllers/SensorController.cs  REST: /sensors /alerts
│   │   │   ├── Hubs/SensorHub.cs               SignalR hub + device groups
│   │   │   └── Middleware/GlobalExceptionMiddleware.cs  RFC 7807 errors
│   │   │
│   │   └── Shared/Constants/Topics.cs   MQTT topic strings (single source of truth)
│   │
│   ├── tests/UnitTests/
│   │   └── AnalyticsEngineTests.cs   7 tests: normal/spike/ROC/thread-safety
│   └── Dockerfile                    Multi-stage: build → test → runtime
│
├── frontend/
│   ├── src/
│   │   ├── components/
│   │   │   ├── Dashboard.jsx        Main layout + device selector + metric cards
│   │   │   ├── AlertsPanel.jsx      Severity-filtered alert feed + ACK button
│   │   │   └── Charts/
│   │   │       ├── VibrationChart.jsx   Recharts area chart + anomaly dots
│   │   │       └── TemperatureChart.jsx Dual-axis line chart
│   │   ├── hooks/
│   │   │   ├── useSignalR.js    SignalR connection lifecycle + typed events
│   │   │   └── useSensorData.js All data state: live + history + alerts
│   │   └── services/api.js     Typed REST client (fetch wrapper)
│   ├── Dockerfile              Node build → nginx serve
│   ├── vite.config.js          Dev proxy + build config
│   └── package.json
│
├── infra/
│   ├── docker-compose.yml      5 services with health-check ordering
│   ├── mosquitto/mosquitto.conf MQTT broker config
│   └── postgres/init.sql       Schema: devices + alerts with indexes
│
├── data/simulation-scripts/
│   └── simulate_sensors.py     Physics-based simulator (harmonics + drift + fault)
│
├── docs/
│   ├── architecture.md         System architecture + improvement rationale
│   ├── data-flow.md            Message lifecycle from ESP32 to dashboard
│   ├── decisions.md            6 Architecture Decision Records (ADRs)
│   └── failure-scenarios.md    Every failure mode + mitigation + data impact
│
└── scripts/setup.sh            One-command setup + health-check wait loop
```

---

## 3. Understanding Every Code File

### Edge Layer (C++)

| File | What it does | Key decisions |
|---|---|---|
| `kalman_filter.h/cpp` | 1-D Kalman filter. Prediction step: `P += Q`. Update step: `K = P/(P+R)`, `X += K*(z-X)`, `P = (1-K)*P` | Process noise Q and measurement noise R are compile-time constants, not runtime params. This prevents misconfiguration. |
| `vibration_sensor.h/cpp` | Wraps MPU6050. Calls `_mpu.getEvent()` then routes each of 6 axes through its own independent `KalmanFilter` instance. Returns `VibrationData` with `rmsAccel_ms2 = √(x²+y²+z²)` | ±8g range chosen for industrial motors. 21Hz low-pass filter removes digital noise above Nyquist for typical bearing defect frequencies. |
| `temperature_sensor.h/cpp` | Wraps DHT22. Guards against `isnan()` return (DHT22 min poll period = 2s). | Low Q=0.001 because temperature changes slowly — high filter trust in the model. |
| `telemetry_formatter.h/cpp` | Builds the canonical JSON string using ArduinoJson `StaticJsonDocument`. Returns empty string on allocation failure. | JSON schema is the **contract**. Isolating it here means one file change to add a field, and schema_version bumps immediately flag mismatches on the backend. |
| `main.cpp` | Non-blocking cooperative loop. Three timers: VIB_INTERVAL_MS=500, TEMP_INTERVAL_MS=3000, PUBLISH_INTERVAL_MS=3000. Watchdog timer resets device if loop stalls > 15s. | `delay()` is never called in the hot path — only in `connectWiFi()` which runs once at boot. |

### Backend Layer (C#)

| File | What it does | Key decisions |
|---|---|---|
| `SensorReading.cs` | Immutable `record` — cannot be mutated after construction. All fields are `init`-only. | Records in C# give value equality semantics free — useful for tests. |
| `Device.cs` | Mutable entity with `IsStale` computed property: `(UtcNow - LastSeenUtc).TotalSeconds > 30`. | Stale detection is pure logic, no DB query needed. |
| `Alert.cs` | Captures anomaly with ZScore, measured value, and threshold at the moment of detection. Acknowledgement is a separate lifecycle (ACK can happen days later). | |
| `IInterfaces.cs` | All 4 application interfaces in one file. `ISensorRepository` = write+query time-series. `IAlertRepository` = CRUD alerts. `IAnalyticsEngine` = stateful per-device evaluation. `IMessageBus` = channel abstraction. | Interfaces live in Application layer — Infrastructure implements them. Nothing in Application imports from Infrastructure. |
| `AnalyticsEngine.cs` | ConcurrentDictionary of `MetricState` keyed by `"{deviceId}:{metric}"`. Each state holds a `Queue<float>` rolling window, sample count, and baseline snapshot. Z-score computed with Bessel's correction (`n-1`). Two anomaly checks: Z-score thresholds + rate-of-change (40% single-step jump). | `lock(state)` per device — concurrent reads from different devices don't block each other. |
| `AlertService.cs` | Creates `Alert` domain entity, saves to repo, and updates cooldown dictionary. 30-second cooldown per `(deviceId, alertType)` key suppresses duplicate alerts during sustained fault. | Cooldown is in-memory — resets on restart. Production: persist cooldown state to Redis. |
| `DeviceService.cs` | In-memory cache (`Dictionary<string, Device>`) + async writes to PostgreSQL. Cache prevents DB query on every heartbeat (2 Hz × N devices). | Cache is single-process only. In multi-replica deployment, replace with Redis. |
| `MqttMessageParser.cs` | Parses `ReadOnlySpan<byte>` payload directly (no string allocation). Validates schema_version, rejects empty device_id. Logs first 200 chars of bad payload for debugging. | Isolated from connectivity concerns. Unit-testable with a byte array — no broker needed. |
| `MqttSubscriberService.cs` | `BackgroundService`. Connects with `CleanSession=false` (retain QoS-1 queue). Subscribes to `sensors/+/telemetry` (wildcard). Exponential reconnect. Writes parsed messages to `Channel<TelemetryMessage>`. | This class knows nothing about business logic. It only produces messages. |
| `IngestionWorker.cs` | Consumes `Channel<TelemetryMessage>`. Maps to `SensorReading`. Fires device heartbeat (non-blocking). Produces to `Channel<SensorReading>`. | Two-stage pipeline: ingestion is decoupled from processing. |
| `ProcessingWorker.cs` | Consumes `Channel<SensorReading>`. Runs analytics on each available metric. Fire-and-forget InfluxDB write. Alert evaluation. `Task.WhenAll()` for concurrent SignalR broadcasts. | InfluxDB write failure does NOT block SignalR — dashboard stays live even if persistence degrades. |
| `SensorHub.cs` | SignalR hub. All clients join `"all"` group on connect. `SubscribeToDevice()` adds client to `"device:{id}"` group for filtered updates. | Groups are managed by SignalR internally — no manual cleanup on disconnect. |
| `GlobalExceptionMiddleware.cs` | Catches unhandled exceptions, returns RFC 7807 Problem Details. In development, includes stack trace. In production, hides details. | Must be registered FIRST in the middleware pipeline. |
| `Program.cs` | Full DI wiring. Creates two bounded channels (capacity 2000 each). Registers all services as singletons (analytics state must survive across requests). CORS with `AllowCredentials()` for SignalR. Health checks for InfluxDB + PostgreSQL. | |
| `AnalyticsEngineTests.cs` | 7 xUnit tests. Tests: insufficient data, stable signal, warning, fault, rate-of-change spike, device isolation, thread safety. | All tests are deterministic — fixed `Random(42)` seed. |

### Frontend Layer (React)

| File | What it does | Key decisions |
|---|---|---|
| `useSignalR.js` | Creates `HubConnection` with `withAutomaticReconnect([0, 2000, 5000, 10000, 30000])`. Exposes `on(event, handler)` — components subscribe without knowing about SignalR internals. | Handlers stored in `handlersRef` (not state) to avoid re-creating the connection on re-render. |
| `useSensorData.js` | Single hook owning all data state. Combines SignalR live stream with REST history fetch. Exposes `liveReadings` (ring buffer, 120 frames), `history`, `alerts`, `devices` (latest per device), `ackAlert`. | `selectedRef` (useRef) prevents stale closure in SignalR callback — avoids registering new listener on every selectedDevice change. |
| `api.js` | Thin `fetch` wrapper. All calls throw on non-2xx. `204 No Content` returns null. Base URL from `VITE_API_URL` env var. | No axios dependency — native fetch is sufficient. |
| `Dashboard.jsx` | Layout: sticky header + device selector + metric cards + charts + alerts panel. Industrial dark aesthetic: `#060b12` background, amber `#f59e0b` accent, IBM Plex Mono for all data values. `isAnimationActive={false}` on all charts — essential for real-time performance (animations at 2 Hz cause frame drops). | |
| `VibrationChart.jsx` | Recharts `AreaChart`. Custom `CustomDot` renders red glowing dot on anomaly frames. Moving average overlay as dashed line. Baseline reference line from `result.Baseline`. | `connectNulls={true}` prevents chart gaps when a sensor is briefly offline. |
| `TemperatureChart.jsx` | Recharts `LineChart` with dual Y axes (temperature left, humidity right). Warning threshold reference line. | |
| `AlertsPanel.jsx` | Severity filter tabs with unacked count badges. Per-alert: severity icon + device ID + type badge + message + metrics (value, baseline, Z-score) + ACK button. | Opacity 0.45 for acknowledged alerts — visible but clearly resolved. |

---

## 4. How Everything Connects

```
┌─────────────────────────────────────────────────────────────────┐
│  ESP32 connects to WiFi                                          │
│  → gMqtt.connect() with LWT                                      │
│  → Publishes {status:online} to sensors/unit-01/status           │
│  → Every 3s: publishTelemetry() → sensors/unit-01/telemetry     │
└──────────────────────────┬──────────────────────────────────────┘
                           │ MQTT TCP
┌──────────────────────────▼──────────────────────────────────────┐
│  Mosquitto Broker                                                │
│  → Retains status messages (online/offline)                      │
│  → Forwards telemetry to all subscribers                         │
└──────────────────────────┬──────────────────────────────────────┘
                           │ MQTT subscription (sensors/+/telemetry)
┌──────────────────────────▼──────────────────────────────────────┐
│  MqttSubscriberService                                           │
│  → MqttMessageParser.Parse() validates JSON                      │
│  → Channel<TelemetryMessage>.WriteAsync() [backpressure]        │
│              │                                                   │
│  IngestionWorker                                                 │
│  → DeviceService.RegisterHeartbeatAsync() → PostgreSQL.Upsert   │
│  → Map to SensorReading                                          │
│  → Channel<SensorReading>.WriteAsync()                           │
│              │                                                   │
│  ProcessingWorker                                                │
│  → AnalyticsEngine.Evaluate() → Z-score + ROC                   │
│  → InfluxDB.WriteAsync() [fire-and-forget]                       │
│  → AlertService.RaiseIfNeededAsync() → PostgreSQL.Insert         │
│  → SignalR: TelemetryReceived + AlertReceived                    │
└──────────────────────────┬──────────────────────────────────────┘
                           │ WebSocket (SignalR)
┌──────────────────────────▼──────────────────────────────────────┐
│  React Dashboard                                                 │
│  → useSignalR.on("TelemetryReceived") → update charts           │
│  → useSignalR.on("AlertReceived")     → update alert panel      │
│  → fetchDevices() on mount            → device selector         │
│  → fetchHistory() on device select    → historical chart        │
└─────────────────────────────────────────────────────────────────┘
```

---

## 5. Step-by-Step Execution Guide

### Prerequisites

| Tool | Version | Purpose |
|---|---|---|
| Docker Desktop | 24+ | Run all infrastructure |
| Python 3.10+ | any | Sensor simulator |
| Node.js 20+ | LTS | Frontend dev server |
| .NET SDK 8 | 8.0 | Backend development |
| PlatformIO | latest | ESP32 firmware (optional) |

---

### Step 1 — Clone and verify structure

```bash
git clone <repo-url> industrial-iot-dashboard
cd industrial-iot-dashboard
ls -la
# Should show: edge/ backend/ frontend/ infra/ data/ docs/ scripts/
```

---

### Step 2 — Start infrastructure (one command)

```bash
bash scripts/setup.sh
```

This does:
1. Checks Docker is installed
2. Installs `paho-mqtt` Python package
3. Runs `docker compose up -d --build` in `infra/`
4. Waits for each service to pass its health check in order
5. Prints access URLs

**Or manually:**
```bash
cd infra
docker compose up -d
# Wait ~60s for all services to start
docker compose ps   # verify all show "healthy"
```

**Expected output:**
```
iot-mosquitto   healthy
iot-influxdb    healthy
iot-postgres    healthy
iot-backend     healthy
iot-frontend    healthy
```

---

### Step 3 — Verify services

```bash
# MQTT broker
mosquitto_pub -h localhost -t test -m hello    # or use MQTT Explorer

# InfluxDB
curl http://localhost:8086/health              # → {"status":"pass"}

# Backend API
curl http://localhost:8080/health              # → {"status":"Healthy"}
curl http://localhost:8080/api/sensors/devices # → [] (empty at first)

# Swagger UI
open http://localhost:8080/swagger

# Dashboard
open http://localhost:3000
```

---

### Step 4 — Start the sensor simulator

```bash
# Normal operation — 2 devices, 2 Hz each
python3 data/simulation-scripts/simulate_sensors.py

# Expected console output:
# [unit-01] seq=1      rms=9.8142 m/s²  temp=42.1°C
# [unit-02] seq=1      rms=9.8098 m/s²  temp=41.9°C
# [unit-01] seq=2      rms=9.8231 m/s²  temp=42.0°C
```

Open http://localhost:3000 — you should see unit-01 and unit-02 appear in the device selector, with live data flowing into the charts.

---

### Step 5 — Inject a fault and watch the dashboard

```bash
# New terminal
python3 data/simulation-scripts/simulate_sensors.py --fault-after 30

# After 30 seconds you'll see:
# [unit-01] 🔴 FAULT INJECTED at t=30.0s
# [unit-01] seq=61     rms=44.2019 m/s²  temp=42.3°C ⚠FAULT
```

In the dashboard:
- Vibration metric card border turns red
- ⚠ ANOMALY label appears on the card
- Red spike dot appears on the VibrationChart
- CRITICAL or FAULT alert appears in the Alerts panel
- Header shows "⚠ 1 UNACKED ALERT"

---

### Step 6 — Acknowledge an alert

**Via dashboard:** Click the **ACK** button on any alert. It fades to 45% opacity.

**Via API:**
```bash
# Get alert ID first
curl http://localhost:8080/api/alerts | python3 -m json.tool | head -30

# Acknowledge
curl -X POST "http://localhost:8080/api/alerts/{alert-id}/acknowledge?acknowledgedBy=operator"
```

---

### Step 7 — Flash ESP32 (real hardware)

```bash
cd edge/firmware

# Edit platformio.ini: replace WIFI_SSID, WIFI_PASSWORD, MQTT_HOST
# MQTT_HOST = your machine's IP address (not localhost — ESP32 needs external IP)

# Build and flash
pio run -e esp32dev -t upload

# Monitor serial output
pio device monitor
```

Expected ESP32 serial output:
```
[BOOT] Industrial IoT Firmware v1.2.0  device=unit-01
[WiFi] Connected  IP=192.168.1.50  RSSI=-62 dBm
[MQTT] Connected to 192.168.1.100:1883 as unit-01
[VibrationSensor] OK — MPU6050 range=±8g, gyro=±500°/s, LPF=21Hz
[TemperatureSensor] OK — DHT22 on pin 4
[TX] seq=1      rms=9.8142 m/s²  temp=42.1  len=287 B
```

---

### Step 8 — Run backend tests

```bash
cd backend
dotnet test tests/UnitTests/ --logger "console;verbosity=detailed"
```

Expected:
```
Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7
```

---

### Step 9 — Run frontend in dev mode (with hot reload)

```bash
cd frontend
npm install
npm run dev
# → http://localhost:3000  (proxies /api and /hubs to localhost:8080)
```

---

### Step 10 — View logs

```bash
# All services
docker compose -f infra/docker-compose.yml logs -f

# Backend only (structured JSON in production)
docker compose -f infra/docker-compose.yml logs -f backend

# Watch MQTT messages in real time
docker exec iot-mosquitto mosquitto_sub -t "sensors/#" -v
```

---

### Teardown

```bash
cd infra
docker compose down -v     # -v removes volumes (clears all data)
```

---

## 6. API Reference

| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/sensors/devices` | List all registered devices with status |
| GET | `/api/sensors/{id}/history?minutes=60` | Time-series history (InfluxDB) |
| GET | `/api/alerts?limit=50` | Recent alerts (PostgreSQL) |
| GET | `/api/alerts/device/{deviceId}?limit=50` | Alerts for one device |
| POST | `/api/alerts/{id}/acknowledge` | Mark alert as acknowledged |
| GET | `/health` | Health check (InfluxDB + PostgreSQL) |
| GET | `/swagger` | Interactive API docs |
| WS | `/hubs/sensors` | SignalR hub (TelemetryReceived, AlertReceived) |

---

## 7. Running Tests

```bash
# Unit tests (no Docker required)
cd backend && dotnet test

# Test with coverage report
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coverage-report"
open coverage-report/index.html

# Lint frontend
cd frontend && npm run lint
```

---

## 8. Use Cases

### UC-1: Bearing Fault Detection
**Scenario:** Motor bearing degrades over weeks. Vibration increases by 15%.

**How the system handles it:**
- `AnalyticsEngine` baseline is established in first 300 samples (~2.5 min)
- As bearing wears, Z-score crosses 1.5σ → WARNING alert
- Maintenance team acknowledges, schedules inspection
- If ignored, Z-score crosses 3.5σ → FAULT alert

### UC-2: Thermal Runaway
**Scenario:** Cooling fan fails. Motor temperature rises 30°C over 10 minutes.

**How the system handles it:**
- TemperatureChart reference line at 70°C gives visual warning
- Z-score calculation detects temperature rising faster than baseline
- Rate-of-change spike detection catches rapid acceleration (>40% in one step)
- CRITICAL ThermalOverheat alert raised, broadcast to all dashboard clients

### UC-3: Network Dropout Recovery
**Scenario:** Factory WiFi router reboots.

**How the system handles it:**
- ESP32: exponential back-off reconnect (2s → 4s → 8s → … → 60s)
- Mosquitto publishes LWT: `sensors/unit-01/status → {status:offline}`
- Device shows Offline/Stale in dashboard
- On reconnect: ESP32 publishes `{status:online}`, device shows Online again
- Data gap appears in charts (acceptable; production fix: local SQLite buffer)

### UC-4: Alert Storm Suppression
**Scenario:** Bearing fault causes 100+ anomaly readings per minute.

**How the system handles it:**
- `AlertService` 30-second cooldown per (device, alertType) key
- Dashboard sees 2 alerts per minute maximum, not 200
- All anomaly frames still broadcast via SignalR (chart shows all red dots)

---

## 9. Extending the System

### Add a new sensor (e.g., BMP280 pressure)
1. Add `PressureSensor` class in `edge/firmware/include/` + `src/sensors/`
2. Add `pressure` field to `TelemetryMessage.cs`
3. Add `SensorType.PressurePa` to `SensorType.cs`
4. Add Kalman filter params for pressure in `AnalyticsEngine.cs`
5. Add pressure field to InfluxDB write in `InfluxDbSensorRepository.cs`
6. Add `PressureChart.jsx` in frontend

### Add production authentication
1. Backend: add `[Authorize]` to controllers + `AddAuthentication().AddJwtBearer()`
2. MQTT: enable password_file in `mosquitto.conf`, set username/password in firmware NVS
3. Frontend: add login page + JWT storage + `Authorization` header in `api.js`

### Scale to 100+ devices
1. Swap `System.Threading.Channel` for Kafka (one config change behind `IMessageBus`)
2. Partition `ProcessingWorker` by deviceId (one worker per partition)
3. Replace in-memory device cache with Redis
4. Scale backend horizontally behind a load balancer (SignalR requires sticky sessions or Redis backplane)
