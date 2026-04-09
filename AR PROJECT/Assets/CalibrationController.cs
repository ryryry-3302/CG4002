using System;
using UnityEngine;

namespace OrchestraMaestro
{
    /// <summary>
    /// Manages left glove calibration UI with countdown timer and progress bar.
    /// Renders full-screen OnGUI overlay during 10-second calibration period.
    /// </summary>
    public class CalibrationController : MonoBehaviour
    {
        public static CalibrationController Instance { get; private set; }

        // Calibration state
        private bool isCalibrating = false;
        private float calibrationStartTime = 0f;
        private const float CALIBRATION_DURATION = 10f;
        private int currentCountdown = 10;
        private float progressPercent = 0f;

        // UI Styles
        private GUIStyle overlayStyle;
        private GUIStyle panelStyle;
        private GUIStyle headerStyle;
        private GUIStyle bodyStyle;
        private GUIStyle countdownStyle;
        private GUIStyle statusStyle;
        private GUIStyle progressBarBgStyle;
        private GUIStyle progressBarFillStyle;
        private GUIStyle progressBarBorderStyle;
        private bool stylesInitialized = false;

        // Events
        public event Action OnCalibrationComplete;

        // Completion flag
        private bool completionHandled = false;

        /// <summary>Whether calibration overlay/flow is currently active.</summary>
        public bool IsCalibrating => isCalibrating;

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

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!isCalibrating) return;

            float elapsed = Time.time - calibrationStartTime;
            float remaining = CALIBRATION_DURATION - elapsed;

            if (remaining <= 0)
            {
                CompleteCalibration();
                return;
            }

