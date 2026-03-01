using UnityEngine;
using UnityEngine.SceneManagement;
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
    private const float AutoPlaceDistance = 1.8f;
    private const float AutoPlaceSpacing = 0.55f;
    private const float ShiftStep = 0.25f;
    
    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private List<GameObject> placedMembers = new List<GameObject>();
    private int selectedIndex = 0;
    private bool isPlacementMode = true;
    // Section grouping for conducting game
    private List<GameObject>[] sectionMembers = new List<GameObject>[4];
    private int currentHighlightedSection = -1;
    private Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();
    
    // Track which sections have a placed member (one per section limit)
    private Dictionary<OrchestraSection, GameObject> sectionPlacedMember = new Dictionary<OrchestraSection, GameObject>();
    // Track which section each placed member belongs to
    private Dictionary<GameObject, OrchestraSection> memberToSection = new Dictionary<GameObject, OrchestraSection>();

    // Tutorial dialog steps (only in Tutorial mode)
    private int tutorialStep = 0;

    // Auto-place: trigger once when planes detected
    private bool autoPlaceTriggered;
    private float autoPlaceDelay = 0.8f;
    
    // Gameplay HUD state
    private string lastJudgement = "";
    private Color lastJudgementColor = Color.white;
    private float judgementTimer = 0f;
    private float judgementDisplayTime = 1.0f;

    // Section animation: play for this long after a correct cue (seconds)
    private const float SectionPlayDuration = 10f;
    private Dictionary<OrchestraSection, float> sectionAnimationEndTime = new Dictionary<OrchestraSection, float>();
    
    // Custom GUI styles (lazy-initialized)
    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private GUIStyle buttonStyle;
    private GUIStyle sectionLabelStyle;
    private GUIStyle judgementStyle;
    private GUIStyle comboStyle;
    private GUIStyle scoreStyle;
    private GUIStyle hudBoxStyle;
    private GUIStyle gesturePromptStyle;
    private bool stylesInitialized = false;
    private Vector2 placementScrollPos;

    // Song list scrolling text (LED-style when label doesn't fit)
    private const float SongScrollSpeed = 35f;   // pixels per second
    private const float SongScrollGap = 50f;     // gap before text repeats
    
    // Persist textures so GC doesn't destroy them
    private Texture2D texDarkBg;
    private Texture2D texAccentBg;
    private Texture2D texAccentHover;
    private Texture2D texSliderBg;
    private Texture2D texSliderFill;
    private Texture2D texSliderMarker;
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

        // Tutorial: ensure dialog controller exists and show welcome
        if (GameSettings.CurrentMode == GameMode.Tutorial && tutorialStep == 0)
        {
            EnsureTutorialDialogController();
            TutorialDialogController.Instance?.Show(
                "Welcome! Let's set up your orchestra. Tap a plane to place each musician, or use Auto Place for quick setup.",
                () => tutorialStep = 1);
        }
    }

    private void EnsureTutorialDialogController()
    {
        if (TutorialDialogController.Instance == null)
        {
            var go = new GameObject("TutorialDialogController");
            go.AddComponent<TutorialDialogController>();
        }
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
        // Auto-place when GameSettings.AutoPlace and planes detected
        if (isPlacementMode && GameSettings.AutoPlace && !autoPlaceTriggered && planeManager != null && raycastManager != null)
        {
            int planeCount = 0;
            foreach (var p in planeManager.trackables) planeCount++;
            if (planeCount > 0 && sectionPlacedMember.Count == 0)
            {
                autoPlaceDelay -= Time.deltaTime;
                if (autoPlaceDelay <= 0f)
                {
                    AutoPlaceAll();
                    autoPlaceTriggered = true;
                }
            }
        }

        // Tick judgement display timer
        if (judgementTimer > 0)
            judgementTimer -= Time.deltaTime;

        // When in game: stop section animations after SectionPlayDuration
        if (!isPlacementMode && sectionAnimationEndTime.Count > 0)
        {
            float now = Time.time;
            var toRemove = new List<OrchestraSection>();
            foreach (var kv in sectionAnimationEndTime)
            {
                if (now >= kv.Value)
                {
                    toRemove.Add(kv.Key);
                    if (sectionPlacedMember.TryGetValue(kv.Key, out GameObject member) && member != null)
                    {
                        var anim = member.GetComponent<Animator>();
                        if (anim != null)
                            anim.speed = 0f;
                    }
                }
            }
            foreach (var sec in toRemove)
                sectionAnimationEndTime.Remove(sec);
        }
        
        if (!isPlacementMode) return;

        // Manual placement: handle tap to place when AutoPlace is off
        if (!GameSettings.AutoPlace && raycastManager != null)
        {
            Vector2? tapPos = GetPlacementTapPosition();
            if (tapPos.HasValue && !GetPlacementPanelScreenRect().Contains(tapPos.Value))
                TryPlaceObject(tapPos.Value);
        }
    }

    /// <summary>Screen rect of the placement menu panel - taps inside are blocked (menu consumes input).</summary>
    private Rect GetPlacementPanelScreenRect()
    {
        const float scale = 3f;
        const float x = 10f, y = 10f, w = 300f, h = 440f;
        float px = x * scale, py = y * scale, pw = w * scale, ph = h * scale;
        return new Rect(px, Screen.height - py - ph, pw, ph);
    }

    private Vector2? GetPlacementTapPosition()
    {
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
                return t.position;
        }
