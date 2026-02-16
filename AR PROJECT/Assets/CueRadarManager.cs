using UnityEngine;
using System.Collections.Generic;

namespace OrchestraMaestro
{
    /// <summary>
    /// Manages all 4 cue radars (one per instrument).
    /// Queues cues per section, shows next after current resolves.
    /// </summary>
    public class CueRadarManager : MonoBehaviour
    {
        [Header("Radar Prefab")]
        [SerializeField] private GameObject cueRadarPrefab;
        
        [Header("Radar Positioning")]
        [SerializeField] private float radarDistance = 0.5f;  // Distance in front of instrument
        [SerializeField] private float radarHeight = 2.0f;    // Height above ground
        [SerializeField] private float radarScale = 0.5f;     // World scale of radar
        
        [Header("Timing")]
        [SerializeField] private float cueLeadTime = 2.0f;    // Show cue X seconds before hit
        
        [Header("References")]
        [SerializeField] private OrchestraPlacement orchestraPlacement;
        [SerializeField] private RhythmMap rhythmMap;
        
        [Header("3D Radar Settings")]
        [SerializeField] private bool use3DRadar = true;  // Use 3D quads instead of Canvas
        [SerializeField] private float radar3DSize = 0.4f; // Size in meters
        
        // Radar instances (one per section) - using interface pattern
        private CueRadarController[] radars = new CueRadarController[4];
        private CueRadar3D[] radars3D = new CueRadar3D[4];
        
        // Cue queues per section
        private Queue<RhythmCue>[] cueQueues = new Queue<RhythmCue>[4];
        
        // Active cues being displayed
        private RhythmCue?[] activeCues = new RhythmCue?[4];
        
        // Track which cues we've already queued (by timestamp + gesture hash)
        private HashSet<int> queuedCueHashes = new HashSet<int>();
        
        // Track if radars have been spawned
        private bool radarsSpawned = false;
        
        // Singleton
        public static CueRadarManager Instance { get; private set; }

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            // Initialize queues
            for (int i = 0; i < 4; i++)
            {
                cueQueues[i] = new Queue<RhythmCue>();
            }
        }

        private void Start()
        {
            // Find references if not set
            if (orchestraPlacement == null)
                orchestraPlacement = FindObjectOfType<OrchestraPlacement>();
            if (rhythmMap == null)
                rhythmMap = FindObjectOfType<RhythmMap>();
            
            // Subscribe to events
            if (rhythmMap != null)
            {
                rhythmMap.OnCueApproaching += HandleCueApproaching;
                rhythmMap.OnCueMissed += HandleCueMissed;
            }
            
            // Subscribe to game controller - use Invoke to wait for Instance to be ready
            Invoke(nameof(SubscribeToGameController), 0.1f);
        }

        private void SubscribeToGameController()
        {
            if (RhythmGameController.Instance != null)
            {
                RhythmGameController.Instance.OnGestureJudged += HandleGestureJudged;
                RhythmGameController.Instance.OnGameStateChanged += HandleGameStateChanged;
                Debug.Log("[CueRadarManager] Subscribed to RhythmGameController events");
            }
            else
            {
                Debug.LogWarning("[CueRadarManager] RhythmGameController.Instance is null!");
            }
        }

        private void Update()
        {
            // Check if we should spawn radars (game started playing)
            if (!radarsSpawned && rhythmMap != null && rhythmMap.IsPlaying)
            {
                Debug.Log("[CueRadarManager] Game is playing - spawning radars now");
                SpawnRadars();
                radarsSpawned = true;
            }
            
            // Only process cues if game is playing
            if (rhythmMap == null || !rhythmMap.IsPlaying) return;
            
            // Check for upcoming cues and queue them
            CheckUpcomingCues();
            
            // Update active radar timers
            UpdateRadarTimers();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            
            if (rhythmMap != null)
            {
                rhythmMap.OnCueApproaching -= HandleCueApproaching;
                rhythmMap.OnCueMissed -= HandleCueMissed;
            }
            
            if (RhythmGameController.Instance != null)
            {
                RhythmGameController.Instance.OnGestureJudged -= HandleGestureJudged;
                RhythmGameController.Instance.OnGameStateChanged -= HandleGameStateChanged;
            }
        }

