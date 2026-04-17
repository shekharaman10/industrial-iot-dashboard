#pragma once
#include <DHT.h>
#include "kalman_filter.h"

#define TEMP_SENSOR_PIN  4
#define TEMP_SENSOR_TYPE DHT22

/** Output from DHT22 sensor. */
struct TemperatureData {
    float celsius;       ///< Filtered temperature (°C)
    float humidity_pct;  ///< Filtered relative humidity (%)
    bool  valid;
};

/**
 * @class TemperatureSensor
 * @brief Wraps DHT22 with light Kalman filtering on both channels.
 *
 * DHT22 has ±0.5°C accuracy and ±2% RH accuracy.
 * The Kalman filter (low Q, higher R) smooths the quantisation steps
 * visible when polling at 2 Hz.
 *
 * Note: DHT22 cannot be sampled faster than once per 2 seconds. The
 * sampling loop in main.cpp must honour this; calling read() faster
 * returns NaN from the library which is handled gracefully here.
 */
class TemperatureSensor {
public:
    TemperatureSensor() noexcept;
    bool            begin();
    TemperatureData read();
    bool            isOnline() const noexcept { return _online; }

private:
    DHT          _dht;
    bool         _online;
    KalmanFilter _kfTemp;   // Q=0.001, R=0.3
    KalmanFilter _kfHumid;  // Q=0.01,  R=1.0
};