            currentCountdown = Mathf.CeilToInt(remaining);
            progressPercent = elapsed / CALIBRATION_DURATION;
        }

        private void OnGUI()
        {
            if (!isCalibrating) return;

            InitStyles();

            // Full-screen dark overlay
            GUI.depth = -200; // Render on top of everything
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "", overlayStyle);

            // Scale for consistent sizing
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * 2.5f);

            // Centered panel (400x320 at scaled size)
            float panelWidth = 400f;
            float panelHeight = 320f;
            float x = (Screen.width / 2.5f - panelWidth) / 2f;
            float y = (Screen.height / 2.5f - panelHeight) / 2f;

            GUILayout.BeginArea(new Rect(x, y, panelWidth, panelHeight), panelStyle);

            // Header
            GUILayout.Space(20);
            GUILayout.Label("CALIBRATION", headerStyle);
            GUILayout.Space(20);

            // Instructions
            GUILayout.Label("Repeatedly open and close your\nLEFT HAND", bodyStyle);
            GUILayout.Space(40);

            // Progress bar
            DrawProgressBar(x, y);

            GUILayout.Space(40);

            // Status text
            bool testMode = MQTTManager.Instance == null || !MQTTManager.Instance.IsConnected;
            string statusText = testMode 
                ? "TEST MODE - UI Only (MQTT disconnected)" 
                : "Calibrating flex sensor...";
            GUILayout.Label(statusText, statusStyle);

            GUILayout.EndArea();

            // Reset matrix
            GUI.matrix = Matrix4x4.identity;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Start calibration sequence. Sends CAL command to ESP32 via MQTT.
        /// </summary>
        public void StartCalibration()
        {
            if (isCalibrating) return;

            Debug.Log("[CalibrationController] Starting calibration");

            isCalibrating = true;
            calibrationStartTime = Time.time;
            currentCountdown = 10;
            progressPercent = 0f;
            completionHandled = false;

            // Send CAL command to ESP32 (if MQTT available)
            SendCalibrationCommand();

            // Subscribe to ESP32 completion event (if MQTT available)
            if (MQTTManager.Instance != null)
            {
                MQTTManager.Instance.OnLeftCalibrationComplete -= HandleCalibrationComplete;
                MQTTManager.Instance.OnLeftCalibrationComplete += HandleCalibrationComplete;
            }
            else
            {
                Debug.LogWarning("[CalibrationController] MQTTManager not available - calibration will run in UI-only mode");
            }
        }

        /// <summary>
        /// Manually stop calibration (for cancel/error scenarios).
        /// </summary>
        public void StopCalibration()
        {
            if (!isCalibrating) return;

            Debug.Log("[CalibrationController] Stopping calibration");
            isCalibrating = false;

            if (MQTTManager.Instance != null)
            {
                MQTTManager.Instance.OnLeftCalibrationComplete -= HandleCalibrationComplete;
            }
        }

        #endregion

        #region Private Methods

        private void SendCalibrationCommand()
        {
            if (MQTTManager.Instance == null)
            {
                Debug.LogWarning("[CalibrationController] MQTTManager not found - running in TEST MODE (UI only)");
                return;
            }

            if (!MQTTManager.Instance.IsConnected)
            {
                Debug.LogWarning("[CalibrationController] MQTT not connected - running in TEST MODE (UI only)");
                return;
            }

            // Send CAL command to ar/left/cmd topic
            MQTTManager.Instance.PublishLeftCommand("CAL");
            Debug.Log("[CalibrationController] Sent CAL command to left glove");
        }

        private void CompleteCalibration()
        {
            if (completionHandled) return;
            completionHandled = true;

            Debug.Log("[CalibrationController] Calibration complete (timer expired)");

            isCalibrating = false;
            progressPercent = 1f;

            // Unsubscribe from ESP32 events
            if (MQTTManager.Instance != null)
            {
                MQTTManager.Instance.OnLeftCalibrationComplete -= HandleCalibrationComplete;
            }

            // Notify listeners
            OnCalibrationComplete?.Invoke();
        }

        private void HandleCalibrationComplete()
        {
            if (completionHandled) return;

            Debug.Log("[CalibrationController] Calibration complete (ESP32 confirmation received)");
            CompleteCalibration();
        }

        private void DrawProgressBar(float panelX, float panelY)
        {
            float barWidth = 340f;
            float barHeight = 24f;
            float barX = 30f;
            float barY = 210f;

            // Background
            Rect bgRect = new Rect(barX, barY, barWidth, barHeight);
            GUI.Box(bgRect, "", progressBarBgStyle);

            // Fill
            float fillWidth = barWidth * progressPercent;
            if (fillWidth > 0)
            {
                Rect fillRect = new Rect(barX, barY, fillWidth, barHeight);
                GUI.Box(fillRect, "", progressBarFillStyle);
            }

            // Border
            GUI.Box(bgRect, "", progressBarBorderStyle);

            // Percentage text
            int percent = Mathf.RoundToInt(progressPercent * 100f);
            GUI.Label(new Rect(barX, barY + barHeight + 5, barWidth, 20), 
                      $"{percent}%", statusStyle);
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;
            stylesInitialized = true;

            // Full-screen overlay (dark transparent)
            overlayStyle = new GUIStyle();
            overlayStyle.normal.background = MakeTex(1, 1, new Color(0f, 0f, 0f, 0.85f));

            // Main panel (dark blue-tinted)
            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = MakeTex(1, 1, new Color(0.05f, 0.05f, 0.15f, 0.95f));
            panelStyle.padding = new RectOffset(20, 20, 20, 20);

            // Header text (large, bold, white)
            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 24;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = new Color(1f, 1f, 1f);
            headerStyle.alignment = TextAnchor.MiddleCenter;

            // Body text (medium, light blue)
            bodyStyle = new GUIStyle(GUI.skin.label);
            bodyStyle.fontSize = 14;
            bodyStyle.normal.textColor = new Color(0.9f, 0.9f, 1f);
            bodyStyle.alignment = TextAnchor.MiddleCenter;
            bodyStyle.wordWrap = true;

            // Countdown number (huge, bold, cyan)
            countdownStyle = new GUIStyle(GUI.skin.label);
            countdownStyle.fontSize = 72;
            countdownStyle.fontStyle = FontStyle.Bold;
            countdownStyle.normal.textColor = new Color(0.2f, 0.9f, 1f);
            countdownStyle.alignment = TextAnchor.MiddleCenter;

            // Status text (small, gray)
            statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.fontSize = 11;
            statusStyle.normal.textColor = new Color(0.7f, 0.7f, 0.8f);
            statusStyle.alignment = TextAnchor.MiddleCenter;

            // Progress bar background (dark)
            progressBarBgStyle = new GUIStyle();
            progressBarBgStyle.normal.background = MakeTex(1, 1, new Color(0.1f, 0.1f, 0.15f, 1f));

            // Progress bar fill (green gradient)
            progressBarFillStyle = new GUIStyle();
            progressBarFillStyle.normal.background = MakeTex(1, 1, new Color(0.2f, 0.8f, 0.3f, 1f));

            // Progress bar border (white)
            progressBarBorderStyle = new GUIStyle();
            progressBarBorderStyle.normal.background = null;
            progressBarBorderStyle.border = new RectOffset(1, 1, 1, 1);
        }

        private static Texture2D MakeTex(int w, int h, Color color)
        {
            var tex = new Texture2D(w, h);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    tex.SetPixel(x, y, color);
            tex.Apply();
            return tex;
        }

        #endregion
    }
}
