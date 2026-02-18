using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Text;
using OrchestraMaestro;

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
        // Debug panel completely disabled - no buttons rendered
    }
}
