using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Note: Requires M2MqttUnity package from https://github.com/gpvigano/M2MqttUnity
// If not available, this will compile but MQTT functionality will be stubbed

namespace OrchestraMaestro
{
    /// <summary>
    /// MQTT client manager for Orchestra Maestro.
    /// Subscribes to gesture and stick events from the broker.
    /// 
    /// Topics:
    /// - orchestra/left_gesture_event: Left-hand gesture classification from Ultra96
    /// - orchestra/stick_stroke: Right-hand downstroke events from stick
    /// - orchestra/system_status: Optional debug/health info
    /// 
    /// Outbound:
    /// - orchestra/app_state: Visualizer state/acknowledgements
    /// - /control/visualizer: READY signal
    /// </summary>
    public class MQTTManager : MonoBehaviour
    {
        [Header("Broker Configuration")]
        [SerializeField] private string brokerAddress = "172.20.10.2";
        [SerializeField] private int brokerPort = 8883;
        [SerializeField] private bool useTLS = true;
        [SerializeField] private string clientId = "unity_visualizer";

        [Header("Topics")]
        [SerializeField] private string leftGestureTopic = "orchestra/left_gesture_event";
        [SerializeField] private string stickStrokeTopic = "orchestra/stick_stroke";
        [SerializeField] private string systemStatusTopic = "orchestra/system_status";
        [SerializeField] private string appStateTopic = "orchestra/app_state";
        [SerializeField] private string controlTopic = "/control/visualizer";

        [Header("Stick Buffer")]
        [SerializeField] private float stickBufferDuration = 2.0f; // 2 second rolling buffer

        [Header("Debug")]
        [SerializeField] private bool debugLogging = true;

        // Connection state
        private bool isConnected = false;
        private bool isConnecting = false;

        // Stick downstroke buffer (timestamps in local time)
        private Queue<float> downstrokeBuffer = new Queue<float>();

        // Events
        public event Action<LeftGestureEvent> OnGestureReceived;
        public event Action<float> OnDownstroke; // Parameter is local timestamp
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnConnectionError;

        // Singleton for easy access
        public static MQTTManager Instance { get; private set; }

        public bool IsConnected => isConnected;

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Auto-connect can be enabled here if desired
            // Connect();
        }

        private void Update()
        {
            // Clean old entries from downstroke buffer
            float cutoffTime = Time.time - stickBufferDuration;
            while (downstrokeBuffer.Count > 0 && downstrokeBuffer.Peek() < cutoffTime)
            {
                downstrokeBuffer.Dequeue();
            }
        }

        private void OnDestroy()
        {
            Disconnect();
            if (Instance == this) Instance = null;
        }

        #endregion

        #region Connection Management

        /// <summary>Connect to MQTT broker</summary>
        public void Connect()
        {
            if (isConnected || isConnecting) return;

            isConnecting = true;
            Log($"Connecting to MQTT broker at {brokerAddress}:{brokerPort}...");

            // TODO: Integrate with M2MqttUnity when available
            // For now, simulate connection for development
            StartCoroutine(SimulateConnection());
        }

        /// <summary>Disconnect from broker</summary>
        public void Disconnect()
        {
            if (!isConnected) return;

            Log("Disconnecting from MQTT broker...");
            isConnected = false;
            OnDisconnected?.Invoke();
        }

        private System.Collections.IEnumerator SimulateConnection()
        {
            yield return new WaitForSeconds(0.5f);
            
            isConnecting = false;
            isConnected = true;
            
            Log("MQTT connection established (simulated)");
            OnConnected?.Invoke();

            // Send READY signal
            PublishReady();
        }

        #endregion

        #region Message Publishing

