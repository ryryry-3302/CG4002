#include <Arduino.h>
#include <Wire.h>
#include "MPU6050.h"
#include <WiFi.h>
#include <mqtt_client.h>
#include <time.h>
#include <secrets.h>     

enum PatternId : uint8_t { 
  PAT_NONE = 0, PAT_BEAT, PAT_DOWNBEAT, PAT_START, 
  PAT_SELECT, PAT_WRONG, PAT_MISS, PAT_END, PAT_CONN_LOST 
};

struct Step { bool on; uint16_t durMs; };
struct PatternDef { const Step* steps; uint8_t count; bool repeat; };

MPU6050 imu; 

const char *ssid = "ipohne";
const char *pass = "passworddd";

IPAddress local_IP(172, 20, 10, 4); // Set your desired IP
IPAddress gateway(172, 20, 10, 1);   // Set your router gateway
IPAddress subnet(255, 255, 255, 240);  // Set subnet mask
IPAddress primaryDNS(8, 8, 8, 8);    // Optional

esp_mqtt_client_handle_t client;
bool mqttConnected = false;

PatternId current = PAT_NONE;
uint8_t stepIdx = 0;
uint32_t stepStartMs = 0;

bool scriptRunning = false;
bool specialActive = false;
uint32_t scriptStartMs = 0;
uint32_t nextBeatMs = 0;
uint32_t beatCount = 0;
const uint16_t DEMO_BPM = 120;

// IMU State
static const uint32_t IMU_HZ = 100;
static const uint32_t IMU_DTUS = 1000000UL / IMU_HZ;
uint32_t lastImuUs = 0;
enum AxisSel { AXIS_X, AXIS_Y, AXIS_Z };
static const AxisSel VERT_AXIS = AXIS_X;
static const float VEL_SWING_THRESH = 2.5f;
static const float VEL_LEAK = 0.60f;
static const uint16_t FLIP_LOCKOUT_MS = 150;
int stickState = 0;
float gravityEst = 0.0f;
float stickVelocity = 0.0f;
uint32_t lastFlipMs = 0;

const uint16_t BPM_WINDOW_SIZE = 40;
uint32_t strokeIntervals[BPM_WINDOW_SIZE];
uint8_t strokeHead = 0;
uint8_t strokeCount = 0;
uint32_t lastValidStrokeMs = 0;

const uint32_t MIN_STROKE_INTERVAL = 250;  // Ignore < 250ms (max ~240 BPM, removes bounce)
const uint32_t MAX_STROKE_INTERVAL = 2000; // Reset > 2000ms (min ~30 BPM, handles pause/missed beat)

// LED blink interval
static const uint32_t LED_BLINK_MS = 300;

// Added onboard LED lights as indicator of statuses. ADDED IN WEEK 10
void updateStatusLed() {
  static uint32_t lastBlinkMs = 0;
  static bool blinkState = false;

  // If connected to wifi, led stays on
  if (WiFi.status() == WL_CONNECTED) {
    digitalWrite(LED_BUILTIN, HIGH);
    return;
  }

  // If not connected to wifi but is powered on, blink
  uint32_t now = millis();
  if (now - lastBlinkMs >= LED_BLINK_MS) {
    lastBlinkMs = now;
    blinkState  = !blinkState ;
    digitalWrite(LED_BUILTIN, blinkState ? HIGH : LOW);
  }
}

const Step STEPS_BEAT[]      = { {true, 90},  {false, 10} };
const Step STEPS_DOWNBEAT[]  = { {true, 110}, {false, 80}, {true, 110}, {false, 10} };
const Step STEPS_START[]     = { {true, 90},  {false, 80}, {true, 90},  {false, 80}, {true, 90}, {false, 10} };
const Step STEPS_SELECT[]    = { {true, 140}, {false, 10} };
const Step STEPS_WRONG[]     = { {true, 120}, {false, 80}, {true, 120}, {false, 80}, {true, 120}, {false, 250} };
const Step STEPS_MISS[]      = { {true, 500}, {false, 250} };
const Step STEPS_END[]       = { {true, 200}, {false, 120}, {true, 120}, {false, 10} };
const Step STEPS_CONN_LOST[] = { {true, 150}, {false, 850} };

