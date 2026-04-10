using UnityEngine;
using System.Collections.Generic;

namespace OrchestraMaestro
{
    /// <summary>
    /// Displays debug logs on screen for mobile testing.
    /// Attach to any GameObject in the scene.
    /// </summary>
    public class MobileDebugLog : MonoBehaviour
    {
        [Header("Display Settings")]
        [SerializeField] private int maxMessages = 15;
        [SerializeField] private int fontSize = 24;
        [SerializeField] private bool showOnStart = false;

        private const bool EnableOverlay = false;
        
        private List<string> logMessages = new List<string>();
        private bool isVisible = true;
        private Vector2 scrollPosition;
        private GUIStyle logStyle;
        private GUIStyle buttonStyle;
        
        private static MobileDebugLog instance;

        private void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            isVisible = false; // Always start hidden
            Application.logMessageReceived += HandleLog;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= HandleLog;
            if (instance == this) instance = null;
        }

        private void HandleLog(string logString, string stackTrace, LogType type)
        {
            string prefix = type switch
            {
                LogType.Error => "<color=red>[ERR]</color> ",
                LogType.Warning => "<color=yellow>[WARN]</color> ",
                _ => ""
            };
            
            // Truncate long messages
            string msg = logString.Length > 100 ? logString.Substring(0, 100) + "..." : logString;
            logMessages.Add(prefix + msg);
            
            // Keep only recent messages
            while (logMessages.Count > maxMessages)
            {
                logMessages.RemoveAt(0);
            }
            
            // Auto-scroll to bottom
            scrollPosition.y = float.MaxValue;
        }

        private void OnGUI()
        {
            if (!EnableOverlay) return;

            // Initialize styles
            if (logStyle == null)
            {
                logStyle = new GUIStyle(GUI.skin.label);
                logStyle.fontSize = fontSize;
                logStyle.richText = true;
                logStyle.wordWrap = true;
                
                buttonStyle = new GUIStyle(GUI.skin.button);
                buttonStyle.fontSize = fontSize;
            }
            
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            
            float scaledWidth = Screen.width / scale;
            float scaledHeight = Screen.height / scale;
            
            // Toggle button (bottom-left)
            Rect toggleRect = new Rect(10, scaledHeight - 70, 150, 60);
            if (GUI.Button(toggleRect, isVisible ? "Hide Log" : "Show Log", buttonStyle))
            {
                isVisible = !isVisible;
            }
            
            if (!isVisible) return;
            
            // Clear button (only visible when log is shown)
            Rect clearRect = new Rect(170, scaledHeight - 70, 150, 60);
            if (GUI.Button(clearRect, "Clear", buttonStyle))
            {
                logMessages.Clear();
            }
            
            // Log panel (bottom half of screen)
            float panelHeight = scaledHeight * 0.4f;
            Rect panelRect = new Rect(10, scaledHeight - panelHeight - 80, scaledWidth - 20, panelHeight);
            
            // Semi-transparent background
            GUI.Box(panelRect, "");
            GUI.Box(panelRect, "");
            
            // Scroll view for logs
            Rect viewRect = new Rect(0, 0, panelRect.width - 30, logMessages.Count * (fontSize + 5));
            scrollPosition = GUI.BeginScrollView(panelRect, scrollPosition, viewRect);
            
            float y = 0;
            foreach (string msg in logMessages)
            {
                GUI.Label(new Rect(5, y, viewRect.width, fontSize + 5), msg, logStyle);
                y += fontSize + 5;
            }
            
            GUI.EndScrollView();
        }
    }
}
