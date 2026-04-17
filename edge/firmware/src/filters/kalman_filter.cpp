#include "kalman_filter.h"

KalmanFilter::KalmanFilter(float processNoise,
                           float measurementNoise,
                           float estimatedError) noexcept
    : _Q(processNoise)
    , _R(measurementNoise)
    , _P(estimatedError)
    , _K(0.0f)
    , _X(0.0f)
    , _initialized(false)
{}

float KalmanFilter::update(float measurement) noexcept {
    if (!_initialized) {
        _X           = measurement;
        _initialized = true;
        return _X;
    }

    // ── Prediction ──────────────────────────────────────────────────────────
    // Project error covariance forward: P = P + Q
    _P += _Q;

    // ── Update ───────────────────────────────────────────────────────────────
    // Compute Kalman gain:   K = P / (P + R)
    _K = _P / (_P + _R);

    // Update estimate:       X = X + K * (measurement - X)
    _X = _X + _K * (measurement - _X);

    // Update error:          P = (1 - K) * P
    _P = (1.0f - _K) * _P;

    return _X;
}

void KalmanFilter::reset() noexcept {
    _initialized = false;
    _P           = 1.0f;
    _K           = 0.0f;
    _X           = 0.0f;
}
