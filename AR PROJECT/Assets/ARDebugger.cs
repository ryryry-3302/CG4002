using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Text;

public class ARDebugger : MonoBehaviour
{
    private ARSession arSession;
    private ARTrackedImageManager imageManager;
    private StringBuilder debugLog = new StringBuilder();
    private Vector2 scrollPosition;
    private bool showLog = true;

    // Magic method to run this without needing to add it to the scene manually
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        GameObject go = new GameObject("AR Debugger Auto");
        go.AddComponent<ARDebugger>();
        DontDestroyOnLoad(go);
    }

    void Start()
    {
        arSession = FindObjectOfType<ARSession>();
        imageManager = FindObjectOfType<ARTrackedImageManager>();
        Application.logMessageReceived += HandleLog;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception)
        {
             debugLog.Insert(0, $"<color=red>{logString}</color>\n");
        }
        else
        {
             debugLog.Insert(0, $"{logString}\n");
        }
        
        if (debugLog.Length > 5000) debugLog.Length = 5000;
    }

    void OnGUI()
    {
        if (!showLog) {
            if (GUI.Button(new Rect(10, 10, 100, 50), "Show Debug")) showLog = true;
            return;
        }

        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * 3); // Scale up for phone screens

        GUILayout.BeginArea(new Rect(10, 10, Screen.width / 3 - 20, Screen.height / 3 - 20));
        
        if (GUILayout.Button("Hide")) showLog = false;

        GUILayout.Label($"<b>AR SESSION:</b> {ARSession.state}"); // Fixed: Accessed via type, not instance
        
        if (imageManager != null)
        {
             GUILayout.Label($"<b>Tracking Mode:</b> {imageManager.trackables.count} images");
             foreach(var img in imageManager.trackables)
             {
                 GUILayout.Label($"- {img.referenceImage.name}: {img.trackingState}");
                 GUILayout.Label($"  Pos: {img.transform.position}");
             }
        }
        else
        {
            GUILayout.Label("<color=red>NO IMAGE MANAGER FOUND</color>");
        }

        GUILayout.Label("<b>--- LOGS ---</b>");
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(300));
        GUILayout.Label(debugLog.ToString());
        GUILayout.EndScrollView();

        GUILayout.EndArea();
    }
}