const PatternDef PATTERNS[] = {
  {nullptr, 0, false}, {STEPS_BEAT, 2, false}, {STEPS_DOWNBEAT, 4, false}, {STEPS_START, 6, false},
  {STEPS_SELECT, 2, false}, {STEPS_WRONG, 6, false}, {STEPS_MISS, 2, false}, {STEPS_END, 4, false}, {STEPS_CONN_LOST, 2, true}
};

void handleCommand(const String &cmdRaw);

void motorOn()  { digitalWrite(25, HIGH); }
void motorOff() { digitalWrite(25, LOW); }

void publishStickState(const char* stateMsg) {
  if (mqttConnected) {
    esp_mqtt_client_publish(client, "ar/right/stick", stateMsg, 0, 0, 0);
  }
}

void stopPattern() { current = PAT_NONE; stepIdx = 0; motorOff(); }

void startPattern(PatternId id) {
  current = id;
  stepIdx = 0; stepStartMs = millis();
  const PatternDef &p = PATTERNS[current];
  if (p.count == 0) { current = PAT_NONE; motorOff(); return; }
  if (p.steps[0].on) motorOn(); else motorOff();
}

bool updateHaptics() {
  if (current == PAT_NONE) return false;
  const PatternDef &p = PATTERNS[current];
  const Step &s = p.steps[stepIdx];
  uint32_t now = millis();
  if (s.durMs == 0 || (now - stepStartMs) >= s.durMs) {
    stepIdx++;
    stepStartMs = now;
    if (stepIdx >= p.count) {
      if (p.repeat) stepIdx = 0;
      else { stopPattern(); return true; }
    }
    const Step &ns = p.steps[stepIdx];
    if (ns.on) motorOn(); else motorOff();
  }
  return false;
}

bool syncTime() {
  const uint32_t NTP_TIMEOUT_MS = 30000; 
  uint32_t startAttemptTime = millis();
  configTime(0, 0, "pool.ntp.org", "time.nist.gov");
  // Serial.print("Waiting for NTP time sync: ");
  esp_mqtt_client_publish(client, "ar/right/status", "Waiting for NTP time sync", 0, 0, 0);
  time_t now = time(nullptr);
  while (now < 8 * 3600 * 2 && (millis() - startAttemptTime < NTP_TIMEOUT_MS)) {
    delay(500);
    // Serial.print(".");
    now = time(nullptr);
  }
  if (now < 8 * 3600 * 2) {
    // Serial.println("\n[CRITICAL] Time sync failed.");
    esp_mqtt_client_publish(client, "ar/right/status", "Time sync failed", 0, 0, 0);
    return false;
  }
  // Serial.println("\nTime synchronized");
  esp_mqtt_client_publish(client, "ar/right/status", "Time synchronized", 0, 0, 0);
  return true;
}

void handleMQTT(void *handler_args, esp_event_base_t base, int32_t event_id, void *event_data) {
    auto *event = static_cast<esp_mqtt_event_handle_t>(event_data);
    switch (event_id) {
        case MQTT_EVENT_CONNECTED:
            // Serial.println("MQTT_EVENT_CONNECTED");
            esp_mqtt_client_publish(client, "ar/right/status", "MQTT Connected - Right Firebeetle", 0, 0, 0);
            mqttConnected = true;
            esp_mqtt_client_subscribe(client, "ar/right/cmd", 0);

            // Publish online status
            esp_mqtt_client_publish(client, "ar/right/status", "online", 0, 1, 1);
            break;
        case MQTT_EVENT_DATA: {
            String msg = String((char*)event->data).substring(0, event->data_len);
            msg.trim();
            if (String(event->topic).substring(0, event->topic_len) == "ar/right/cmd") {
                handleCommand(msg);
            }
            break;
        }
        case MQTT_EVENT_DISCONNECTED:
            mqttConnected = false;
            break;
        case MQTT_EVENT_ERROR:
            // Serial.println("MQTT_EVENT_ERROR");
            esp_mqtt_client_publish(client, "ar/right/status", "MQTT Event Error", 0, 0, 0);
            break;
        default:
            break;
    }
}

void connectWiFi() {
  WiFi.mode(WIFI_STA);
  if (!WiFi.config(local_IP, gateway, subnet, primaryDNS)) {
    // Serial.println("STA Failed to configure");
    esp_mqtt_client_publish(client, "ar/right/status", "STA Failed to configure", 0, 0, 0);
  }
  WiFi.begin(ssid, pass);
    while (WiFi.status() != WL_CONNECTED) {
    updateStatusLed();
    delay(10);
    // Serial.print(".");
  }

  digitalWrite(LED_BUILTIN, HIGH);
  // Serial.println("\nWiFi OK: " + WiFi.localIP().toString());
  String ipMsg = "WiFi OK: " + WiFi.localIP().toString();
  esp_mqtt_client_publish(client, "ar/right/status", ipMsg.c_str(), 0, 1, 1);
}