        private void HandleGameStateChanged(RhythmGameController.GameState newState)
        {
            if (newState == RhythmGameController.GameState.Playing && !radarsSpawned)
            {
                SpawnRadars();
                radarsSpawned = true;
            }
            else if (newState == RhythmGameController.GameState.Results)
            {
                // Hide all radars when game ends
                for (int i = 0; i < 4; i++)
                {
                    if (radars[i] != null)
                        radars[i].Hide();
                    if (radars3D[i] != null)
                        radars3D[i].Hide();
                }
            }
        }

        #endregion

        #region Radar Spawning

        private void SpawnRadars()
        {
            if (cueRadarPrefab == null)
            {
                // Use 3D radars if enabled (more reliable in AR)
                if (use3DRadar)
                {
                    Debug.Log("[CueRadarManager] Creating 3D radars (no prefab, use3DRadar=true)");
                    Create3DRadars();
                    return;
                }
                
                Debug.LogWarning("[CueRadarManager] No radar prefab assigned. Creating placeholder radars.");
                CreatePlaceholderRadars();
                return;
            }
            
            Debug.Log("[CueRadarManager] Spawning radars from prefab...");
            
            for (int i = 0; i < 4; i++)
            {
                OrchestraSection section = (OrchestraSection)i;
                Vector3 position = GetRadarPosition(section);
                
                GameObject radarObj = Instantiate(cueRadarPrefab, position, Quaternion.identity);
                radarObj.name = $"CueRadar_{section}";
                
                // Fix for AR: Set the Canvas camera and sorting
                Canvas canvas = radarObj.GetComponentInChildren<Canvas>();
                if (canvas != null)
                {
                    canvas.worldCamera = Camera.main;
                    canvas.sortingOrder = 100; // Render on top
                    Debug.Log($"[CueRadarManager] Set canvas camera for {section}");
                }
                else
                {
                    Debug.LogError($"[CueRadarManager] No Canvas found in prefab!");
                }
                
                // Face camera
                if (Camera.main != null)
                {
                    radarObj.transform.LookAt(Camera.main.transform);
                    radarObj.transform.Rotate(0, 180, 0); // Flip to face camera
                }
                
                radars[i] = radarObj.GetComponent<CueRadarController>();
                
                if (radars[i] == null)
                {
                    Debug.LogError($"[CueRadarManager] Prefab missing CueRadarController component!");
                    radars[i] = radarObj.AddComponent<CueRadarController>();
                }
                
                // Keep radar active - don't hide it initially for debugging
                radarObj.SetActive(true);
                // radars[i].Hide(); // Commented out for debugging
                
                Debug.Log($"[CueRadarManager] Spawned radar for {section} at {position}");
            }
            
            Debug.Log("[CueRadarManager] Spawned 4 cue radars from prefab");
        }

        private void Create3DRadars()
        {
            // Debug: log all section positions
            for (int i = 0; i < 4; i++)
            {
                OrchestraSection section = (OrchestraSection)i;
                Vector3? pos = orchestraPlacement != null ? orchestraPlacement.GetSectionPosition(section) : null;
                Debug.Log($"[CueRadarManager] Section {section} position: {(pos.HasValue ? pos.Value.ToString() : "NONE (no instrument placed)")}");
            }
            
            for (int i = 0; i < 4; i++)
            {
                OrchestraSection section = (OrchestraSection)i;
                
                // Only create radar if the section has placed instruments
                Vector3? sectionPos = orchestraPlacement != null 
                    ? orchestraPlacement.GetSectionPosition(section) 
                    : null;
                    
                if (!sectionPos.HasValue)
                {
                    Debug.LogWarning($"[CueRadarManager] Skipping radar for {section} - no instrument placed! Cues for this section will have no radar.");
                    radars3D[i] = null;
                    continue;
                }
                
                Vector3 position = GetRadarPosition(section);
                radars3D[i] = CueRadar3D.Create(position, radar3DSize);
                radars3D[i].gameObject.name = $"CueRadar3D_{section}";
                
                Debug.Log($"[CueRadarManager] Created 3D radar for {section} at {position} (instrument at {sectionPos.Value})");
            }
            
            // Summary
            int created = 0;
            for (int i = 0; i < 4; i++) { if (radars3D[i] != null) created++; }
            Debug.Log($"[CueRadarManager] Created {created}/4 radars. Place instruments for all 4 sections to see all radars.");
        }

