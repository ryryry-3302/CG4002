using UnityEngine;

namespace OrchestraMaestro
{
    /// <summary>
    /// Debug panel with dummy input buttons for testing without hardware.
    /// Simulates MQTT gesture events for LEFT, RIGHT, UP, DOWN, PUNCH.
    /// Toggle visibility by tapping top-right corner of screen (or F1 on keyboard).
    /// </summary>
    public class DummyInputPanel : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool showPanel = true;
        
        [Header("Toggle Button (Top-Right Corner)")]
        [SerializeField] private float toggleButtonSize = 80f;
        
        [Header("Button Layout")]
        [SerializeField] private float buttonWidth = 80f;
        [SerializeField] private float buttonHeight = 50f;
        [SerializeField] private float padding = 10f;
        [SerializeField] private float panelScale = 2f;
        
        // Panel position (bottom-right corner)
        private Rect panelRect;
        private Rect toggleButtonRect;
        
        private void Start()
        {
            // Calculate panel position
            float panelWidth = (buttonWidth * 3 + padding * 4) * panelScale;
            float panelHeight = (buttonHeight * 3 + padding * 5) * panelScale;
            panelRect = new Rect(
                Screen.width - panelWidth - 20,
                Screen.height - panelHeight - 20,
                panelWidth,
                panelHeight
            );
            
            // Toggle button in top-right corner
            toggleButtonRect = new Rect(
                Screen.width - toggleButtonSize * panelScale - 10,
                10,
                toggleButtonSize * panelScale,
                toggleButtonSize * panelScale
            );
        }
        
        private void Update()
        {
            // Toggle panel visibility with keyboard (editor testing only)
            #if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.F1))
            {
                showPanel = !showPanel;
            }
            #endif
            
            // Toggle panel with touch in top-right corner
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    // Check if touch is in toggle button area (top-right)
                    Vector2 touchPos = touch.position;
                    // Convert to GUI coordinates (Y is inverted)
                    float guiY = Screen.height - touchPos.y;
                    
                    if (touchPos.x > Screen.width - toggleButtonSize * panelScale - 20 &&
                        guiY < toggleButtonSize * panelScale + 20)
                    {
                        showPanel = !showPanel;
                    }
                }
            }
            
            // Keyboard shortcuts for testing (editor only)
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                SimulateGesture("LEFT");
            if (Input.GetKeyDown(KeyCode.RightArrow))
                SimulateGesture("RIGHT");
            if (Input.GetKeyDown(KeyCode.UpArrow))
                SimulateGesture("UP");
            if (Input.GetKeyDown(KeyCode.DownArrow))
                SimulateGesture("DOWN");
            if (Input.GetKeyDown(KeyCode.Space))
                SimulateGesture("PUNCH");
            if (Input.GetKeyDown(KeyCode.D))
                SimulateDownstroke();
        }
        
        private void OnGUI()
        {
            // Apply scaling for mobile
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * panelScale);
            
            // Panel always shown for dummy input testing
            showPanel = true;
            // ...existing code...
            // No toggle or hide logic
            
            // Recalculate rect for scaled coordinates
            float scaledPanelWidth = buttonWidth * 3 + padding * 4;
            float scaledPanelHeight = buttonHeight * 3 + padding * 5;
            Rect scaledRect = new Rect(
                (Screen.width / panelScale) - scaledPanelWidth - 10,
                (Screen.height / panelScale) - scaledPanelHeight - 10,
                scaledPanelWidth,
                scaledPanelHeight
            );
            
            GUILayout.BeginArea(scaledRect, GUI.skin.box);
            
            GUILayout.Label("<b>Dummy Input</b>", GUILayout.Height(20));
            GUILayout.Space(5);
            
            // Row 1: UP button centered
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↑ UP", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
            {
                SimulateGesture("UP");
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            // Row 2: LEFT, PUNCH, RIGHT
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("← LEFT", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
            {
                SimulateGesture("LEFT");
            }
            if (GUILayout.Button("👊 PUNCH", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
            {
                SimulateGesture("PUNCH");
            }
            if (GUILayout.Button("→ RIGHT", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
            {
                SimulateGesture("RIGHT");
            }
            GUILayout.EndHorizontal();
            
            // Row 3: DOWN button centered
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↓ DOWN", GUILayout.Width(buttonWidth), GUILayout.Height(buttonHeight)))
            {
                SimulateGesture("DOWN");
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUILayout.Space(5);
            
            // Downstroke button
            if (GUILayout.Button("🎵 DOWNSTROKE (D key)", GUILayout.Height(buttonHeight * 0.7f)))
            {
                SimulateDownstroke();
            }
            
            // Status display
            GUILayout.Space(5);
            string sectionName = RhythmGameController.Instance != null 
                ? RhythmGameController.Instance.SelectedSection.ToString() 
                : "---";
            int score = RhythmGameController.Instance?.TotalScore ?? 0;
            int combo = RhythmGameController.Instance?.Combo ?? 0;
            
            GUILayout.Label($"Section: {sectionName}");
            GUILayout.Label($"Score: {score} | Combo: {combo}");
            
            GUILayout.EndArea();
        }
        
        private void SimulateGesture(string gestureId)
        {
            if (MQTTManager.Instance != null)
            {
                MQTTManager.Instance.SimulateGesture(gestureId, true);
                Debug.Log($"[DummyInput] Simulated gesture: {gestureId}");
            }
            else
            {
                Debug.LogWarning("[DummyInput] MQTTManager not found!");
            }
        }
        
        private void SimulateDownstroke()
        {
            if (MQTTManager.Instance != null)
            {
                MQTTManager.Instance.SimulateDownstroke();
                Debug.Log("[DummyInput] Simulated downstroke");
            }
            else
            {
                Debug.LogWarning("[DummyInput] MQTTManager not found!");
            }
        }
    }
}
