#!/usr/bin/env bash
# seed-data.sh
# Inserts development test fixtures into PostgreSQL and verifies InfluxDB is writable.
# Run AFTER docker compose is healthy.
#
# Usage:
#   bash scripts/seed-data.sh
#   bash scripts/seed-data.sh --reset   # drops all data first

set -euo pipefail

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; RED='\033[0;31m'; NC='\033[0m'
log()  { echo -e "${GREEN}[seed]${NC}  $*"; }
warn() { echo -e "${YELLOW}[warn]${NC}  $*"; }
fail() { echo -e "${RED}[fail]${NC}  $*"; exit 1; }

PG_HOST="${PG_HOST:-localhost}"
PG_PORT="${PG_PORT:-5432}"
PG_DB="${PG_DB:-iotdb}"
PG_USER="${PG_USER:-iot}"
PG_PASS="${PG_PASS:-iot_secret}"

INFLUX_URL="${INFLUX_URL:-http://localhost:8086}"
INFLUX_TOKEN="${INFLUX_TOKEN:-dev-token-change-in-production}"
INFLUX_ORG="${INFLUX_ORG:-iot-org}"
INFLUX_BUCKET="${INFLUX_BUCKET:-sensor-readings}"

export PGPASSWORD="$PG_PASS"

# ── Verify postgres connection ─────────────────────────────────────────────────
log "Checking PostgreSQL connection..."
psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d "$PG_DB" -c "SELECT 1;" > /dev/null 2>&1 \
    || fail "Cannot connect to PostgreSQL at $PG_HOST:$PG_PORT"
log "PostgreSQL OK"

# ── Optional reset ─────────────────────────────────────────────────────────────
if [[ "${1:-}" == "--reset" ]]; then
    warn "Resetting all data..."
    psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d "$PG_DB" << 'SQL'
        TRUNCATE alerts RESTART IDENTITY CASCADE;
        TRUNCATE devices RESTART IDENTITY CASCADE;
SQL
    log "Data cleared"
fi

# ── Seed devices ──────────────────────────────────────────────────────────────
log "Seeding devices..."
psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d "$PG_DB" << 'SQL'
INSERT INTO devices (id, location, firmware_version, status, last_seen_utc, registered_utc)
VALUES
    ('unit-01', 'Assembly-Line-A', '1.2.0', 'Offline', NOW() - INTERVAL '5 minutes', NOW() - INTERVAL '30 days'),
    ('unit-02', 'Assembly-Line-B', '1.2.0', 'Offline', NOW() - INTERVAL '3 minutes', NOW() - INTERVAL '25 days'),
    ('unit-03', 'CNC-Station-1',   '1.1.0', 'Offline', NOW() - INTERVAL '2 hours',   NOW() - INTERVAL '10 days')
ON CONFLICT (id) DO UPDATE SET
    location         = EXCLUDED.location,
    firmware_version = EXCLUDED.firmware_version;
SQL
log "Devices seeded"

# ── Seed sample alerts ────────────────────────────────────────────────────────
log "Seeding sample alerts..."
psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d "$PG_DB" << 'SQL'
INSERT INTO alerts
    (id, device_id, severity, type, message, measured_value, threshold_value, z_score, timestamp, acknowledged)
VALUES
    (gen_random_uuid(), 'unit-01', 'Warning',  'VibrationSpike',    'Z-score 1.8σ exceeds warning threshold (1.5σ).', 11.23, 9.81, 1.8, NOW() - INTERVAL '10 minutes', false),
    (gen_random_uuid(), 'unit-01', 'Critical', 'RateOfChangeSpike', 'Rate-of-change spike: >40% change in one step.',  19.45, 9.81, 4.2, NOW() - INTERVAL '8 minutes',  false),
    (gen_random_uuid(), 'unit-02', 'Warning',  'ThermalOverheat',   'Z-score 1.6σ ≥ 1.5σ — elevated reading.',        66.3,  42.0, 1.6, NOW() - INTERVAL '5 minutes',  false),
    (gen_random_uuid(), 'unit-01', 'Fault',    'VibrationSpike',    'Z-score 4.1σ ≥ 3.5σ — potential equipment failure.', 50.12, 9.81, 4.1, NOW() - INTERVAL '2 minutes', false),
    (gen_random_uuid(), 'unit-03', 'Warning',  'VibrationSpike',    'Z-score 1.7σ ≥ 1.5σ — elevated reading.',        12.4,  9.81, 1.7, NOW() - INTERVAL '1 hour',     true)
ON CONFLICT DO NOTHING;
SQL
log "Alerts seeded"

# ── Verify InfluxDB connectivity ───────────────────────────────────────────────
log "Checking InfluxDB..."
HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$INFLUX_URL/health")
if [[ "$HTTP_STATUS" == "200" ]]; then
    log "InfluxDB OK"
else
    warn "InfluxDB health returned HTTP $HTTP_STATUS — check container status"
fi

# ── Summary ───────────────────────────────────────────────────────────────────
DEVICE_COUNT=$(psql -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d "$PG_DB" -tAc "SELECT COUNT(*) FROM devices;")
ALERT_COUNT=$(psql  -h "$PG_HOST" -p "$PG_PORT" -U "$PG_USER" -d "$PG_DB" -tAc "SELECT COUNT(*) FROM alerts;")

echo ""
echo -e "${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${GREEN} Seed complete${NC}"
echo -e "${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo "  Devices : $DEVICE_COUNT"
echo "  Alerts  : $ALERT_COUNT"
echo ""
echo "  Start the simulator to generate live data:"
echo "    python3 data/simulation-scripts/simulate_sensors.py"
echo ""
