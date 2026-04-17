#pragma once
/**
 * @file config.h
 * @brief Centralised device configuration for the ESP32 edge firmware.
 *
 * HOW TO USE:
 *   All tuneable parameters live here. Build-time overrides come from
 *   platformio.ini build_flags (e.g. -DWIFI_SSID=\"MyNetwork\").
 *   In production, load sensitive values (WiFi password, MQTT credentials)
 *   from ESP32 NVS (Non-Volatile Storage) provisioned during manufacturing.
 *
 * PRODUCTION CHECKLIST:
 *   [ ] Replace hardcoded WiFi credentials with NVS reads
 *   [ ] Enable MQTT TLS on port 8883 (add CA cert to SPIFFS)
 *   [ ] Set MQTT_USERNAME / MQTT_PASSWORD from NVS
 *   [ ] Assign unique DEVICE_ID per unit (use ESP32 chip ID)
 *   [ ] Enable NTP for accurate timestamps
 */

// ─── Device identity ──────────────────────────────────────────────────────────
#ifndef DEVICE_ID
#  define DEVICE_ID        "unit-01"
#endif

#ifndef DEVICE_LOCATION
#  define DEVICE_LOCATION  "Assembly-Line-A"
#endif

#define FIRMWARE_VERSION   "1.2.0"

// ─── WiFi ─────────────────────────────────────────────────────────────────────
#ifndef WIFI_SSID
#  define WIFI_SSID        "YOUR_SSID"
#endif

#ifndef WIFI_PASSWORD
#  define WIFI_PASSWORD    "YOUR_PASSWORD"
#endif

/// Number of 500ms attempts before giving up and rebooting
#define WIFI_CONNECT_ATTEMPTS  40

// ─── MQTT Broker ──────────────────────────────────────────────────────────────
#ifndef MQTT_HOST
#  define MQTT_HOST        "192.168.1.100"
#endif

#ifndef MQTT_PORT
#  define MQTT_PORT        1883
#endif

// Uncomment and set for authenticated brokers
// #define MQTT_USERNAME   "edge-device"
// #define MQTT_PASSWORD   "change-me"

// Buffer size for MQTT messages (bytes). Must be >= largest telemetry payload.
#define MQTT_BUFFER_SIZE   1024

// ─── MQTT Topics (must match backend/src/Shared/Constants/Topics.cs) ─────────
#define TOPIC_TELEMETRY    "sensors/" DEVICE_ID "/telemetry"
#define TOPIC_STATUS       "sensors/" DEVICE_ID "/status"
#define TOPIC_COMMANDS     "devices/" DEVICE_ID "/commands"

// ─── Sampling intervals ───────────────────────────────────────────────────────
/// Vibration sampling interval (ms). MPU6050 supports up to ~8kHz; 500ms = 2Hz is sufficient.
#define VIB_INTERVAL_MS      500UL

/// Temperature sampling interval (ms). DHT22 minimum is 2000ms; use 3000ms for margin.
#define TEMP_INTERVAL_MS    3000UL

/// Telemetry publish interval (ms). Should match the slowest sensor.
#define PUBLISH_INTERVAL_MS 3000UL

// ─── Watchdog ─────────────────────────────────────────────────────────────────
/// Hardware watchdog timeout (seconds). Resets device if loop() stalls.
#define WDT_TIMEOUT_S        15

// ─── Sensor hardware ──────────────────────────────────────────────────────────
/// GPIO pin for DHT22 data line.
#define DHT22_DATA_PIN       4

/// DHT22 sensor type (DHT22 = AM2302).
#define DHT22_TYPE           DHT22

/// MPU6050 I2C address (0x68 if AD0=LOW, 0x69 if AD0=HIGH).
#define MPU6050_I2C_ADDR     0x68

// ─── Kalman filter tuning ─────────────────────────────────────────────────────
// Q = process noise (how fast the true value changes)
// R = measurement noise (sensor's inherent noise floor)
// Higher Q → trusts measurements more (faster response, noisier output)
// Higher R → trusts model more (smoother output, slower response)

#define KALMAN_ACCEL_Q       0.01f
#define KALMAN_ACCEL_R       0.50f   // MPU6050 accel noise density: ~400 μg/√Hz
#define KALMAN_GYRO_Q        0.01f
#define KALMAN_GYRO_R        0.10f   // MPU6050 gyro noise density: ~0.005 °/s/√Hz
#define KALMAN_TEMP_Q        0.001f  // Temperature changes slowly
#define KALMAN_TEMP_R        0.30f   // DHT22 accuracy: ±0.5°C
#define KALMAN_HUMID_Q       0.010f
#define KALMAN_HUMID_R       1.00f   // DHT22 accuracy: ±2% RH
#define KALMAN_INIT_P        1.00f   // Initial estimation error (converges quickly when high)

// ─── Last-Will-Testament payload ──────────────────────────────────────────────
#define LWT_PAYLOAD \
    "{\"status\":\"offline\",\"device_id\":\"" DEVICE_ID "\"}"

#define ONLINE_PAYLOAD \
    "{\"status\":\"online\",\"device_id\":\"" DEVICE_ID \
    "\",\"location\":\"" DEVICE_LOCATION \
    "\",\"firmware\":\"" FIRMWARE_VERSION "\"}"
