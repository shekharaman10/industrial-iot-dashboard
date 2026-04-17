# Sensor Simulator

Physics-based MQTT sensor simulator for the Industrial IoT Dashboard.

## What it simulates

Unlike a random data generator, this simulator models real industrial machine behaviour:

| Property | Real machine | Simulator |
|---|---|---|
| Base vibration | Gravity + static load | `BASE_VIB = 9.81 m/s²` |
| Harmonic content | Motor rotation speed harmonics | `sin(2π × n × t)` at 1×, 2×, 3× carrier |
| Bearing wear | Gradual RMS increase over weeks | `vib_drift += 0.0001` per sample |
| Thermal expansion | Temperature rises with load | `temp_drift += 0.002` per sample |
| Fault impulse | Bearing spall: sudden impact | Random Gaussian at `FAULT_MULTIPLIER = 4.5×` |
| Fault thermal | Cooling failure: sustained rise | `temp_drift += 0.05` per sample |

This means:
- The analytics engine establishes a realistic baseline
- Fault injection triggers real anomaly alerts (not noise)
- Drift is detectable over time before it becomes a fault

## Usage

```bash
# Install dependencies
pip install -r requirements.txt

# Normal operation — 2 devices at 2 Hz
python3 simulate_sensors.py

# Inject a fault on unit-01 after 30 seconds
python3 simulate_sensors.py --fault-after 30

# Connect to a remote broker
python3 simulate_sensors.py --host 192.168.1.50 --port 1883

# All options
python3 simulate_sensors.py --help
```

## Expected console output

```
[Simulator] Connecting to MQTT broker at localhost:1883
[Simulator] Publishing at 2 Hz  |  Ctrl-C to stop

[unit-01] seq=1      rms=9.8142 m/s²  temp=42.1°C
[unit-02] seq=1      rms=9.8098 m/s²  temp=41.9°C
[unit-01] seq=2      rms=9.8231 m/s²  temp=42.0°C
...
[unit-01] 🔴 FAULT INJECTED at t=30.0s
[unit-01] seq=61     rms=44.2019 m/s²  temp=42.3°C ⚠FAULT
```

## Watching alerts fire in the dashboard

1. Start Docker stack: `bash scripts/setup.sh`
2. Open dashboard: `http://localhost:3000`
3. Run simulator: `python3 simulate_sensors.py --fault-after 30`
4. Wait ~2.5 min for baseline to establish (300 samples at 2 Hz)
5. At t=30s the fault injects — watch the vibration chart spike and
   CRITICAL alert appear in the alerts panel within 1–2 seconds

## Alert timeline after fault injection

| Time | What happens |
|---|---|
| t=0 | Simulator starts, devices register |
| t=5s | unit-01 appears in device selector |
| t=25s | Baseline established (300 samples) |
| t=30s | Fault injected — RMS jumps from ~9.81 to ~40–50 m/s² |
| t=31s | `RateOfChangeSpike` alert fires (>40% single-step) |
| t=32s | `VibrationSpike` FAULT alert fires (Z-score > 3.5σ) |
| t=62s | Next alert fires (30s cooldown elapsed) |

## Adding more devices

Edit the `devices` list at the bottom of `simulate_sensors.py`:

```python
devices = [
    ("unit-01", "Assembly-Line-A", args.fault_after),
    ("unit-02", "Assembly-Line-B", None),
    ("unit-03", "CNC-Station-1",   None),   # Add new device here
]
```
