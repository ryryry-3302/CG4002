using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace OrchestraMaestro
{
    /// <summary>
    /// MQTT client manager for Orchestra Maestro.
    /// Extends M2MqttUnityClient to provide real mTLS MQTT connectivity.
    /// 
    /// Subscribes to gesture and stick events from the broker.
    /// Topics:
    /// - orchestra/left_gesture_event: Left-hand gesture classification from Ultra96
    /// - orchestra/stick_stroke: Right-hand downstroke events from stick
    /// - orchestra/system_status: Optional debug/health info
    /// 
    /// Outbound:
    /// - orchestra/app_state: Visualizer state/acknowledgements
    /// - /control/visualizer: READY signal
    /// </summary>
    public class MQTTManager : M2MqttUnity.M2MqttUnityClient
    {
        [Header("Local Testing")]
        [SerializeField] private bool useLocalTesting = false;
        [SerializeField] private string localBrokerIP = "192.168.1.100";
        [SerializeField] private int localBrokerPort = 1883;

        [Header("Topics")]
        [SerializeField] private string leftGestureTopic = "orchestra/left_gesture_event";
        [SerializeField] private string stickStrokeTopic = "ar/right/stick";
        [SerializeField] private string stickBpmTopic = "ar/right/bpm";
        [SerializeField] private string systemStatusTopic = "orchestra/system_status";
        [SerializeField] private string appStateTopic = "orchestra/app_state";
        [SerializeField] private string controlTopic = "/control/visualizer";
        [SerializeField] private string rightCommandTopic = "ar/right/cmd";

        [Header("Auto Reconnect")]
        [SerializeField] private bool autoReconnect = true;
        [SerializeField] private float reconnectInterval = 5f;
        private Coroutine reconnectCoroutine;

        [Header("Stick Buffer")]
        [SerializeField] private float stickBufferDuration = 2.0f;

        [Header("Debug")]
        [SerializeField] private bool debugLogging = true;

        [Header("Test Publishing")]
        [SerializeField] private bool enableTestPublish = false;
        [SerializeField] private string testPublishTopic = "orchestra/test";
        [SerializeField] private float testPublishInterval = 1.0f;

        [Header("Test Reading")]
        [SerializeField] private bool enableTestRead = false;
        [SerializeField] private string testReadTopic = "orchestra/test";
        [SerializeField] private string latestTestReadMessage = "";

        private Coroutine testPublishCoroutine;

        // Stick downstroke buffer (timestamps in local time)
        private Queue<float> downstrokeBuffer = new Queue<float>();

        // Events
        public event Action<LeftGestureEvent> OnGestureReceived;
        public event Action<float> OnDownstroke;
        public event Action<float> OnBpmReceived; // Passes the received BPM value
        public event Action MqttConnected;
        public event Action MqttDisconnected;
        public event Action<string> OnConnectionError;

        // Singleton for easy access
        public static MQTTManager Instance { get; private set; }

        public bool IsConnected => client != null && client.IsConnected;

        #region Unity Lifecycle

        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            base.Awake();
        }

        protected override void Start()
        {
            if (GameSettings.TestMode)
            {
                Log("Test mode: skipping real MQTT connection");
                MqttConnected?.Invoke();
                PublishReady();
            }
            else
            {
                if (useLocalTesting)
                {
                    brokerAddress = localBrokerIP;
                    brokerPort = localBrokerPort;
                    isEncrypted = false;
                    Debug.Log($"[MQTTManager] Local testing enabled. Overriding broker to {brokerAddress}:{brokerPort}");
                }

                // Set autoConnect = false so we control when to connect
                autoConnect = false;
                base.Start();
                Connect();
            }
        }

        protected override void Update()
        {
            base.Update();

            // Clean old entries from downstroke buffer
            float cutoffTime = Time.time - stickBufferDuration;
            while (downstrokeBuffer.Count > 0 && downstrokeBuffer.Peek() < cutoffTime)
                downstrokeBuffer.Dequeue();
        }

        protected override void OnApplicationQuit()
        {
            base.OnApplicationQuit();
            if (Instance == this) Instance = null;
        }

        #endregion

        #region M2MqttUnityClient Overrides

        protected override void SubscribeTopics()
        {
            List<string> topics = new List<string>
            {
                leftGestureTopic,
                stickStrokeTopic,
                stickBpmTopic,
                systemStatusTopic
            };

            List<byte> qosLevels = new List<byte>
            {
                MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE,
                MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE,
                MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE, // Changed this line previously missing a value
                MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE
            };

            if (enableTestRead && !string.IsNullOrWhiteSpace(testReadTopic))
            {
                topics.Add(testReadTopic);
                qosLevels.Add(MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE);
            }

            client.Subscribe(topics.ToArray(), qosLevels.ToArray());
            Log($"Subscribed to topics: {string.Join(", ", topics)}");
        }

        protected override void UnsubscribeTopics()
        {
            List<string> topics = new List<string>
            {
                leftGestureTopic,
                stickStrokeTopic,
                stickBpmTopic,
                systemStatusTopic
            };

            if (enableTestRead && !string.IsNullOrWhiteSpace(testReadTopic))
                topics.Add(testReadTopic);

            client.Unsubscribe(topics.ToArray());
        }

        protected override void OnConnected()
        {
            base.OnConnected();
            Log($"Connected to MQTT broker at {brokerAddress}:{brokerPort}");
            if (reconnectCoroutine != null)
            {
                StopCoroutine(reconnectCoroutine);
                reconnectCoroutine = null;
            }
            MqttConnected?.Invoke();
            PublishReady();
            if (enableTestPublish)
                StartTestPublish();
        }

        protected override void OnDisconnected()
        {
            base.OnDisconnected();
            Log("Disconnected from MQTT broker");
            StopTestPublish();
            MqttDisconnected?.Invoke();
            HandleReconnection();
        }

        protected override void OnConnectionFailed(string errorMessage)
        {
            base.OnConnectionFailed(errorMessage);
            Debug.LogError($"[MQTTManager] Connection failed: {errorMessage}");
            OnConnectionError?.Invoke(errorMessage);
            HandleReconnection();
        }

        private void HandleReconnection()
        {
            if (autoReconnect && reconnectCoroutine == null && !GameSettings.TestMode)
            {
                reconnectCoroutine = StartCoroutine(ReconnectRoutine());
            }
        }

        private IEnumerator ReconnectRoutine()
        {
            Log($"Attempting to reconnect in {reconnectInterval} seconds...");
            yield return new WaitForSeconds(reconnectInterval);
            reconnectCoroutine = null;
            Log("Reconnecting...");
            Connect();
        }

        protected override void DecodeMessage(string topic, byte[] payload)
        {
            string message = Encoding.UTF8.GetString(payload);

            if (debugLogging)
                Log($"[MQTT IN] {topic}: {message}");

            if (topic == leftGestureTopic)
                HandleGestureMessage(message);
            else if (topic == stickStrokeTopic)
                HandleStickMessage(message);
            else if (topic == stickBpmTopic)
                HandleBpmMessage(message);
            else if (topic == systemStatusTopic)
                HandleStatusMessage(message);
            else if (enableTestRead && topic == testReadTopic)
                HandleTestReadMessage(message);
        }

        #endregion

        #region Message Publishing

        /// <summary>Publish READY signal to control topic</summary>
        public void PublishReady()
        {
            string payload = JsonUtility.ToJson(new ControlPacket
            {
                device_id = mqttClientId,
                status = "READY",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            Publish(controlTopic, payload);
            Log($"Published READY to {controlTopic}");
        }

        /// <summary>Publish app state update</summary>
        public void PublishAppState(string state)
        {
            string payload = JsonUtility.ToJson(new AppStatePacket
            {
                state = state,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            Publish(appStateTopic, payload);
        }

        public void PublishRightCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(rightCommandTopic))
            {
                LogWarning("Cannot publish right command: rightCommandTopic is empty");
                return;
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                LogWarning("Cannot publish right command: command is empty");
                return;
            }

            Publish(rightCommandTopic, command.Trim().ToUpperInvariant());
        }

        private void Publish(string topic, string payload)
        {
            if (!IsConnected)
            {
                LogWarning($"Cannot publish to {topic}: not connected");
                return;
            }
            client.Publish(topic, Encoding.UTF8.GetBytes(payload), MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE, false);
            Log($"[MQTT OUT] {topic}: {payload}");
        }

        #endregion

        #region Message Handling

        private void HandleGestureMessage(string json)
        {
            try
            {
                LeftGestureEvent evt = JsonUtility.FromJson<LeftGestureEvent>(json);
                evt.Normalize();
                
                Log($"Gesture received: {evt.gestureId} (clenched: {evt.isClenched})");
                OnGestureReceived?.Invoke(evt);
            }
            catch (Exception e)
            {
                LogWarning($"Failed to parse gesture message: {e.Message}");
            }
        }

        private void HandleStickMessage(string message)
        {
            string normalized = (message ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                LogWarning("Received empty stick message");
                return;
            }

            string strokeType = null;

            if (normalized.StartsWith("{"))
            {
                try
                {
                    RightStickEvent evt = JsonUtility.FromJson<RightStickEvent>(normalized);
                    strokeType = evt.type;
                }
                catch (Exception e)
                {
                    LogWarning($"Failed to parse stick JSON message: {e.Message}");
                }
            }

            if (string.IsNullOrWhiteSpace(strokeType))
            {
                if (string.Equals(normalized, "DOWN", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalized, "DOWNSTROKE", StringComparison.OrdinalIgnoreCase))
                {
                    strokeType = normalized;
                }
                else if (string.Equals(normalized, "UP", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(normalized, "UPSTROKE", StringComparison.OrdinalIgnoreCase))
                {
                    strokeType = normalized;
                }
            }

            if (string.Equals(strokeType, "DOWN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(strokeType, "DOWNSTROKE", StringComparison.OrdinalIgnoreCase))
            {
                float localTime = Time.time;
                downstrokeBuffer.Enqueue(localTime);
                Log($"Downstroke at {localTime:F3}");
                OnDownstroke?.Invoke(localTime);
            }
            else if (string.Equals(strokeType, "UP", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(strokeType, "UPSTROKE", StringComparison.OrdinalIgnoreCase))
            {
                // We don't currently have an OnUpstroke event, but we acknowledge receipt.
                Log($"Upstroke received at {Time.time:F3}");
            }
            else if (debugLogging)
            {
                Log($"Ignored stick message: {normalized}");
            }
        }

        private void HandleBpmMessage(string message)
        {
            string normalized = (message ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized)) return;

            try
            {
                BpmEvent evt = JsonUtility.FromJson<BpmEvent>(normalized);
                Log($"BPM received: {evt.bpm}");
                OnBpmReceived?.Invoke(evt.bpm);
            }
            catch (Exception e)
            {
                LogWarning($"Failed to parse BPM message: {e.Message}");
            }
        }

        private void HandleStatusMessage(string json)
        {
            Log($"System status: {json}");
        }

        private void HandleTestReadMessage(string message)
        {
            latestTestReadMessage = message;
            Log($"[TEST READ] {testReadTopic}: {message}");
        }

        #endregion

        #region Stick Buffer Queries

        public List<float> GetRecentDownstrokes(float withinSeconds = -1)
        {
            if (withinSeconds < 0) withinSeconds = stickBufferDuration;
            float cutoffTime = Time.time - withinSeconds;
            List<float> result = new List<float>();
            foreach (float ts in downstrokeBuffer)
                if (ts >= cutoffTime) result.Add(ts);
            return result;
        }

        public bool MatchStickPattern(float[] patternIntervals, float tolerance = 0.2f)
        {
            List<float> strokes = GetRecentDownstrokes();
            if (strokes.Count < patternIntervals.Length + 1) return false;
            int startIdx = strokes.Count - patternIntervals.Length - 1;
            for (int i = 0; i < patternIntervals.Length; i++)
            {
                float actualInterval = strokes[startIdx + i + 1] - strokes[startIdx + i];
                if (Mathf.Abs(actualInterval - patternIntervals[i]) > tolerance) return false;
            }
            return true;
        }

        public int CountDownstrokes(float withinSeconds) => GetRecentDownstrokes(withinSeconds).Count;

        public bool IsStickStill(float forSeconds) => GetRecentDownstrokes(forSeconds).Count == 0;

        #endregion

        #region Dummy Input (for testing without hardware)

        public void SimulateGesture(string gestureId, bool isClenched = true)
        {
            LeftGestureEvent evt = new LeftGestureEvent
            {
                gestureId = gestureId,
                isClenched = isClenched,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                confidence = 1.0f
            };
            Log($"[SIMULATED] Gesture: {gestureId}");
            OnGestureReceived?.Invoke(evt);
        }

        public void SimulateDownstroke()
        {
            float localTime = Time.time;
            downstrokeBuffer.Enqueue(localTime);
            Log($"[SIMULATED] Downstroke at {localTime:F3}");
            OnDownstroke?.Invoke(localTime);
        }

        /// <summary>Start continuously publishing "test" to testPublishTopic every testPublishInterval seconds.</summary>
        public void StartTestPublish()
        {
            StopTestPublish();
            testPublishCoroutine = StartCoroutine(TestPublishLoop());
            Log($"Test publish started on topic '{testPublishTopic}' every {testPublishInterval}s");
        }

        /// <summary>Stop the test publish loop.</summary>
        public void StopTestPublish()
        {
            if (testPublishCoroutine != null)
            {
                StopCoroutine(testPublishCoroutine);
                testPublishCoroutine = null;
                Log("Test publish stopped");
            }
        }

        private IEnumerator TestPublishLoop()
        {
            while (true)
            {
                Publish(testPublishTopic, "test");
                yield return new WaitForSeconds(testPublishInterval);
            }
        }

        #endregion

        #region Logging

        private void Log(string message)
        {
            if (debugLogging) Debug.Log($"[MQTTManager] {message}");
        }

        private void LogWarning(string message) => Debug.LogWarning($"[MQTTManager] {message}");

        #endregion

        #region JSON Packet Classes

        [Serializable]
        private class ControlPacket
        {
            public string device_id;
            public string status;
            public long timestamp;
        }

        [Serializable]
        private class AppStatePacket
        {
            public string state;
            public long timestamp;
        }

        [Serializable]
        private class BpmEvent
        {
            public float bpm;
            public long time;
        }

        #endregion
    }
}
