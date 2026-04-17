#include "telemetry_formatter.h"
#include <Arduino.h>

namespace TelemetryFormatter {

String build(const VibrationData&   vib,
             const TemperatureData& temp,
             const char*            deviceId,
             const char*            location,
             uint32_t               seq) noexcept {

    StaticJsonDocument<JSON_DOC_SIZE> doc;

    doc["schema_version"] = SCHEMA_VERSION;
    doc["device_id"]      = deviceId;
    doc["location"]       = location;
    doc["firmware"]       = "1.2.0";
    doc["seq"]            = seq;
    doc["ts_ms"]          = millis();   // replace with NTP epoch in production

    if (vib.valid) {
        JsonObject v   = doc.createNestedObject("vibration");
        // Round to 4 decimal places — sufficient for m/s² precision
        v["accel_x"]   = serialized(String(vib.accelX_ms2,  4));
        v["accel_y"]   = serialized(String(vib.accelY_ms2,  4));
        v["accel_z"]   = serialized(String(vib.accelZ_ms2,  4));
        v["gyro_x"]    = serialized(String(vib.gyroX_rads,  4));
        v["gyro_y"]    = serialized(String(vib.gyroY_rads,  4));
        v["gyro_z"]    = serialized(String(vib.gyroZ_rads,  4));
        v["rms"]       = serialized(String(vib.rmsAccel_ms2,4));
    }

    if (temp.valid) {
        JsonObject t   = doc.createNestedObject("temperature");
        t["celsius"]   = serialized(String(temp.celsius,      2));
        t["humidity"]  = serialized(String(temp.humidity_pct, 2));
    }

    String out;
    out.reserve(JSON_DOC_SIZE);

    if (serializeJson(doc, out) == 0) {
        // serializeJson returns 0 on allocation failure
        return "";
    }

    return out;
}

} // namespace TelemetryFormatter