        private void CreatePlaceholderRadars()
        {
            Debug.Log("[CueRadarManager] Creating placeholder radars with visible UI");
            
            // Calculate canvas size based on radarScale (base size 1 unit = 1 meter)
            float canvasSize = 1f; // 1 meter base
            float pixelsPerUnit = 200f; // 200 pixels = 1 meter at scale 1
            
            // Create simple placeholder radars without prefab
            for (int i = 0; i < 4; i++)
            {
                OrchestraSection section = (OrchestraSection)i;
                Vector3 position = GetRadarPosition(section);
                
                GameObject radarObj = new GameObject($"CueRadar_{section}");
                radarObj.transform.position = position;
                
                // Add world-space canvas
                Canvas canvas = radarObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                RectTransform canvasRect = canvas.GetComponent<RectTransform>();
                
                // Scale the canvas size based on radarScale
                float scaledSize = canvasSize * radarScale;
                canvasRect.sizeDelta = new Vector2(pixelsPerUnit, pixelsPerUnit);
                canvasRect.localScale = new Vector3(scaledSize / pixelsPerUnit, scaledSize / pixelsPerUnit, 1f);
                
                // Add CanvasGroup for fading
                CanvasGroup canvasGroup = radarObj.AddComponent<CanvasGroup>();
                
                // Create outer ring
                GameObject outerRingObj = new GameObject("OuterRing");
                outerRingObj.transform.SetParent(canvasRect, false);
                UnityEngine.UI.Image outerRing = outerRingObj.AddComponent<UnityEngine.UI.Image>();
                outerRing.color = Color.white;
                RectTransform outerRect = outerRingObj.GetComponent<RectTransform>();
                outerRect.sizeDelta = new Vector2(180, 180);
                outerRect.anchoredPosition = Vector2.zero;
                
                // Create inner circle
                GameObject innerCircleObj = new GameObject("InnerCircle");
                innerCircleObj.transform.SetParent(canvasRect, false);
                UnityEngine.UI.Image innerCircle = innerCircleObj.AddComponent<UnityEngine.UI.Image>();
                innerCircle.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
                RectTransform innerRect = innerCircleObj.GetComponent<RectTransform>();
                innerRect.sizeDelta = new Vector2(140, 140);
                innerRect.anchoredPosition = Vector2.zero;
                
                // Create gesture text
                GameObject textObj = new GameObject("GestureText");
                textObj.transform.SetParent(canvasRect, false);
                TMPro.TextMeshProUGUI gestureText = textObj.AddComponent<TMPro.TextMeshProUGUI>();
                gestureText.text = "?";
                gestureText.fontSize = 80;
                gestureText.alignment = TMPro.TextAlignmentOptions.Center;
                gestureText.color = Color.white;
                RectTransform textRect = textObj.GetComponent<RectTransform>();
                textRect.sizeDelta = new Vector2(150, 150);
                textRect.anchoredPosition = Vector2.zero;
                
                // Add controller and wire references
                CueRadarController controller = radarObj.AddComponent<CueRadarController>();
                controller.SetReferences(outerRing, innerCircle, gestureText);
                radars[i] = controller;
                
                // Start hidden
                radarObj.SetActive(false);
                
                Debug.Log($"[CueRadarManager] Created radar for {section} at {position}, scale={radarScale}");
            }
        }

