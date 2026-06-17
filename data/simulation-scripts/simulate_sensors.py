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
    - Bearing fault: linearly escalating RMS + impulse bursts

This simulator models those properties so the analytics engine reacts
exactly as it would with real hardware.

Usage:
  # Normal operation (2 devices):
  python simulate_sensors.py

  # Spin up 10 simulated devices:
  python simulate_sensors.py --device-count 10

  # Inject a fault on unit-01 after 30 seconds:
  python simulate_sensors.py --fault-after 30 --fault-device unit-01

  # Simulate an offline/reconnect cycle every 60 seconds:
  python simulate_sensors.py --offline-duration 5

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
FIRMWARE        = "sim-2.0.0"
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

FAULT_MULTIPLIER= 4.5        # spike amplitude multiplier during fault
# Bearing faults show gradual RMS escalation before hard failure.
# This ramp rate adds ~0.1 m/s² per second during a fault.
FAULT_RMS_RAMP  = 0.1 / PUBLISH_HZ

LOCATIONS = [
    "Assembly-Line-A", "Assembly-Line-B", "Assembly-Line-C",
    "Compressor-1",    "Compressor-2",    "Pump-Station",
    "CNC-Machine-1",   "CNC-Machine-2",   "Conveyor-Belt",
    "Packaging-Unit",
]

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
        self._fault_ramp   = 0.0       # accumulated RMS escalation since fault start
        self._start_time   = time.time()

    def tick(self):
        self.seq += 1
        self.t   += 1.0 / PUBLISH_HZ

        # Check if fault injection time has come
        elapsed = time.time() - self._start_time
        if self.fault_after and elapsed >= self.fault_after and not self._fault_active:
            self._fault_active = True
            print(f"[{self.device_id}] FAULT INJECTED at t={elapsed:.1f}s — "
                  f"RMS will escalate linearly")

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

        fault_tag = f" [FAULT rms+{self._fault_ramp:.2f}]" if self._fault_active else ""
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
            # 1. Linear RMS escalation — models progressive bearing degradation.
            #    A real fault doesn't just spike randomly; the baseline rises
            #    steadily as the bearing surface deteriorates.
            self._fault_ramp += FAULT_RMS_RAMP

            # 2. Occasional high-amplitude impulses at bearing defect frequency
            #    (~0.3 probability per sample at 2 Hz)
            impulse = FAULT_MULTIPLIER * abs(random.gauss(0, 1)) \
                      if random.random() < 0.3 else 0

            return abs(base + self._fault_ramp + noise + impulse)

        return abs(base + noise)

    def _generate_temperature(self) -> float:
        self.temp_drift += TEMP_DRIFT_RATE
        noise = random.gauss(0, TEMP_NOISE)

        if self._fault_active:
            # Elevated heat generation from a damaged bearing
            self.temp_drift += 0.05

        return BASE_TEMP + self.temp_drift + noise


def run_device(args, device_id: str, location: str,
               fault_after: float | None, offline_duration: float | None):
    interval = 1.0 / PUBLISH_HZ
    cycle    = 0

    while True:
        client = mqtt.Client(client_id=f"simulator-{device_id}-{cycle}",
                             clean_session=True)
        try:
            client.connect(args.host, args.port, keepalive=60)
        except Exception as e:
            print(f"[{device_id}] Connect failed: {e} — retrying in 5s")
            time.sleep(5)
            continue

        client.loop_start()
        cycle += 1

        client.publish(
            f"sensors/{device_id}/status",
            json.dumps({"status": "online", "device_id": device_id,
                        "location": location}),
            retain=True,
        )
        print(f"[{device_id}] Online (cycle {cycle})")

        sim = SensorSimulator(device_id, location, client,
                              fault_after_seconds=fault_after)

        online_until = (time.time() + offline_duration * 10) if offline_duration else None

        try:
            while True:
                # Simulate an offline/reconnect cycle if requested
                if online_until and time.time() >= online_until:
                    print(f"[{device_id}] Going offline for {offline_duration}s "
                          f"(testing reconnect handling)")
                    break

                start = time.time()
                sim.tick()
                elapsed = time.time() - start
                time.sleep(max(0, interval - elapsed))

        except KeyboardInterrupt:
            client.publish(
                f"sensors/{device_id}/status",
                json.dumps({"status": "offline", "device_id": device_id}),
                retain=True,
            )
            client.loop_stop()
            client.disconnect()
            return

        client.publish(
            f"sensors/{device_id}/status",
            json.dumps({"status": "offline", "device_id": device_id}),
            retain=True,
        )
        client.loop_stop()
        client.disconnect()

        if offline_duration:
            print(f"[{device_id}] Disconnected. Reconnecting in {offline_duration}s...")
            time.sleep(offline_duration)
        else:
            break


def main():
    parser = argparse.ArgumentParser(
        description="Industrial IoT sensor simulator",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--host",           default="localhost",
                        help="MQTT broker host (default: localhost)")
    parser.add_argument("--port",           default=1883, type=int,
                        help="MQTT broker port (default: 1883)")
    parser.add_argument("--device-count",   default=2, type=int,
                        help="Number of simulated devices (default: 2)")
    parser.add_argument("--fault-after",    default=None, type=float,
                        help="Inject bearing fault after N seconds")
    parser.add_argument("--fault-device",   default="unit-01",
                        help="Device ID to inject fault on (default: unit-01)")
    parser.add_argument("--offline-duration", default=None, type=float,
                        help="Simulate disconnect/reconnect cycles every N*10 seconds")
    args = parser.parse_args()

    if args.device_count < 1:
        print("--device-count must be >= 1")
        sys.exit(1)

    # Build device list dynamically
    devices = []
    for i in range(1, args.device_count + 1):
        device_id = f"unit-{i:02d}"
        location  = LOCATIONS[(i - 1) % len(LOCATIONS)]
        fault_at  = args.fault_after if device_id == args.fault_device else None
        devices.append((device_id, location, fault_at))

    print(f"[Simulator] Broker:  {args.host}:{args.port}")
    print(f"[Simulator] Devices: {args.device_count}  |  Rate: {PUBLISH_HZ} Hz")
    if args.fault_after:
        print(f"[Simulator] Fault:   {args.fault_device} at t+{args.fault_after}s "
              f"(linear RMS escalation + impulses)")
    if args.offline_duration:
        print(f"[Simulator] Offline: {args.offline_duration}s disconnect every "
              f"{args.offline_duration * 10:.0f}s")
    print()

    threads = []
    for device_id, location, fault_after in devices:
        t = threading.Thread(
            target=run_device,
            args=(args, device_id, location, fault_after, args.offline_duration),
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
