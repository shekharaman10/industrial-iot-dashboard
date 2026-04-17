# Architecture Decision Records (ADR)

Each decision includes: **Context → Decision → Consequences → Alternatives rejected**.

---

## ADR-001 — Two Bounded Channels Instead of One

**Context:** MQTT messages arrive at 2 Hz per device. Processing (analytics + DB write) takes variable time depending on InfluxDB latency. A single channel from MQTT → processing risks backpressure starving the MQTT subscriber.

**Decision:** Two channels: `MqttChannel` (MQTT → Ingestion) and `ProcessingChannel` (Ingestion → Processing). Each has capacity 2000.

**Consequences:** Ingestion (JSON parsing, device heartbeat) is decoupled from processing (analytics, DB). If InfluxDB is slow, only the ProcessingChannel fills; MQTT ingestion continues at full rate until capacity is hit.

**Alternatives rejected:**
- Single channel: simpler but couples connectivity to processing latency.
- RabbitMQ/Kafka: correct at scale but adds operational overhead for a single-node deployment. Abstracted behind `IMessageBus` so swap is one line.

---

## ADR-002 — InfluxDB + PostgreSQL (Not One Database)

**Context:** Two distinct data patterns: (1) continuous time-series writes at 2 Hz per device, (2) relational alert records with ACK state and device FK constraints.

**Decision:** InfluxDB for time-series; PostgreSQL for alerts + device registry.

**Consequences:**
- InfluxDB handles millions of writes/second; PostgreSQL handles relational queries.
- Two connection pools to manage.
- Flux query language for InfluxDB (learning curve).

**Alternatives rejected:**
- TimescaleDB only: PostgreSQL with time-series extension. Good but Flux downsampling tasks in InfluxDB are simpler.
- InfluxDB only: alert ACK workflow needs UPDATE + WHERE; InfluxDB is append-only.

---

## ADR-003 — Statistical Anomaly Detection (No ML)

**Context:** Predictive maintenance systems are often "enhanced" with ML in demos. Real industrial ML requires months of labeled failure data, a training pipeline, model versioning, and inference infrastructure.

**Decision:** Z-score on a 60-sample rolling window + rate-of-change threshold. No ML.

**Consequences:**
- Deterministic: same input always produces same output. Auditable in seconds.
- No training data required — works from the first 10 samples.
- Cannot detect complex multi-variate failure patterns (requires ML).

**Alternatives rejected:**
- Isolation Forest: requires scikit-learn sidecar or ONNX runtime. Added complexity for marginal gain without labeled data.
- LSTM: requires weeks of training data, GPU, model serving infrastructure.

---

## ADR-004 — Kalman Filter on Edge (Not Moving Average)

**Context:** MPU6050 has significant measurement noise. Options: moving average, exponential smoothing, Kalman filter.

**Decision:** Kalman filter on the ESP32.

**Consequences:**
- Zero phase lag (moving average delays by window/2 samples).
- Optimal for Gaussian sensor noise (MPU6050 datasheet noise density matches Gaussian assumption).
- Slightly more CPU than moving average (3 float multiplications per sample — negligible on ESP32).

**Alternatives rejected:**
- Moving average (N=5): introduces 2.5-sample lag, potentially masking fast transients relevant to fault detection.
- Send raw data, filter on backend: doubles transmission bandwidth; wastes WiFi radio power.

---

## ADR-005 — Alert Deduplication with Cooldown Window

**Context:** An anomaly condition (e.g., sustained vibration) will trigger a new alert on every analytics cycle (500ms). Without deduplication, the alert panel floods with hundreds of identical alerts per minute.

**Decision:** `AlertService` maintains a per-(device, type) timestamp. Alerts suppressed if raised within 30 seconds of the previous one.

**Consequences:**
- Alert panel remains readable.
- A brief recovery followed by immediate re-fault within 30s produces only one alert.

**Alternatives rejected:**
- State machine (NORMAL → ALERTING → RECOVERY): more accurate but requires tracking per-metric state, complex to implement correctly. Added to backlog for v2.

---

## ADR-006 — Dapper Over EF Core for Repositories

**Context:** Three simple tables: devices, alerts (two repos). Choice between Dapper (micro-ORM) and Entity Framework Core.

**Decision:** Dapper.

**Consequences:**
- SQL is explicit and visible — no hidden N+1 queries, no lazy-load surprises.
- No migration tooling (schema managed by `init.sql`).
- Less type safety on raw SQL strings.

**Alternatives rejected:**
- EF Core: adds value when you have complex navigation properties, LINQ queries, or change tracking. None of these apply here.
