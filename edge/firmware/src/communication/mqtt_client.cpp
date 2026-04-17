/**
 * @file mqtt_client.cpp
 * @brief Full implementation of the MqttClient wrapper.
 *
 * This is the ONLY file that includes PubSubClient or WiFiClient.
 * All connectivity concerns are isolated here.
 */

#include "mqtt_client.h"
#include <Arduino.h>

// Static instance pointer required for PubSubClient's C-style callback
MqttClient* MqttClient::_instance = nullptr;

// ─── Constructor ──────────────────────────────────────────────────────────────
MqttClient::MqttClient(const MqttConfig& cfg, CommandHandler onCommand) noexcept
    : _cfg(cfg)
    , _client(_wifi)
    , _onCommand(onCommand)
{
    _instance = this;
}

// ─── begin() — called once after WiFi connects ────────────────────────────────
void MqttClient::begin() {
    _client.setServer(_cfg.host, _cfg.port);
    _client.setCallback(_staticCallback);
    _client.setBufferSize(1024);

    while (!_client.connected()) {
        _connect();
        if (!_client.connected()) {
            Serial.printf("[MQTT] Retrying in %lu ms\n", _reconnectDelay);
            delay(_reconnectDelay);
        }
    }
}

// ─── loop() ──────────────────────────────────────────────────────────────────
void MqttClient::loop() {
    if (!_client.connected()) {
        unsigned long now = millis();
        if (now - _lastReconnectMs >= _reconnectDelay) {
            _lastReconnectMs = now;
            Serial.printf("[MQTT] Reconnecting (delay=%lu ms)...\n", _reconnectDelay);
            _connect();
            if (_client.connected()) {
                _reconnectDelay = 2000;
            } else {
                _reconnectDelay = min(_reconnectDelay * 2UL, 60000UL);
            }
        }
    }
    _client.loop();
}

// ─── publish() — QoS-0 telemetry ─────────────────────────────────────────────
bool MqttClient::publish(const char* payload) {
    if (!_client.connected()) {
        Serial.println(F("[MQTT] publish() skipped — not connected"));
        return false;
    }
    bool ok = _client.publish(_cfg.telemetryTopic, payload, false);
    if (!ok) Serial.println(F("[MQTT] publish() FAILED — buffer full or disconnected"));
    return ok;
}

// ─── sendCommand() ────────────────────────────────────────────────────────────
bool MqttClient::sendCommand(const char* topic, const char* payload) {
    if (!_client.connected()) return false;
    return _client.publish(topic, payload);
}

// ─── _connect() ───────────────────────────────────────────────────────────────
void MqttClient::_connect() {
    bool ok = _client.connect(
        _cfg.clientId,
        nullptr, nullptr,
        _cfg.statusTopic, 1, true,
        _cfg.willPayload
    );

    if (!ok) {
        Serial.printf("[MQTT] Connect FAILED rc=%d\n", _client.state());
        return;
    }

    Serial.printf("[MQTT] Connected to %s:%u as %s\n",
                  _cfg.host, _cfg.port, _cfg.clientId);

    _client.subscribe(_cfg.commandTopic, 1);

    char online[128];
    snprintf(online, sizeof(online),
             "{\"status\":\"online\",\"device_id\":\"%s\"}", _cfg.clientId);
    _client.publish(_cfg.statusTopic, online, true);
}

// ─── Static callback ──────────────────────────────────────────────────────────
void MqttClient::_staticCallback(char* topic, byte* payload, unsigned int len) {
    if (!_instance || !_instance->_onCommand) return;
    char buf[256] = {};
    size_t copyLen = min((size_t)len, sizeof(buf) - 1);
    memcpy(buf, payload, copyLen);
    _instance->_onCommand(topic, buf);
}
