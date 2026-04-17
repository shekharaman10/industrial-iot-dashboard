#include "temperature_sensor.h"
#include <Arduino.h>

TemperatureSensor::TemperatureSensor() noexcept
    : _dht(TEMP_SENSOR_PIN, TEMP_SENSOR_TYPE)
    , _online(false)
    , _kfTemp (0.001f, 0.30f, 1.0f)   // slow-changing physical property → low Q
    , _kfHumid(0.010f, 1.00f, 1.0f)
{}

bool TemperatureSensor::begin() {
    _dht.begin();
    // DHT22 has no I2C address; we verify it on first successful read.
    // Set online optimistically — read() sets valid=false on NaN.
    _online = true;
    Serial.println(F("[TemperatureSensor] OK — DHT22 on pin " STRINGIFY(TEMP_SENSOR_PIN)));
    return true;
}

TemperatureData TemperatureSensor::read() {
    TemperatureData out{};
    out.valid = false;

    float rawTemp = _dht.readTemperature();   // Celsius
    float rawHum  = _dht.readHumidity();

    if (isnan(rawTemp) || isnan(rawHum)) {
        // DHT22 returns NaN on CRC error or when polled faster than 2 s
        Serial.println(F("[TemperatureSensor] NaN — skipping sample"));
        return out;
    }

    out.celsius      = _kfTemp.update(rawTemp);
    out.humidity_pct = _kfHumid.update(rawHum);
    out.valid        = true;
    return out;
}
