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
        [SerializeField] private float cueLeadTime = 3.0f;    // Show cue X seconds before hit
        
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
        
        /// <summary>Get the currently active gesture name, section, and timing color (for HUD display)</summary>
        public (string gestureName, string sectionName, Color timingColor) GetCurrentActiveGesture()
        {
            for (int i = 0; i < 4; i++)
            {
                if (activeCues[i].HasValue)
                {
                    var cue = activeCues[i].Value;
                    string gesture = cue.gestureType switch
                    {
                        GestureType.UP => "↑ UP",
                        GestureType.DOWN => "↓ DOWN",
                        GestureType.PUNCH => "👊 PUNCH",
                        GestureType.WITHDRAW => "👊 WITHDRAW",
                        GestureType.W_SHAPE => "W",
                        GestureType.HOURGLASS_SHAPE => "HOURGLASS",
                        GestureType.LIGHTNING_BOLT_SHAPE => "LIGHTNING",
                        GestureType.TRIPLE_CLOCKWISE_CIRCLE => "3x CLOCKWISE",
                        _ => cue.gestureType.ToString().Replace("_SHAPE", "").Replace("_", " ")
                    };
                    
                    // Get timing color from the active radar
                    Color color = Color.white;
                    if (radars3D[i] != null)
                        color = radars3D[i].CurrentTimingColor;
                    
                    return (gesture, cue.targetSection.ToString(), color);
                }
            }
            return (null, null, Color.white);
        }

        /// <summary>
        /// Try to get the gesture type of the currently active requested cue.
        /// Returns true when a cue is active, false otherwise.
        /// </summary>
        public bool TryGetCurrentActiveGestureType(out GestureType gestureType)
        {
            for (int i = 0; i < activeCues.Length; i++)
            {
                if (activeCues[i].HasValue)
                {
                    gestureType = activeCues[i].Value.gestureType;
                    return true;
                }
            }

            gestureType = GestureType.ERROR;
            return false;
        }
        
        /// <summary>Get timing progress of the nearest active cue (0 = just appeared, 1 = hit time, >1 = past due). Returns -1 if no active cue.</summary>
        public float GetCurrentCueProgress()
        {
            if (rhythmMap == null) return -1f;
            float songTime = rhythmMap.CurrentSongTime;
            for (int i = 0; i < 4; i++)
            {
                if (activeCues[i].HasValue)
                {
                    float remaining = activeCues[i].Value.timestamp - songTime;
                    return 1f - Mathf.Clamp01(remaining / cueLeadTime);
                }
            }
            return -1f;
        }

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
            
            // Re-bind when a new scene loads (handles DontDestroyOnLoad survival)
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            BindReferences();
        }
        
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            // Scene reloaded — old references are dead, re-acquire everything
            ResetForNewGame();
            BindReferences();
            // Debug.Log($"[CueRadarManager] Scene '{scene.name}' loaded — re-bound references");
        }
        
        private void BindReferences()
        {
            orchestraPlacement = FindObjectOfType<OrchestraPlacement>();
            rhythmMap = FindObjectOfType<RhythmMap>();

            cueLeadTime = GameSettings.DifficultyLevel switch
            {
                Difficulty.Easy => 8f,
                Difficulty.Hard => 4f,
                _ => 6f
            };

            // Unsubscribe from any old (potentially destroyed) objects first, then subscribe fresh
            if (rhythmMap != null)
            {
                rhythmMap.OnCueApproaching -= HandleCueApproaching;
                rhythmMap.OnCueMissed -= HandleCueMissed;
                rhythmMap.OnCueApproaching += HandleCueApproaching;
                rhythmMap.OnCueMissed += HandleCueMissed;
            }
            
            // Subscribe to game controller (delay to let Awake set Instance)
            Invoke(nameof(SubscribeToGameController), 0.1f);
        }

        private void SubscribeToGameController()
        {
            if (RhythmGameController.Instance != null)
            {
                RhythmGameController.Instance.OnGestureJudged -= HandleGestureJudged;
                RhythmGameController.Instance.OnGameStateChanged -= HandleGameStateChanged;
                RhythmGameController.Instance.OnGestureJudged += HandleGestureJudged;
                RhythmGameController.Instance.OnGameStateChanged += HandleGameStateChanged;
                // Debug.Log("[CueRadarManager] Subscribed to RhythmGameController events");
            }
            else
            {
                // Debug.LogWarning("[CueRadarManager] RhythmGameController.Instance is null!");
            }
        }

        private void Update()
        {
            // Check if we should spawn radars (game started playing)
            if (!radarsSpawned && rhythmMap != null && rhythmMap.IsPlaying)
            {
                // Debug.Log("[CueRadarManager] Game is playing - spawning radars now");
                SpawnRadars();
                radarsSpawned = true;
            }

            if (ShouldSuppressRadarCues())
            {
                HideAndClearAllRadars();
                return;
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
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
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
                    // Debug.Log("[CueRadarManager] Creating 3D radars (no prefab, use3DRadar=true)");
                    Create3DRadars();
                    return;
                }
                
                // Debug.LogWarning("[CueRadarManager] No radar prefab assigned. Creating placeholder radars.");
                CreatePlaceholderRadars();
                return;
            }
            
            // Debug.Log("[CueRadarManager] Spawning radars from prefab...");
            
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
                    // Debug.Log($"[CueRadarManager] Set canvas camera for {section}");
                }
                else
                {
                    // Debug.LogError($"[CueRadarManager] No Canvas found in prefab!");
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
                    // Debug.LogError($"[CueRadarManager] Prefab missing CueRadarController component!");
                    radars[i] = radarObj.AddComponent<CueRadarController>();
                }
                
                // Keep radar active - don't hide it initially for debugging
                radarObj.SetActive(true);
                // radars[i].Hide(); // Commented out for debugging
                
                // Debug.Log($"[CueRadarManager] Spawned radar for {section} at {position}");
            }
            
            // Debug.Log("[CueRadarManager] Spawned 4 cue radars from prefab");
        }

        private void Create3DRadars()
        {
            // Debug: log all section positions
            for (int i = 0; i < 4; i++)
            {
                OrchestraSection section = (OrchestraSection)i;
                Vector3? pos = orchestraPlacement != null ? orchestraPlacement.GetSectionPosition(section) : null;
                // Debug.Log($"[CueRadarManager] Section {section} position: {(pos.HasValue ? pos.Value.ToString() : "NONE (no instrument placed)")}");
            }
            
            int created = 0;
            for (int i = 0; i < 4; i++)
            {
                OrchestraSection section = (OrchestraSection)i;
                
                // Always create radar - use fallback position when no instrument placed (avoids "radar is null" after scene reload)
                Vector3 position = GetRadarPosition(section);
                radars3D[i] = CueRadar3D.Create(position, radar3DSize);
                radars3D[i].gameObject.name = $"CueRadar3D_{section}";
                created++;
                
                Vector3? sectionPos = orchestraPlacement != null ? orchestraPlacement.GetSectionPosition(section) : null;
                Debug.Log($"[CueRadarManager] Created 3D radar for {section} at {position}" + 
                    (sectionPos.HasValue ? $" (instrument at {sectionPos.Value})" : " (fallback - no instrument)"));
            }
            
            // Debug.Log("[CueRadarManager] Created 4/4 radars.");
        }

        private void CreatePlaceholderRadars()
        {
            // Debug.Log("[CueRadarManager] Creating placeholder radars with visible UI");
            
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
                outerRing.enabled = false; // Hide old target
                RectTransform outerRect = outerRingObj.GetComponent<RectTransform>();
                outerRect.sizeDelta = new Vector2(180, 180);
                outerRect.anchoredPosition = Vector2.zero;
                
                // Create inner circle
                GameObject innerCircleObj = new GameObject("InnerCircle");
                innerCircleObj.transform.SetParent(canvasRect, false);
                UnityEngine.UI.Image innerCircle = innerCircleObj.AddComponent<UnityEngine.UI.Image>();
                innerCircle.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
                innerCircle.enabled = false; // Hide old target
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
                
                // Debug.Log($"[CueRadarManager] Created radar for {section} at {position}, scale={radarScale}");
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
                    Vector3 finalPos = sectionPos.Value + toCamera * radarDistance + Vector3.up * (radarHeight * 0.75f);
                    // Debug.Log($"[CueRadarManager] GetRadarPosition({section}): sectionPos={sectionPos.Value}, finalPos={finalPos}");
                    return finalPos;
                }
                else
                {
                    // Debug.LogWarning($"[CueRadarManager] No position found for section {section}");
                }
            }
            
            // Fallback: spread radars in a line
            float spacing = 1.0f;
            float startX = -1.5f * spacing;
            Vector3 fallbackPos = new Vector3(startX + (int)section * spacing, radarHeight, -2f);
            // Debug.Log($"[CueRadarManager] GetRadarPosition({section}): using fallback={fallbackPos}");
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

                // Tutorial: LEFT/RIGHT are navigation training gestures, not countdown radar cues.
                if (GameSettings.CurrentMode == GameMode.Tutorial &&
                    (cue.gestureType == GestureType.LEFT || cue.gestureType == GestureType.RIGHT))
                {
                    continue;
                }
                
                // Generate hash to avoid duplicates
                int hash = GetCueHash(cue);
                if (queuedCueHashes.Contains(hash)) continue;
                
                // Queue this cue
                int sectionIndex = (int)cue.targetSection.Value;
                cueQueues[sectionIndex].Enqueue(cue);
                queuedCueHashes.Add(hash);

                // Debug.Log($"[CueRadarManager] Queued cue: {cue.gestureType} for {cue.targetSection}");
            }

            // Keep only one globally active cue at a time (prevents multi-cue clutter on large lead times).
            if (!HasAnyActiveCue())
            {
                ShowEarliestQueuedCue();
            }
        }

        private bool ShouldSuppressRadarCues()
        {
            if (GameSettings.CurrentMode != GameMode.Tutorial) return false;
            if (RhythmGameController.Instance == null) return false;
            return RhythmGameController.Instance.IsGuidedTutorialActive;
        }

        private void HideAndClearAllRadars()
        {
            for (int i = 0; i < 4; i++)
            {
                cueQueues[i].Clear();
                activeCues[i] = null;

                if (radars[i] != null)
                    radars[i].Hide();
                if (radars3D[i] != null)
                    radars3D[i].Hide();
            }

            queuedCueHashes.Clear();
        }

        private bool HasAnyActiveCue()
        {
            for (int i = 0; i < activeCues.Length; i++)
            {
                if (activeCues[i].HasValue) return true;
            }
            return false;
        }

        private void ShowEarliestQueuedCue()
        {
            int bestSection = -1;
            float bestTimestamp = float.MaxValue;

            for (int i = 0; i < cueQueues.Length; i++)
            {
                if (cueQueues[i].Count == 0) continue;
                RhythmCue candidate = cueQueues[i].Peek();
                if (candidate.timestamp < bestTimestamp)
                {
                    bestTimestamp = candidate.timestamp;
                    bestSection = i;
                }
            }

            if (bestSection >= 0)
            {
                ShowNextCue(bestSection);
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
            
            // Debug.Log($"[CueRadarManager] ShowNextCue: section={sectionIndex}, gesture={nextCue.gestureType}, timeUntilHit={timeUntilHit}");
            
            if (radars[sectionIndex] != null)
            {
                // Debug.Log($"[CueRadarManager] Calling ShowCue on radar {sectionIndex}");
                radars[sectionIndex].ShowCue(nextCue.gestureType, timeUntilHit);
                
                // Update radar position (in case instruments moved)
                UpdateRadarPosition(sectionIndex);
            }
            else if (radars3D[sectionIndex] != null)
            {
                // Debug.Log($"[CueRadarManager] Calling ShowCue on 3D radar {sectionIndex}");
                radars3D[sectionIndex].ShowCue(nextCue.gestureType, timeUntilHit);
                
                // Update radar position
                radars3D[sectionIndex].transform.position = GetRadarPosition((OrchestraSection)sectionIndex);
            }
            else
            {
                // Debug.LogError($"[CueRadarManager] Radar {sectionIndex} is null!");
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

            activeCues[sectionIndex] = null;

            if (radars[sectionIndex] != null)
            {
                radars[sectionIndex].Hide();
            }
            if (radars3D[sectionIndex] != null)
            {
                radars3D[sectionIndex].Hide();
            }

            ShowEarliestQueuedCue();
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
            
            // Debug.Log("[CueRadarManager] Reset for new game - radars destroyed, ready to respawn");
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

        /// <summary>
        /// Returns the section index for the highest priority cue requiring attention.
        /// Prefers currently active cues (visualized on radar). Falls back to the earliest queued cue.
        /// Returns null if no cues have an assigned section.
        /// </summary>
        public OrchestraSection? GetNextTargetSection()
        {
            // First pass: any active cue already shown on a radar is the most urgent.
            float soonestTime = float.MaxValue;
            int? bestSectionIndex = null;

            for (int i = 0; i < activeCues.Length; i++)
            {
                if (!activeCues[i].HasValue) continue;

                float remaining = activeCues[i].Value.timestamp - (float)(rhythmMap?.CurrentSongTime ?? 0f);
                if (remaining < soonestTime)
                {
                    soonestTime = remaining;
                    bestSectionIndex = i;
                }
            }

            if (bestSectionIndex.HasValue)
            {
                return (OrchestraSection)bestSectionIndex.Value;
            }

            // Second pass: peek earliest queued cue (not yet active) by timestamp.
            RhythmCue? bestQueuedCue = null;
            for (int i = 0; i < cueQueues.Length; i++)
            {
                if (cueQueues[i].Count == 0) continue;
                RhythmCue candidate = cueQueues[i].Peek();
                if (!bestQueuedCue.HasValue || candidate.timestamp < bestQueuedCue.Value.timestamp)
                {
                    bestQueuedCue = candidate;
                }
            }

            if (bestQueuedCue.HasValue && bestQueuedCue.Value.targetSection.HasValue)
            {
                return bestQueuedCue.Value.targetSection.Value;
            }

            return null;
        }

        #endregion
    }
}
