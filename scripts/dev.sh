#!/usr/bin/env bash
# dev.sh — Developer shortcut commands
#
# Usage:
#   bash scripts/dev.sh <command>
#
# Commands:
#   up          Start the full Docker stack (builds if needed)
#   down        Stop and remove containers (preserves volumes)
#   reset       Stop, remove containers AND volumes (wipes all data)
#   logs        Tail logs for all services
#   logs-back   Tail backend logs only
#   test        Run all backend tests
#   test-watch  Run tests in watch mode
#   sim         Start the sensor simulator
#   fault       Start simulator and inject fault after 30s on unit-01
#   seed        Seed development fixtures into PostgreSQL
#   psql        Open psql console on the iotdb database
#   influx      Open InfluxDB CLI
#   mqtt-watch  Subscribe to all MQTT topics and print messages
#   build-fw    Build ESP32 firmware (dev profile)
#   flash-fw    Build and flash ESP32 firmware
#   monitor-fw  Open serial monitor for ESP32

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."   # always run from repo root

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'

COMPOSE="docker compose -f infra/docker-compose.yml -f infra/docker-compose.override.yml"
PGCONN="docker exec iot-postgres psql -U iot -d iotdb"

usage() {
  echo -e "${CYAN}Usage:${NC} bash scripts/dev.sh <command>"
  echo ""
  echo "  ${GREEN}up${NC}          Start full Docker stack"
  echo "  ${GREEN}down${NC}        Stop containers (keep volumes)"
  echo "  ${GREEN}reset${NC}       Stop containers + wipe all volumes"
  echo "  ${GREEN}logs${NC}        Tail all service logs"
  echo "  ${GREEN}logs-back${NC}   Tail backend logs"
  echo "  ${GREEN}test${NC}        Run all backend tests"
  echo "  ${GREEN}test-watch${NC}  Run tests in watch mode"
  echo "  ${GREEN}sim${NC}         Start sensor simulator"
  echo "  ${GREEN}fault${NC}       Start simulator with fault injection at 30s"
  echo "  ${GREEN}seed${NC}        Seed PostgreSQL with test data"
  echo "  ${GREEN}psql${NC}        Open PostgreSQL console"
  echo "  ${GREEN}influx${NC}      Open InfluxDB CLI"
  echo "  ${GREEN}mqtt-watch${NC}  Watch all MQTT messages"
  echo "  ${GREEN}build-fw${NC}    Build ESP32 firmware (dev)"
  echo "  ${GREEN}flash-fw${NC}    Build + flash to connected ESP32"
  echo "  ${GREEN}monitor-fw${NC}  Open serial monitor"
  echo ""
}

case "${1:-help}" in

  up)
    echo -e "${GREEN}[dev]${NC} Starting Docker stack..."
    $COMPOSE up -d --build
    echo ""
    echo "  Dashboard  → http://localhost:3000"
    echo "  API        → http://localhost:8080/swagger"
    echo "  InfluxDB   → http://localhost:8086"
    ;;

  down)
    echo -e "${YELLOW}[dev]${NC} Stopping containers..."
    $COMPOSE down
    ;;

  reset)
    echo -e "${RED}[dev]${NC} Stopping containers and wiping volumes..."
    read -r -p "This deletes ALL data. Are you sure? [y/N] " confirm
    [[ "$confirm" =~ ^[Yy]$ ]] || exit 0
    $COMPOSE down -v
    echo "Done. Run 'bash scripts/dev.sh up' to restart."
    ;;

  logs)
    $COMPOSE logs -f --tail=50
    ;;

  logs-back)
    docker logs iot-backend -f --tail=100
    ;;

  test)
    echo -e "${GREEN}[dev]${NC} Running backend tests..."
    cd backend
    dotnet test --logger "console;verbosity=normal"
    ;;

  test-watch)
    cd backend
    dotnet watch test
    ;;

  sim)
    echo -e "${GREEN}[dev]${NC} Starting sensor simulator..."
    python3 data/simulation-scripts/simulate_sensors.py "${@:2}"
    ;;

  fault)
    echo -e "${YELLOW}[dev]${NC} Starting simulator — fault on unit-01 at t=30s..."
    python3 data/simulation-scripts/simulate_sensors.py --fault-after 30
    ;;

  seed)
    echo -e "${GREEN}[dev]${NC} Seeding test data..."
    bash scripts/seed-data.sh
    ;;

  psql)
    $PGCONN
    ;;

  influx)
    docker exec -it iot-influxdb influx \
      --token "dev-token-change-in-production" \
      --org  "iot-org"
    ;;

  mqtt-watch)
    echo -e "${GREEN}[dev]${NC} Subscribing to sensors/#..."
    docker exec iot-mosquitto mosquitto_sub -h localhost -t "sensors/#" -v -F "%t: %p"
    ;;

  build-fw)
    echo -e "${GREEN}[dev]${NC} Building ESP32 firmware..."
    cd edge/firmware
    pio run -e esp32dev
    ;;

  flash-fw)
    echo -e "${GREEN}[dev]${NC} Building and flashing ESP32 firmware..."
    cd edge/firmware
    pio run -e esp32dev -t upload
    ;;

  monitor-fw)
    cd edge/firmware
    pio device monitor
    ;;

  help|--help|-h)
    usage
    ;;

  *)
    echo -e "${RED}[dev]${NC} Unknown command: $1"
    usage
    exit 1
    ;;

esac