void setupMQTTS() {
  if(syncTime()){
    esp_mqtt_client_config_t mqtt_cfg = {};
    mqtt_cfg.broker.address.uri = "mqtts://172.20.10.2:8883";
    mqtt_cfg.credentials.client_id = "firebeetle_right";

    mqtt_cfg.session.last_will.topic = "ar/right/status";
    mqtt_cfg.session.last_will.msg = "right-fb-offline";
    mqtt_cfg.session.last_will.qos = 1;
    mqtt_cfg.session.last_will.retain = true;

    mqtt_cfg.broker.verification.certificate = root_ca;
    mqtt_cfg.credentials.authentication.certificate = client_crt;
    mqtt_cfg.credentials.authentication.key = client_key;

    client = esp_mqtt_client_init(&mqtt_cfg);
    esp_mqtt_client_register_event(client, (esp_mqtt_event_id_t)ESP_EVENT_ANY_ID, handleMQTT, NULL);
    esp_mqtt_client_start(client);
  } else {
    ESP.restart();
  }
}


static inline uint32_t beatIntervalMs(uint16_t bpm) {
  if (bpm < 1) bpm = 1;
  return 60000UL / (uint32_t)bpm;
}

void startDemoScript() {
  scriptRunning = true; specialActive = true; scriptStartMs = millis();
  nextBeatMs = scriptStartMs; beatCount = 0;
  startPattern(PAT_START);
}

void stopDemoScript() { scriptRunning = false; specialActive = false; } 

void updateDemoScript() {
  if (!scriptRunning) return;
  uint32_t now = millis();
  if ((uint32_t)(now - scriptStartMs) >= 60000UL) { scriptRunning = false; return; }
  if (specialActive) return;
  if ((int32_t)(now - nextBeatMs) >= 0) {
    uint8_t posInBar = (uint8_t)(beatCount % 4);
    if (posInBar == 0) startPattern(PAT_DOWNBEAT); 
    else startPattern(PAT_BEAT);
    beatCount++;
    nextBeatMs = now + beatIntervalMs(DEMO_BPM);
  }
}

void handleCommand(const String &cmdRaw) {
  String cmd = cmdRaw; cmd.trim();
  if (cmd.length() == 0) return;
  String up = cmd; up.toUpperCase();
  if (up == "STOP_ALL") { stopDemoScript(); stopPattern(); return; }
  if (up == "READY") { if (current == PAT_CONN_LOST) stopPattern(); specialActive = false; return; }
  if (up == "START") { startDemoScript(); return; }
  if (up == "BEAT") { startPattern(PAT_BEAT); specialActive = true; }
  else if (up == "DOWNBEAT") { startPattern(PAT_DOWNBEAT); specialActive = true; }
  else if (up == "SELECT") { startPattern(PAT_SELECT); specialActive = true; }
  else if (up == "WRONG") { startPattern(PAT_WRONG); specialActive = true; }
  else if (up == "MISS") { startPattern(PAT_MISS); specialActive = true; }
  else if (up == "END") { startPattern(PAT_END); specialActive = true; }
  else if (up == "CONN_LOST") { startPattern(PAT_CONN_LOST); specialActive = true; }
}

void handleIncomingCommands() {
  while (Serial.available()) {
    String line = Serial.readStringUntil('\n');
    handleCommand(line);
  }
}

static inline float rawToG(int16_t a_raw) { return (float)a_raw / 16384.0f; }

static inline float getVertAccelG(int16_t ax_raw, int16_t ay_raw, int16_t az_raw) {
  if (VERT_AXIS == AXIS_X) return rawToG(ax_raw);
  if (VERT_AXIS == AXIS_Y) return rawToG(ay_raw);
  return rawToG(az_raw);
} 

