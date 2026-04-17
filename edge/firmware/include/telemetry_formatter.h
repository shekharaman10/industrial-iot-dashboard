#pragma once
#include <Arduino.h>
#include <ArduinoJson.h>
#include "vibration_sensor.h"
#include "temperature_sensor.h"

/**
 * @file telemetry_formatter.h
 * @brief Builds the canonical JSON telemetry payload.
 *
 * WHY A SEPARATE FILE?
 * The JSON schema is the contract between edge and backend.
 * Keeping it isolated here means:
 *   - One place to change the schema
 *   - Easier to version (add "schema_version" field)
 *   - Unit-testable on host without hardware
 *
 * SCHEMA (v1):
 * {
 *   "schema_version": 1,
 *   "device_id":      "unit-01",
 *   "location":       "Assembly-Line-A",
 *   "firmware":       "1.2.0",
 *   "seq":            12345,
 *   "ts_ms":          millis(),          ← replace with NTP epoch in production
 *   "vibration": {
 *     "accel_x": 0.0023,
 *     "accel_y": 9.7821,
 *     "accel_z": 0.1142,
 *     "gyro_x":  0.0012,
 *     "gyro_y": -0.0003,
 *     "gyro_z":  0.0007,
 *     "rms":     9.7833
 *   },
 *   "temperature": {
 *     "celsius":  42.3,
 *     "humidity": 61.2
 *   }
 * }
 */

static constexpr uint8_t SCHEMA_VERSION = 1;
static constexpr size_t  JSON_DOC_SIZE  = 512;

namespace TelemetryFormatter {

    /**
     * @brief Serialise sensor readings into JSON string.
     * @param vib       Filtered vibration data (may be invalid)
     * @param temp      Filtered temperature data (may be invalid)
     * @param deviceId  MQTT client identifier string
     * @param location  Human-readable sensor location
     * @param seq       Monotonic sequence counter (detect dropped messages)
     * @return          Serialised JSON string; empty on allocation failure.
     */
    String build(const VibrationData&   vib,
                 const TemperatureData& temp,
                 const char*            deviceId,
                 const char*            location,
                 uint32_t               seq) noexcept;

} // namespace TelemetryFormatter
