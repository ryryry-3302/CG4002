using System;
using UnityEngine;

namespace OrchestraMaestro
{
    /// <summary>
    /// Main game controller for Orchestra Maestro conducting game.
    /// Handles game state, section selection, gesture processing, and scoring.
    /// </summary>
    public class RhythmGameController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RhythmMap rhythmMap;
        [SerializeField] private AudioMixerController audioMixer;
        [SerializeField] private OrchestraPlacement orchestraPlacement;
        [SerializeField] private HUDController hudController;

        [Header("Song Configuration")]
        [SerializeField] private SongData currentSong;
        [SerializeField] private SongData[] availableSongs;
        [SerializeField] private AudioSource audioSource;

        [Header("Game Settings")]
        [SerializeField] private bool autoStartTestMap = true;

        [Header("Combo Validation")]
        [SerializeField] private float comboStrokeWindow = 1.0f; // Time window for combo stick patterns

        [Header("Miss Sound Effects")]
        [Tooltip("Assign your own out-of-tune clips per section. Leave empty to use procedural fallback.")]
        [SerializeField] private AudioClip missSfxDrum;
        [SerializeField] private AudioClip missSfxFlute;
        [SerializeField] private AudioClip missSfxPipe;
        [SerializeField] private AudioClip missSfxXylophone;

        [Header("Combo Sound Effects")]
        [Tooltip("Plays when combo hits 5x, 10x, or 20x. Assign clips in Inspector.")]
        [SerializeField] private AudioClip combo5xSfx;   // mini applause
        [SerializeField] private AudioClip combo10xSfx;  // loud applause
        [SerializeField] private AudioClip combo20xSfx;  // applause and cheer

        [Header("Ambience & Ending")]
        [Tooltip("Plays once when the game/song starts.")]
        [SerializeField] private AudioClip ambienceSfx;
        [Tooltip("Plays once when entering the last 10 seconds of the song.")]
        [SerializeField] private AudioClip endingSfx;

        // Game State
        public enum GameState { Setup, Playing, Paused, Results }
        private GameState currentState = GameState.Setup;

        // Section Selection (0-3, wraps around)
        private int selectedSectionIndex = 0;
        public OrchestraSection SelectedSection => (OrchestraSection)selectedSectionIndex;

        // Scoring
        private int totalScore = 0;
        private int combo = 0;
        private int maxCombo = 0;
        private int perfectCount = 0;
        private int goodCount = 0;
        private int missCount = 0;

        // Events
        public event Action<GameState> OnGameStateChanged;
        public event Action<OrchestraSection> OnSectionChanged;
        public event Action<ScoringResult> OnGestureJudged;
        public event Action<int, int> OnScoreChanged; // score, combo

        // Singleton
        public static RhythmGameController Instance { get; private set; }

        // Public accessors
        public GameState CurrentState => currentState;
        public int TotalScore => totalScore;
        public int Combo => combo;
        public int MaxCombo => maxCombo;
        public int PerfectCount => perfectCount;
        public int GoodCount => goodCount;
        public int MissCount => missCount;

        /// <summary>Tutorial: wrong gesture hint to display. Cleared when correct or after timeout.</summary>
        public string TutorialWrongGestureHint { get; private set; }
        /// <summary>Tutorial: whether playback is paused waiting for user gesture.</summary>
        public bool IsTutorialPaused => rhythmMap != null && rhythmMap.IsPausedForTutorial;