        /// <summary>Publish READY signal to control topic</summary>
        public void PublishReady()
        {
            string payload = JsonUtility.ToJson(new ControlPacket
            {
                device_id = clientId,
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

        private void Publish(string topic, string payload)
        {
            if (!isConnected)
            {
                LogWarning($"Cannot publish to {topic}: not connected");
                return;
            }

            // TODO: Actual MQTT publish via M2Mqtt
            Log($"[MQTT OUT] {topic}: {payload}");
        }

        #endregion

        #region Message Handling

        /// <summary>
        /// Process incoming MQTT message. 
        /// Call this from M2MqttUnity message callback.
        /// </summary>
        public void HandleMessage(string topic, byte[] payload)
        {
            string message = Encoding.UTF8.GetString(payload);
            
            if (debugLogging)
            {
                Log($"[MQTT IN] {topic}: {message}");
            }

            if (topic == leftGestureTopic)
            {
                HandleGestureMessage(message);
            }
            else if (topic == stickStrokeTopic)
            {
                HandleStickMessage(message);
            }
            else if (topic == systemStatusTopic)
            {
                HandleStatusMessage(message);
            }
        }

        private void HandleGestureMessage(string json)
        {
            try
            {
                LeftGestureEvent evt = JsonUtility.FromJson<LeftGestureEvent>(json);
                Log($"Gesture received: {evt.gestureId} (clenched: {evt.isClenched})");
                OnGestureReceived?.Invoke(evt);
            }
            catch (Exception e)
            {
                LogWarning($"Failed to parse gesture message: {e.Message}");
            }
        }

        private void HandleStickMessage(string json)
        {
            try
            {
                RightStickEvent evt = JsonUtility.FromJson<RightStickEvent>(json);
                
                if (evt.type == "DOWNSTROKE")
                {
                    float localTime = Time.time;
                    downstrokeBuffer.Enqueue(localTime);
                    Log($"Downstroke at {localTime:F3}");
                    OnDownstroke?.Invoke(localTime);
                }
            }
            catch (Exception e)
            {
                LogWarning($"Failed to parse stick message: {e.Message}");
            }
        }

        private void HandleStatusMessage(string json)
        {
            // Handle system status for debug display
            Log($"System status: {json}");
        }

        #endregion

        #region Stick Buffer Queries

        /// <summary>
        /// Get all downstroke timestamps within the last N seconds.
        /// </summary>
        public List<float> GetRecentDownstrokes(float withinSeconds = -1)
        {
            if (withinSeconds < 0) withinSeconds = stickBufferDuration;

            float cutoffTime = Time.time - withinSeconds;
            List<float> result = new List<float>();

            foreach (float ts in downstrokeBuffer)
            {
                if (ts >= cutoffTime)
                {
                    result.Add(ts);
                }
            }

            return result;
        }

        /// <summary>
        /// Check if a stick pattern matches within the buffer.
        /// Used for combo gesture validation.
        /// </summary>
        /// <param name="patternIntervals">Expected intervals between strokes in seconds</param>
        /// <param name="tolerance">Timing tolerance for each interval</param>
        public bool MatchStickPattern(float[] patternIntervals, float tolerance = 0.2f)
        {
            List<float> strokes = GetRecentDownstrokes();
            
            if (strokes.Count < patternIntervals.Length + 1)
            {
                return false; // Not enough strokes
            }

            // Check intervals from most recent strokes
            int startIdx = strokes.Count - patternIntervals.Length - 1;
            
            for (int i = 0; i < patternIntervals.Length; i++)
            {
                float actualInterval = strokes[startIdx + i + 1] - strokes[startIdx + i];
                float expectedInterval = patternIntervals[i];
                
                if (Mathf.Abs(actualInterval - expectedInterval) > tolerance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Count downstrokes within a time window.
        /// </summary>
        public int CountDownstrokes(float withinSeconds)
        {
            return GetRecentDownstrokes(withinSeconds).Count;
        }

        /// <summary>
        /// Check if stick has been still (no strokes) for a duration.
        /// Used for HOLD, READY, CLEAR_CUTOFF combos.
        /// </summary>
        public bool IsStickStill(float forSeconds)
        {
            List<float> recent = GetRecentDownstrokes(forSeconds);
            return recent.Count == 0;
        }

        #endregion

        #region Dummy Input (for testing without hardware)

        /// <summary>Simulate a gesture event for testing</summary>
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

        /// <summary>Simulate a downstroke event for testing</summary>
        public void SimulateDownstroke()
        {
            float localTime = Time.time;
            downstrokeBuffer.Enqueue(localTime);
            Log($"[SIMULATED] Downstroke at {localTime:F3}");
            OnDownstroke?.Invoke(localTime);
        }

        #endregion

        #region Logging

        private void Log(string message)
        {
            if (debugLogging)
            {
                Debug.Log($"[MQTTManager] {message}");
            }
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[MQTTManager] {message}");
        }

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

        #endregion
    }
}
