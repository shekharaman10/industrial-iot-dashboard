#!/usr/bin/env bash
# setup.sh — One-command dev environment setup
# Usage: bash scripts/setup.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'

log()  { echo -e "${GREEN}[setup]${NC} $*"; }
warn() { echo -e "${YELLOW}[warn]${NC}  $*"; }
fail() { echo -e "${RED}[fail]${NC}  $*"; exit 1; }

# ── Dependency checks ──────────────────────────────────────────────────────────
log "Checking dependencies..."

command -v docker      >/dev/null 2>&1 || fail "Docker not found. Install from https://docs.docker.com/get-docker/"
command -v docker-compose >/dev/null 2>&1 || command -v docker compose >/dev/null 2>&1 || \
    fail "docker-compose not found."
command -v python3     >/dev/null 2>&1 || warn "python3 not found — simulator won't run without it."
command -v pip3        >/dev/null 2>&1 || warn "pip3 not found — install paho-mqtt manually."

log "All core dependencies found."

# ── Python simulator dependencies ─────────────────────────────────────────────
if command -v pip3 >/dev/null 2>&1; then
    log "Installing Python dependencies for simulator..."
    pip3 install paho-mqtt --quiet
fi

# ── Create required directories ───────────────────────────────────────────────
log "Ensuring Mosquitto data directory exists..."
mkdir -p "$ROOT_DIR/infra/mosquitto/data"

# ── Start infrastructure ──────────────────────────────────────────────────────
log "Starting Docker services..."
cd "$ROOT_DIR/infra"

docker compose down --remove-orphans 2>/dev/null || true
docker compose up -d --build

# ── Wait for health checks ────────────────────────────────────────────────────
log "Waiting for services to be healthy..."

wait_healthy() {
    local name=$1
    local max_wait=${2:-60}
    local elapsed=0
    while [ $elapsed -lt $max_wait ]; do
        status=$(docker inspect --format='{{.State.Health.Status}}' "iot-$name" 2>/dev/null || echo "starting")
        if [ "$status" = "healthy" ]; then
            log "$name is healthy ✓"
            return 0
        fi
        sleep 3
        elapsed=$((elapsed + 3))
        echo -n "."
    done
    fail "$name did not become healthy within ${max_wait}s"
}

echo ""
wait_healthy "mosquitto" 30
wait_healthy "influxdb"  60
wait_healthy "postgres"  30
wait_healthy "backend"   90

# ── Print access URLs ──────────────────────────────────────────────────────────
echo ""
echo -e "${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${GREEN} Industrial IoT Dashboard — All services running${NC}"
echo -e "${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""
echo "  Dashboard       →  http://localhost:3000"
echo "  API (Swagger)   →  http://localhost:8080/swagger"
echo "  InfluxDB UI     →  http://localhost:8086"
echo "  MQTT broker     →  localhost:1883"
echo ""
echo "  To run the sensor simulator:"
echo "    python3 data/simulation-scripts/simulate_sensors.py"
echo ""
echo "  To inject a fault after 30s:"
echo "    python3 data/simulation-scripts/simulate_sensors.py --fault-after 30"
echo ""
echo "  To view logs:"
echo "    docker compose -f infra/docker-compose.yml logs -f backend"
echo ""
