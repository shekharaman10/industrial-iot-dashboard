# Industrial IoT Dashboard

> Production-grade predictive maintenance system for industrial equipment.
> ESP32 edge devices → MQTT → .NET 8 backend pipeline → React 18 real-time dashboard.

**Audit Date:** 2026-06-17 | **Version:** 1.2.0 | **Status:** Feature-Complete Prototype

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Repository Structure](#2-repository-structure)
3. [Understanding Every Code File](#3-understanding-every-code-file)
4. [How Everything Connects](#4-how-everything-connects)
5. [Authentication & Security](#5-authentication--security)
6. [Step-by-Step Execution Guide](#6-step-by-step-execution-guide)
7. [API Reference](#7-api-reference)
8. [Running Tests](#8-running-tests)
9. [Use Cases](#9-use-cases)
10. [Known Issues & Production Checklist](#10-known-issues--production-checklist)
11. [Extending the System](#11-extending-the-system)

---

## 1. Architecture Overview

```
[ESP32 + Sensors]  →  [Mosquitto MQTT]  →  [.NET 8 Backend]  →  [React Dashboard]
      C++                               C# (3 workers)           JavaScript/Recharts
  Kalman filter                        Channel pipeline            SignalR real-time
  Telemetry JSON               InfluxDB + PostgreSQL               REST + WebSocket
                                       JWT Auth                   Bearer token
```

**Three-stage backend pipeline:**
```
MqttSubscriberService
  → Channel<TelemetryMessage>  (cap 2000, backpressure Wait)
  → IngestionWorker  (parse + device registry)
  → Channel<SensorReading>     (cap 2000, backpressure Wait)
  → ProcessingWorker (analytics + alerts + SignalR broadcast)
```

**Authentication flow:**
```
POST /api/auth/token  { apiKey }
  → 200 { token, expiresIn: 3600 }
  → store in sessionStorage["iot_token"]
  → REST:    Authorization: Bearer {token}
  → SignalR: ?access_token={token}  (JwtBearerEvents in Program.cs)
```

---

## 2. Repository Structure

```
industrial-iot-dashboard/
├── .env.example                        Environment variables template (copy → .env)
├── .github/
│   └── workflows/ci.yml                Build + test + lint + firmware compile
│
├── edge/firmware/                      ESP32 C++ firmware (PlatformIO)
│   ├── include/
│   │   ├── config.h                    Centralised config (WiFi, MQTT, Kalman tuning)
│   │   ├── kalman_filter.h             Kalman filter interface
│   │   ├── vibration_sensor.h          MPU6050 wrapper interface
│   │   ├── temperature_sensor.h        DHT22 wrapper interface
│   │   ├── telemetry_formatter.h       JSON schema contract (schema_version:1)
│   │   └── mqtt_client.h               MQTT client interface
│   ├── src/
│   │   ├── main.cpp                    Entry point: WiFi, MQTT, cooperative loop
│   │   ├── filters/kalman_filter.cpp   Prediction + update steps (Bessel-corrected)
│   │   ├── sensors/vibration_sensor.cpp     MPU6050 + 6× Kalman filters
│   │   ├── sensors/temperature_sensor.cpp   DHT22 + 2× Kalman filters
│   │   ├── sensors/telemetry_formatter.cpp  ArduinoJson schema builder
│   │   └── communication/mqtt_client.cpp    MQTT isolates networking
│   └── platformio.ini                  Build config (dev + release profiles)
│
├── backend/
│   ├── src/
│   │   ├── Domain/                     Pure domain — no framework dependencies
│   │   │   ├── Entities/
│   │   │   │   ├── SensorReading.cs    Immutable time-series record
│   │   │   │   ├── Device.cs           Registry entity + IsStale computed prop
│   │   │   │   └── Alert.cs            Anomaly event + acknowledgement lifecycle
│   │   │   └── Enums/SensorType.cs     VibrationRms, TemperatureCelsius, Humidity
│   │   │
│   │   ├── Application/                Business logic — depends only on Domain
│   │   │   ├── Interfaces/
│   │   │   │   ├── IAlertRepository.cs
│   │   │   │   ├── IAnalyticsEngine.cs
│   │   │   │   ├── IDeviceRepository.cs
│   │   │   │   ├── ISensorRepository.cs
│   │   │   │   ├── IMessageBus.cs
│   │   │   │   └── IInterfaces.cs      (legacy — individual files above are canonical)
│   │   │   ├── Services/
│   │   │   │   ├── AnalyticsEngine.cs  Z-score + ROC, ConcurrentDict, TTL eviction
│   │   │   │   ├── AnalyticsEngineOptions.cs  Tuning constants (configurable)
│   │   │   │   ├── AlertService.cs     Alert creation + 30s cooldown dedup
│   │   │   │   └── DeviceService.cs    Heartbeat cache + online/offline transitions
│   │   │   └── Models/
│   │   │       └── TelemetryMessage.cs JSON contract (mirrors firmware schema_version:1)
│   │   │
│   │   ├── Infrastructure/             External adapters
│   │   │   ├── HealthChecks/
│   │   │   │   ├── InfluxHealthCheck.cs
│   │   │   │   ├── PostgresHealthCheck.cs
│   │   │   │   └── ChannelHealthCheck.cs
│   │   │   ├── Logging/SerilogConfig.cs     JSON prod / coloured dev
│   │   │   ├── Messaging/
│   │   │   │   ├── MqttSubscriberService.cs  MQTT broker + exponential reconnect
│   │   │   │   └── MqttMessageParser.cs      Zero-alloc JSON parse + validation
│   │   │   └── Persistence/
│   │   │       ├── InfluxDb/InfluxDbSensorRepository.cs  Flux queries + writes
│   │   │       └── Postgres/
│   │   │           ├── AlertRepository.cs    Dapper: keyset-paginated alert CRUD
│   │   │           ├── DeviceRepository.cs   Dapper: device upsert + queries
│   │   │           └── PostgresOptions.cs
│   │   │
│   │   ├── Worker/
│   │   │   ├── IngestionWorker.cs      Stage 1: TelemetryMessage → SensorReading
│   │   │   └── ProcessingWorker.cs     Stage 2: analytics → DB → SignalR
│   │   │
│   │   ├── Api/
│   │   │   ├── Controllers/
│   │   │   │   ├── AuthController.cs       POST /api/auth/token (API-key → JWT)
│   │   │   │   ├── AlertsController.cs     GET/POST /api/alerts  ← needs [Authorize]
│   │   │   │   ├── DevicesController.cs    GET/POST /api/devices ← needs [Authorize]
│   │   │   │   └── SensorController.cs     GET /api/sensors      ← needs [Authorize]
│   │   │   ├── Hubs/SensorHub.cs           SignalR hub [Authorize] + device groups
│   │   │   ├── Middleware/
│   │   │   │   ├── GlobalExceptionMiddleware.cs  RFC 7807; hides traces in Production
│   │   │   │   └── RequestLoggingMiddleware.cs   Structured request/response logging
│   │   │   └── Program.cs                  Full DI, JWT, CORS, health checks
│   │   │
│   │   └── Shared/Constants/Topics.cs      MQTT topic strings (single source of truth)
│   │
│   ├── tests/
│   │   ├── UnitTests/
│   │   │   ├── AnalyticsEngineTests.cs     7 tests: normal/spike/ROC/isolation/thread-safety
│   │   │   ├── AlertServiceTests.cs        6 tests: dedup cooldown, metric isolation
│   │   │   ├── TopicsTests.cs              9 tests: topic string parsing
│   │   │   └── SensorReadingTests.cs       Entity property tests
│   │   └── IntegrationTests/
│   │       └── MqttMessageParserTests.cs   6 JSON parse integration tests
│   │
│   └── Dockerfile                          Multi-stage: SDK build → aspnet runtime
│
├── frontend/
│   ├── src/
│   │   ├── components/
│   │   │   ├── Dashboard.jsx               Main layout + ChartErrorBoundary
│   │   │   ├── DeviceCard.jsx              Per-device status card
│   │   │   ├── AlertsPanel.jsx             Severity-filtered feed + ACK button
│   │   │   ├── AnalysisOverlay.jsx         Z-score gauge + stats per metric
│   │   │   ├── StatsBanner.jsx             Header statistics row
│   │   │   └── Charts/
│   │   │       ├── VibrationChart.jsx      Area chart + anomaly dots + baseline line
│   │   │       └── TemperatureChart.jsx    Dual-axis (°C + %RH) line chart
│   │   ├── hooks/
│   │   │   ├── useSignalR.js               JWT-authenticated SignalR lifecycle
│   │   │   ├── useSensorData.js            All data state: live ring + history + alerts
│   │   │   └── useDevices.js               Device list management
│   │   ├── services/api.js                 Typed fetch wrapper (Bearer token)
│   │   └── utils/
│   │       ├── constants.js                API_BASE, SIGNALR_URL, thresholds
│   │       └── formatters.js               Z-score, timestamp formatters
│   ├── Dockerfile                          Node build → nginx:1.27-alpine serve
│   ├── nginx.conf                          SPA fallback + asset caching
│   ├── vite.config.js                      Dev proxy + build config
│   └── package.json                        React 18, Recharts, @microsoft/signalr
│
├── infra/
│   ├── docker-compose.yml                  5 services with health-check dependency ordering
│   ├── docker-compose.override.yml         Local overrides
│   ├── mosquitto/mosquitto.conf            MQTT broker (allow_anonymous=true in dev)
│   ├── postgres/
│   │   ├── init.sql                        Schema: devices + alerts + indexes + seed
│   │   └── migrations/001_initial.sql      Migration script
│   └── grafana/provisioning/               Pre-wired InfluxDB datasource + dashboard
│
├── data/simulation-scripts/
│   └── simulate_sensors.py                 Physics-based simulator (harmonics + drift + fault)
│
├── docs/
│   ├── architecture.md                     System design + rationale
│   ├── api-reference.md                    Full endpoint reference
│   ├── data-flow.md                        Message lifecycle ESP32 → dashboard
│   ├── decisions.md                        Architecture Decision Records (ADRs)
│   └── failure-scenarios.md                Every failure mode + mitigation
│
└── scripts/
    ├── setup.sh                            One-command setup + health-check wait
    ├── dev.sh                              Dev shortcuts
    └── seed-data.sh                        Seed test data into InfluxDB + PostgreSQL
```

---

## 3. Understanding Every Code File

### Edge Layer (C++)

| File | What it does | Key decisions |
|---|---|---|
| `config.h` | Centralised compile-time config: WiFi SSID/password, MQTT host/port, Kalman Q/R values, GPIO pins, timing intervals. All overridable via `platformio.ini` build_flags. | Sensitive credentials should come from ESP32 NVS in production. Placeholders `YOUR_SSID / YOUR_PASSWORD` make it obvious when defaults are in use. |
| `kalman_filter.h/cpp` | 1-D Kalman filter. Prediction: `P += Q`. Update: `K = P/(P+R)`, `X += K*(z-X)`, `P = (1-K)*P`. | Q and R are compile-time constants per sensor axis. Higher Q trusts measurements more; higher R trusts the model. |
| `vibration_sensor.h/cpp` | Wraps MPU6050. Routes each of 6 axes through its own `KalmanFilter` instance. Returns `VibrationData` with `rmsAccel_ms2 = √(ax²+ay²+az²)`. | ±8g range for industrial motors. 21Hz LPF removes digital noise above Nyquist for typical bearing defect frequencies. |
| `temperature_sensor.h/cpp` | Wraps DHT22. Guards `isnan()` (DHT22 min poll = 2s). Low Q=0.001 for temperature (changes slowly). | Minimum 3s between readings (extra margin above the 2s DHT22 limit). |
| `telemetry_formatter.h/cpp` | Builds JSON using ArduinoJson `StaticJsonDocument`. Returns empty string on allocation failure. | Schema isolation: one file change to add a field + bump schema_version. |
| `main.cpp` | Non-blocking cooperative loop. Three timers: VIB=500ms, TEMP=3000ms, PUBLISH=3000ms. Hardware watchdog resets device if loop stalls > 15s. | `delay()` never called in hot path — only in boot `connectWiFi()`. |
| `mqtt_client.cpp` | MQTT QoS-1 connection with LWT. Inbound command dispatch via `onCommand` callback. | Publishes `{status:online}` on connect; LWT publishes `{status:offline}` on disconnect. |

### Backend Layer (C#)

| File | What it does | Key decisions |
|---|---|---|
| `SensorReading.cs` | Immutable `record` with `init`-only fields. `HasVibration` / `HasTemperature` computed from nullable fields. | Value equality semantics free with records — useful for tests and channel deduplication. |
| `Device.cs` | Mutable entity. `IsStale` = `(UtcNow - LastSeenUtc).TotalSeconds > 30`. | Stale detection is pure logic, no DB query. |
| `Alert.cs` | Captures anomaly with ZScore, measured value, threshold, and acknowledgement lifecycle. | Acknowledgement is a separate state transition — not part of creation. |
| `AnalyticsEngine.cs` | `ConcurrentDictionary<string, MetricState>` keyed by `"{deviceId}:{metric}"`. Each state holds a `Queue<float>` rolling window (60 samples), sample count, and baseline snapshot. Z-score uses Bessel's correction (`n-1`). Two anomaly checks: Z-score thresholds + rate-of-change (configurable % jump). Background timer evicts stale entries (no activity for `StateEvictionSeconds`). | `lock(state)` per device — concurrent reads from different devices don't block each other. All thresholds are configurable via `AnalyticsEngineOptions` (no recompile needed). |
| `AlertService.cs` | Creates `Alert` entity, saves to repo, updates in-memory cooldown dict. 30s cooldown per `(deviceId, alertType)`. Uses `SemaphoreSlim(1,1)` for thread-safe cooldown updates. | Cooldown is in-memory — resets on restart. On restart, one alert fires immediately (acceptable — restart is operationally significant). |
| `DeviceService.cs` | In-memory `Dictionary<string, Device>` cache + async PostgreSQL upsert. Prevents DB query on every 2Hz heartbeat. `MarkOfflineAsync()` called when MQTT LWT is received. | Cache is single-process only. In multi-replica: replace with Redis. |
| `MqttMessageParser.cs` | Parses `ReadOnlySpan<byte>` directly (zero string allocation). Validates `schema_version`, rejects empty `device_id`. Logs first 200 chars of bad payload. | Isolated from connectivity — unit-testable with a byte array, no broker needed. |
| `MqttSubscriberService.cs` | `BackgroundService`. Connects `CleanSession=false` (retain QoS-1 queue). Subscribes `sensors/+/telemetry`. Exponential reconnect (2s…60s). Writes to `Channel<TelemetryMessage>`. | Knows nothing about business logic — only produces messages. Status topic (`sensors/+/status`) subscription pending for offline detection. |
| `IngestionWorker.cs` | Dequeues `TelemetryMessage`. Maps to `SensorReading`. Fire-and-forget device heartbeat. Writes to `Channel<SensorReading>`. | Two-stage pipeline: MQTT ingestion decoupled from DB/analytics latency. |
| `ProcessingWorker.cs` | Dequeues `SensorReading`. Runs analytics per metric. Fire-and-logs InfluxDB write. Evaluates alerts. `Task.WhenAll()` for concurrent SignalR broadcasts. Tracks consecutive InfluxDB failures. | InfluxDB failure does NOT block SignalR — dashboard stays live during persistence degradation. |
| `AuthController.cs` | `POST /api/auth/token { apiKey }` → 200 `{ token, expiresIn: 3600 }`. Validates against `Jwt:ApiKey` config. Issues HMAC-SHA256 JWT with 1h expiry. | API key → JWT exchange. Frontend stores token in sessionStorage and passes as Bearer + SignalR query param. |
| `AlertsController.cs` | CRUD for alerts. Keyset pagination (`before` cursor). Unacknowledged count endpoint. Bulk ACK. | **Known issue:** Missing `[Authorize]`. Bulk ACK uses N+1 queries — needs a single bulk `UPDATE`. |
| `DevicesController.cs` | Device registry CRUD + per-device alerts + analytics baseline reset. | **Known issue:** Missing `[Authorize]`. Reset-analytics is operationally sensitive. |
| `SensorController.cs` | Time-series history from InfluxDB. Device ID list from InfluxDB tag values. | **Known issue:** Missing `[Authorize]`. History Flux join fails when only one measurement type exists. |
| `SensorHub.cs` | SignalR hub with `[Authorize]`. All clients join "all" group. `SubscribeToDevice()` joins `device:{id}` group for filtered updates. | JWT passed as `?access_token=` query param (standard SignalR pattern, handled by JwtBearerEvents). |
| `GlobalExceptionMiddleware.cs` | Catches unhandled exceptions. Returns RFC 7807 Problem Details. Includes stack trace in Development; hides in Production. | Must be registered first in middleware pipeline. |
| `Program.cs` | Full DI wiring: two bounded channels, JWT auth, CORS with credentials, health checks (InfluxDB + PostgreSQL + channel pipeline), Swagger with Bearer security definition. | `ASPNETCORE_ENVIRONMENT=Development` is currently hardcoded in the Dockerfile — override with env var in production. |

### Frontend Layer (React 18)

| File | What it does | Key decisions |
|---|---|---|
| `useSignalR.js` | Creates `HubConnectionBuilder` with `accessTokenFactory: () => sessionStorage.getItem("iot_token")`. Retry schedule: `[0, 2000, 5000, 10000, 30000]`. Re-registers all handlers on reconnect. | Handlers in `handlersRef` (stable ref) — no connection recreate on re-render. Token is read on every connect/reconnect so refreshes are automatic. |
| `useSensorData.js` | Single source of truth. SignalR live stream → 120-point ring buffer per device. REST history fetch on device/window change with `AbortController` cancellation. Exposes: `liveReadings`, `history`, `alerts`, `devices`, `loading`, `ackAlert`. | `selectedRef` prevents stale closure in SignalR callback. Ring buffer avoids allocating a new array on every 2Hz frame. |
| `api.js` | Thin `fetch` wrapper. Reads `sessionStorage["iot_token"]` and adds `Authorization: Bearer`. Throws on non-2xx. `204` returns `null`. Base URL from `VITE_API_URL`. | No axios dependency — native fetch is sufficient. Token flow: `getToken(apiKey)` → store → all subsequent calls authenticated. |
| `Dashboard.jsx` | Main layout: sticky header + device grid + metric cards + charts + alerts panel. `ChartErrorBoundary` wraps both charts. Industrial dark aesthetic: `#060b12` bg, amber `#f59e0b` accent, IBM Plex Mono for data values. | `isAnimationActive={false}` on all charts — essential at 2Hz (animations cause frame drops). Clock display does not auto-update (known issue). |
| `VibrationChart.jsx` | `AreaChart` with custom `CustomDot` (red glow on anomaly frames). Moving average as dashed overlay. Baseline reference line. `connectNulls={true}` prevents gaps on partial data. | `useMemo` on data transformation. `isAnimationActive={false}`. |
| `TemperatureChart.jsx` | `LineChart` with dual Y axes (°C left, %RH right). Warning threshold reference line at 70°C. | |
| `AlertsPanel.jsx` | Severity filter tabs with unacked count badges. Per-alert: icon + device + type badge + message + metrics (value, baseline, Z-score) + ACK button. Acknowledged alerts at 45% opacity. | |
| `AnalysisOverlay.jsx` | Z-score gauge bar (0–4σ), moving average vs baseline comparison, sample count progress. | Threshold markers at 1.5σ, 2.5σ, 3.5σ. |
| `constants.js` | `API_BASE`, `SIGNALR_URL`, `MAX_LIVE_POINTS` (120), `HISTORY_WINDOWS`, `TEMP_WARNING_THRESHOLD` (70°C), `MAX_ALERTS_IN_MEMORY` (100). | All environment-sensitive values come from `import.meta.env`. |

---

## 4. How Everything Connects

```
┌──────────────────────────────────────────────────────────────────────┐
│  ESP32 boots                                                          │
│  → WiFi connect (exponential retry, WDT 15s hard reset)              │
│  → MQTT connect with LWT {status:offline}                            │
│  → Publish {status:online} to sensors/{id}/status (retained)         │
│  → Every 3s: TelemetryFormatter.build() → sensors/{id}/telemetry     │
└────────────────────────────┬─────────────────────────────────────────┘
                             │ MQTT QoS-1
┌────────────────────────────▼─────────────────────────────────────────┐
│  Mosquitto 2.x                                                        │
│  → Retains status messages (online / LWT offline)                     │
│  → Delivers telemetry to all subscribers with QoS-1 guarantee         │
└────────────────────────────┬─────────────────────────────────────────┘
                             │ MQTTnet subscription (sensors/+/telemetry)
┌────────────────────────────▼─────────────────────────────────────────┐
│  MqttSubscriberService                                                │
│  → MqttMessageParser.Parse() → validates JSON + schema_version        │
│  → Channel<TelemetryMessage>.WriteAsync()  [backpressure, cap 2000]   │
│               │                                                        │
│  IngestionWorker                                                       │
│  → DeviceService.RegisterHeartbeatAsync() → PostgreSQL Upsert         │
│  → MapToReading() → SensorReading domain entity                       │
│  → Channel<SensorReading>.WriteAsync()     [backpressure, cap 2000]   │
│               │                                                        │
│  ProcessingWorker                                                      │
│  → AnalyticsEngine.Evaluate() per metric → Z-score + ROC anomaly      │
│  → InfluxDbSensorRepo.WriteAsync()  [fire-and-log, non-blocking]      │
│  → AlertService.RaiseIfNeededAsync() → cooldown check → PostgreSQL    │
│  → SignalR.Clients.All.SendAsync("TelemetryReceived", dto)            │
│  → SignalR.Clients.All.SendAsync("AlertReceived", alert)              │
└────────────────────────────┬─────────────────────────────────────────┘
                             │ WebSocket (SignalR, JWT required)
┌────────────────────────────▼─────────────────────────────────────────┐
│  React Dashboard                                                       │
│  → POST /api/auth/token → get JWT → store sessionStorage              │
│  → SignalR connect with ?access_token=...                             │
│  → on("TelemetryReceived") → ring buffer → VibrationChart update      │
│  → on("AlertReceived")     → prepend to alerts → AlertsPanel update   │
│  → GET /api/devices        → device grid (on mount)                   │
│  → GET /api/sensors/{id}/history → historical chart on device select  │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 5. Authentication & Security

### Current Authentication State

| Component | Status | Notes |
|-----------|--------|-------|
| JWT configuration | ✅ Implemented | HMAC-SHA256, 1h expiry, issuer/audience validation |
| `POST /api/auth/token` | ✅ Implemented | API-key → JWT exchange |
| SignalR hub `[Authorize]` | ✅ Implemented | JWT passed as `?access_token=` |
| REST controllers `[Authorize]` | ❌ **Missing** | AlertsController, DevicesController, SensorsController open |
| Frontend login page | ❌ **Missing** | Token must be manually inserted |
| MQTT authentication | ❌ Dev only | `allow_anonymous true` — disable in production |

### Getting a JWT Token (Development)

```bash
# 1. Get a token (requires JWT_API_KEY in .env)
curl -X POST http://localhost:8080/api/auth/token \
  -H "Content-Type: application/json" \
  -d "{\"apiKey\":\"$(grep JWT_API_KEY .env | cut -d= -f2)\"}"
# → { "token": "eyJ...", "expiresIn": 3600 }

# 2. Use token for REST calls
TOKEN="eyJ..."
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/devices
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/alerts

# 3. Inject into browser (for SignalR)
# Open browser DevTools → Console:
sessionStorage.setItem("iot_token", "eyJ...");
# Refresh page — SignalR connects with the token
```

### Production Security Checklist

```
[ ] Add [Authorize] to AlertsController, DevicesController, SensorsController
[ ] Build frontend LoginPage that calls POST /api/auth/token
[ ] Remove ENV ASPNETCORE_ENVIRONMENT=Development from backend/Dockerfile
[ ] Remove ASPNETCORE_ENVIRONMENT: Development from docker-compose.yml backend env
[ ] Bind DB ports to 127.0.0.1 in docker-compose.yml (lines 57, 87)
[ ] Enable Mosquitto password_file + TLS on port 8883
[ ] Add nginx Content-Security-Policy, X-Frame-Options, X-Content-Type-Options headers
[ ] Replace appsettings.json dev credentials with environment-variable-only approach
[ ] Use strong random values for JWT_SECRET (>= 32 chars), JWT_API_KEY, all passwords
```

---

## 6. Step-by-Step Execution Guide

### Prerequisites

| Tool | Version | Purpose |
|---|---|---|
| Docker Desktop | 24+ | Run all infrastructure |
| Python 3.10+ | any | Sensor simulator |
| Node.js 20 LTS | 20.x | Frontend dev server |
| .NET SDK | 8.0.x | Backend development |
| PlatformIO | latest | ESP32 firmware (optional) |

---

### Step 1 — Configure environment

```bash
# Copy the template (never commit .env)
cp .env.example .env

# Edit .env — at minimum set:
#   JWT_SECRET=<random string, >= 32 characters>
#   JWT_API_KEY=<shared secret for dashboard clients>
#   All passwords (change from "changeme" defaults)
```

---

### Step 2 — Start infrastructure

```bash
bash scripts/setup.sh
```

This: checks Docker, runs `docker compose up -d --build` in `infra/`, waits for each service health check, prints access URLs.

**Or manually:**
```bash
cd infra
docker compose up -d
docker compose ps   # wait until all show "(healthy)"
```

**Expected:**
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
curl http://localhost:8086/health        # InfluxDB → {"status":"pass"}
curl http://localhost:8080/health        # Backend  → {"status":"Healthy"}
curl http://localhost:8080/health/ready  # All three checks Healthy
```

---

### Step 4 — Authenticate

```bash
# Get a JWT token
curl -X POST http://localhost:8080/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"apiKey":"your-jwt-api-key-from-env"}'
# → {"token":"eyJ...","expiresIn":3600}

export TOKEN="eyJ..."
```

Swagger UI (Development mode only): http://localhost:8080/swagger
- Click "Authorize" → paste the token

Dashboard: http://localhost:3000
- Open DevTools Console → `sessionStorage.setItem("iot_token", "eyJ...")`
- Refresh page → SignalR connects

---

### Step 5 — Start the sensor simulator

```bash
# Normal operation — 2 devices at 2 Hz
python3 data/simulation-scripts/simulate_sensors.py

# Example output:
# [unit-01] seq=1      rms=9.8142 m/s²  temp=42.1°C
# [unit-02] seq=1      rms=9.8098 m/s²  temp=41.9°C
```

Open http://localhost:3000 — unit-01 and unit-02 appear with live charts.

---

### Step 6 — Inject a fault and watch the dashboard

```bash
python3 data/simulation-scripts/simulate_sensors.py --fault-after 30
# After 30s:
# [unit-01] FAULT INJECTED at t=30.0s — RMS will escalate linearly
# [unit-01] seq=61     rms=44.2 m/s²  [FAULT rms+3.05]
```

Dashboard response:
- Vibration metric card border turns red, "⚠ ANOMALY DETECTED" label appears
- Red glowing dot on VibrationChart at the spike
- CRITICAL or FAULT alert in the Alerts panel
- "⚠ 1 UNACKED ALERT" in the header

---

### Step 7 — Acknowledge an alert

**Via dashboard:** Click the **ACK** button — alert fades to 45% opacity.

**Via API:**
```bash
# List alerts
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/alerts | python3 -m json.tool

# Acknowledge one
ALERT_ID="<uuid-from-above>"
curl -X POST \
  -H "Authorization: Bearer $TOKEN" \
  "http://localhost:8080/api/alerts/$ALERT_ID/acknowledge?acknowledgedBy=operator"

# Bulk acknowledge all
curl -X POST \
  -H "Authorization: Bearer $TOKEN" \
  "http://localhost:8080/api/alerts/acknowledge-all?acknowledgedBy=operator"
```

---

### Step 8 — Flash ESP32 (real hardware)

```bash
cd edge/firmware

# Edit platformio.ini build_flags or config.h:
#   WIFI_SSID, WIFI_PASSWORD, MQTT_HOST (your machine IP, not localhost)

pio run -e esp32dev -t upload
pio device monitor
```

Expected ESP32 serial output:
```
[BOOT] Firmware v1.2.0  device=unit-01  location=Assembly-Line-A
[WiFi] Connected  IP=192.168.1.50  RSSI=-62 dBm
[MQTT] Connected to 192.168.1.100:1883 (sessionPresent=false)
[VibrationSensor] OK — MPU6050 range=±8g, LPF=21Hz
[TemperatureSensor] OK — DHT22 pin 4
[TX] seq=1      rms=9.8142 m/s²  temp=42.1°C  287 B
```

---

### Step 9 — Run backend tests

```bash
cd backend
dotnet test --configuration Debug --logger "console;verbosity=detailed"
```

Expected:
```
Passed! - Failed: 0, Passed: 28, Skipped: 0, Total: 28
Tests:  AnalyticsEngineTests (7), AlertServiceTests (6), TopicsTests (9),
        SensorReadingTests (n), MqttMessageParserTests (6)
```

---

### Step 10 — Run frontend in dev mode

```bash
cd frontend
npm install
npm run dev         # → http://localhost:5173 (proxies /api and /hubs to :8080)
npm run lint        # ESLint check
npm run build       # Production bundle to dist/
```

---

### Step 11 — View logs

```bash
# All services
docker compose -f infra/docker-compose.yml logs -f

# Backend only (structured Serilog JSON)
docker compose -f infra/docker-compose.yml logs -f backend

# Live MQTT messages
docker exec iot-mosquitto mosquitto_sub -t "sensors/#" -v

# Grafana (optional monitoring)
docker compose -f infra/docker-compose.yml --profile monitoring up -d grafana
open http://localhost:3001   # admin / admin (change in .env)
```

---

### Teardown

```bash
cd infra
docker compose down         # stops services, keeps volumes
docker compose down -v      # stops + removes all data
```

---

## 7. API Reference

### Authentication

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/auth/token` | None | Exchange API key for JWT. Body: `{ "apiKey": "..." }` |

### Devices

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/devices` | Bearer | List all registered devices with status + firmware |
| GET | `/api/devices/{id}` | Bearer | Get single device |
| GET | `/api/devices/{id}/alerts?limit=50` | Bearer | Alerts for one device |
| POST | `/api/devices/{id}/reset-analytics` | Bearer | Reset Z-score baseline (use after maintenance) |

### Sensors (InfluxDB)

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/sensors/devices` | Bearer | Device IDs with data in InfluxDB |
| GET | `/api/sensors/{id}/history?minutes=60` | Bearer | Time-series history (1–1440 min) |

### Alerts (PostgreSQL)

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/alerts?limit=50&before={ts}` | Bearer | Recent alerts, keyset-paginated |
| GET | `/api/alerts/unacknowledged/count` | Bearer | Unacknowledged badge count |
| POST | `/api/alerts/{id}/acknowledge?acknowledgedBy=operator` | Bearer | Acknowledge one alert |
| POST | `/api/alerts/acknowledge-all?acknowledgedBy=operator` | Bearer | Bulk acknowledge all |

### Health

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/health` | None | Overall health (Healthy / Unhealthy) |
| GET | `/health/ready` | None | Readiness: influxdb + postgres + channel-pipeline |

### SignalR (WebSocket)

| Event | Direction | Payload |
|-------|-----------|---------|
| `TelemetryReceived` | Server → Client | `{ deviceId, timestamp, vibration, temperature, humidity, analysis: { vibration, temperature } }` |
| `AlertReceived` | Server → Client | `{ id, deviceId, severity, type, message, value, threshold, zScore, timestamp }` |
| `SubscribeToDevice(deviceId)` | Client → Server | Join device-scoped group |
| `UnsubscribeFromDevice(deviceId)` | Client → Server | Leave device-scoped group |

**Connection:** `ws://localhost:8080/hubs/sensors?access_token={jwt}`

### SignalR Event: TelemetryReceived (full shape)

```json
{
  "deviceId": "unit-01",
  "location": "Assembly-Line-A",
  "timestamp": "2026-06-17T10:30:00Z",
  "seq": 1234,
  "vibration": 9.8142,
  "accelX": 0.98, "accelY": 0.49, "accelZ": 9.81,
  "temperature": 42.3,
  "humidity": 61.2,
  "analysis": {
    "vibration": {
      "isAnomaly": false, "zScore": 0.234,
      "movingAvg": 9.812, "stdDev": 0.031,
      "baseline": 9.810, "severity": null, "reason": null
    },
    "temperature": {
      "isAnomaly": false, "zScore": 0.102,
      "movingAvg": 42.1, "stdDev": 0.28,
      "baseline": 42.0, "severity": null, "reason": null
    }
  }
}
```

---

## 8. Running Tests

```bash
# All tests (Debug mode — required on Windows due to Smart App Control)
cd backend && dotnet test --configuration Debug

# Specific test class
dotnet test --filter "FullyQualifiedName~AnalyticsEngineTests"
dotnet test --filter "FullyQualifiedName~AlertServiceTests"

# With coverage
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coverage-report"

# Frontend lint
cd frontend && npm run lint

# Firmware compile check
cd edge/firmware && pio run -e esp32dev
```

**Windows note:** Run `dotnet test` without `--configuration` (defaults to Debug). CI uses Release on Linux — this is intentional. Smart App Control on Windows 11 blocks unsigned Release DLLs locally.

---

## 9. Use Cases

### UC-1: Bearing Fault Detection

**Scenario:** Motor bearing degrades over weeks; vibration increases 15%.

1. `AnalyticsEngine` baseline established in first 300 samples (~2.5 min at 2 Hz)
2. As bearing wears, Z-score crosses 1.5σ → WARNING alert (30s cooldown limits storm)
3. Maintenance acknowledges, schedules inspection
4. Ignored: Z-score crosses 3.5σ → FAULT alert → immediate action required
5. After maintenance: `POST /api/devices/{id}/reset-analytics` resets baseline

### UC-2: Thermal Runaway

**Scenario:** Cooling fan fails; motor temperature rises 30°C over 10 minutes.

1. TemperatureChart warning line at 70°C gives visual early warning
2. Z-score detects temperature rising faster than baseline
3. Rate-of-change spike (>40% single-step) fires Critical alert
4. CRITICAL ThermalOverheat broadcast to all SignalR clients

### UC-3: Network Dropout Recovery

**Scenario:** Factory WiFi router reboots.

1. ESP32: exponential back-off reconnect (2s → 4s → … → 60s), WDT prevents permanent stall
2. Mosquitto: publishes LWT `{status:offline}` for device
3. Backend: device status transitions to Offline when status topic is subscribed *(pending fix — LWT subscription not yet implemented)*
4. On reconnect: ESP32 publishes `{status:online}`, device returns to Online

### UC-4: Alert Storm Suppression

**Scenario:** Bearing fault triggers 120 anomaly readings/minute.

1. `AlertService` 30s cooldown per `(deviceId, alertType)` → max 2 alerts/minute
2. Dashboard alert panel remains readable
3. All anomaly frames still broadcast via SignalR (chart shows all red dots)

### UC-5: Multiple Device Monitoring

**Scenario:** 10 devices on 3 assembly lines.

1. Device grid shows all devices with current status + last-seen
2. Select any device → history chart + live ring buffer scoped to that device
3. Alerts panel shows cross-device feed; filter by severity or device
4. `SubscribeToDevice(id)` on SignalR for per-device group (reduces noise)

---

## 10. Known Issues & Production Checklist

These items were identified during a full-stack audit on 2026-06-17.

### Critical (must fix before production)

| # | Issue | File | Fix |
|---|-------|------|-----|
| C1 | REST controllers missing `[Authorize]` | `AlertsController.cs`, `DevicesController.cs`, `SensorController.cs` | Add `[Authorize]` attribute to each controller class |
| C2 | `ASPNETCORE_ENVIRONMENT=Development` hardcoded in Dockerfile | `backend/Dockerfile:34` | Remove the `ENV` line; inject via docker-compose env only for dev |
| C3 | Same dev environment set in docker-compose backend service | `infra/docker-compose.yml:119` | Change to `Production` for prod override |
| C4 | No frontend login flow | `frontend/src/App.jsx` | Add `LoginPage` that calls `POST /api/auth/token` and stores token |

### High (fix before production)

| # | Issue | File | Fix |
|---|-------|------|-----|
| H1 | MQTT broker allows anonymous connections | `infra/mosquitto/mosquitto.conf:27` | `allow_anonymous false` + password_file + TLS on 8883 |
| H2 | PostgreSQL/InfluxDB ports on all interfaces | `docker-compose.yml:57,87` | `"127.0.0.1:5432:5432"` and `"127.0.0.1:8086:8086"` |
| H3 | No rate limiting on REST endpoints | All controllers | Add `Microsoft.AspNetCore.RateLimiting` middleware |

### Medium (fix in next sprint)

| # | Issue | File | Fix |
|---|-------|------|-----|
| M1 | Device offline never transitions automatically | `MqttSubscriberService.cs` | Subscribe to `sensors/+/status`; call `MarkOfflineAsync` on LWT |
| M2 | InfluxDB Flux inner join returns empty when one sensor type missing | `InfluxDbSensorRepository.cs:122` | Use separate queries or outer join approach |
| M3 | AcknowledgeAll N+1 queries | `AlertsController.cs:99` | Add `AcknowledgeAllAsync` with single bulk `UPDATE` SQL |
| M4 | DeviceService in-memory cache unbounded | `DeviceService.cs:19` | Replace `Dictionary` with `IMemoryCache` + sliding TTL |
| M5 | Header clock doesn't update | `Dashboard.jsx:237` | Add `setInterval` hook; update every second |
| M6 | `loadDevices` not polled — new devices invisible | `Dashboard.jsx:157` | Add 30s polling interval or SignalR `DeviceStatusChanged` event |
| M7 | Duplicate `PostgresHealthCheck` class (dead code) | `Program.cs:210-233` | Remove inline class; use Infrastructure version |
| M8 | No nginx security headers | `frontend/nginx.conf` | Add CSP, X-Frame-Options, X-Content-Type-Options |

### Low (backlog)

| # | Issue | Fix |
|---|-------|-----|
| L1 | `SIGNALR_URL` duplicated in `useSignalR.js` and `constants.js` | Import from `constants.js` in `useSignalR.js` |
| L2 | ESLint v9 `--ext .js,.jsx` flag is a no-op | Remove `--ext` from lint script in `package.json` |
| L3 | `acknowledgedBy` query param has no length validation | Add `if (acknowledgedBy.Length > 200) return BadRequest(...)` |
| L4 | Compiled `bin/` and `dist/` artifacts tracked in git | Add to `.gitignore`; run `git rm -r --cached backend/src/Api/bin` |
| L5 | `IoT_Dashboard_Complete_Report.docx` tracked in git root | Move to release artifacts or wiki |
| L6 | ESP32 command parsing uses `strstr` | Use ArduinoJson to parse command JSON properly |

---

## 11. Extending the System

### Add a new sensor (e.g., BMP280 pressure)

1. Add `PressureSensor` class in `edge/firmware/include/` + `src/sensors/`
2. Add `pressure` JSON field to firmware `telemetry_formatter.cpp`
3. Add `SensorType.PressurePa` to `Domain/Enums/SensorType.cs`
4. Add field to `Application/Models/TelemetryMessage.cs`
5. Map field in `IngestionWorker.MapToReading()`
6. Add field to `InfluxDbSensorRepository.WriteAsync()` point
7. Run analytics in `ProcessingWorker.ProcessAsync()` on new metric
8. Add `PressureChart.jsx` to frontend

### Add production authentication (partially implemented)

The backend JWT infrastructure is complete. Remaining work:

1. **Backend:** Add `[Authorize]` to `AlertsController`, `DevicesController`, `SensorsController`
2. **Frontend:** Build `LoginPage.jsx` that calls `getToken(apiKey)` from `api.js`, stores result in sessionStorage, and gates `Dashboard` behind a check
3. **MQTT:** Enable `password_file` in `mosquitto.conf`; provision credentials per device from NVS
4. **Firmware:** Set `MQTT_USERNAME` and `MQTT_PASSWORD` in `config.h` (from NVS in production)

### Scale to 100+ devices

1. **SignalR backplane:** Add `Microsoft.AspNetCore.SignalR.StackExchangeRedis` → enables horizontal backend scaling
2. **Message bus:** Swap `System.Threading.Channel` for Azure Service Bus or Kafka behind `IMessageBus` interface (one DI registration change)
3. **Device cache:** Replace `DeviceService` in-memory `Dictionary` with Redis (`IDistributedCache`)
4. **InfluxDB:** Configure continuous queries for 1h and 1d aggregates; set retention policies

### Add Grafana monitoring

```bash
# Already pre-configured — just enable the profile
docker compose --profile monitoring up -d grafana
open http://localhost:3001   # admin / admin (change GF_SECURITY_ADMIN_PASSWORD in .env)
```

Pre-wired InfluxDB datasource and IoT sensor dashboard are in `infra/grafana/provisioning/`.

### MQTT topic changes

Topics are the **contract** between firmware and backend. Always change both simultaneously:
- Backend: `backend/src/Shared/Constants/Topics.cs`
- Firmware: `edge/firmware/include/config.h` (`TOPIC_TELEMETRY`, `TOPIC_STATUS`, `TOPIC_COMMANDS`)

---

## Ports Quick Reference

| Service | URL | Notes |
|---------|-----|-------|
| Dashboard | http://localhost:3000 | React + nginx |
| Backend API | http://localhost:8080 | ASP.NET Core |
| Swagger (dev) | http://localhost:8080/swagger | Development mode only |
| InfluxDB | http://localhost:8086 | Admin UI |
| PostgreSQL | localhost:5432 | psql / DBeaver |
| MQTT | localhost:1883 | Use MQTT Explorer |
| MQTT WS | localhost:9001 | Browser debugging |
| Grafana | http://localhost:3001 | `--profile monitoring` |

---

*Last updated: 2026-06-17 — post full-stack audit. See `docs/failure-scenarios.md` for complete failure mode analysis and `docs/decisions.md` for Architecture Decision Records.*
