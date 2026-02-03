using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

namespace OrchestraMaestro
{

public class OrchestraPlacement : MonoBehaviour
{
    [Header("AR Components")]
    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private ARPlaneManager planeManager;
    
    [Header("Orchestra Member Prefabs")]
    [Tooltip("Drag your orchestra member prefabs here")]
    [SerializeField] private GameObject[] orchestraPrefabs;
    
    [Header("Section Assignment")]
    [Tooltip("Which section each prefab belongs to (same order as prefabs)")]
    [SerializeField] private OrchestraSection[] prefabSections;
    
    [Header("Visual Feedback")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.84f, 0f, 1f); // Gold
    [SerializeField] private Color perfectColor = new Color(0f, 1f, 0.5f, 1f);   // Green
    [SerializeField] private Color goodColor = new Color(1f, 1f, 0f, 1f);        // Yellow
    [SerializeField] private Color missColor = new Color(1f, 0.3f, 0.3f, 1f);    // Red
    [SerializeField] private float highlightIntensity = 2f;
    [SerializeField] private float feedbackDuration = 0.5f;
    
    [Header("Settings")]
    [SerializeField] private float prefabScale = 0.5f;
    
    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private List<GameObject> placedMembers = new List<GameObject>();
    private int selectedIndex = 0;
    private bool isPlacementMode = true;
    
    // Section grouping for conducting game
    private List<GameObject>[] sectionMembers = new List<GameObject>[4];
    private int currentHighlightedSection = -1;
    private Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();
    
    // Singleton
    public static OrchestraPlacement Instance { get; private set; }

    void Start()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        // Initialize section lists
        for (int i = 0; i < 4; i++)
        {
            sectionMembers[i] = new List<GameObject>();
        }
        
        if (raycastManager == null)
            raycastManager = FindObjectOfType<ARRaycastManager>();
        if (planeManager == null)
            planeManager = FindObjectOfType<ARPlaneManager>();
    }
    
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
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
        
        // Combine the hit rotation (surface alignment) with the prefab's inherent rotation
        Quaternion finalRotation = rotation * prefab.transform.rotation;

        // Calculate final position using the prefab's local scale for the offset to match the object's dimensions
        Vector3 scaledOffset = Vector3.Scale(prefab.transform.localPosition, prefab.transform.localScale);
        Vector3 finalPosition = position + (rotation * scaledOffset);
        
        GameObject member = Instantiate(prefab, finalPosition, finalRotation);
        // Use the scale specified in the prefab instead of the script override
        member.transform.localScale = prefab.transform.localScale;
        placedMembers.Add(member);
        
        // Add to appropriate section
        OrchestraSection section = GetSectionForPrefab(selectedIndex);
        sectionMembers[(int)section].Add(member);
        
        // Store original colors for highlighting
        CacheOriginalColors(member);
        
