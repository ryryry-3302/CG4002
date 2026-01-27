using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

public class OrchestraPlacement : MonoBehaviour
{
    [Header("AR Components")]
    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private ARPlaneManager planeManager;
    
    [Header("Orchestra Member Prefabs")]
    [Tooltip("Drag your orchestra member prefabs here")]
    [SerializeField] private GameObject[] orchestraPrefabs;
    
    [Header("Settings")]
    [SerializeField] private float prefabScale = 0.5f;
    
    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private List<GameObject> placedMembers = new List<GameObject>();
    private int selectedIndex = 0;
    private bool isPlacementMode = true;

    void Start()
    {
        if (raycastManager == null)
            raycastManager = FindObjectOfType<ARRaycastManager>();
        if (planeManager == null)
            planeManager = FindObjectOfType<ARPlaneManager>();
    }

    void Update()
    {
        if (!isPlacementMode) return;
        
        // Check for touch input
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            
            // Only place on tap (not drag)
            if (touch.phase == TouchPhase.Began)
            {
                // Ignore if touching UI area (top-left corner)
                if (touch.position.x < 250 && touch.position.y > Screen.height - 500)
                    return;
                
                TryPlaceObject(touch.position);
            }
        }
        
        // Mouse input for testing in editor
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Input.mousePosition;
            // Ignore UI area
            if (mousePos.x < 250 && mousePos.y > Screen.height - 500)
                return;
                
            TryPlaceObject(mousePos);
        }
    }

    void TryPlaceObject(Vector2 screenPosition)
    {
        if (raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;
            PlaceOrchestraMember(hitPose.position, hitPose.rotation);
        }
    }

    void PlaceOrchestraMember(Vector3 position, Quaternion rotation)
    {
        if (orchestraPrefabs == null || orchestraPrefabs.Length == 0)
        {
            Debug.LogWarning("No orchestra prefabs assigned!");
            return;
        }
        
        GameObject prefab = orchestraPrefabs[selectedIndex];
        if (prefab == null) return;
        
        GameObject member = Instantiate(prefab, position, rotation);
        member.transform.localScale = Vector3.one * prefabScale;
        placedMembers.Add(member);
        
        Debug.Log($"Placed {prefab.name} at {position}");
    }

    public void SelectNextMember()
    {
        if (orchestraPrefabs.Length > 0)
            selectedIndex = (selectedIndex + 1) % orchestraPrefabs.Length;
    }

    public void SelectPreviousMember()
    {
        if (orchestraPrefabs.Length > 0)
            selectedIndex = (selectedIndex - 1 + orchestraPrefabs.Length) % orchestraPrefabs.Length;
    }

    public void UndoLastPlacement()
    {
        if (placedMembers.Count > 0)
        {
            GameObject last = placedMembers[placedMembers.Count - 1];
            placedMembers.RemoveAt(placedMembers.Count - 1);
            Destroy(last);
        }
    }

    public void ClearAllPlacements()
    {
        foreach (var member in placedMembers)
        {
            if (member != null)
                Destroy(member);
        }
        placedMembers.Clear();
    }

    public void TogglePlaneVisibility()
    {
        foreach (var plane in planeManager.trackables)
        {
            plane.gameObject.SetActive(!plane.gameObject.activeSelf);
        }
    }

    public void LockPlacements()
    {
        isPlacementMode = false;
        // Hide all planes when done placing
        foreach (var plane in planeManager.trackables)
        {
            plane.gameObject.SetActive(false);
        }
        planeManager.enabled = false;
    }

    public void UnlockPlacements()
    {
        isPlacementMode = true;
        planeManager.enabled = true;
    }

    void OnGUI()
    {
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * 3);
        
        GUILayout.BeginArea(new Rect(10, 10, 220, 500));
        
        // Status
        GUILayout.Label($"<b>Planes Found:</b> {planeManager?.trackables.count ?? 0}");
        GUILayout.Label($"<b>Placed:</b> {placedMembers.Count} members");
        GUILayout.Label($"<b>Mode:</b> {(isPlacementMode ? "PLACING" : "LOCKED")}");
        
        GUILayout.Space(10);
        
        // Member selection
        GUILayout.Label("<b>Selected:</b>");
        string currentName = "None";
        if (orchestraPrefabs != null && orchestraPrefabs.Length > 0 && orchestraPrefabs[selectedIndex] != null)
            currentName = orchestraPrefabs[selectedIndex].name;
        GUILayout.Label($"  {selectedIndex + 1}/{orchestraPrefabs?.Length ?? 0}: {currentName}");
        
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("< Prev")) SelectPreviousMember();
        if (GUILayout.Button("Next >")) SelectNextMember();
        GUILayout.EndHorizontal();
        
        GUILayout.Space(10);
        
        // Actions
        if (GUILayout.Button("Undo Last"))
            UndoLastPlacement();
        
        if (GUILayout.Button("Clear All"))
            ClearAllPlacements();
        
        if (GUILayout.Button("Toggle Planes"))
            TogglePlaneVisibility();
        
        GUILayout.Space(10);
        
        if (isPlacementMode)
        {
            if (GUILayout.Button("DONE - Lock Placements"))
                LockPlacements();
        }
        else
        {
            if (GUILayout.Button("Unlock & Edit"))
                UnlockPlacements();
        }
        
        GUILayout.Space(10);
        GUILayout.Label("<i>Tap anywhere on a\ndetected plane to\nplace a member</i>");
        
        GUILayout.EndArea();
    }
}