        private Vector3 GetRadarPosition(OrchestraSection section)
        {
            // Try to get position from orchestra placement
            if (orchestraPlacement != null)
            {
                Vector3? sectionPos = orchestraPlacement.GetSectionPosition(section);
                if (sectionPos.HasValue)
                {
                    // Position radar in front of and above the section
                    Vector3 toCamera = (Camera.main.transform.position - sectionPos.Value).normalized;
                    Vector3 finalPos = sectionPos.Value + toCamera * radarDistance + Vector3.up * radarHeight;
                    Debug.Log($"[CueRadarManager] GetRadarPosition({section}): sectionPos={sectionPos.Value}, finalPos={finalPos}");
                    return finalPos;
                }
                else
                {
                    Debug.LogWarning($"[CueRadarManager] No position found for section {section}");
                }
            }
            
            // Fallback: spread radars in a line
            float spacing = 1.0f;
            float startX = -1.5f * spacing;
            Vector3 fallbackPos = new Vector3(startX + (int)section * spacing, radarHeight, -2f);
            Debug.Log($"[CueRadarManager] GetRadarPosition({section}): using fallback={fallbackPos}");
            return fallbackPos;
        }

        #endregion

        #region Cue Management

        private void CheckUpcomingCues()
        {
            if (rhythmMap == null) return;
            
            // Get cues within lead time window
            var upcomingCues = rhythmMap.GetUpcomingCues(cueLeadTime);
            
            foreach (var cue in upcomingCues)
            {
                // Skip section navigation cues
                if (!cue.targetSection.HasValue) continue;
                if (cue.consumed) continue;
                
                // Generate hash to avoid duplicates
                int hash = GetCueHash(cue);
                if (queuedCueHashes.Contains(hash)) continue;
                
                // Queue this cue
                int sectionIndex = (int)cue.targetSection.Value;
                cueQueues[sectionIndex].Enqueue(cue);
                queuedCueHashes.Add(hash);
                
                Debug.Log($"[CueRadarManager] Queued cue: {cue.gestureType} for {cue.targetSection}");
                
                // If no active cue for this section, show it
                if (!activeCues[sectionIndex].HasValue)
                {
                    ShowNextCue(sectionIndex);
                }
            }
        }

        private void ShowNextCue(int sectionIndex)
        {
            if (cueQueues[sectionIndex].Count == 0)
            {
                activeCues[sectionIndex] = null;
                return;
            }
            
            RhythmCue nextCue = cueQueues[sectionIndex].Dequeue();
            activeCues[sectionIndex] = nextCue;
            
            // Calculate time until hit
            float timeUntilHit = nextCue.timestamp - (float)rhythmMap.CurrentSongTime;
            
            Debug.Log($"[CueRadarManager] ShowNextCue: section={sectionIndex}, gesture={nextCue.gestureType}, timeUntilHit={timeUntilHit}");
            
            if (radars[sectionIndex] != null)
            {
                Debug.Log($"[CueRadarManager] Calling ShowCue on radar {sectionIndex}");
                radars[sectionIndex].ShowCue(nextCue.gestureType, timeUntilHit);
                
                // Update radar position (in case instruments moved)
                UpdateRadarPosition(sectionIndex);
            }
            else if (radars3D[sectionIndex] != null)
            {
                Debug.Log($"[CueRadarManager] Calling ShowCue on 3D radar {sectionIndex}");
                radars3D[sectionIndex].ShowCue(nextCue.gestureType, timeUntilHit);
                
                // Update radar position
                radars3D[sectionIndex].transform.position = GetRadarPosition((OrchestraSection)sectionIndex);
            }
            else
            {
                Debug.LogError($"[CueRadarManager] Radar {sectionIndex} is null!");
            }
        }

        private void UpdateRadarTimers()
        {
            for (int i = 0; i < 4; i++)
            {
                if (!activeCues[i].HasValue) continue;
                
                RhythmCue cue = activeCues[i].Value;
                float remaining = cue.timestamp - (float)rhythmMap.CurrentSongTime;
                
                if (radars[i] != null)
                {
                    radars[i].UpdateTimer(remaining);
                    radars[i].transform.LookAt(Camera.main.transform);
                    radars[i].transform.Rotate(0, 180, 0);
                }
                else if (radars3D[i] != null)
                {
                    radars3D[i].UpdateTimer(remaining);
                    
                    // Continuously reposition radar at its section's instrument position
                    Vector3 targetPos = GetRadarPosition((OrchestraSection)i);
                    radars3D[i].transform.position = targetPos;
                    // 3D radar handles its own facing in Update()
                }
            }
        }

