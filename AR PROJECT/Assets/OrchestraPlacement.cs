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
    
    // GUI interaction guard - prevents placement when tapping buttons
    private bool guiConsumedInput = false;
    private Rect guiAreaScreenRect; // The actual screen-pixel rect of the OnGUI panel
    
    // Section grouping for conducting game
    private List<GameObject>[] sectionMembers = new List<GameObject>[4];
    private int currentHighlightedSection = -1;
    private Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();
    
    // Track which sections have a placed member (one per section limit)
    private Dictionary<OrchestraSection, GameObject> sectionPlacedMember = new Dictionary<OrchestraSection, GameObject>();
    // Track which section each placed member belongs to
    private Dictionary<GameObject, OrchestraSection> memberToSection = new Dictionary<GameObject, OrchestraSection>();
    
    // Gameplay HUD state
    private string lastJudgement = "";
    private Color lastJudgementColor = Color.white;
    private float judgementTimer = 0f;
    private float judgementDisplayTime = 1.0f;
    
    // Custom GUI styles (lazy-initialized)
    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private GUIStyle buttonStyle;
    private GUIStyle sectionLabelStyle;
    private GUIStyle judgementStyle;
    private GUIStyle comboStyle;
    private GUIStyle scoreStyle;
    private GUIStyle hudBoxStyle;
    private bool stylesInitialized = false;
    
    // Persist textures so GC doesn't destroy them
    private Texture2D texDarkBg;
    private Texture2D texAccentBg;
    private Texture2D texAccentHover;
    private Texture2D texSubtleBg;
    private Texture2D texSubtleHover;
    
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
        
        // Unsubscribe from game events
        if (RhythmGameController.Instance != null)
        {
            RhythmGameController.Instance.OnGestureJudged -= OnGestureJudgedHUD;
        }
    }

    void Update()
    {
        // Tick judgement display timer
        if (judgementTimer > 0)
            judgementTimer -= Time.deltaTime;
        
        if (!isPlacementMode) return;
        
        // If OnGUI consumed input this frame, skip placement
        if (guiConsumedInput)
        {
            guiConsumedInput = false;
            return;
        }
        
        // Check for touch input
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            
            // Only place on tap (not drag)
            if (touch.phase == TouchPhase.Began)
            {
                // Ignore if touching the GUI panel area
                if (IsInsideGUIArea(touch.position))
                    return;
                
                TryPlaceObject(touch.position);
            }
        }
        
        // Mouse input for testing in editor
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Input.mousePosition;
            // Ignore GUI panel area
            if (IsInsideGUIArea(mousePos))
                return;
                
            TryPlaceObject(mousePos);
        }
    }
    
    /// <summary>
    /// Check if a screen position (Input coordinates, origin bottom-left) is inside the OnGUI panel.
    /// OnGUI uses top-left origin and is scaled by GUI.matrix (3x), so we convert.
    /// </summary>
    private bool IsInsideGUIArea(Vector2 screenPos)
    {
        // Placement panel: Rect(10, 10, 200, 340) with GUI.matrix scale 3x
        float guiScale = 3f;
        float guiLeft = 10f * guiScale;     // 30
        float guiTop = 10f * guiScale;      // 30
        float guiWidth = 200f * guiScale;   // 600
        float guiHeight = 340f * guiScale;  // 1020
        
        // Convert Input screen pos (bottom-left origin) to top-left origin
        float topLeftY = Screen.height - screenPos.y;
        
        return screenPos.x >= guiLeft && screenPos.x <= (guiLeft + guiWidth) &&
               topLeftY >= guiTop && topLeftY <= (guiTop + guiHeight);
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

        // Place directly at the hit position (no offset from prefab localPosition)
        Vector3 finalPosition = position;
        
        // Check section assignment
        OrchestraSection section = GetSectionForPrefab(selectedIndex);
        
        // Enforce one member per section - remove existing if present
        if (sectionPlacedMember.TryGetValue(section, out GameObject existing))
        {
            Debug.Log($"Section {section} already has a member - replacing it");
            placedMembers.Remove(existing);
            sectionMembers[(int)section].Remove(existing);
            memberToSection.Remove(existing);
            // Remove cached colors for old member
            Renderer[] oldRenderers = existing.GetComponentsInChildren<Renderer>();
            foreach (var r in oldRenderers)
                originalColors.Remove(r);
            Destroy(existing);
        }
        
        GameObject member = Instantiate(prefab, finalPosition, finalRotation);
        // Use the scale specified in the prefab instead of the script override
        member.transform.localScale = prefab.transform.localScale;
        
        // Correct for off-center mesh geometry in FBX models:
        // Calculate the visual center offset from the renderers' bounds and shift the object
        // so the visual center aligns with the tap position
        Renderer[] renderers = member.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds combinedBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                combinedBounds.Encapsulate(renderers[i].bounds);
            
            // Only correct horizontal offset (X/Z), keep Y at the hit surface
            Vector3 boundsCenter = combinedBounds.center;
            Vector3 correction = member.transform.position - boundsCenter;
            correction.y = 0f; // Don't shift vertically
            member.transform.position += correction;
        }
        placedMembers.Add(member);
        
        // Add to appropriate section
        sectionMembers[(int)section].Add(member);
        sectionPlacedMember[section] = member;
        memberToSection[member] = section;
        
        // Store original colors for highlighting
        CacheOriginalColors(member);
        
        Debug.Log($"Placed {prefab.name} at {member.transform.position} (hit: {position}) in section {section}");
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
            
            // Remove from section tracking
            if (memberToSection.TryGetValue(last, out OrchestraSection section))
            {
                sectionMembers[(int)section].Remove(last);
                sectionPlacedMember.Remove(section);
                memberToSection.Remove(last);
            }
            
            // Remove cached colors
            Renderer[] renderers = last.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
                originalColors.Remove(r);
            
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
        
        // Clear all section tracking
        for (int i = 0; i < 4; i++)
            sectionMembers[i].Clear();
        sectionPlacedMember.Clear();
        memberToSection.Clear();
        originalColors.Clear();
    }

    public void TogglePlaneVisibility()
    {
        foreach (var plane in planeManager.trackables)
        {
            plane.gameObject.SetActive(!plane.gameObject.activeSelf);
        }
    }
    
    /// <summary>Remove all tracked planes and restart plane detection from scratch</summary>
    public void RescanPlanes()
    {
        // Reset the AR session which clears all trackables (planes, anchors, etc.)
        // and starts fresh detection
        var arSession = FindObjectOfType<UnityEngine.XR.ARFoundation.ARSession>();
        if (arSession != null)
        {
            arSession.Reset();
            Debug.Log("[OrchestraPlacement] AR session reset - rescanning environment...");
        }
        else
        {
            // Fallback: just toggle the plane manager
            planeManager.enabled = false;
            planeManager.enabled = true;
            Debug.Log("[OrchestraPlacement] Toggled plane manager - rescanning...");
        }
    }

    // Event when placements are locked
    public event System.Action OnPlacementsLocked;

    public void LockPlacements()
    {
        isPlacementMode = false;
        // Hide all planes when done placing
        foreach (var plane in planeManager.trackables)
        {
            plane.gameObject.SetActive(false);
        }
        planeManager.enabled = false;
        
        // Subscribe to game events for HUD
        Invoke(nameof(SubscribeGameHUD), 0.2f);
        
        // Notify listeners that placements are locked
        OnPlacementsLocked?.Invoke();
        Debug.Log("[OrchestraPlacement] Placements locked - ready to start game");
    }
    
    private void SubscribeGameHUD()
    {
        if (RhythmGameController.Instance != null)
        {
            RhythmGameController.Instance.OnGestureJudged += OnGestureJudgedHUD;
        }
    }
    
    private void OnGestureJudgedHUD(ScoringResult result)
    {
        judgementTimer = judgementDisplayTime;
        switch (result.judgement)
        {
            case JudgementType.Perfect:
                lastJudgement = "✦ PERFECT ✦";
                lastJudgementColor = new Color(0.2f, 1f, 0.6f);
                break;
            case JudgementType.Good:
                lastJudgement = "GOOD";
                lastJudgementColor = new Color(1f, 0.9f, 0.2f);
                break;
            default:
                lastJudgement = "MISS";
                lastJudgementColor = new Color(1f, 0.35f, 0.35f);
                break;
        }
    }

    public void UnlockPlacements()
    {
        isPlacementMode = true;
        planeManager.enabled = true;
        
        // Show existing detected planes again
        foreach (var plane in planeManager.trackables)
        {
            plane.gameObject.SetActive(true);
        }
        
        // Clean up game state if a game was running
        if (RhythmGameController.Instance != null)
        {
            RhythmGameController.Instance.OnGestureJudged -= OnGestureJudgedHUD;
            
            // Stop the game if it's playing or in results
            if (RhythmGameController.Instance.CurrentState == RhythmGameController.GameState.Playing ||
                RhythmGameController.Instance.CurrentState == RhythmGameController.GameState.Results)
            {
                RhythmGameController.Instance.EndGame();
            }
        }
        
        // Clean up radars
        if (CueRadarManager.Instance != null)
        {
            CueRadarManager.Instance.ResetForNewGame();
        }
        
        // Hide the Canvas HUD
        if (HUDController.Instance != null)
        {
            HUDController.Instance.HideAll();
        }
        
        // Clear section highlights
        ClearHighlight();
        
        Debug.Log("[OrchestraPlacement] Unlocked - back to placement mode");
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
    
    /// <summary>Get the average position of a section (for radar placement)</summary>
    public Vector3? GetSectionPosition(OrchestraSection section)
    {
        List<GameObject> members = sectionMembers[(int)section];
        
        if (members == null || members.Count == 0)
        {
            return null;
        }
        
        Vector3 sum = Vector3.zero;
        int count = 0;
        
        foreach (var member in members)
        {
            if (member != null)
            {
                // Use the visual center (renderer bounds) instead of the raw transform position
                // This accounts for FBX models whose mesh geometry is offset from the transform origin
                Renderer[] renderers = member.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds combinedBounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        combinedBounds.Encapsulate(renderers[i].bounds);
                    // Use horizontal center of bounds, but keep the ground-level Y from the transform
                    Vector3 visualCenter = combinedBounds.center;
                    visualCenter.y = member.transform.position.y;
                    sum += visualCenter;
                }
                else
                {
                    sum += member.transform.position;
                }
                count++;
            }
        }
        
        if (count == 0) return null;
        
        return sum / count;
    }
    
    /// <summary>Get all placed members</summary>
    public List<GameObject> GetAllMembers()
    {
        return new List<GameObject>(placedMembers);
    }
    
    #endregion

    void OnGUI()
    {
        InitStyles();
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * 3);
        
        if (isPlacementMode)
        {
            DrawPlacementUI();
        }
        else
        {
            DrawGameplayHUD();
        }
    }
    
    private void InitStyles()
    {
        if (stylesInitialized) return;
        stylesInitialized = true;
        
        // Dark semi-transparent box
        texDarkBg = new Texture2D(1, 1);
        texDarkBg.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.12f, 0.85f));
        texDarkBg.Apply();
        
        texAccentBg = new Texture2D(1, 1);
        texAccentBg.SetPixel(0, 0, new Color(0.15f, 0.4f, 0.9f, 0.9f));
        texAccentBg.Apply();
        
        texAccentHover = new Texture2D(1, 1);
        texAccentHover.SetPixel(0, 0, new Color(0.25f, 0.5f, 1f, 0.95f));
        texAccentHover.Apply();
        
        texSubtleBg = new Texture2D(1, 1);
        texSubtleBg.SetPixel(0, 0, new Color(0.2f, 0.2f, 0.3f, 0.8f));
        texSubtleBg.Apply();
        
        texSubtleHover = new Texture2D(1, 1);
        texSubtleHover.SetPixel(0, 0, new Color(0.3f, 0.3f, 0.45f, 0.85f));
        texSubtleHover.Apply();
        
        hudBoxStyle = new GUIStyle(GUI.skin.box);
        hudBoxStyle.normal.background = texDarkBg;
        hudBoxStyle.padding = new RectOffset(12, 12, 10, 10);
        
        headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize = 14;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = new Color(0.9f, 0.85f, 1f);
        headerStyle.alignment = TextAnchor.MiddleCenter;
        
        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 10;
        labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.9f);
        labelStyle.richText = true;
        
        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 10;
        buttonStyle.fontStyle = FontStyle.Bold;
        buttonStyle.normal.background = texSubtleBg;
        buttonStyle.hover.background = texSubtleHover;
        buttonStyle.active.background = texAccentBg;
        buttonStyle.normal.textColor = Color.white;
        buttonStyle.hover.textColor = Color.white;
        buttonStyle.active.textColor = Color.white;
        buttonStyle.padding = new RectOffset(8, 8, 6, 6);
        
        scoreStyle = new GUIStyle(GUI.skin.label);
        scoreStyle.fontSize = 18;
        scoreStyle.fontStyle = FontStyle.Bold;
        scoreStyle.normal.textColor = Color.white;
        scoreStyle.alignment = TextAnchor.MiddleCenter;
        
        comboStyle = new GUIStyle(GUI.skin.label);
        comboStyle.fontSize = 12;
        comboStyle.fontStyle = FontStyle.Bold;
        comboStyle.normal.textColor = new Color(1f, 0.84f, 0f);
        comboStyle.alignment = TextAnchor.MiddleCenter;
        
        sectionLabelStyle = new GUIStyle(GUI.skin.label);
        sectionLabelStyle.fontSize = 10;
        sectionLabelStyle.normal.textColor = new Color(0.6f, 0.8f, 1f);
        sectionLabelStyle.alignment = TextAnchor.MiddleCenter;
        
        judgementStyle = new GUIStyle(GUI.skin.label);
        judgementStyle.fontSize = 16;
        judgementStyle.fontStyle = FontStyle.Bold;
        judgementStyle.alignment = TextAnchor.MiddleCenter;
    }
    
    private void DrawPlacementUI()
    {
        float panelW = 300f;
        float panelH = 380f;
        GUILayout.BeginArea(new Rect(10, 10, panelW, panelH), hudBoxStyle);
        
        GUILayout.Label("🎵 ORCHESTRA SETUP", headerStyle);
        GUILayout.Space(6);
        
        // Status line
        int placedCount = sectionPlacedMember.Count;
        int planeCount = 0;
        if (planeManager != null)
        {
            foreach (var p in planeManager.trackables) planeCount++;
        }
        GUILayout.Label($"Planes: {planeCount}  |  Placed: {placedCount}/4", labelStyle);
        
        // Section placement status dots
        GUILayout.BeginHorizontal();
        for (int i = 0; i < 4; i++)
        {
            OrchestraSection sec = (OrchestraSection)i;
            bool placed = sectionPlacedMember.ContainsKey(sec);
            string dot = placed ? "<color=#44FF88>●</color>" : "<color=#555555>○</color>";
            GUILayout.Label($"{dot} {sec}", labelStyle);
        }
        GUILayout.EndHorizontal();
        
        GUILayout.Space(8);
        
        // Current selection
        string currentName = "None";
        string secName = "";
        if (orchestraPrefabs != null && orchestraPrefabs.Length > 0 && orchestraPrefabs[selectedIndex] != null)
        {
            currentName = orchestraPrefabs[selectedIndex].name;
            OrchestraSection sec = GetSectionForPrefab(selectedIndex);
            bool alreadyPlaced = sectionPlacedMember.ContainsKey(sec);
            secName = $"[{sec}]" + (alreadyPlaced ? " ✓" : "");
        }
        GUILayout.Label($"Selected: {currentName}", labelStyle);
        GUILayout.Label($"  {selectedIndex + 1}/{orchestraPrefabs?.Length ?? 0}  {secName}", labelStyle);
        
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("◀ Prev", buttonStyle)) SelectPreviousMember();
        if (GUILayout.Button("Next ▶", buttonStyle)) SelectNextMember();
        GUILayout.EndHorizontal();
        
        GUILayout.Space(8);
        
        if (GUILayout.Button("↩ Undo Last", buttonStyle))
            UndoLastPlacement();
        if (GUILayout.Button("✕ Clear All", buttonStyle))
            ClearAllPlacements();
        if (GUILayout.Button("🔄 Rescan Planes", buttonStyle))
            RescanPlanes();
        
        GUILayout.Space(8);
        
        // Lock button — highlight if all 4 placed
        GUI.enabled = placedCount > 0;
        string lockLabel = placedCount >= 4 ? "✔ START GAME" : $"Lock ({placedCount}/4 placed)";
        if (GUILayout.Button(lockLabel, buttonStyle, GUILayout.Height(30)))
            LockPlacements();
        GUI.enabled = true;
        
        GUILayout.Space(4);
        GUILayout.Label("<i>Tap a plane to place</i>", labelStyle);
        
        GUILayout.EndArea();
    }
    
    private void DrawGameplayHUD()
    {
        var gameState = RhythmGameController.Instance?.CurrentState 
            ?? RhythmGameController.GameState.Setup;
        
        if (gameState == RhythmGameController.GameState.Results)
        {
            DrawResultsScreen();
            return;
        }
        
        float hudW = 160f;
        float hudH = 100f;
        
        // Top-center: Score + Combo
        float centerX = (Screen.width / 3f - hudW) / 2f; // account for 3x GUI scale
        GUILayout.BeginArea(new Rect(centerX, 8, hudW, hudH), hudBoxStyle);
        
        int score = RhythmGameController.Instance?.TotalScore ?? 0;
        int combo = RhythmGameController.Instance?.Combo ?? 0;
        
        GUILayout.Label(score.ToString("N0"), scoreStyle);
        
        if (combo > 1)
        {
            GUILayout.Label($"{combo}x COMBO", comboStyle);
        }
        
        // Current target section
        string sectionName = RhythmGameController.Instance?.SelectedSection.ToString() ?? "---";
        GUILayout.Label($"▸ {sectionName}", sectionLabelStyle);
        
        GUILayout.EndArea();
        
        // Center: Judgement feedback (floating)
        if (judgementTimer > 0)
        {
            float alpha = Mathf.Clamp01(judgementTimer / 0.3f); // fade out in last 0.3s
            Color col = lastJudgementColor;
            col.a = alpha;
            judgementStyle.normal.textColor = col;
            
            float jW = 200f;
            float jH = 40f;
            float jX = (Screen.width / 3f - jW) / 2f;
            float jY = Screen.height / 3f * 0.35f; // upper third
            GUI.Label(new Rect(jX, jY, jW, jH), lastJudgement, judgementStyle);
        }
        
        // Bottom-left: small unlock button
        if (GUI.Button(new Rect(10, Screen.height / 3f - 35, 70, 22), "✎ Edit", buttonStyle))
        {
            UnlockPlacements();
        }
    }
    
    private void DrawResultsScreen()
    {
        var ctrl = RhythmGameController.Instance;
        if (ctrl == null) return;
        
        float panelW = 200f;
        float panelH = 260f;
        float centerX = (Screen.width / 3f - panelW) / 2f;
        float centerY = (Screen.height / 3f - panelH) / 2f;
        
        GUILayout.BeginArea(new Rect(centerX, centerY, panelW, panelH), hudBoxStyle);
        
        GUILayout.Label("🎵 ROUND COMPLETE", headerStyle);
        GUILayout.Space(10);
        
        GUILayout.Label(ctrl.TotalScore.ToString("N0"), scoreStyle);
        GUILayout.Space(6);
        
        GUILayout.Label($"<color=#44FF88>✦ Perfect:</color> {ctrl.PerfectCount}", labelStyle);
        GUILayout.Label($"<color=#FFEE33>Good:</color> {ctrl.GoodCount}", labelStyle);
        GUILayout.Label($"<color=#FF5555>Miss:</color> {ctrl.MissCount}", labelStyle);
        GUILayout.Label($"Max Combo: {ctrl.MaxCombo}x", labelStyle);
        
        GUILayout.Space(12);
        
        if (GUILayout.Button("▶ PLAY AGAIN", buttonStyle, GUILayout.Height(30)))
        {
            ctrl.RestartGame();
        }
        
        GUILayout.Space(4);
        
        if (GUILayout.Button("✎ Edit Placement", buttonStyle))
        {
            UnlockPlacements();
        }
        
        GUILayout.EndArea();
    }
}

} // namespace OrchestraMaestro