#pragma once
#include <Adafruit_MPU6050.h>
#include <Adafruit_Sensor.h>
#include "kalman_filter.h"

/** Raw + filtered output from MPU6050. All units SI. */
struct VibrationData {
    float accelX_ms2;    ///< Filtered acceleration X  (m/s²)
    float accelY_ms2;    ///< Filtered acceleration Y  (m/s²)
    float accelZ_ms2;    ///< Filtered acceleration Z  (m/s²)
    float gyroX_rads;    ///< Filtered angular velocity X (rad/s)
    float gyroY_rads;    ///< Filtered angular velocity Y (rad/s)
    float gyroZ_rads;    ///< Filtered angular velocity Z (rad/s)
    float rmsAccel_ms2;  ///< √(x²+y²+z²) — primary predictive-maintenance metric
    bool  valid;         ///< false if sensor is not responding
};

/**
 * @class VibrationSensor
 * @brief Wraps Adafruit MPU6050 with per-axis Kalman filtering.
 *
 * Configuration (set once in begin()):
 *   Accel range : ±8 g  (industrial motors rarely exceed 6g shock)
 *   Gyro range  : ±500 °/s
 *   Bandwidth   : 21 Hz (Nyquist for 40 Hz vibration; enough for bearing freq)
 *
 * Usage:
 *   VibrationSensor vs;
 *   vs.begin();
 *   VibrationData d = vs.read();
 *   if (d.valid) use(d.rmsAccel_ms2);
 */
class VibrationSensor {
public:
    VibrationSensor() noexcept;

    /**
     * @brief Initialise the MPU6050 over I²C and set ranges.
     * @return true on success; false if sensor not found.
     */
    bool begin();

    /**
     * @brief Read + filter one sample from the MPU6050.
     * @return VibrationData with valid=false if sensor is unavailable.
     */
    VibrationData read();

    bool isOnline() const noexcept { return _online; }

private:
    Adafruit_MPU6050 _mpu;
    bool             _online;

    // Six independent Kalman filters — one per axis
    KalmanFilter _kfAX, _kfAY, _kfAZ;   // accel
    KalmanFilter _kfGX, _kfGY, _kfGZ;   // gyro

    static float rms3(float x, float y, float z) noexcept;
};
