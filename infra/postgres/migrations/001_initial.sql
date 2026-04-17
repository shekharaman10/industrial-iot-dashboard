-- ─── Migration 001 — Initial Schema ─────────────────────────────────────────
-- Applied by: infra/postgres/init.sql (Docker entrypoint on first run)
-- For production, apply this via Flyway / Liquibase / dbmate.
--
-- Run manually:
--   psql -h localhost -U iot -d iotdb -f infra/postgres/migrations/001_initial.sql

CREATE EXTENSION IF NOT EXISTS "pgcrypto";   -- gen_random_uuid()

-- ─── Migration tracking table ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS schema_migrations (
    version     TEXT        PRIMARY KEY,
    applied_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ─── devices ──────────────────────────────────────────────────────────────────
-- Upserted on every telemetry heartbeat by DeviceService.
-- source of truth for device metadata and online/offline status.

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

COMMENT ON TABLE  devices               IS 'Edge device registry — upserted on every heartbeat';
COMMENT ON COLUMN devices.id            IS 'Device identifier — matches DEVICE_ID in firmware config.h';
COMMENT ON COLUMN devices.status        IS 'Online|Offline|Degraded|Unknown';
COMMENT ON COLUMN devices.last_seen_utc IS 'Timestamp of most recent telemetry frame';

-- ─── alerts ───────────────────────────────────────────────────────────────────
-- Anomaly events raised by AnalyticsEngine via AlertService.
-- Three key indexes covering the main query patterns.

CREATE TABLE IF NOT EXISTS alerts (
    id               UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    device_id        TEXT         NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    severity         TEXT         NOT NULL
                                  CHECK (severity IN ('Info','Warning','Critical','Fault')),
    type             TEXT         NOT NULL,   -- VibrationSpike, ThermalOverheat, etc.
    message          TEXT         NOT NULL,
    measured_value   DOUBLE PRECISION NOT NULL,
    threshold_value  DOUBLE PRECISION NOT NULL DEFAULT 0,
    z_score          DOUBLE PRECISION NOT NULL DEFAULT 0,
    timestamp        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    acknowledged     BOOLEAN      NOT NULL DEFAULT FALSE,
    acknowledged_at  TIMESTAMPTZ,
    acknowledged_by  TEXT
);

-- Per-device time-series queries (AlertsController GET /api/devices/{id}/alerts)
CREATE INDEX IF NOT EXISTS idx_alerts_device_id
    ON alerts (device_id, timestamp DESC);

-- Severity filter queries (AlertsPanel filter tabs)
CREATE INDEX IF NOT EXISTS idx_alerts_severity
    ON alerts (severity, timestamp DESC);

-- Unacknowledged count query (badge in dashboard header)
-- Partial index — only indexes rows that matter for the count
CREATE INDEX IF NOT EXISTS idx_alerts_unacked
    ON alerts (acknowledged, timestamp DESC)
    WHERE acknowledged = FALSE;

COMMENT ON TABLE  alerts                 IS 'Anomaly events from the analytics pipeline';
COMMENT ON COLUMN alerts.measured_value  IS 'The sensor value at the moment of alert';
COMMENT ON COLUMN alerts.threshold_value IS 'Baseline value for context in alert message';
COMMENT ON COLUMN alerts.z_score         IS 'Z-score at alert time for severity calibration';

-- ─── Development seed data ────────────────────────────────────────────────────
-- Only inserted on first run. Removed in production via environment flag.

INSERT INTO devices (id, location, firmware_version, status, registered_utc)
VALUES
    ('unit-01', 'Assembly-Line-A', '1.2.0', 'Offline', NOW()),
    ('unit-02', 'Assembly-Line-B', '1.2.0', 'Offline', NOW()),
    ('unit-03', 'CNC-Station-1',   '1.1.0', 'Offline', NOW())
ON CONFLICT (id) DO NOTHING;

-- ─── Record this migration ────────────────────────────────────────────────────
INSERT INTO schema_migrations (version) VALUES ('001_initial')
ON CONFLICT (version) DO NOTHING;