        Debug.Log($"Placed {prefab.name} at {finalPosition} in section {section}");
    }
    
    /// <summary>Get the section assignment for a prefab index</summary>
    private OrchestraSection GetSectionForPrefab(int prefabIndex)
    {
        if (prefabSections != null && prefabIndex < prefabSections.Length)
        {
            return prefabSections[prefabIndex];
        }
        // Default: distribute evenly across sections
        return (OrchestraSection)(prefabIndex % 4);
    }
    
    /// <summary>Cache original material colors for later restoration</summary>
    private void CacheOriginalColors(GameObject member)
    {
        Renderer[] renderers = member.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            if (renderer.material.HasProperty("_Color"))
            {
                originalColors[renderer] = renderer.material.color;
            }
            else if (renderer.material.HasProperty("_BaseColor"))
            {
                originalColors[renderer] = renderer.material.GetColor("_BaseColor");
            }
        }
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

    #region Section Highlighting & Feedback
    
    /// <summary>Highlight a specific orchestra section</summary>
    public void HighlightSection(int sectionIndex)
    {
        // Remove highlight from previous section
        if (currentHighlightedSection >= 0 && currentHighlightedSection != sectionIndex)
        {
            SetSectionHighlight(currentHighlightedSection, false);
        }
        
        // Apply highlight to new section
        if (sectionIndex >= 0 && sectionIndex < 4)
        {
            SetSectionHighlight(sectionIndex, true);
            currentHighlightedSection = sectionIndex;
        }
    }
    
    /// <summary>Remove all section highlights</summary>
    public void ClearHighlight()
    {
        if (currentHighlightedSection >= 0)
        {
            SetSectionHighlight(currentHighlightedSection, false);
            currentHighlightedSection = -1;
        }
    }
    
    private void SetSectionHighlight(int sectionIndex, bool highlighted)
    {
        List<GameObject> members = sectionMembers[sectionIndex];
        
        foreach (var member in members)
        {
            if (member == null) continue;
            
            Renderer[] renderers = member.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (highlighted)
                {
                    // Apply highlight tint
                    ApplyColorTint(renderer, highlightColor, highlightIntensity);
                }
                else
                {
                    // Restore original color
                    RestoreOriginalColor(renderer);
                }
            }
        }
    }
    
    private void ApplyColorTint(Renderer renderer, Color tint, float intensity)
    {
        if (originalColors.TryGetValue(renderer, out Color original))
        {
            Color blended = Color.Lerp(original, tint, 0.4f) * intensity;
            blended.a = original.a;
            
            if (renderer.material.HasProperty("_Color"))
            {
                renderer.material.color = blended;
            }
            else if (renderer.material.HasProperty("_BaseColor"))
            {
                renderer.material.SetColor("_BaseColor", blended);
            }
            
            // Enable emission if available
            if (renderer.material.HasProperty("_EmissionColor"))
            {
                renderer.material.EnableKeyword("_EMISSION");
                renderer.material.SetColor("_EmissionColor", tint * (intensity - 1f));
            }
        }
    }
    
    private void RestoreOriginalColor(Renderer renderer)
    {
        if (originalColors.TryGetValue(renderer, out Color original))
        {
            if (renderer.material.HasProperty("_Color"))
            {
                renderer.material.color = original;
            }
            else if (renderer.material.HasProperty("_BaseColor"))
            {
                renderer.material.SetColor("_BaseColor", original);
            }
            
            // Disable emission
            if (renderer.material.HasProperty("_EmissionColor"))
            {
                renderer.material.SetColor("_EmissionColor", Color.black);
            }
        }
    }
    
    /// <summary>Trigger visual feedback for a hit/miss on a section</summary>
    public void TriggerHitFeedback(int sectionIndex, JudgementType judgement)
    {
        if (sectionIndex < 0 || sectionIndex >= 4) return;
        
        Color feedbackColor = judgement switch
        {
            JudgementType.Perfect => perfectColor,
            JudgementType.Good => goodColor,
            JudgementType.Miss => missColor,
            _ => Color.white
        };
        
        StartCoroutine(HitFeedbackCoroutine(sectionIndex, feedbackColor));
    }
    
    private System.Collections.IEnumerator HitFeedbackCoroutine(int sectionIndex, Color feedbackColor)
    {
        List<GameObject> members = sectionMembers[sectionIndex];
        
        // Flash the feedback color
        foreach (var member in members)
        {
            if (member == null) continue;
            
            Renderer[] renderers = member.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                ApplyColorTint(renderer, feedbackColor, highlightIntensity * 1.5f);
            }
        }
        
        // Wait and fade back
        float elapsed = 0f;
        while (elapsed < feedbackDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / feedbackDuration;
            float intensity = Mathf.Lerp(highlightIntensity * 1.5f, highlightIntensity, t);
            
            foreach (var member in members)
            {
                if (member == null) continue;
                
                Renderer[] renderers = member.GetComponentsInChildren<Renderer>();
                foreach (var renderer in renderers)
                {
                    // Blend from feedback color back to highlight (if section is highlighted)
                    Color targetColor = (sectionIndex == currentHighlightedSection) ? highlightColor : Color.white;
                    Color blendedColor = Color.Lerp(feedbackColor, targetColor, t);
                    ApplyColorTint(renderer, blendedColor, intensity);
                }
            }
            
            yield return null;
        }
        
        // Restore to normal state
        if (sectionIndex == currentHighlightedSection)
        {
            SetSectionHighlight(sectionIndex, true);
        }
        else
        {
            foreach (var member in members)
            {
                if (member == null) continue;
                
                Renderer[] renderers = member.GetComponentsInChildren<Renderer>();
                foreach (var renderer in renderers)
                {
                    RestoreOriginalColor(renderer);
                }
            }
        }
    }
    
    /// <summary>Get members in a specific section</summary>
    public List<GameObject> GetSectionMembers(OrchestraSection section)
    {
        return sectionMembers[(int)section];
    }
    
    /// <summary>Get all placed members</summary>
    public List<GameObject> GetAllMembers()
    {
        return new List<GameObject>(placedMembers);
    }
    
    #endregion

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

} // namespace OrchestraMaestro