void updateStickUpDown() {
  uint32_t nowUs = micros();
  if ((uint32_t)(nowUs - lastImuUs) < IMU_DTUS) return;
  lastImuUs += IMU_DTUS;
  int16_t ax, ay, az; imu.getAcceleration(&ax, &ay, &az);
  float rawVertG = getVertAccelG(ax, ay, az);
  if (gravityEst == 0.0f) gravityEst = rawVertG;
  gravityEst = (0.02f * rawVertG) + (0.98f * gravityEst);

  // Serial.print("Gravity_Estimate:"); // Label for the Plotter
  // Serial.println(gravityEst);        // The actual value to plot

  float dynamicG = rawVertG - gravityEst;
  stickVelocity = (stickVelocity + dynamicG) * VEL_LEAK;
  uint32_t nowMs = millis();
  int prev = stickState;
  if ((nowMs - lastFlipMs) > FLIP_LOCKOUT_MS) {
    if (stickVelocity > VEL_SWING_THRESH) stickState = 1;
    else if (stickVelocity < -VEL_SWING_THRESH) stickState = 0;
    else stickState = 2;
    // if (stickState == 0 && stickVelocity > VEL_SWING_THRESH) stickState = 1;
    // else if (stickState == 1 && stickVelocity < -VEL_SWING_THRESH) stickState = 0;
  }
  if (stickState != prev) {
    lastFlipMs = nowMs;

    if (stickState == 0 || stickState == 1) { // 0 is DOWN, 1 is UP. Ignore 2 (NEUTRAL)
      uint32_t interval = (lastValidStrokeMs > 0) ? (nowMs - lastValidStrokeMs) : 0;
      
      bool isBounce = (lastValidStrokeMs > 0 && interval < MIN_STROKE_INTERVAL);
      
      if (!isBounce) {
        if (interval > MAX_STROKE_INTERVAL) {
          // Gap too big, reset the BPM window (missed beat or pause)
          strokeCount = 0;
          strokeHead = 0;
        } else if (lastValidStrokeMs > 0) {
          // Valid stroke, record the interval
          strokeIntervals[strokeHead] = interval;
          strokeHead = (strokeHead + 1) % BPM_WINDOW_SIZE;
          if (strokeCount < BPM_WINDOW_SIZE) strokeCount++;
          
          float weightedSum = 0.0f;
          float weightTotal = 0.0f;
          float currentWeight = 1.0f;
          const float DECAY_FACTOR = 0.6f; // Much heavier weight on most recent strokes

          for (int i = 0; i < strokeCount; i++) {
            // Iterate backwards from most recent stroke
            int index = (strokeHead - 1 - i + BPM_WINDOW_SIZE) % BPM_WINDOW_SIZE;
            weightedSum += strokeIntervals[index] * currentWeight;
            weightTotal += currentWeight;
            currentWeight *= DECAY_FACTOR;
          }
          
          float avgInterval = weightedSum / weightTotal;
          float bpm = 60000.0f / avgInterval;
          // Serial.print("BPM: ");
          // Serial.println(bpm);

          if (mqttConnected) {
            time_t nowEpoch = time(nullptr);
            String payload = "{\"bpm\":" + String(bpm, 2) + ",\"time\":" + String(nowEpoch) + "}";
            esp_mqtt_client_publish(client, "ar/right/bpm", payload.c_str(), 0, 0, 0);
          }
        }
        lastValidStrokeMs = nowMs;
      }
    }

    const char* label = (stickState == 1) ? "UP" : (stickState == 2) ? "NEUTRAL" : "DOWN";
    // const char* label = (stickState == 1) ? "UP" : "DOWN";
    // Serial.println(label);
    publishStickState(label);
  }
}

void setup() {
  pinMode(LED_BUILTIN, OUTPUT);
  digitalWrite(LED_BUILTIN, LOW);
  pinMode(25, OUTPUT);
  motorOff();
  Serial.begin(115200);
  delay(100);

  connectWiFi(); 
  setupMQTTS(); 

  Wire.begin();
  imu.initialize();
  if (!imu.testConnection()) { 
    // Serial.println("NO IMU detected");
    esp_mqtt_client_publish(client, "ar/right/status", "NO IMU detected", 0, 1, 1);
  }
    lastImuUs = micros();
}

void loop() {
  static unsigned long lastReconnectAttempt = 0;
  updateStatusLed();
  if (WiFi.status() != WL_CONNECTED && millis() - lastReconnectAttempt >= 5000) {
    WiFi.begin(ssid, pass);
    lastReconnectAttempt = millis();
  }

  handleIncomingCommands();           
  bool finished = updateHaptics();    
  if (specialActive && finished) specialActive = false;
  updateDemoScript();                 
  updateStickUpDown();
  delay(1);
}