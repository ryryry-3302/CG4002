using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Text;

public class ARDebugger : MonoBehaviour
{
    private ARSession arSession;
    private ARPlaneManager planeManager;
    private ARRaycastManager raycastManager;
    private OrchestraPlacement orchestraPlacement;
    private StringBuilder debugLog = new StringBuilder();
    private Vector2 scrollPosition;
    private bool showLog = false; // Start hidden, toggle with button

    // Magic method to run this without needing to add it to the scene manually
    // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    // static void AutoStart()
    // {
    //     GameObject go = new GameObject("AR Debugger Auto");
    //     go.AddComponent<ARDebugger>();
    //     DontDestroyOnLoad(go);
    // }

    void Start()
    {
        arSession = FindObjectOfType<ARSession>();
        planeManager = FindObjectOfType<ARPlaneManager>();
        raycastManager = FindObjectOfType<ARRaycastManager>();
        orchestraPlacement = FindObjectOfType<OrchestraPlacement>();
        Application.logMessageReceived += HandleLog;
        
        Debug.Log("AR Debugger initialized for Plane Detection mode");
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        string timestamp = System.DateTime.Now.ToString("HH:mm:ss");
        if (type == LogType.Error || type == LogType.Exception)
        {
            debugLog.Insert(0, $"<color=red>[{timestamp}] {logString}</color>\n");
        }
        else if (type == LogType.Warning)
        {
            debugLog.Insert(0, $"<color=yellow>[{timestamp}] {logString}</color>\n");
        }
        else
        {
            debugLog.Insert(0, $"[{timestamp}] {logString}\n");
        }
        
        if (debugLog.Length > 5000) debugLog.Length = 5000;
    }

    void OnGUI()
    {
        // Position debug button in top-right corner (away from placement UI)
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * 3);
        
        float buttonX = Screen.width / 3 - 120;
        
        if (!showLog) {
            if (GUI.Button(new Rect(buttonX, 10, 100, 40), "Debug")) showLog = true;
            return;
        }

        // Debug panel on the right side
        GUILayout.BeginArea(new Rect(buttonX - 200, 10, 300, Screen.height / 3 - 20));
        
        if (GUILayout.Button("Hide Debug")) showLog = false;

        // AR Session Status
        GUILayout.Label($"<b>AR SESSION:</b> {ARSession.state}");
        
        // Plane Manager Status
        if (planeManager != null)
        {
            int planeCount = 0;
            int horizontalCount = 0;
            int verticalCount = 0;
            
            foreach (var plane in planeManager.trackables)
            {
                planeCount++;
                if (plane.alignment == PlaneAlignment.HorizontalUp || 
                    plane.alignment == PlaneAlignment.HorizontalDown)
                    horizontalCount++;
                else if (plane.alignment == PlaneAlignment.Vertical)
                    verticalCount++;
            }
            
            GUILayout.Label($"<b>PLANES:</b> {planeCount} detected");
            GUILayout.Label($"  Horizontal: {horizontalCount} | Vertical: {verticalCount}");
            GUILayout.Label($"  Detection: {(planeManager.enabled ? "<color=green>ON</color>" : "<color=red>OFF</color>")}");
        }
        else
        {
            GUILayout.Label("<color=red>NO PLANE MANAGER</color>");
        }
        
        // Raycast Manager Status
        if (raycastManager != null)
        {
            GUILayout.Label($"<b>RAYCAST:</b> <color=green>Ready</color>");
        }
        else
        {
            GUILayout.Label("<color=red>NO RAYCAST MANAGER</color>");
        }
        
        // Orchestra Placement Status
        if (orchestraPlacement != null)
        {
            GUILayout.Label($"<b>PLACEMENT:</b> <color=green>Active</color>");
        }
        else
        {
            GUILayout.Label("<color=yellow>PLACEMENT SCRIPT NOT FOUND</color>");
        }

        GUILayout.Label("<b>--- LOGS ---</b>");
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
        GUILayout.Label(debugLog.ToString());
        GUILayout.EndScrollView();

        GUILayout.EndArea();
    }
}
