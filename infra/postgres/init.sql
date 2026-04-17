-- ─── Industrial IoT Dashboard — PostgreSQL Schema ───────────────────────────
-- Executed once on first container start via Docker entrypoint.
-- InfluxDB handles time-series data; Postgres owns relational data.

CREATE EXTENSION IF NOT EXISTS "pgcrypto";   -- gen_random_uuid()

-- ─── devices ─────────────────────────────────────────────────────────────────
-- Tracks every edge device that has ever connected.
-- Upserted on every telemetry heartbeat by DeviceService.

CREATE TABLE IF NOT EXISTS devices (
    id               TEXT         PRIMARY KEY,
    location         TEXT         NOT NULL DEFAULT '',
    firmware_version TEXT         NOT NULL DEFAULT '',
    status           TEXT         NOT NULL DEFAULT 'Unknown'
                                  CHECK (status IN ('Unknown','Online','Offline','Degraded')),
    last_seen_utc    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    registered_utc   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_devices_status       ON devices (status);
CREATE INDEX IF NOT EXISTS idx_devices_last_seen    ON devices (last_seen_utc DESC);

-- ─── alerts ──────────────────────────────────────────────────────────────────
-- Anomaly events raised by the AnalyticsEngine.
-- Indexed for fast per-device and per-severity queries.

CREATE TABLE IF NOT EXISTS alerts (
    id               UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    device_id        TEXT         NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    severity         TEXT         NOT NULL
                                  CHECK (severity IN ('Info','Warning','Critical','Fault')),
    type             TEXT         NOT NULL,
    message          TEXT         NOT NULL,
    measured_value   DOUBLE PRECISION NOT NULL,
    threshold_value  DOUBLE PRECISION NOT NULL DEFAULT 0,
    z_score          DOUBLE PRECISION NOT NULL DEFAULT 0,
    timestamp        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    acknowledged     BOOLEAN      NOT NULL DEFAULT FALSE,
    acknowledged_at  TIMESTAMPTZ,
    acknowledged_by  TEXT
);

CREATE INDEX IF NOT EXISTS idx_alerts_device_id  ON alerts (device_id, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_alerts_severity   ON alerts (severity, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_alerts_unacked    ON alerts (acknowledged, timestamp DESC)
    WHERE acknowledged = FALSE;

-- ─── Seed: test device (removed in production) ───────────────────────────────
-- Only exists in development so the UI shows something before hardware is ready.
INSERT INTO devices (id, location, firmware_version, status)
VALUES ('unit-01', 'Assembly-Line-A', '1.2.0', 'Offline')
ON CONFLICT (id) DO NOTHING;

INSERT INTO devices (id, location, firmware_version, status)
VALUES ('unit-02', 'Assembly-Line-B', '1.2.0', 'Offline')
ON CONFLICT (id) DO NOTHING;