#if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
            return Input.mousePosition;
#endif
        return null;
    }
    
    void LateUpdate()
    {
        UpdateSelectorHalo();
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
        member.transform.localScale = prefab.transform.localScale * 0.6f;
        
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

    /// <summary>Get prefab index for a section (for auto place)</summary>
    private int GetPrefabIndexForSection(OrchestraSection section)
    {
        if (prefabSections == null || orchestraPrefabs == null) return (int)section % Mathf.Max(1, orchestraPrefabs?.Length ?? 4);
        for (int i = 0; i < prefabSections.Length && i < orchestraPrefabs.Length; i++)
        {
            if (prefabSections[i] == section) return i;
        }
        return (int)section % Mathf.Max(1, orchestraPrefabs.Length);
    }

    /// <summary>Auto-place all 4 characters with spacing, at good distance from user</summary>
    private void AutoPlaceAll()
    {
        if (orchestraPrefabs == null || orchestraPrefabs.Length == 0)
        {
            Debug.LogWarning("[OrchestraPlacement] No prefabs for auto place");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[OrchestraPlacement] No camera for auto place");
            return;
        }

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (!raycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            Debug.LogWarning("[OrchestraPlacement] Auto place: no plane detected. Point at a surface or resume scanning.");
            return;
        }

        Pose hitPose = hits[0].pose;
        Vector3 camPos = cam.transform.position;
        float hitDist = Vector3.Distance(camPos, hitPose.position);
        float useDist = Mathf.Max(hitDist, AutoPlaceDistance);
        Vector3 center = camPos + (hitPose.position - camPos).normalized * useDist;
        center.y = hitPose.position.y;

        Vector3 right = Vector3.Cross(Vector3.up, (center - camPos).normalized).normalized;
        if (right.sqrMagnitude < 0.01f) right = hitPose.rotation * Vector3.right;

        ClearAllPlacements();

        float[] offsets = { -1.5f, -0.5f, 0.5f, 1.5f };
        for (int i = 0; i < 4; i++)
        {
            OrchestraSection section = (OrchestraSection)i;
            int prefabIdx = GetPrefabIndexForSection(section);
            if (orchestraPrefabs[prefabIdx] == null) continue;

            Vector3 pos = center + right * (offsets[i] * AutoPlaceSpacing);
            Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(pos - camPos, Vector3.up));
            PlaceOrchestraMemberAt(pos, rot, section, prefabIdx);
        }

        Debug.Log($"[OrchestraPlacement] Auto placed 4 characters at distance {useDist:F2}m, spacing {AutoPlaceSpacing}m");
    }

    /// <summary>Shift all placed orchestra members toward the camera (up)</summary>
    private void ShiftOrchestraUp()
    {
        ShiftOrchestraTowardCamera(ShiftStep);
    }

    /// <summary>Shift all placed orchestra members away from the camera (down)</summary>
    private void ShiftOrchestraDown()
    {
        ShiftOrchestraTowardCamera(-ShiftStep);
    }

    private void ShiftOrchestraTowardCamera(float step)
    {
        if (placedMembers.Count == 0) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 camPos = cam.transform.position;
        Vector3 avgPos = Vector3.zero;
        foreach (var m in placedMembers) avgPos += m.transform.position;
        avgPos /= placedMembers.Count;
        Vector3 dir = (camPos - avgPos).normalized;
        dir.y = 0;
        if (dir.sqrMagnitude < 0.01f) return;
        dir.Normalize();

        foreach (var m in placedMembers)
            m.transform.position += dir * step;
    }

    /// <summary>Raise all placed orchestra members (world Y up)</summary>
    private void ShiftOrchestraRaise()
    {
        ShiftOrchestraVertical(ShiftStep);
    }

    /// <summary>Lower all placed orchestra members (world Y down)</summary>
    private void ShiftOrchestraLower()
    {
        ShiftOrchestraVertical(-ShiftStep);
    }

    private void ShiftOrchestraVertical(float step)
    {
        if (placedMembers.Count == 0) return;
        foreach (var m in placedMembers)
            m.transform.position += Vector3.up * step;
    }

    private void PlaceOrchestraMemberAt(Vector3 position, Quaternion rotation, OrchestraSection section, int prefabIndex)
    {
        GameObject prefab = orchestraPrefabs[prefabIndex];
        if (prefab == null) return;

        Quaternion finalRotation = rotation * prefab.transform.rotation;
        GameObject member = Instantiate(prefab, position, finalRotation);
        member.transform.localScale = prefab.transform.localScale * 0.6f;

        Renderer[] renderers = member.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds combinedBounds = renderers[0].bounds;
            for (int j = 1; j < renderers.Length; j++)
                combinedBounds.Encapsulate(renderers[j].bounds);
            Vector3 boundsCenter = combinedBounds.center;
            Vector3 correction = position - boundsCenter;
            correction.y = 0f;
            member.transform.position += correction;
        }

        placedMembers.Add(member);
        sectionMembers[(int)section].Add(member);
        sectionPlacedMember[section] = member;
        memberToSection[member] = section;
        CacheOriginalColors(member);
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
    
    /// <summary>Remove all tracked planes and restart plane detection from scratch. Also clears all placements.</summary>
    public void RescanPlanes()
    {
        ClearAllPlacements();
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
        
        // Make characters static until a correct cue triggers their animation
        SetAllSectionAnimatorsSpeed(0f);
        
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

        // Play this section's character animation for SectionPlayDuration on Perfect/Good
        if (result.judgement == JudgementType.Perfect || result.judgement == JudgementType.Good)
        {
            OrchestraSection section = result.targetSection;
            sectionAnimationEndTime[section] = Time.time + SectionPlayDuration;
            if (sectionPlacedMember.TryGetValue(section, out GameObject member) && member != null)
            {
                var anim = member.GetComponent<Animator>();
                if (anim != null)
                    anim.speed = 1f;
            }
        }
    }

    /// <summary>Reset game state and return to main menu.</summary>
    public void ExitToMainMenu()
    {
        ClearAllPlacements();
        if (RhythmGameController.Instance != null)
        {
            RhythmGameController.Instance.OnGestureJudged -= OnGestureJudgedHUD;
            RhythmGameController.Instance.EndGame();  // Stop playback
            RhythmGameController.Instance.ResetToSetup();  // Reset state for next game
        }
        if (CueRadarManager.Instance != null)
            CueRadarManager.Instance.ResetForNewGame();
        isPlacementMode = true;
        SceneManager.LoadScene("StartScreen");
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
        
        // Restore animator speed when returning to placement
        SetAllSectionAnimatorsSpeed(1f);
        sectionAnimationEndTime.Clear();
        
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

    /// <summary>Set Animator.speed on all placed section members (0 = static, 1 = playing)</summary>
    private void SetAllSectionAnimatorsSpeed(float speed)
    {
        foreach (var kv in sectionPlacedMember)
        {
            if (kv.Value == null) continue;
            var anim = kv.Value.GetComponent<Animator>();
            if (anim != null)
                anim.speed = speed;
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

    /// <summary>Get the center of mass of a section (full 3D bounds center, for effects targeting the character body)</summary>
    public Vector3? GetSectionCenterOfMass(OrchestraSection section)
    {
        List<GameObject> members = sectionMembers[(int)section];

        if (members == null || members.Count == 0)
            return null;

        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (var member in members)
        {
            if (member != null)
            {
                Renderer[] renderers = member.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds combinedBounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        combinedBounds.Encapsulate(renderers[i].bounds);
                    sum += combinedBounds.center;
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
        
        gesturePromptStyle = new GUIStyle(GUI.skin.label);
        gesturePromptStyle.fontSize = 13;
        gesturePromptStyle.fontStyle = FontStyle.Bold;
        gesturePromptStyle.normal.textColor = new Color(1f, 0.95f, 0.6f);
        gesturePromptStyle.alignment = TextAnchor.MiddleCenter;
        
        texSliderBg = new Texture2D(1, 1);
        texSliderBg.SetPixel(0, 0, new Color(0.2f, 0.2f, 0.3f, 0.6f));
        texSliderBg.Apply();
        
        texSliderFill = new Texture2D(1, 1);
        texSliderFill.SetPixel(0, 0, new Color(0.3f, 0.7f, 1f, 0.85f));
        texSliderFill.Apply();
        
        texSliderMarker = new Texture2D(1, 1);
        texSliderMarker.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.95f));
        texSliderMarker.Apply();
    }
    
    [Header("Selector Halo")]
    [Tooltip("Prefab for the glowing selector under the selected member (e.g. Magic circle).")]
    [SerializeField] private GameObject selectorHaloPrefab;
    
    private GameObject selectorHaloInstance;

    private void UpdateSelectorHalo()
    {
        // Only show halo during the active game (not placement mode)
        var gameState = RhythmGameController.Instance?.CurrentState ?? RhythmGameController.GameState.Setup;
        if (gameState != RhythmGameController.GameState.Playing || selectorHaloPrefab == null)
        {
            if (selectorHaloInstance != null) selectorHaloInstance.SetActive(false);
            return;
        }
        // Find the currently selected section and its member
        OrchestraSection section = RhythmGameController.Instance.SelectedSection;
        GameObject selectedMember = null;
        sectionPlacedMember.TryGetValue(section, out selectedMember);
        if (selectedMember == null)
        {
            if (selectorHaloInstance != null) selectorHaloInstance.SetActive(false);
            return;
        }
        if (selectorHaloInstance == null)
        {
            selectorHaloInstance = Instantiate(selectorHaloPrefab);
            selectorHaloInstance.name = "SelectorHaloInstance";
        }
        selectorHaloInstance.SetActive(true);
        // Use renderer bounds center for robust centering (like radar)
        Renderer[] renderers = selectedMember.GetComponentsInChildren<Renderer>();
        Vector3 haloPos = selectedMember.transform.position;
        if (renderers.Length > 0) {
            Bounds combinedBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                combinedBounds.Encapsulate(renderers[i].bounds);
            haloPos = combinedBounds.center;
            haloPos.y = selectedMember.transform.position.y;
        }
        selectorHaloInstance.transform.position = haloPos + Vector3.down * 0.01f;
        selectorHaloInstance.transform.rotation = Quaternion.identity;
        float baseScale = Mathf.Max(selectedMember.transform.localScale.x, selectedMember.transform.localScale.z);
        float s = baseScale * 0.1f; 
        selectorHaloInstance.transform.localScale = new Vector3(s, s, s);
    }

    private void DrawPlacementUI()
    {
        float panelW = 300f;
        float panelH = 440f;
        GUILayout.BeginArea(new Rect(10, 10, panelW, panelH), hudBoxStyle);
        
        GUILayout.Label("🎵 ORCHESTRA SETUP", headerStyle);
        if (GUILayout.Button("← Exit to Menu", buttonStyle))
            ExitToMainMenu();
        GUILayout.Space(4);

        placementScrollPos = GUILayout.BeginScrollView(placementScrollPos, GUILayout.Height(panelH - 80));
        GUILayout.Space(4);
        
        // Status line
        int placedCount = sectionPlacedMember.Count;
        int planeCount = 0;
        if (planeManager != null)
        {
            foreach (var p in planeManager.trackables) planeCount++;
        }
        GUILayout.Label($"Planes: {planeCount}  |  Placed: {placedCount}/4", labelStyle);
        
        GUILayout.Space(8);
        
        if (GUILayout.Button("🔄 Rescan Planes", buttonStyle))
            RescanPlanes();

        if (GUILayout.Button("✨ Auto Place", buttonStyle))
            AutoPlaceAll();

        GUI.enabled = placedCount > 0;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("▲ Closer", buttonStyle))
            ShiftOrchestraUp();
        if (GUILayout.Button("▼ Further", buttonStyle))
            ShiftOrchestraDown();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("⬆ Raise", buttonStyle))
            ShiftOrchestraRaise();
        if (GUILayout.Button("⬇ Lower", buttonStyle))
            ShiftOrchestraLower();
        GUILayout.EndHorizontal();
        GUI.enabled = true;
        
        GUILayout.Space(8);

        // Manual placement: section selector when Auto Place is off
        if (!GameSettings.AutoPlace)
        {
            GUILayout.BeginHorizontal();
            string[] sectionNames = { "Drum", "Flute", "Pipe", "Xylophone" };
            for (int i = 0; i < 4; i++)
            {
                OrchestraSection sec = (OrchestraSection)i;
                bool hasPlaced = sectionPlacedMember.ContainsKey(sec);
                bool isSelected = selectedIndex == GetPrefabIndexForSection(sec);
                string label = hasPlaced ? $"{sectionNames[i]} ✓" : sectionNames[i];
                if (isSelected) label = "▶ " + label;
                if (GUILayout.Button(label, buttonStyle, GUILayout.Height(28)))
                {
                    selectedIndex = GetPrefabIndexForSection(sec);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("<i>Tap a plane (right side of screen) to place</i>", labelStyle);
            GUILayout.Space(4);
        }
        
        GUILayout.Space(8);
        
        // Lock button — highlight if all 4 placed
        GUI.enabled = placedCount > 0;
        string lockLabel = placedCount >= 4 ? "✔ START GAME" : $"Lock ({placedCount}/4 placed)";
        if (GUILayout.Button(lockLabel, buttonStyle, GUILayout.Height(30)))
            LockPlacements();
        GUI.enabled = true;

        // Tutorial: preLock hint when all 4 placed (show once)
        if (GameSettings.CurrentMode == GameMode.Tutorial && placedCount >= 4 && tutorialStep == 1)
        {
            EnsureTutorialDialogController();
            TutorialDialogController.Instance?.Show("Great! Tap START GAME to lock placements and pick a song.");
            tutorialStep = 2;
        }
        
        GUILayout.Space(4);
        GUILayout.Label(GameSettings.AutoPlace ? "<i>Scan the room, then tap Auto Place</i>" : "<i>Select a musician above, then tap a plane to place</i>", labelStyle);
        
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
    
    private void DrawGameplayHUD()
    {
        var gameState = RhythmGameController.Instance?.CurrentState 
            ?? RhythmGameController.GameState.Setup;
        
        if (gameState == RhythmGameController.GameState.Setup)
        {
            DrawSongSelection();
            return;
        }
        
        if (gameState == RhythmGameController.GameState.Results)
        {
            DrawResultsScreen();
            return;
        }
        
        float sw = Screen.width / 3f;
        float sh = Screen.height / 3f;
        var ctrl = RhythmGameController.Instance;
        
        // ── Top-left: song title + time ──
        float t = ctrl != null ? ctrl.CurrentSongTime : 0f;
        string songTitle = ctrl?.CurrentSong != null ? ctrl.CurrentSong.songName : "—";
        string timeStr = $"{Mathf.FloorToInt(t / 60f)}:{Mathf.FloorToInt(t % 60f):D2}";
        int savedLabelSize = labelStyle.fontSize;
        labelStyle.fontSize = 14;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.wordWrap = true;
        DrawOutlinedLabel(new Rect(10, 6, 220f, 44f), $"{songTitle}  {timeStr}", labelStyle, new Color(0.85f, 0.88f, 0.95f));
        labelStyle.wordWrap = false;
        labelStyle.fontStyle = FontStyle.Normal;
        labelStyle.fontSize = savedLabelSize;
        
        // ── Top-right: score + combo ──
        int score = ctrl?.TotalScore ?? 0;
        int combo = ctrl?.Combo ?? 0;
        scoreStyle.fontSize = 27;
        scoreStyle.alignment = TextAnchor.MiddleRight;
        DrawOutlinedLabel(new Rect(sw - 150, 4, 140, 30), score.ToString("N0"), scoreStyle, Color.white);
        if (combo > 1)
        {
            comboStyle.alignment = TextAnchor.MiddleRight;
            DrawOutlinedLabel(new Rect(sw - 150, 34, 140, 20), $"{combo}x COMBO", comboStyle, new Color(1f, 0.84f, 0f));
        }
        scoreStyle.fontSize = 18;
        scoreStyle.alignment = TextAnchor.MiddleCenter;
        comboStyle.alignment = TextAnchor.MiddleCenter;
        
        // ── Top-center: gesture prompt (bold, no box) + timing slider ──
        var cueInfo = CueRadarManager.Instance?.GetCurrentActiveGesture() ?? (null, null, Color.white);
        float cueProgress = CueRadarManager.Instance?.GetCurrentCueProgress() ?? -1f;
        
        if (cueInfo.gestureName != null)
        {
            float promptW = 260f;
            float promptX = (sw - promptW) / 2f;
            
            // Gesture name — large bold (doubled from 16 to 32)
            gesturePromptStyle.fontSize = 32;
            gesturePromptStyle.alignment = TextAnchor.MiddleCenter;
            DrawOutlinedLabel(new Rect(promptX, 0, promptW, 42), cueInfo.gestureName, gesturePromptStyle, cueInfo.timingColor);
            gesturePromptStyle.fontSize = 13;
            
            // Section name below — also outlined
            int savedSectionSize = sectionLabelStyle.fontSize;
            sectionLabelStyle.fontSize = 14;
            DrawOutlinedLabel(new Rect(promptX, 40, promptW, 20), $"[{cueInfo.sectionName}]", sectionLabelStyle, cueInfo.timingColor);
            sectionLabelStyle.normal.textColor = new Color(0.6f, 0.8f, 1f);
            sectionLabelStyle.fontSize = savedSectionSize;
            
            // Timing slider below the gesture
            if (cueProgress >= 0f)
            {
                float sliderW = 160f;
                float sliderH = 8f;
                float sliderX = (sw - sliderW) / 2f;
                float sliderY = 62f;
                
                // Background track
                GUI.DrawTexture(new Rect(sliderX, sliderY, sliderW, sliderH), texSliderBg);
                
                // Fill (left to right as cue approaches)
                float fillW = sliderW * Mathf.Clamp01(cueProgress);
                Color fillCol = cueInfo.timingColor;
                texSliderFill.SetPixel(0, 0, new Color(fillCol.r, fillCol.g, fillCol.b, 0.85f));
                texSliderFill.Apply();
                GUI.DrawTexture(new Rect(sliderX, sliderY, fillW, sliderH), texSliderFill);
                
                // Hit marker (white line at right end = perfect timing)
                GUI.DrawTexture(new Rect(sliderX + sliderW - 2, sliderY - 1, 3, sliderH + 2), texSliderMarker);
            }
        }
        
        // ── Right-center: skip button ──
        if (ctrl != null && ctrl.CanSkipToFirstCue)
        {
            float skipW = 110f;
            float skipH = 28f;
            float skipX = sw - skipW - 10;
            float skipY = (sh - skipH) / 2f;
            if (GUI.Button(new Rect(skipX, skipY, skipW, skipH), "⏩ Skip", buttonStyle))
                ctrl.SkipToFirstCueMinus5();
        }
        
        // ── Center: Judgement feedback (Perfect / Good / Miss) ──
        if (judgementTimer > 0)
        {
            float alpha = Mathf.Clamp01(judgementTimer / 0.3f);
            Color col = lastJudgementColor;
            col.a = alpha;
            
            float jW = 200f;
            float jH = 40f;
            float jX = (sw - jW) / 2f;
            float jY = sh * 0.45f;
            Rect jRect = new Rect(jX, jY, jW, jH);
            DrawOutlinedLabel(jRect, lastJudgement, judgementStyle, col);
        }

        // ── Tutorial: paused — perform gesture ──
        if (ctrl != null && ctrl.IsTutorialPaused)
        {
            headerStyle.normal.textColor = new Color(1f, 0.9f, 0.5f);
            GUI.Label(new Rect((sw - 200f) / 2f, sh * 0.25f, 200f, 30f), "Perform the gesture!", headerStyle);
        }

        // Tutorial: wrong gesture hint
        string wrongHint = ctrl?.TutorialWrongGestureHint;
        if (!string.IsNullOrEmpty(wrongHint))
        {
            labelStyle.normal.textColor = new Color(1f, 0.6f, 0.3f);
            GUI.Label(new Rect((sw - 280f) / 2f, sh * 0.5f, 280f, 50f), wrongHint, labelStyle);
        }
        
        // ── Bottom-left: MQTT indicator + edit button ──
        bool mqttConnected = GameSettings.TestMode || (MQTTManager.Instance != null && MQTTManager.Instance.IsConnected);
        string mqttText = GameSettings.TestMode ? "MQTT: Connected (Test)" : (mqttConnected ? "MQTT: Connected" : "MQTT: Disconnected");
        Color mqttColor = mqttConnected ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.3f, 0.2f);
        int savedLabelSize2 = labelStyle.fontSize;
        labelStyle.fontSize = 11;
        DrawOutlinedLabel(new Rect(10, sh - 58, 160, 18), mqttText, labelStyle, mqttColor);
        labelStyle.fontSize = savedLabelSize2;

        if (GUI.Button(new Rect(10, sh - 35, 70, 22), "✎ Edit", buttonStyle))
        {
            UnlockPlacements();
        }
    }
    
    private void DrawOutlinedLabel(Rect rect, string text, GUIStyle style, Color fgColor)
    {
        float a = fgColor.a;
        style.normal.textColor = new Color(0, 0, 0, a);
        for (int ox = -1; ox <= 1; ox++)
            for (int oy = -1; oy <= 1; oy++)
                if (ox != 0 || oy != 0)
                    GUI.Label(new Rect(rect.x + ox, rect.y + oy, rect.width, rect.height), text, style);
        style.normal.textColor = fgColor;
        GUI.Label(rect, text, style);
    }

    private void DrawSongSelection()
    {
        var ctrl = RhythmGameController.Instance;
        if (ctrl == null) return;

        // Tutorial: song selection hint (show once)
        if (GameSettings.CurrentMode == GameMode.Tutorial && tutorialStep == 2)
        {
            EnsureTutorialDialogController();
            TutorialDialogController.Instance?.Show("Choose a song to conduct. Tap one to begin.");
            tutorialStep = 3;
        }

        float panelW = 280f;  // Wider for long song names
        float panelH = 320f;
        float centerX = (Screen.width / 3f - panelW) / 2f;
        float centerY = (Screen.height / 3f - panelH) / 2f;

        GUILayout.BeginArea(new Rect(centerX, centerY, panelW, panelH), hudBoxStyle);
        GUILayout.Label("Choose a Song", headerStyle);
        GUILayout.Space(12);

        var songs = ctrl.AvailableSongs;
        if (songs != null && songs.Length > 0)
        {
            float btnH = 36f;
            float btnY = 12f + 24f + 12f;  // below header + space
            float btnW = panelW - 24f;     // padding
            for (int i = 0; i < songs.Length; i++)
            {
                var song = songs[i];
                if (song == null) continue;
                string label = string.IsNullOrEmpty(song.artistName)
                    ? song.songName
                    : $"{song.songName} — {song.artistName}";
                Rect btnRect = new Rect(12, btnY, btnW, btnH);
                if (GUI.Button(btnRect, ""))
                {
                    ctrl.SelectSongAndStart(song);
                }
                // Draw scrolling text when it doesn't fit (LED-style)
                float textW = buttonStyle.CalcSize(new GUIContent(label)).x;
                GUI.BeginGroup(btnRect);
                if (textW > btnW - 16f)
                {
                    float cycle = textW + SongScrollGap;
                    float offset = (Time.time * SongScrollSpeed) % cycle;
                    float textX = btnW - textW - offset;  // Start from right, scroll left
                    Rect textRect = new Rect(textX, 0, textW + cycle, btnH);
                    GUI.Label(textRect, label, buttonStyle);
                }
                else
                {
                    GUI.Label(new Rect(0, 0, btnW, btnH), label, buttonStyle);
                }
                GUI.EndGroup();
                btnY += btnH + 4f;
            }
            float reservedH = btnY - (12f + 24f + 12f) + 8f;
            GUILayout.Space(Mathf.Max(8, reservedH));  // Reserve space so Back button doesn't overlap
        }

        GUILayout.Space(12);
        if (GUILayout.Button("← Back to Placement", buttonStyle))
        {
            UnlockPlacements();
        }
        GUILayout.EndArea();
    }
    
    private void DrawResultsScreen()
    {
        var ctrl = RhythmGameController.Instance;
        if (ctrl == null) return;
        
        float panelW = 200f;
        float panelH = 300f;
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
            ctrl.ReturnToSongSelection();
        }
        
        GUILayout.Space(4);
        
        if (GUILayout.Button("✎ Edit Placement", buttonStyle))
        {
            UnlockPlacements();
        }

        GUILayout.Space(4);
        if (GUILayout.Button("← Return to Main Menu", buttonStyle))
        {
            ExitToMainMenu();
        }
        
        GUILayout.EndArea();
    }
}

} // namespace OrchestraMaestro