#!/usr/bin/env python3
"""
simulate_sensors.py
────────────────────────────────────────────────────────────────────────────
Physics-based MQTT sensor simulator for the Industrial IoT Dashboard.

Why physics-based (not random)?
  Random data is immediately obvious — no trend, no periodicity, no drift.
  Real machine vibration has:
    - A carrier frequency (rotation speed)
    - Gradual drift (thermal expansion, wear)
    - Bursts at harmonics of the carrier
    - Occasional fault impulses

This simulator models those properties so the analytics engine reacts
exactly as it would with real hardware.

Usage:
  # Normal operation (2 devices):
  python simulate_sensors.py

  # Inject a fault on unit-01 after 30 seconds:
  python simulate_sensors.py --fault-after 30 --device unit-01

  # Custom broker:
  python simulate_sensors.py --host 192.168.1.100 --port 1883

Requirements:
  pip install paho-mqtt
"""

import argparse
import json
import math
import random
import time
import threading
import sys

import paho.mqtt.client as mqtt

# ─── Configuration ────────────────────────────────────────────────────────────
SCHEMA_VERSION  = 1
FIRMWARE        = "sim-1.0.0"
PUBLISH_HZ      = 2          # samples per second

# Normal operating parameters (mimics industrial motor at 50 Hz)
BASE_VIB        = 9.81       # m/s² — resting acceleration (gravity)
VIB_NOISE       = 0.05       # Gaussian noise sigma
VIB_DRIFT_RATE  = 0.0001     # slow drift per sample (bearing wear simulation)
VIB_HARMONICS   = [1.0, 2.0, 3.0]   # harmonic multipliers
HARMONIC_AMP    = 0.03       # amplitude of each harmonic

BASE_TEMP       = 42.0       # °C — normal operating temperature
TEMP_NOISE      = 0.3
TEMP_DRIFT_RATE = 0.002      # thermal drift per sample

FAULT_MULTIPLIER= 4.5        # spike = baseline × this during fault

# ─── Simulator ────────────────────────────────────────────────────────────────

class SensorSimulator:
    def __init__(self, device_id: str, location: str, client: mqtt.Client,
                 fault_after_seconds: float | None = None):
        self.device_id     = device_id
        self.location      = location
        self.client        = client
        self.fault_after   = fault_after_seconds
        self.seq           = 0
        self.t             = 0.0       # time counter (seconds)
        self.vib_drift     = 0.0
        self.temp_drift    = 0.0
        self._fault_active = False
        self._start_time   = time.time()

    def tick(self):
        self.seq += 1
        self.t   += 1.0 / PUBLISH_HZ

        # Check if fault injection time has come
        elapsed = time.time() - self._start_time
        if self.fault_after and elapsed >= self.fault_after and not self._fault_active:
            self._fault_active = True
            print(f"[{self.device_id}] 🔴 FAULT INJECTED at t={elapsed:.1f}s")

        vib  = self._generate_vibration()
        temp = self._generate_temperature()

        payload = {
            "schema_version": SCHEMA_VERSION,
            "device_id"     : self.device_id,
            "location"      : self.location,
            "firmware"      : FIRMWARE,
            "seq"           : self.seq,
            "ts_ms"         : int(time.time() * 1000),
            "vibration"     : {
                "accel_x": round(vib * 0.1 + random.gauss(0, VIB_NOISE * 0.3), 4),
                "accel_y": round(vib * 0.05+ random.gauss(0, VIB_NOISE * 0.3), 4),
                "accel_z": round(vib,        4),
                "gyro_x" : round(random.gauss(0, 0.01), 4),
                "gyro_y" : round(random.gauss(0, 0.01), 4),
                "gyro_z" : round(random.gauss(0, 0.008),4),
                "rms"    : round(vib,        4),
            },
            "temperature"   : {
                "celsius" : round(temp, 2),
                "humidity": round(random.gauss(60.0, 1.5), 2),
            },
        }

        topic   = f"sensors/{self.device_id}/telemetry"
        message = json.dumps(payload)
        self.client.publish(topic, message, qos=0)

        # Console output
        fault_tag = " ⚠FAULT" if self._fault_active else ""
        print(f"[{self.device_id}] seq={self.seq:<6} rms={vib:.4f} m/s²  "
              f"temp={temp:.1f}°C{fault_tag}")

    def _generate_vibration(self) -> float:
        # Slow drift (simulates bearing wear)
        self.vib_drift += VIB_DRIFT_RATE

        # Sum of harmonics (simulates rotating machinery)
        harmonic_sum = sum(
            HARMONIC_AMP * math.sin(2 * math.pi * mult * self.t)
            for mult in VIB_HARMONICS
        )

        base  = BASE_VIB + self.vib_drift + harmonic_sum
        noise = random.gauss(0, VIB_NOISE)

        if self._fault_active:
            # Impulse every ~0.5s during fault
            impulse = FAULT_MULTIPLIER * abs(random.gauss(0, 1)) \
                      if random.random() < 0.3 else 0
            return abs(base + noise + impulse)

        return abs(base + noise)

    def _generate_temperature(self) -> float:
        self.temp_drift += TEMP_DRIFT_RATE
        noise = random.gauss(0, TEMP_NOISE)

        if self._fault_active:
            # Temperature climbs during fault
            self.temp_drift += 0.05

        return BASE_TEMP + self.temp_drift + noise


def run_device(args, device_id: str, location: str, fault_after: float | None):
    client = mqtt.Client(client_id=f"simulator-{device_id}", clean_session=True)
    client.connect(args.host, args.port, keepalive=60)
    client.loop_start()

    # Publish online status
    client.publish(
        f"sensors/{device_id}/status",
        json.dumps({"status": "online", "device_id": device_id, "location": location}),
        retain=True,
    )

    sim      = SensorSimulator(device_id, location, client, fault_after_seconds=fault_after)
    interval = 1.0 / PUBLISH_HZ

    try:
        while True:
            start = time.time()
            sim.tick()
            elapsed = time.time() - start
            sleep_for = max(0, interval - elapsed)
            time.sleep(sleep_for)
    except KeyboardInterrupt:
        pass
    finally:
        client.publish(
            f"sensors/{device_id}/status",
            json.dumps({"status": "offline", "device_id": device_id}),
            retain=True,
        )
        client.loop_stop()
        client.disconnect()


def main():
    parser = argparse.ArgumentParser(description="Industrial IoT sensor simulator")
    parser.add_argument("--host",        default="localhost", help="MQTT broker host")
    parser.add_argument("--port",        default=1883, type=int)
    parser.add_argument("--fault-after", default=None, type=float,
                        help="Inject fault on unit-01 after N seconds")
    args = parser.parse_args()

    devices = [
        ("unit-01", "Assembly-Line-A", args.fault_after),
        ("unit-02", "Assembly-Line-B", None),
    ]

    print(f"[Simulator] Connecting to MQTT broker at {args.host}:{args.port}")
    print(f"[Simulator] Publishing at {PUBLISH_HZ} Hz  |  Ctrl-C to stop\n")

    threads = []
    for device_id, location, fault_after in devices:
        t = threading.Thread(
            target=run_device,
            args=(args, device_id, location, fault_after),
            daemon=True,
            name=f"sim-{device_id}",
        )
        t.start()
        threads.append(t)

    try:
        while all(t.is_alive() for t in threads):
            time.sleep(0.5)
    except KeyboardInterrupt:
        print("\n[Simulator] Shutting down...")
        sys.exit(0)


if __name__ == "__main__":
    main()
