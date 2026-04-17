# System Architecture

## Overview

Industrial IoT predictive maintenance system for monitoring rotating equipment.
Detects bearing faults, thermal overloads, and vibration anomalies in real time.

```
┌─────────────────────────────────────────────────────────────────────┐
│  EDGE LAYER  (ESP32 + C++ / PlatformIO)                             │
│                                                                     │
│  MPU6050 ──► Kalman(×6) ──┐                                        │
│  DHT22   ──► Kalman(×2) ──┴──► TelemetryFormatter ──► MQTT Pub    │
│                                 schema_version: 1                   │
│  Watchdog timer (15s)   Non-blocking loop (no delay() hot path)    │
│  LWT: {status:offline}  Exponential back-off reconnect (2→60s)     │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ MQTT/TCP  QoS-0
                               ▼
┌─────────────────────────────────────────────────────────────────────┐
│  BROKER  (Mosquitto 2.x)                                            │
│  Topics:                                                            │
│    sensors/+/telemetry   ← edge publishes, backend subscribes       │
│    sensors/+/status      ← LWT + online announce (retained)         │
│    devices/+/commands    ← backend → edge (OTA, rate change, reset) │
│    backend/status        ← backend LWT                              │
│  Persistence: yes  |  QoS-1 buffer: CleanSession=false             │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ MQTT subscription (wildcard)
                               ▼
┌─────────────────────────────────────────────────────────────────────┐
│  BACKEND  (.NET 8 / Clean Architecture)                             │
│                                                                     │
│  MqttSubscriberService                                              │
│    └─ MqttMessageParser  (schema validate, JSON → TelemetryMessage) │
│         └─ Channel<TelemetryMessage>  capacity=2000, Wait mode      │
│              │                                                      │
│  IngestionWorker                                                    │
│    ├─ DeviceService.RegisterHeartbeatAsync() ──► PostgreSQL upsert  │
│    └─ Map → SensorReading                                           │
│         └─ Channel<SensorReading>  capacity=2000                    │
│              │                                                      │
│  ProcessingWorker                                                   │
│    ├─ AnalyticsEngine.Evaluate()  (Z-score + ROC per device/metric) │
│    ├─ InfluxDB write  (fire-and-forget — does not block broadcast)  │
│    ├─ AlertService.RaiseIfNeededAsync()  (30s cooldown dedup)       │
│    │    └─ PostgreSQL INSERT alerts                                  │
│    └─ SignalR broadcast  TelemetryReceived + AlertReceived           │
│                                                                     │
│  REST API  (ASP.NET Core)                                           │
│    GET  /api/sensors/devices          → PostgreSQL                  │
│    GET  /api/sensors/{id}/history     → InfluxDB (Flux)             │
│    GET  /api/alerts                   → PostgreSQL                  │
│    POST /api/alerts/{id}/acknowledge  → PostgreSQL                  │
│    GET  /health                       → InfluxDB + PostgreSQL ping  │
│                                                                     │
│  ┌──────────────────────┐  ┌───────────────────────┐               │
│  │  InfluxDB 2.x        │  │  PostgreSQL 16        │               │
│  │  sensor readings     │  │  devices + alerts     │               │
│  │  vibration +         │  │  ACK workflow         │               │
│  │  temperature buckets │  │  FK constraints       │               │
│  └──────────────────────┘  └───────────────────────┘               │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ SignalR WebSocket + REST
                               ▼
┌─────────────────────────────────────────────────────────────────────┐
│  FRONTEND  (React 18 + Recharts + SignalR client)                   │
│                                                                     │
│  useSignalR()      ← manages hub connection + auto-reconnect        │
│  useSensorData()   ← live ring buffer (120 pts) + REST history     │
│                                                                     │
│  Dashboard         ← device selector, metric cards, view toggle     │
│  VibrationChart    ← AreaChart + anomaly dots + moving avg line     │
│  TemperatureChart  ← Dual-axis LineChart + warning reference line   │
│  AlertsPanel       ← Severity filter tabs + ACK button             │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Layer Dependency Rules (Clean Architecture)

```
Domain          ← no dependencies (pure C# records and enums)
Application     ← depends on Domain only (interfaces + services)
Infrastructure  ← implements Application interfaces (InfluxDB, Postgres, MQTT)
Api             ← wires everything (Program.cs DI container)
Worker          ← depends on Application interfaces only
```

Breaking these rules is a build error — cross-layer imports are prevented by
project structure (not enforced by compiler here, but would be in a multi-project
solution with explicit `<ProjectReference>` restrictions).

---

## Analytics Engine Detail

```
Per (deviceId, metric) — isolated state, independent lock

Sample arrives
    │
    ├── Rate-of-change check (first)
    │   delta = |value - lastValue| / |lastValue|
    │   if delta > 40% → CRITICAL "Rate-of-change spike"
    │
    ├── Update rolling window (FIFO queue, capacity 60)
    │
    ├── Compute statistics (Bessel-corrected stddev)
    │   μ  = Σ(window) / n
    │   σ  = √(Σ(x - μ)² / (n-1))
    │   z  = |value - μ| / σ
    │
    ├── Snapshot baseline at sample #300 (≈2.5 min at 2 Hz)
    │
    └── Classify
        z < 1.5σ  → normal
        z ≥ 1.5σ  → WARNING
        z ≥ 2.5σ  → CRITICAL
        z ≥ 3.5σ  → FAULT
```

---

## Data Storage Strategy

| Data Type | Store | Reason |
|---|---|---|
| Raw sensor readings | InfluxDB | Optimised for time-series writes; automatic downsampling |
| Device registry | PostgreSQL | Relational; FK to alerts; small dataset |
| Alert events | PostgreSQL | ACK workflow needs UPDATE; FK to devices; full-text search |
| Analytics state | In-memory | Fast; ephemeral; resets on restart (acceptable) |
| Alert cooldown state | In-memory | Same as above |

---

## Concurrency Model

```
Thread 1: MqttSubscriberService
  Receives MQTT callbacks on MQTTnet threadpool
  Writes to Channel<TelemetryMessage> (thread-safe)

Thread 2: IngestionWorker (BackgroundService)
  ReadAllAsync from Channel<TelemetryMessage>
  Single reader — no lock needed on channel
  Writes to Channel<SensorReading>

Thread 3: ProcessingWorker (BackgroundService)
  ReadAllAsync from Channel<SensorReading>
  Single reader
  Calls AnalyticsEngine (ConcurrentDictionary + per-key lock)
  Fires async tasks concurrently (WhenAll)

ASP.NET Core threadpool: handles HTTP requests
  Reads from PostgreSQL / InfluxDB
  No shared mutable state with workers (DI singleton repos are thread-safe)
```

---

## Scalability Path

| Current | Scaled |
|---|---|
| System.Threading.Channel | Apache Kafka (partition by deviceId) |
| In-memory device cache | Redis (shared across replicas) |
| Single ProcessingWorker | One worker per Kafka partition |
| Single backend instance | Horizontal scaling + SignalR Redis backplane |
| InfluxDB single node | InfluxDB Clustered or TimescaleDB |