        /// <summary>Current playback time in seconds. 0 if no map.</summary>
        public float CurrentSongTime => rhythmMap != null ? rhythmMap.CurrentSongTime : 0f;
        private float tutorialWrongGestureTime;
        private bool endingSfxPlayed;

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            BindReferences();
        }
        
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            // Re-acquire references after scene reload (this object survives via DontDestroyOnLoad on its GameObject)
            BindReferences();
            ResetToSetup();
            Debug.Log($"[RhythmGameController] Scene '{scene.name}' loaded — re-bound references, reset to Setup");
        }
        
        private void BindReferences()
        {
            // Always re-find to handle scene reload (serialized refs become stale)
            orchestraPlacement = FindObjectOfType<OrchestraPlacement>();
            rhythmMap = FindObjectOfType<RhythmMap>();
            hudController = FindObjectOfType<HUDController>();
            audioSource = GetComponent<AudioSource>();
            
            // Subscribe to MQTT events (MQTTManager also persists, so -= first)
            if (MQTTManager.Instance != null)
            {
                MQTTManager.Instance.OnGestureReceived -= HandleGestureReceived;
                MQTTManager.Instance.OnDownstroke -= HandleDownstroke;
                MQTTManager.Instance.OnGestureReceived += HandleGestureReceived;
                MQTTManager.Instance.OnDownstroke += HandleDownstroke;
            }

            // Subscribe to rhythm map events
            if (rhythmMap != null)
            {
                rhythmMap.OnCueMissed -= HandleCueMissed;
                rhythmMap.OnCueAutoHit -= HandleCueAutoHit;
                rhythmMap.OnSongFinished -= HandleSongFinished;
                rhythmMap.OnTutorialPauseRequested -= HandleTutorialPauseRequested;
                rhythmMap.OnCueMissed += HandleCueMissed;
                rhythmMap.OnCueAutoHit += HandleCueAutoHit;
                rhythmMap.OnSongFinished += HandleSongFinished;
                rhythmMap.OnTutorialPauseRequested += HandleTutorialPauseRequested;
            }

            // Subscribe to orchestra placement lock event
            if (orchestraPlacement != null)
            {
                orchestraPlacement.OnPlacementsLocked -= HandlePlacementsLocked;
                orchestraPlacement.OnPlacementsLocked += HandlePlacementsLocked;
            }
        }

        private void HandlePlacementsLocked()
        {
            Debug.Log("[RhythmGameController] Placements locked - awaiting song selection");
            // Game stays in Setup; song selection UI will call SelectSongAndStart when user picks
        }

        private void Update()
        {
            if (!string.IsNullOrEmpty(TutorialWrongGestureHint) && Time.time - tutorialWrongGestureTime > 3f)
                TutorialWrongGestureHint = null;

            // Ending SFX: play once when entering last 10 seconds
            if (currentState == GameState.Playing && !endingSfxPlayed && endingSfx != null && audioSource != null && audioSource.clip != null)
            {
                float duration = audioSource.clip.length;
                float currentTime = rhythmMap != null ? rhythmMap.CurrentSongTime : 0f;
                if (currentTime >= duration - 10f)
                {
                    endingSfxPlayed = true;
                    if (Camera.main != null)
                        AudioSource.PlayClipAtPoint(endingSfx, Camera.main.transform.position, 0.6f);
                }
            }
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            
            if (MQTTManager.Instance != null)
            {
                MQTTManager.Instance.OnGestureReceived -= HandleGestureReceived;
                MQTTManager.Instance.OnDownstroke -= HandleDownstroke;
            }

            if (orchestraPlacement != null)
            {
                orchestraPlacement.OnPlacementsLocked -= HandlePlacementsLocked;
            }

            if (rhythmMap != null)
            {
                rhythmMap.OnCueMissed -= HandleCueMissed;
                rhythmMap.OnCueAutoHit -= HandleCueAutoHit;
                rhythmMap.OnSongFinished -= HandleSongFinished;
                rhythmMap.OnTutorialPauseRequested -= HandleTutorialPauseRequested;
            }

            if (Instance == this) Instance = null;
        }

        #endregion

        #region Game Flow

        /// <summary>Start a test game with the built-in test map</summary>
        public void StartTestGame()
        {
            if (rhythmMap == null)
            {
                Debug.LogError("[RhythmGameController] RhythmMap reference not set!");
                return;
            }

            rhythmMap.LoadTestMap();
            StartGame();
        }

        /// <summary>Start the game with the currently loaded rhythm map</summary>
        /// <summary>Start the game with the currently loaded rhythm map</summary>
        public void StartGame()
        {
            ResetScore();
            selectedSectionIndex = 0;
            tutorialFirstPauseShown = false;
            
            // Load song data if available
            if (currentSong != null)
            {
                Debug.Log($"[RhythmGameController] Loading song: {currentSong.songName}");
                
                // Load map
                if (currentSong.rhythmMapJson != null)
                {
                    rhythmMap.LoadFromAsset(currentSong.rhythmMapJson);
                }
                else
                {
                    Debug.LogWarning("[RhythmGameController] Song has no rhythm map! Using default test map.");
                    rhythmMap.LoadTestMap();
                }

                // Prepare audio
                if (audioSource != null && currentSong.audioClip != null)
                {
                    audioSource.clip = currentSong.audioClip;
                    audioSource.Play();
                    Debug.Log($"[RhythmGameController] Playing audio: {currentSong.audioClip.name}");
                }
                else
                {
                    Debug.LogWarning("[RhythmGameController] Missing AudioSource or AudioClip!");
                }
            }
            else
            {
                Debug.LogWarning("[RhythmGameController] No SongData assigned! Using default test map.");
                rhythmMap.LoadTestMap();
            }
            
            rhythmMap.StartPlayback();
            SetGameState(GameState.Playing);
            endingSfxPlayed = false;

            if (ambienceSfx != null && Camera.main != null)
                AudioSource.PlayClipAtPoint(ambienceSfx, Camera.main.transform.position, 0.5f);

            Debug.Log("[RhythmGameController] Game started!");
        }

        /// <summary>Pause the game</summary>
        /// <summary>Pause the game</summary>
        public void PauseGame()
        {
            if (currentState != GameState.Playing) return;

            rhythmMap.PausePlayback();
            if (audioSource != null) audioSource.Pause();
            
            SetGameState(GameState.Paused);
        }

        /// <summary>Resume from pause</summary>
        /// <summary>Resume from pause</summary>
        public void ResumeGame()
        {
            if (currentState != GameState.Paused) return;

            rhythmMap.StartPlayback();
            if (audioSource != null) audioSource.UnPause();
            
            SetGameState(GameState.Playing);
        }

        /// <summary>End the game and show results</summary>
        /// <summary>End the game and show results</summary>
        public void EndGame()
        {
            rhythmMap.StopPlayback();
            
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }

            SetGameState(GameState.Results);

            Debug.Log($"[RhythmGameController] Game ended! Score: {totalScore}, Max Combo: {maxCombo}");
        }

        private void SetGameState(GameState newState)
        {
            if (currentState == newState) return;

            currentState = newState;
            OnGameStateChanged?.Invoke(newState);

            Debug.Log($"[RhythmGameController] State changed to: {newState}");
        }

        private void ResetScore()
        {
            totalScore = 0;
            combo = 0;
            maxCombo = 0;
            perfectCount = 0;
            goodCount = 0;
            missCount = 0;

            OnScoreChanged?.Invoke(totalScore, combo);
        }

        /// <summary>Force reset to Setup state. Call when exiting to menu so next game starts fresh.</summary>
        public void ResetToSetup()
        {
            currentState = GameState.Setup;
            ResetScore();
            OnGameStateChanged?.Invoke(GameState.Setup);
            Debug.Log("[RhythmGameController] Reset to Setup");
        }

        #endregion

        #region Section Navigation

        /// <summary>Move section selection left (wraps around)</summary>
        public void SelectPreviousSection()
        {
            selectedSectionIndex = (selectedSectionIndex - 1 + 4) % 4;
            NotifySectionChanged();
        }

        /// <summary>Move section selection right (wraps around)</summary>
        public void SelectNextSection()
        {
            selectedSectionIndex = (selectedSectionIndex + 1) % 4;
            NotifySectionChanged();
        }

        /// <summary>Select a specific section</summary>
        public void SelectSection(OrchestraSection section)
        {
            selectedSectionIndex = (int)section;
            NotifySectionChanged();
        }

        private void NotifySectionChanged()
        {
            OrchestraSection section = SelectedSection;
            OnSectionChanged?.Invoke(section);

            // Update orchestra highlight
            if (orchestraPlacement != null)
            {
                orchestraPlacement.HighlightSection(selectedSectionIndex);
            }

            Debug.Log($"[RhythmGameController] Section changed to: {section}");
        }

        #endregion

        #region Gesture Handling

        private void HandleGestureReceived(LeftGestureEvent evt)
        {
            if (currentState != GameState.Playing && !(rhythmMap != null && rhythmMap.IsPausedForTutorial)) return;

            GestureType gesture = evt.GetGestureType();
            
            Debug.Log($"[RhythmGameController] Processing gesture: {gesture}");

            // Handle section navigation gestures (LEFT/RIGHT) - always allow, including during tutorial pause
            if (GestureUtils.IsSectionNavigation(gesture))
            {
                if (gesture == GestureType.LEFT)
                    SelectPreviousSection();
                else
                    SelectNextSection();
                
                return; // Navigation gestures don't get scored
            }

            // Handle combo gestures - validate stick pattern
            if (GestureUtils.IsComboGesture(gesture))
            {
                if (!ValidateComboGesture(gesture, evt.isClenched))
                {
                    Debug.Log($"[RhythmGameController] Combo gesture {gesture} failed validation");
                    return;
                }
            }

            // Judge timing against rhythm map
            ScoringResult result = rhythmMap.JudgeGesture(gesture, SelectedSection);

            // Tutorial pause: wrong gesture - show hint, don't count as miss
            if (rhythmMap != null && rhythmMap.IsPausedForTutorial && result.judgement == JudgementType.Miss)
            {
                if (tutorialWaitingCue.HasValue)
                {
                    var cue = tutorialWaitingCue.Value;
                    string sectionName = cue.targetSection.HasValue ? cue.targetSection.Value.ToString() : "any";
                    TutorialWrongGestureHint = $"Try {cue.gestureType} for {sectionName}. Use LEFT/RIGHT to change section.";
                    tutorialWrongGestureTime = Time.time;
                }
                return;
            }

            ProcessScoringResult(result, gesture);

            // Tutorial pause: correct gesture - resume playback
            if (rhythmMap != null && rhythmMap.IsPausedForTutorial)
            {
                rhythmMap.ResumeFromTutorial();
                if (audioSource != null) audioSource.UnPause();
                tutorialWaitingCue = null;
                TutorialWrongGestureHint = null;
            }
        }

        private void HandleDownstroke(float timestamp)
        {
            // Downstrokes are buffered by MQTTManager
            // Used for beat visualization and combo validation
            
            if (hudController != null)
            {
                hudController.ShowBeatPulse();
            }
        }

        private void HandleCueMissed(RhythmCue cue)
        {
            if (currentState != GameState.Playing) return;

            missCount++;
            combo = 0;
            OnScoreChanged?.Invoke(totalScore, combo);

            if (hudController != null)
                hudController.ShowJudgement(JudgementType.Miss, 0);

            PlayMissSfx(cue.targetSection ?? OrchestraSection.Drum);

            Debug.Log($"[RhythmGameController] Missed cue: {cue.gestureType} at {cue.timestamp}");
        }

        private static AudioClip[] proceduralMissCache;

        private void PlayMissSfx(OrchestraSection section)
        {
            AudioClip clip = section switch
            {
                OrchestraSection.Drum => missSfxDrum,
                OrchestraSection.Flute => missSfxFlute,
                OrchestraSection.Pipe => missSfxPipe,
                OrchestraSection.Xylophone => missSfxXylophone,
                _ => missSfxDrum
            };

            if (clip == null)
            {
                if (proceduralMissCache == null)
                {
                    proceduralMissCache = new AudioClip[4];
                    float[] baseFreq = { 140f, 350f, 260f, 280f };
                    for (int i = 0; i < 4; i++)
                        proceduralMissCache[i] = CreateMissSfxClip(baseFreq[i]);
                }
                clip = proceduralMissCache[(int)section];
            }

            if (clip != null && Camera.main != null)
                AudioSource.PlayClipAtPoint(clip, Camera.main.transform.position, 0.6f);
        }

        private void PlayComboMilestoneSfx(int combo)
        {
            AudioClip clip = combo switch
            {
                5 => combo5xSfx,
                10 => combo10xSfx,
                20 => combo20xSfx,
                _ => null
            };
            if (clip != null && Camera.main != null)
                AudioSource.PlayClipAtPoint(clip, Camera.main.transform.position, 0.8f);
        }

        private static AudioClip CreateMissSfxClip(float baseFreq)
        {
            int sampleRate = 44100;
            float duration = 0.25f;
            int samples = Mathf.RoundToInt(sampleRate * duration);
            var data = new float[samples * 2];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float freq = baseFreq * (1f + 0.08f * Mathf.Sin(t * 40f));
                float wave = Mathf.Sin(2f * Mathf.PI * freq * t) * (1f - t / duration);
                float noise = (UnityEngine.Random.value - 0.5f) * 0.15f;
                float s = Mathf.Clamp(wave + noise, -1f, 1f);
                data[i * 2] = data[i * 2 + 1] = s;
            }

            var clip = AudioClip.Create("MissSfx", samples, 2, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void HandleCueAutoHit(ScoringResult result)
        {
            if (currentState != GameState.Playing) return;
            // Move selector to the correct section so user sees it follow the cues
            SelectSection(result.targetSection);
            ProcessScoringResult(result, result.gestureType);
            Debug.Log($"[RhythmGameController] Cheats auto-perfect: {result.gestureType} at {result.matchedCue?.timestamp}");
        }
        
        private void HandleSongFinished()
        {
            if (currentState != GameState.Playing) return;
            Debug.Log("[RhythmGameController] Song finished - ending game");
            EndGame();
        }

        private void HandleTutorialPauseRequested(RhythmCue cue)
        {
            if (currentState != GameState.Playing) return;
            rhythmMap.PauseForTutorial(cue.timestamp);
            if (audioSource != null && audioSource.isPlaying)
                audioSource.Pause();
            tutorialWaitingCue = cue;
            TutorialWrongGestureHint = null;

            if (!tutorialFirstPauseShown)
            {
                tutorialFirstPauseShown = true;
                if (TutorialDialogController.Instance == null)
                {
                    var go = new GameObject("TutorialDialogController");
                    go.AddComponent<TutorialDialogController>();
                }
                TutorialDialogController.Instance?.Show("When you see a gesture, perform it! The song will pause so you can practice.");
            }

            Debug.Log($"[RhythmGameController] Tutorial pause at cue: {cue.gestureType} for {cue.targetSection}");
        }

        private RhythmCue? tutorialWaitingCue;
        private bool tutorialFirstPauseShown;
        
        /// <summary>Return to song selection (keeps placements). Called from Play Again.</summary>
        public void ReturnToSongSelection()
        {
            Debug.Log("[RhythmGameController] Returning to song selection...");

            rhythmMap.StopPlayback();
            if (audioSource != null && audioSource.isPlaying)
                audioSource.Stop();

            if (CueRadarManager.Instance != null)
                CueRadarManager.Instance.ResetForNewGame();

            SetGameState(GameState.Setup);
        }

        /// <summary>
        /// Set the song to play (called from menu or inspector).
        /// </summary>
        public void SetSong(SongData song)
        {
            currentSong = song;
        }

        /// <summary>Select a song and start the game. Called from song selection UI.</summary>
        public void SelectSongAndStart(SongData song)
        {
            SetSong(song);
            StartGame();
        }

        /// <summary>Songs available in the selection menu. Null/empty uses test map.</summary>
        public SongData[] AvailableSongs => availableSongs;

        /// <summary>Currently selected/playing song. Null if none.</summary>
        public SongData CurrentSong => currentSong;

        /// <summary>Skip to 5 seconds before the first cue. Only valid when first cue is at or after 20s and we're before it.</summary>
        public void SkipToFirstCueMinus5()
        {
            if (currentState != GameState.Playing || rhythmMap == null) return;
            float firstCue = rhythmMap.FirstCueTimestamp;
            if (firstCue >= 20f && rhythmMap.CurrentSongTime < firstCue)
            {
                float target = Mathf.Max(0, firstCue - 5f);
                rhythmMap.SeekTo(target);
                if (audioSource != null && audioSource.isPlaying)
                    audioSource.time = target;
                Debug.Log($"[RhythmGameController] Skipped to {target:F1}s (5s before first cue at {firstCue:F1}s)");
            }
        }

        /// <summary>True if the song has no cue in the first 20s and we're still in that window (skip button can show).</summary>
        public bool CanSkipToFirstCue =>
            currentState == GameState.Playing
            && rhythmMap != null
            && rhythmMap.FirstCueTimestamp >= 20f
            && rhythmMap.CurrentSongTime < 20f;


        #endregion

        #region Combo Gesture Validation

        /// <summary>
        /// Validate that a combo gesture has the required stick pattern.
        /// </summary>
        private bool ValidateComboGesture(GestureType gesture, bool isClenched)
        {
            if (!isClenched)
            {
                // Combo gestures require fist to be clenched
                return false;
            }

            MQTTManager mqtt = MQTTManager.Instance;
            if (mqtt == null) return false;

            switch (gesture)
            {
                case GestureType.HOLD:
                case GestureType.READY:
                    // No strokes for 1+ beat - check if stick is still
                    return mqtt.IsStickStill(1.0f);

                case GestureType.STRONG_ACCENT:
                    // Down-Up-Down pattern (3 strokes in quick succession)
                    return mqtt.CountDownstrokes(comboStrokeWindow) >= 3;

                case GestureType.CLEAR_CUTOFF:
                    // One down then hold still
                    return mqtt.CountDownstrokes(comboStrokeWindow) == 1 && 
                           mqtt.IsStickStill(0.5f);

                case GestureType.SUBDIVIDE:
                    // Down-Up-Down-Up fast (4+ strokes)
                    return mqtt.CountDownstrokes(comboStrokeWindow) >= 4;

                case GestureType.BRING_OUT:
                    // Up-Down-Up (3 strokes with up leading)
                    return mqtt.CountDownstrokes(comboStrokeWindow) >= 2;

                case GestureType.TRANSITION:
                    // Down-Up-Down-Up then hold
                    return mqtt.CountDownstrokes(comboStrokeWindow * 1.5f) >= 4 && 
                           mqtt.IsStickStill(0.3f);

                default:
                    return true;
            }
        }

        #endregion

        #region Scoring

        private void ProcessScoringResult(ScoringResult result, GestureType gesture)
        {
            // Update counts
            switch (result.judgement)
            {
                case JudgementType.Perfect:
                    perfectCount++;
                    combo++;
                    break;
                case JudgementType.Good:
                    goodCount++;
                    combo++;
                    break;
                case JudgementType.Miss:
                    missCount++;
                    combo = 0;
                    PlayMissSfx(result.targetSection);
                    break;
            }

            // Update max combo
            if (combo > maxCombo) maxCombo = combo;

            // Combo milestone SFX (5x, 10x, 20x)
            if (result.judgement != JudgementType.Miss)
                PlayComboMilestoneSfx(combo);

            // Calculate score with combo multiplier
            int comboMultiplier = Mathf.Min(1 + combo / 10, 4); // Max 4x multiplier
            int scoreGained = result.scoreAwarded * comboMultiplier;
            totalScore += scoreGained;

            // Notify listeners
            OnGestureJudged?.Invoke(result);
            OnScoreChanged?.Invoke(totalScore, combo);

            // Apply audio effects
            ApplyGestureAudioEffect(gesture, result);

            // Apply visual effects
            ApplyGestureVisualEffect(gesture, result);

            // Update HUD
            if (hudController != null)
            {
                hudController.ShowJudgement(result.judgement, result.timingOffset);
                hudController.UpdateScore(totalScore, combo);
            }

            Debug.Log($"[RhythmGameController] {gesture}: {result.judgement} (offset: {result.timingOffset:F3}s, score: +{scoreGained})");
        }

        #endregion

        #region Audio Effects

        private void ApplyGestureAudioEffect(GestureType gesture, ScoringResult result)
        {
            if (audioMixer == null) return;

            OrchestraSection section = result.targetSection;

            switch (gesture)
            {
                case GestureType.UP:
                case GestureType.V_SHAPE:
                    // Increase volume
                    audioMixer.IncreaseSectionVolume(section);
                    break;

                case GestureType.DOWN:
                case GestureType.LAMBDA_SHAPE:
                    // Decrease volume
                    audioMixer.DecreaseSectionVolume(section);
                    break;

                case GestureType.PUNCH:
                case GestureType.STRONG_ACCENT:
                    // Accent/hit
                    audioMixer.TriggerAccent(section);
                    break;

                case GestureType.WITHDRAW:
                case GestureType.CLEAR_CUTOFF:
                    // Cutoff
                    audioMixer.TriggerCutoff(section);
                    break;

                // TODO: Implement tempo and timbre effects
                default:
                    break;
            }
        }

        #endregion

        #region Visual Effects

        private void ApplyGestureVisualEffect(GestureType gesture, ScoringResult result)
        {
            if (orchestraPlacement == null) return;

            // Trigger VFX on the target section
            orchestraPlacement.TriggerHitFeedback(
                (int)result.targetSection, 
                result.judgement
            );
        }

        #endregion
    }
}
