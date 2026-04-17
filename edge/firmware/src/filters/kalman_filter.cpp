#include "kalman_filter.h"

KalmanFilter::KalmanFilter(float processNoise,
                           float measurementNoise,
                           float estimatedError) noexcept
    : _Q(processNoise)
    , _R(measurementNoise)
    , _P_err(estimatedError)
    , _K(0.0f)
    , _X_est(0.0f)
    , _initialized(false)
{}

float KalmanFilter::update(float measurement) noexcept {
    if (!_initialized) {
        _X_est       = measurement;
        _initialized = true;
        return _X_est;
    }

    // Prediction
    _P_err += _Q;

    // Update
    _K     = _P_err / (_P_err + _R);
    _X_est = _X_est + _K * (measurement - _X_est);
    _P_err = (1.0f - _K) * _P_err;

    return _X_est;
}

void KalmanFilter::reset() noexcept {
    _initialized = false;
    _P_err       = 1.0f;
    _K           = 0.0f;
    _X_est       = 0.0f;
}
