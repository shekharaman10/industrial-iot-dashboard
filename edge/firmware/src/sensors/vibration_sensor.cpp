#include "vibration_sensor.h"
#include <Arduino.h>
#include <math.h>

// Kalman parameters tuned for MPU6050 in a vibrating industrial environment.
// These were derived empirically; adjust if baseline noise floor changes.
static constexpr float ACCEL_Q  = 0.01f;   // Process noise — accel
static constexpr float ACCEL_R  = 0.50f;   // Measurement noise — accel (MPU6050 datasheet: ±0.5 mg/√Hz)
static constexpr float GYRO_Q   = 0.01f;
static constexpr float GYRO_R   = 0.10f;
static constexpr float INIT_P   = 1.00f;

VibrationSensor::VibrationSensor() noexcept
    : _online(false)
    , _kfAX(ACCEL_Q, ACCEL_R, INIT_P)
    , _kfAY(ACCEL_Q, ACCEL_R, INIT_P)
    , _kfAZ(ACCEL_Q, ACCEL_R, INIT_P)
    , _kfGX(GYRO_Q,  GYRO_R,  INIT_P)
    , _kfGY(GYRO_Q,  GYRO_R,  INIT_P)
    , _kfGZ(GYRO_Q,  GYRO_R,  INIT_P)
{}

bool VibrationSensor::begin() {
    if (!_mpu.begin()) {
        Serial.println(F("[VibrationSensor] FAIL — MPU6050 not found on I2C bus (addr 0x68)"));
        _online = false;
        return false;
    }

    // ±8g covers most industrial machinery; ±16g only if expecting severe shock
    _mpu.setAccelerometerRange(MPU6050_RANGE_8_G);

    // ±500°/s is more than enough for rotating equipment at typical RPM
    _mpu.setGyroRange(MPU6050_RANGE_500_DEG);

    // 21 Hz low-pass: removes high-frequency digital noise above Nyquist
    // for typical bearing defect frequencies (0–20 Hz range)
    _mpu.setFilterBandwidth(MPU6050_BAND_21_HZ);

    Serial.println(F("[VibrationSensor] OK — MPU6050 range=±8g, gyro=±500°/s, LPF=21Hz"));
    _online = true;
    return true;
}

VibrationData VibrationSensor::read() {
    VibrationData out{};
    out.valid = false;

    if (!_online) return out;

    sensors_event_t accel, gyro, tmp;
    _mpu.getEvent(&accel, &gyro, &tmp);

    // Apply Kalman filter per axis
    out.accelX_ms2  = _kfAX.update(accel.acceleration.x);
    out.accelY_ms2  = _kfAY.update(accel.acceleration.y);
    out.accelZ_ms2  = _kfAZ.update(accel.acceleration.z);
    out.gyroX_rads  = _kfGX.update(gyro.gyro.x);
    out.gyroY_rads  = _kfGY.update(gyro.gyro.y);
    out.gyroZ_rads  = _kfGZ.update(gyro.gyro.z);

    // RMS magnitude = primary health indicator
    // Gravity (~9.81 m/s²) is always present on the Z axis at rest.
    // In production, subtract gravity vector after calibration.
    out.rmsAccel_ms2 = rms3(out.accelX_ms2, out.accelY_ms2, out.accelZ_ms2);
    out.valid        = true;
    return out;
}

float VibrationSensor::rms3(float x, float y, float z) noexcept {
    return sqrtf(x * x + y * y + z * z);
}
