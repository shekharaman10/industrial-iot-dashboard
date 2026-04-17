#pragma once

/**
 * @file kalman_filter.h
 * @brief Discrete 1-D Kalman Filter for scalar sensor noise reduction.
 *
 * Why Kalman and not a simple moving average?
 *   - Moving average introduces phase lag proportional to window size.
 *   - Kalman is optimal for Gaussian noise; it adapts its gain based on
 *     estimated error, giving fast response to real signal changes while
 *     rejecting measurement noise.
 *
 * Tuning guide:
 *   processNoise (Q)       — how fast the true value is expected to change.
 *                            Higher Q = trusts measurements more (faster, noisier).
 *   measurementNoise (R)   — sensor's inherent noise variance.
 *                            Higher R = trusts model more (smoother, slower).
 *   estimatedError (P)     — initial uncertainty. Set high to converge quickly.
 *
 * Typical values:
 *   MPU6050 accel : Q=0.01, R=0.5
 *   MPU6050 gyro  : Q=0.01, R=0.1
 *   DHT22 temp    : Q=0.001, R=0.3
 */
class KalmanFilter {
public:
    explicit KalmanFilter(float processNoise      = 0.01f,
                          float measurementNoise  = 0.1f,
                          float estimatedError    = 1.0f) noexcept;

    /**
     * @brief Feed a raw measurement, get back the filtered estimate.
     * @param measurement  Raw sensor value.
     * @return             Filtered (de-noised) value.
     */
    float update(float measurement) noexcept;

    /** @brief Hard-reset the filter state (use after sensor reconnect). */
    void  reset() noexcept;

    /** @brief Returns the current Kalman gain (useful for diagnostics). */
    float gain()  const noexcept { return _K; }

private:
    const float _Q;          ///< Process noise covariance (const after construction)
    const float _R;          ///< Measurement noise covariance (const after construction)
    float       _P_err;      ///< Estimation error covariance (updated each step)
    float       _K;          ///< Kalman gain
    float       _X_est;      ///< State estimate (filtered output)
    bool        _initialized;
};
