/**
 * @file main.cpp
 * @brief Industrial IoT Edge Firmware — ESP32 entry point (v1.2.0)
 *
 * All configuration is centralised in include/config.h.
 * All MQTT concerns are delegated to MqttClient (mqtt_client.h/cpp).
 * All sensor concerns are in VibrationSensor / TemperatureSensor classes.
 * This file only orchestrates the top-level timing loop.
 *
 * Execution model:
 *   setup() — one-shot init (WiFi, MqttClient, sensors, watchdog)
 *   loop()  — cooperative multitasking, NO blocking delay() in hot path
 *
 * Timing contracts:
 *   Vibration sample   : VIB_INTERVAL_MS    (500 ms, 2 Hz)
 *   Temperature sample : TEMP_INTERVAL_MS   (3000 ms, DHT22 limit)
 *   Telemetry publish  : PUBLISH_INTERVAL_MS(3000 ms)
 */

#include <Arduino.h>
#include <WiFi.h>
#include <esp_task_wdt.h>

#include "config.h"
#include "mqtt_client.h"
#include "vibration_sensor.h"
#include "temperature_sensor.h"
#include "telemetry_formatter.h"

// ─── Module instances ─────────────────────────────────────────────────────────
static VibrationSensor   gVib;
static TemperatureSensor gTemp;

// Forward-declare so it can be passed to MqttClient constructor
static void onCommand(const char* topic, const char* payload);

static const MqttConfig MQTT_CFG = {
    .host           = MQTT_HOST,
    .port           = MQTT_PORT,
    .clientId       = DEVICE_ID,
    .statusTopic    = TOPIC_STATUS,
    .commandTopic   = TOPIC_COMMANDS,
    .telemetryTopic = TOPIC_TELEMETRY,
    .willPayload    = LWT_PAYLOAD,
};

static MqttClient gMqtt(MQTT_CFG, onCommand);

// ─── State ────────────────────────────────────────────────────────────────────
static uint32_t      gSeq           = 0;
static unsigned long gLastVibMs     = 0;
static unsigned long gLastTempMs    = 0;
static unsigned long gLastPublishMs = 0;
static VibrationData   gLatestVib{};
static TemperatureData gLatestTemp{};

// ─── Setup ────────────────────────────────────────────────────────────────────
void setup() {
    Serial.begin(115200);
    delay(200);
    Serial.printf("\n[BOOT] Firmware v%s  device=%s  location=%s\n",
                  FIRMWARE_VERSION, DEVICE_ID, DEVICE_LOCATION);

    // Hardware watchdog — auto-resets if loop() stalls (WiFi/MQTT deadlock)
    esp_task_wdt_init(WDT_TIMEOUT_S, true);
    esp_task_wdt_add(nullptr);

    // ── WiFi connection ──────────────────────────────────────────────────────
    Serial.printf("[WiFi] Connecting to %s...\n", WIFI_SSID);
    WiFi.mode(WIFI_STA);
    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

    uint8_t tries = 0;
    while (WiFi.status() != WL_CONNECTED && tries++ < WIFI_CONNECT_ATTEMPTS) {
        delay(500);
        Serial.print('.');
    }
    if (WiFi.status() != WL_CONNECTED) {
        Serial.println(F("\n[WiFi] Connection FAILED — rebooting"));
        ESP.restart();
    }
    Serial.printf("\n[WiFi] Connected  IP=%s  RSSI=%d dBm\n",
                  WiFi.localIP().toString().c_str(), WiFi.RSSI());

    // ── MQTT (blocks until first successful connect) ──────────────────────────
    gMqtt.begin();

    // ── Sensor init ──────────────────────────────────────────────────────────
    if (!gVib.begin())  Serial.println(F("[WARN] Vibration sensor offline — data will be partial"));
    if (!gTemp.begin()) Serial.println(F("[WARN] Temperature sensor offline — data will be partial"));

    Serial.println(F("[BOOT] Initialisation complete — entering main loop"));
}

// ─── Loop ─────────────────────────────────────────────────────────────────────
void loop() {
    esp_task_wdt_reset();   // Reset watchdog every iteration

    // MQTT: drives reconnect logic + processes inbound command messages
    gMqtt.loop();

    unsigned long now = millis();

    // ── Sample vibration (2 Hz) ──────────────────────────────────────────────
    if (now - gLastVibMs >= VIB_INTERVAL_MS) {
        gLastVibMs = now;
        gLatestVib = gVib.read();
    }

    // ── Sample temperature (DHT22 min period = 2 s; use 3 s for safety) ──────
    if (now - gLastTempMs >= TEMP_INTERVAL_MS) {
        gLastTempMs = now;
        gLatestTemp = gTemp.read();
    }

    // ── Publish telemetry ────────────────────────────────────────────────────
    if (now - gLastPublishMs >= PUBLISH_INTERVAL_MS) {
        gLastPublishMs = now;

        if (!gLatestVib.valid && !gLatestTemp.valid) {
            Serial.println(F("[TX] Both sensors invalid — skipping publish"));
            return;
        }

        String payload = TelemetryFormatter::build(
            gLatestVib, gLatestTemp,
            DEVICE_ID, DEVICE_LOCATION,
            ++gSeq);

        if (payload.isEmpty()) {
            Serial.println(F("[TX] Formatter allocation failure — skipping"));
            return;
        }

        bool sent = gMqtt.publish(payload.c_str());

        if (sent) {
            Serial.printf("[TX] seq=%-6u  rms=%.4f m/s²  temp=%.1f°C  %u B\n",
                gSeq,
                gLatestVib.valid  ? gLatestVib.rmsAccel_ms2 : -1.0f,
                gLatestTemp.valid ? gLatestTemp.celsius      : -1.0f,
                payload.length());
        }
    }
}

// ─── Inbound command handler ──────────────────────────────────────────────────
static void onCommand(const char* topic, const char* payload) {
    Serial.printf("[CMD] %s → %s\n", topic, payload);

    if (strstr(payload, "\"action\":\"restart\"")) {
        Serial.println(F("[CMD] Restart requested by backend"));
        delay(100);
        ESP.restart();
    }

    if (strstr(payload, "\"action\":\"reset_kalman\"")) {
        // Future: gVib.resetFilters(); gTemp.resetFilters();
        Serial.println(F("[CMD] Kalman reset acknowledged (not yet implemented)"));
    }

    if (strstr(payload, "\"action\":\"status\"")) {
        Serial.printf("[CMD] Status: uptime=%lu s  heap=%u B  rssi=%d dBm\n",
                      millis() / 1000,
                      esp_get_free_heap_size(),
                      WiFi.RSSI());
    }
}