        private void UpdateRadarPosition(int sectionIndex)
        {
            if (radars[sectionIndex] != null)
            {
                Vector3 newPos = GetRadarPosition((OrchestraSection)sectionIndex);
                radars[sectionIndex].transform.position = newPos;
            }
        }

        private int GetCueHash(RhythmCue cue)
        {
            return (cue.timestamp.GetHashCode() * 397) ^ cue.gestureType.GetHashCode();
        }

        #endregion

        #region Event Handlers

        private void HandleCueApproaching(RhythmCue cue)
        {
            // Cues are now queued in Update via CheckUpcomingCues
            // This event can be used for additional effects if needed
        }

        private void HandleCueMissed(RhythmCue cue)
        {
            if (!cue.targetSection.HasValue) return;
            
            int sectionIndex = (int)cue.targetSection.Value;
            
            // Show miss feedback - check both Canvas and 3D radars
            if (activeCues[sectionIndex].HasValue && 
                Mathf.Approximately(activeCues[sectionIndex].Value.timestamp, cue.timestamp))
            {
                if (use3DRadar && radars3D[sectionIndex] != null)
                {
                    radars3D[sectionIndex].ShowResult(JudgementType.Miss);
                    StartCoroutine(ShowNextCueDelayed(sectionIndex, 0.5f));
                }
                else if (radars[sectionIndex] != null)
                {
                    radars[sectionIndex].ShowResult(JudgementType.Miss);
                    StartCoroutine(ShowNextCueDelayed(sectionIndex, 0.5f));
                }
            }
        }

        private void HandleGestureJudged(ScoringResult result)
        {
            // Find which section this was for
            int sectionIndex = (int)result.targetSection;
            
            if (!activeCues[sectionIndex].HasValue) return;
            
            // Check if this result matches the active cue (or just accept it for the section)
            bool isMatch = true;
            if (result.matchedCue.HasValue)
            {
                // Verify the matched cue corresponds to our active cue
                isMatch = Mathf.Approximately(result.matchedCue.Value.timestamp, 
                                               activeCues[sectionIndex].Value.timestamp);
            }
            
            if (isMatch)
            {
                // Show result on the appropriate radar type
                if (use3DRadar && radars3D[sectionIndex] != null)
                {
                    radars3D[sectionIndex].ShowResult(result.judgement);
                    StartCoroutine(ShowNextCueDelayed(sectionIndex, 0.5f));
                }
                else if (radars[sectionIndex] != null)
                {
                    radars[sectionIndex].ShowResult(result.judgement);
                    StartCoroutine(ShowNextCueDelayed(sectionIndex, 0.5f));
                }
            }
        }

        private System.Collections.IEnumerator ShowNextCueDelayed(int sectionIndex, float delay)
        {
            yield return new WaitForSeconds(delay);
            ShowNextCue(sectionIndex);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Full reset for starting a new game. Destroys old radars so fresh ones are spawned.
        /// </summary>
        public void ResetForNewGame()
        {
            // Clear cue queues and active cues
            for (int i = 0; i < 4; i++)
            {
                cueQueues[i].Clear();
                activeCues[i] = null;
                
                // Destroy old Canvas radars
                if (radars[i] != null)
                {
                    Destroy(radars[i].gameObject);
                    radars[i] = null;
                }
                // Destroy old 3D radars
                if (radars3D[i] != null)
                {
                    Destroy(radars3D[i].gameObject);
                    radars3D[i] = null;
                }
            }
            
            queuedCueHashes.Clear();
            radarsSpawned = false;
            
            Debug.Log("[CueRadarManager] Reset for new game - radars destroyed, ready to respawn");
        }

        /// <summary>
        /// Clear all queued cues and hide radars (for game reset)
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < 4; i++)
            {
                cueQueues[i].Clear();
                activeCues[i] = null;
                
                if (radars[i] != null)
                {
                    radars[i].Hide();
                }
                if (radars3D[i] != null)
                {
                    radars3D[i].Hide();
                }
            }
            
            queuedCueHashes.Clear();
        }

        /// <summary>
        /// Force update radar positions (call after orchestra placed)
        /// </summary>
        public void RefreshRadarPositions()
        {
            for (int i = 0; i < 4; i++)
            {
                UpdateRadarPosition(i);
            }
        }

        #endregion
    }
}
