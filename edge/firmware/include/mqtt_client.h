#pragma once
/**
 * @file mqtt_client.h
 * @brief Isolated MQTT networking layer for ESP32.
 *
 * WHY A SEPARATE CLASS?
 *   main.cpp should not know about WiFiClient, PubSubClient internals,
 *   or reconnect logic. This class owns all MQTT state and exposes
 *   a clean 3-method API: begin(), loop(), publish().
 *
 * RECONNECT STRATEGY:
 *   Exponential back-off starting at 2 s, doubling each attempt,
 *   capped at 60 s. Reset to 2 s on successful connect.
 *   This prevents hammering a temporarily unavailable broker.
 *
 * LAST-WILL-TESTAMENT:
 *   Configured at connect time. If TCP drops without a clean disconnect,
 *   Mosquitto automatically publishes the LWT payload so the backend
 *   detects the outage within one keep-alive period (~30 s).
 */

#include <Arduino.h>
#include <WiFi.h>
#include <PubSubClient.h>
#include <functional>

/** Signature of the command callback invoked on inbound MQTT messages. */
using CommandHandler = std::function<void(const char* topic, const char* payload)>;

struct MqttConfig {
    const char* host;             ///< Broker IP or hostname
    uint16_t    port;             ///< Default: 1883
    const char* clientId;         ///< Unique per device (use device serial)
    const char* statusTopic;      ///< Retained online/offline status
    const char* commandTopic;     ///< Backend → edge commands
    const char* telemetryTopic;   ///< Edge → backend data
    const char* willPayload;      ///< LWT payload (offline JSON)
};

class MqttClient {
public:
    /**
     * @param cfg           Connection parameters (stored by value)
     * @param onCommand     Called on every inbound command message
     */
    explicit MqttClient(const MqttConfig& cfg, CommandHandler onCommand = nullptr) noexcept;

    /**
     * @brief Connect to broker; block until first connection succeeds.
     *        Must be called once after WiFi is up.
     */
    void begin();

    /**
     * @brief Drive the MQTT state machine.
     *        Call every loop() iteration — handles keepalive + reconnect.
     */
    void loop();

    /**
     * @brief Publish a telemetry payload (QoS-0, fire-and-forget).
     * @param payload  Null-terminated JSON string.
     * @return true if the broker accepted the publish.
     */
    bool publish(const char* payload);

    /**
     * @brief Send a command to another device or backend (QoS-1).
     * @param topic    Full topic string.
     * @param payload  Null-terminated JSON command.
     */
    bool sendCommand(const char* topic, const char* payload);

    bool isConnected() noexcept { return _client.connected(); }

    /** Exponential back-off reconnect delay in ms (read-only, for logging). */
    unsigned long reconnectDelay() const noexcept { return _reconnectDelay; }

private:
    MqttConfig     _cfg;
    WiFiClient     _wifi;
    PubSubClient   _client;
    CommandHandler _onCommand;

    unsigned long  _lastReconnectMs = 0;
    unsigned long  _reconnectDelay  = 2000;   // 2 s initial

    void           _connect();
    static void    _staticCallback(char* topic, byte* payload, unsigned int len);

    // Singleton needed because PubSubClient callback is a plain C function pointer
    static MqttClient* _instance;
};
