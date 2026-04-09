using System;
using System.Collections.Generic;
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
        [Tooltip("Optional background music for the tutorial mode.")]
        [SerializeField] private AudioClip tutorialBgm;
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

        // BPM Preview State
        private bool bpmPreviewActive = false;
        private const float BPM_PREVIEW_LEAD_TIME = 5.0f; // Start preview 5 seconds before tempo section

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
        public bool IsGuidedTutorialActive => tutorialNavigationTrainingActive || tutorialGestureTrainingActive;
        public bool IsTutorialMode => GameSettings.CurrentMode == GameMode.Tutorial;
        public bool TutorialGestureTrainingActive => tutorialGestureTrainingActive;

        public bool TryGetGuidedTutorialCue(out GestureType gesture, out OrchestraSection targetSection)
        {
            if (tutorialGestureTrainingActive && tutorialWaitingForTryItOutInput && tutorialExpectedGesture != GestureType.ERROR)
            {
                gesture = tutorialExpectedGesture;
                targetSection = tutorialExpectedSection;
                return true;
            }

            gesture = GestureType.ERROR;
            targetSection = OrchestraSection.Drum;
            return false;
        }

        /// <summary>Current playback time in seconds. 0 if no map.</summary>
        public float CurrentSongTime => rhythmMap != null ? rhythmMap.CurrentSongTime : 0f;
        
        public float TempoSectionStart => rhythmMap != null ? rhythmMap.TempoSectionStart : -1f;
        public float TempoSectionEnd => rhythmMap != null ? rhythmMap.TempoSectionEnd : -1f;
        private float tutorialWrongGestureTime;
        private bool endingSfxPlayed;
        private bool tutorialNavigationTrainingActive;
        private bool tutorialLeftNavigationCompleted;
        private bool tutorialRightNavigationCompleted;
        private GestureType tutorialExpectedNavigationGesture = GestureType.ERROR;
        private bool tutorialGestureTrainingActive;
        private bool tutorialWaitingForTryItOutInput;
        private int tutorialGestureTrainingIndex;
        private GestureType tutorialExpectedGesture = GestureType.ERROR;
        private OrchestraSection tutorialExpectedSection = OrchestraSection.Drum;
        private static readonly GestureType[] TutorialGestureTrainingOrder =
        {
            GestureType.UP,
            GestureType.DOWN,
            GestureType.PUNCH,
            GestureType.WITHDRAW,
            GestureType.W_SHAPE,
            GestureType.HOURGLASS_SHAPE,
            GestureType.LIGHTNING_BOLT_SHAPE,
            GestureType.TRIPLE_CLOCKWISE_CIRCLE
        };

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
                MQTTManager.Instance.MqttDisconnected -= HandleMqttDisconnected;
                MQTTManager.Instance.MqttConnected -= HandleMqttConnected;

                MQTTManager.Instance.OnGestureReceived += HandleGestureReceived;
                MQTTManager.Instance.OnDownstroke += HandleDownstroke;
                MQTTManager.Instance.MqttDisconnected += HandleMqttDisconnected;
                MQTTManager.Instance.MqttConnected += HandleMqttConnected;
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

            // BPM Preview: Vibrate at BPM for 5 seconds before tempo section starts
            if (currentState == GameState.Playing && TempoSectionStart >= 0)
            {
                float currentTime = CurrentSongTime;
                float previewStartTime = TempoSectionStart - BPM_PREVIEW_LEAD_TIME;
                
                // Check if we should start BPM preview
                if (!bpmPreviewActive && currentTime >= previewStartTime && currentTime < TempoSectionStart)
                {
                    StartBpmPreview();
                }
                // Check if we should stop BPM preview (reached tempo section)
                else if (bpmPreviewActive && currentTime >= TempoSectionStart)
                {
                    StopBpmPreview();
                }
            }

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
                MQTTManager.Instance.MqttDisconnected -= HandleMqttDisconnected;
                MQTTManager.Instance.MqttConnected -= HandleMqttConnected;
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
            tutorialNavigationTrainingActive = false;
            tutorialLeftNavigationCompleted = false;
            tutorialRightNavigationCompleted = false;
            tutorialExpectedNavigationGesture = GestureType.ERROR;
            tutorialGestureTrainingActive = false;
            tutorialWaitingForTryItOutInput = false;
            tutorialGestureTrainingIndex = 0;
            tutorialExpectedGesture = GestureType.ERROR;
            tutorialExpectedSection = OrchestraSection.Drum;
            
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

            if (GameSettings.CurrentMode == GameMode.Tutorial)
            {
                // In tutorial mode, play special BGM and skip audience ambience
                if (tutorialBgm != null && audioSource != null)
                {
                    audioSource.clip = tutorialBgm;
                    audioSource.loop = true;
                    audioSource.Play();
                }
                BeginTutorialNavigationTraining();
            }
            else
            {
                if (ambienceSfx != null && Camera.main != null)
                    AudioSource.PlayClipAtPoint(ambienceSfx, Camera.main.transform.position, 0.5f);
            }

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
        public void SelectPreviousSection() => NavigateSection(-1);

        /// <summary>Move section selection right (wraps around)</summary>
        public void SelectNextSection() => NavigateSection(1);

        /// <summary>Select a specific section</summary>
        public void SelectSection(OrchestraSection section)
        {
            selectedSectionIndex = (int)section;
            NotifySectionChanged();
        }

        private void NavigateSection(int rawDirection)
        {
            int direction = rawDirection < 0 ? -1 : 1;
            if (TryAutoLock(direction)) return;

            selectedSectionIndex = (selectedSectionIndex + direction + 4) % 4;
            NotifySectionChanged();
        }

        private bool TryAutoLock(int direction)
        {
            var radar = CueRadarManager.Instance;
            if (radar == null) return false;

            OrchestraSection? nextTarget = radar.GetNextTargetSection();
            if (!nextTarget.HasValue) return false;

            int targetIndex = (int)nextTarget.Value;
            if (targetIndex == selectedSectionIndex) return false;

            int forwardSteps = Modulo(targetIndex - selectedSectionIndex, 4);
            int backwardSteps = Modulo(selectedSectionIndex - targetIndex, 4);

            bool movingRight = direction > 0;
            bool rightIsCloser = forwardSteps > 0 && forwardSteps <= backwardSteps;
            bool leftIsCloser = backwardSteps > 0 && backwardSteps <= forwardSteps;

            if ((movingRight && rightIsCloser) || (!movingRight && leftIsCloser))
            {
                SelectSection(nextTarget.Value);
                return true;
            }

            return false;
        }

        private static int Modulo(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
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

            GestureType[] candidateGestures = LimitCandidatesByDifficulty(evt.GetCandidateGestureTypes());
            if (candidateGestures == null || candidateGestures.Length == 0) return;

            GestureType topGesture = candidateGestures[0];

            if (topGesture == GestureType.ERROR)
            {
                Debug.Log("[RhythmGameController] Ignoring ERROR/IDLE gesture event");
                return;
            }
            
            Debug.Log($"[RhythmGameController] Processing gesture candidates: {string.Join(", ", candidateGestures)}");

            if (GameSettings.CurrentMode == GameMode.Tutorial && tutorialNavigationTrainingActive)
            {
                HandleTutorialNavigationGesture(topGesture);
                return;
            }

            if (GameSettings.CurrentMode == GameMode.Tutorial && tutorialGestureTrainingActive)
            {
                HandleTutorialGuidedGesture(topGesture);
                return;
            }

            // Edge case: if top prediction is LEFT/RIGHT and selector is not at the expected section,
            // treat it as section navigation instead of a top-3 scoring fallback.
            if (ShouldTreatAsSectionNavigation(topGesture))
            {
                if (topGesture == GestureType.LEFT)
                    SelectPreviousSection();
                else
                    SelectNextSection();
                
                return; // Navigation gestures don't get scored
            }

            // Judge timing against rhythm map using top-3 candidates
            ScoringResult result = rhythmMap.JudgeGestureCandidates(candidateGestures, SelectedSection);
            GestureType matchedGesture = result.judgement == JudgementType.Miss ? topGesture : result.gestureType;

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

            ProcessScoringResult(result, matchedGesture);

            // Tutorial pause: correct gesture - resume playback
            if (rhythmMap != null && rhythmMap.IsPausedForTutorial)
            {
                rhythmMap.ResumeFromTutorial();
                if (audioSource != null) audioSource.UnPause();
                tutorialWaitingCue = null;
                TutorialWrongGestureHint = null;
                TutorialDialogController.Instance?.Clear();
            }
        }

        private static GestureType[] LimitCandidatesByDifficulty(GestureType[] candidates)
        {
            if (candidates == null || candidates.Length == 0)
            {
                return candidates;
            }

            int maxCandidates = GameSettings.DifficultyLevel switch
            {
                Difficulty.Easy => 3,
                Difficulty.Medium => 2,
                Difficulty.Hard => 1,
                _ => 2
            };

            if (candidates.Length <= maxCandidates)
            {
                return candidates;
            }

            GestureType[] limited = new GestureType[maxCandidates];
            Array.Copy(candidates, limited, maxCandidates);
            return limited;
        }

        private bool ShouldTreatAsSectionNavigation(GestureType topGesture)
        {
            if (!GestureUtils.IsSectionNavigation(topGesture)) return false;

            var radar = CueRadarManager.Instance;
            OrchestraSection? nextTarget = radar?.GetNextTargetSection();

            if (!nextTarget.HasValue)
            {
                return true;
            }

            return nextTarget.Value != SelectedSection;
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
            if (GameSettings.CurrentMode == GameMode.Tutorial) return; // No crowd cheers in tutorial

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

        private string GetVideoNameForGesture(GestureType gesture)
        {
            switch (gesture)
            {
                case GestureType.LEFT: return "left";
                case GestureType.RIGHT: return "right";
                case GestureType.UP: return "up";
                case GestureType.DOWN: return "down";
                case GestureType.PUNCH: return "punch";
                case GestureType.WITHDRAW: return "withdraw";
                case GestureType.W_SHAPE: return "w";
                case GestureType.HOURGLASS_SHAPE: return "hourglass";
                case GestureType.LIGHTNING_BOLT_SHAPE: return "lightning-bolt";
                case GestureType.TRIPLE_CLOCKWISE_CIRCLE: return "triple-circle";
                default: return null;
            }
        }

        /// <summary>
        /// Manual mapping for how gesture names appear in Tutorial Dialogs.
        /// Edit these strings to change the display name.
        /// </summary>
        private string GetTutorialDisplayNameForGesture(GestureType gesture)
        {
            return gesture switch
            {
                GestureType.UP => "Up",
                GestureType.DOWN => "Down",
                GestureType.LEFT => "Left",
                GestureType.RIGHT => "Right",
                GestureType.PUNCH => "Punch",
                GestureType.WITHDRAW => "Withdraw",
                GestureType.W_SHAPE => "W Shape",
                GestureType.HOURGLASS_SHAPE => "Hourglass",
                GestureType.LIGHTNING_BOLT_SHAPE => "Lightning",
                GestureType.TRIPLE_CLOCKWISE_CIRCLE => "Triple Circle",
                _ => gesture.ToString().Replace("_SHAPE", "").Replace("_", " ")
            };
        }

        private string GetSignificanceForGesture(GestureType gesture)
        {
            switch (gesture)
            {
                case GestureType.UP: return "Use in an orchestra: Crescendo (gradually getting louder).";
                case GestureType.DOWN: return "Use in an orchestra: Decrescendo (gradually getting softer).";
                case GestureType.PUNCH: return "Use in an orchestra: Accent (emphasize a single note).";
                case GestureType.WITHDRAW: return "Use in an orchestra: Cutoff (immediately cease all sound).";
                case GestureType.W_SHAPE: return "Use in an orchestra: Sforzando (sudden, strong emphasis on a single note).";
                case GestureType.HOURGLASS_SHAPE: return "Use in an orchestra: Fortissimo (extremely loud & powerful passage).";
                case GestureType.LIGHTNING_BOLT_SHAPE: return "Use in an orchestra: Pianissimo (extremely soft & delicate passage).";
                case GestureType.TRIPLE_CLOCKWISE_CIRCLE: return "Use in an orchestra: Grand Finale (dramatic conclusion of the song).";
                case GestureType.LEFT: return "Known as a section cue, this gesture is used to switch the conducting focus to the left";
                case GestureType.RIGHT: return "Known as a section cue, this gesture is used to switch the conducting focus to the right";
                default: return "";
            }
        }

        private void BeginTutorialNavigationTraining()
        {
            if (rhythmMap == null) return;

            tutorialNavigationTrainingActive = true;
            tutorialLeftNavigationCompleted = false;
            tutorialRightNavigationCompleted = false;
            tutorialExpectedNavigationGesture = GestureType.LEFT;
            tutorialExpectedGesture = GestureType.ERROR;
            tutorialGestureTrainingActive = false;
            tutorialWaitingForTryItOutInput = false;
            TutorialWrongGestureHint = null;

            rhythmMap.PauseForTutorial(rhythmMap.CurrentSongTime);
            if (audioSource != null && audioSource.isPlaying)
                audioSource.Pause();

            if (TutorialDialogController.Instance == null)
            {
                var go = new GameObject("TutorialDialogController");
                go.AddComponent<TutorialDialogController>();
            }

            string msg = "During gameplay navigation gestures are used to select the right section.\n\n You will first learn how to navigate left";
            TutorialDialogController.Instance?.Show(
                msg,
                () => {
                    tutorialExpectedNavigationGesture = GestureType.LEFT;
                    tutorialWaitingForTryItOutInput = true;
                },
                LoadTutorialClipForGesture(GestureType.LEFT),
                "Try it out");
        }

        private void HandleTutorialNavigationGesture(GestureType performedGesture)
        {
            if (!tutorialWaitingForTryItOutInput) return;

            if (tutorialExpectedNavigationGesture == GestureType.LEFT)
            {
                if (performedGesture == GestureType.LEFT)
                {
                    tutorialLeftNavigationCompleted = true;
                    SelectPreviousSection();
                    TutorialWrongGestureHint = null;
                    tutorialWaitingForTryItOutInput = false;

                    string msg = "Similarly, you can navigate right. Try it out!";
                    TutorialDialogController.Instance?.Show(
                        msg,
                        () => {
                            tutorialExpectedNavigationGesture = GestureType.RIGHT;
                            tutorialWaitingForTryItOutInput = true;
                        },
                        LoadTutorialClipForGesture(GestureType.RIGHT),
                        "Try it out");
                    return;
                }

                TutorialWrongGestureHint = "Please try performing the left gesture again.";
                tutorialWrongGestureTime = Time.time;
                return;
            }

            if (tutorialExpectedNavigationGesture == GestureType.RIGHT)
            {
                if (performedGesture == GestureType.RIGHT)
                {
                    tutorialRightNavigationCompleted = true;
                    SelectNextSection();
                    TutorialWrongGestureHint = null;
                    tutorialWaitingForTryItOutInput = false;
                    CompleteTutorialNavigationTraining();
                    return;
                }

                TutorialWrongGestureHint = "Please try performing the right gesture again.";
                tutorialWrongGestureTime = Time.time;
            }
        }

        private void CompleteTutorialNavigationTraining()
        {
            tutorialNavigationTrainingActive = false;
            tutorialExpectedNavigationGesture = GestureType.ERROR;
            tutorialWaitingForTryItOutInput = false;

            TutorialDialogController.Instance?.Clear();

            BeginTutorialGestureTraining();

            Debug.Log("[RhythmGameController] Tutorial navigation training completed (LEFT/RIGHT)");
        }

        private void BeginTutorialGestureTraining()
        {
            tutorialGestureTrainingActive = true;
            tutorialGestureTrainingIndex = 0;
            tutorialExpectedGesture = GestureType.ERROR;
            tutorialWaitingForTryItOutInput = false;

            ShowCurrentTutorialGestureStep();
        }

        private void ShowCurrentTutorialGestureStep()
        {
            if (tutorialGestureTrainingIndex < 0 || tutorialGestureTrainingIndex >= TutorialGestureTrainingOrder.Length)
            {
                CompleteTutorialGestureTraining();
                return;
            }

            GestureType gesture = TutorialGestureTrainingOrder[tutorialGestureTrainingIndex];
            OrchestraSection targetSection = (OrchestraSection)(tutorialGestureTrainingIndex % 4);
            
            string gestureDisplayName = GetTutorialDisplayNameForGesture(gesture);
            string message =
                $"Next gesture: {gestureDisplayName} \n Please Move the selector to {targetSection}, then perform {gestureDisplayName}.";

            tutorialExpectedGesture = gesture;
            tutorialExpectedSection = targetSection;
            tutorialWaitingForTryItOutInput = false;

            TutorialDialogController.Instance?.Clear();
            TutorialDialogController.Instance?.Show(
                message,
                () => tutorialWaitingForTryItOutInput = true,
                LoadTutorialClipForGesture(gesture),
                "Try it out");
        }

        private void HandleTutorialGuidedGesture(GestureType performedGesture)
        {
            if (!tutorialWaitingForTryItOutInput) return;

            if (performedGesture == GestureType.LEFT)
            {
                SelectPreviousSection();
                return;
            }

            if (performedGesture == GestureType.RIGHT)
            {
                SelectNextSection();
                return;
            }

            if (performedGesture == tutorialExpectedGesture)
            {
                if (SelectedSection != tutorialExpectedSection)
                {
                    TutorialWrongGestureHint = $"Move selector to {tutorialExpectedSection} using LEFT/RIGHT, then perform {tutorialExpectedGesture}.";
                    tutorialWrongGestureTime = Time.time;
                    return;
                }

                TutorialWrongGestureHint = null;
                tutorialWaitingForTryItOutInput = false;

                string significance = GetSignificanceForGesture(tutorialExpectedGesture);
                string gestureDisplayName = GetTutorialDisplayNameForGesture(tutorialExpectedGesture);
                string scoreMsg = $"Success! You performed {gestureDisplayName} correctly.\n\n" + significance;

                TutorialDialogController.Instance?.Show(
                    scoreMsg,
                    () => {
                        tutorialGestureTrainingIndex++;
                        ShowCurrentTutorialGestureStep();
                    },
                    null,
                    "Next Gesture");
                return;
            }

            string wrongGestureName = GetTutorialDisplayNameForGesture(tutorialExpectedGesture);
            TutorialWrongGestureHint = $"Wrong gesture. Try {wrongGestureName} on {tutorialExpectedSection}.";
            tutorialWrongGestureTime = Time.time;
        }

        private void CompleteTutorialGestureTraining()
        {
            tutorialGestureTrainingActive = false;
            tutorialWaitingForTryItOutInput = false;
            tutorialExpectedGesture = GestureType.ERROR;
            tutorialExpectedSection = OrchestraSection.Drum;

            string finalMsg = "Great job! You've learned all the gestures and how they influence the orchestra.\n\nNow continue playing the song, use LEFT/RIGHT to navigate, and match the incoming cues!";

            TutorialDialogController.Instance?.Show(
                finalMsg,
                () => {
                    if (rhythmMap != null)
                        rhythmMap.ResumeFromTutorial();
                    if (audioSource != null)
                        audioSource.UnPause();
                },
                null,
                "Start Song");

            Debug.Log("[RhythmGameController] Guided tutorial gesture training completed");
        }

        private UnityEngine.Video.VideoClip LoadTutorialClipForGesture(GestureType gesture)
        {
            string videoName = GetVideoNameForGesture(gesture);
            if (string.IsNullOrEmpty(videoName)) return null;

            var clip = UnityEngine.Resources.Load<UnityEngine.Video.VideoClip>("GIFS_WEBM/" + videoName);
            if (clip == null)
            {
                clip = UnityEngine.Resources.Load<UnityEngine.Video.VideoClip>("GIFS/" + videoName);
            }
            if (clip == null)
            {
                Debug.LogWarning($"[RhythmGameController] Tutorial video not found: Resources/GIFS_WEBM/{videoName}.webm (or GIFS/{videoName})");
            }
            return clip;
        }

        private void ShowTutorialNavigationPrompt(GestureType gesture, Action onTryItOut)
        {
            string message = gesture == GestureType.LEFT
                ? "Navigation gesture: LEFT moves the selector to the previous character. Watch and tap TRY IT OUT."
                : "Navigation gesture: RIGHT moves the selector to the next character. Watch and tap TRY IT OUT.";

            TutorialDialogController.Instance?.Clear();
            TutorialDialogController.Instance?.Show(
                message,
                onTryItOut,
                LoadTutorialClipForGesture(gesture),
                "Try it out");
        }

        private void HandleTutorialPauseRequested(RhythmCue cue)
        {
            if (currentState != GameState.Playing) return;

            if (GameSettings.CurrentMode == GameMode.Tutorial)
            {
                return;
            }

            rhythmMap.PauseForTutorial(cue.timestamp);
            if (audioSource != null && audioSource.isPlaying)
                audioSource.Pause();
            tutorialWaitingCue = cue;
            TutorialWrongGestureHint = null;

            if (TutorialDialogController.Instance == null)
            {
                var go = new GameObject("TutorialDialogController");
                go.AddComponent<TutorialDialogController>();
            }

            string videoName = GetVideoNameForGesture(cue.gestureType);
            UnityEngine.Video.VideoClip clip = LoadTutorialClipForGesture(cue.gestureType);

            string msg = tutorialFirstPauseShown 
                ? $"Now perform the {cue.gestureType} gesture." 
                : $"When you see a gesture, perform it! The song will pause so you can practice.\n\nFirst up: {cue.gestureType}";
            
            tutorialFirstPauseShown = true;
            TutorialDialogController.Instance?.Show(msg, null, clip);

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
        #endregion

        #region BPM Preview

        private void StartBpmPreview()
        {
            if (bpmPreviewActive) return;
            
            bpmPreviewActive = true;
            
            // Get BPM from current song
            float bpm = currentSong != null ? currentSong.bpm : 120f;
            
            // Send command to ESP32 to start BPM preview with the song's BPM
            string command = $"BPM_PREVIEW_START:{bpm:F0}";
            MQTTManager.Instance?.PublishRightCommand(command);
            
            Debug.Log($"[RhythmGameController] Started BPM preview at {bpm} BPM");
        }

        private void StopBpmPreview()
        {
            if (!bpmPreviewActive) return;
            
            bpmPreviewActive = false;
            
            // Send command to ESP32 to stop BPM preview
            MQTTManager.Instance?.PublishRightCommand("BPM_PREVIEW_STOP");
            
            Debug.Log("[RhythmGameController] Stopped BPM preview");
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

            // Send haptic feedback to right stick (suppress during tempo section to avoid interfering with BPM detection)
            bool inTempoSection = false;
            if (TempoSectionStart >= 0 && TempoSectionEnd >= 0)
            {
                float currentTime = CurrentSongTime;
                inTempoSection = currentTime >= TempoSectionStart && currentTime <= TempoSectionEnd;
            }

            if (!inTempoSection)
            {
                MQTTManager.Instance?.PublishRightCommand(result.judgement.ToString());
            }

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
                case GestureType.W_SHAPE:
                    // Increase volume
                    audioMixer.IncreaseSectionVolume(section);
                    break;

                case GestureType.DOWN:
                    // Decrease volume
                    audioMixer.DecreaseSectionVolume(section);
                    break;

                case GestureType.PUNCH:
                    // Accent/hit
                    audioMixer.TriggerAccent(section);
                    break;

                case GestureType.WITHDRAW:
                    // Cutoff
                    audioMixer.TriggerCutoff(section);
                    break;

                // W_SHAPE, HOURGLASS_SHAPE, LIGHTNING_BOLT_SHAPE, TRIPLE_CLOCKWISE_CIRCLE
                // TODO: Implement additional effects for new gestures
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

        #region Reconnection Flow

        private bool pausedForDisconnect = false;

        private void HandleMqttDisconnected()
        {
            if (currentState == GameState.Playing)
            {
                PauseGame();
                pausedForDisconnect = true;
            }
        }

        private void HandleMqttConnected()
        {
            if (currentState == GameState.Paused && pausedForDisconnect)
            {
                ResumeGame();
                pausedForDisconnect = false;
            }
        }

        private void OnGUI()
        {
            if (currentState == GameState.Paused && pausedForDisconnect)
            {
                float width = 600;
                float height = 150;
                float x = (Screen.width - width) / 2f;
                float y = (Screen.height - height) / 2f;

                // Make a dark background box
                GUI.color = new Color(1, 1, 1, 0.9f);
                GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
                GUI.Box(new Rect(x - 20, y - 60, width + 40, height + 100), "");
                
                GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.fontSize = 40;
                labelStyle.alignment = TextAnchor.MiddleCenter;
                labelStyle.normal.textColor = Color.red;

                GUI.Label(new Rect(x, y - 40, width, 60), "CONNECTION LOST", labelStyle);

                // Show re-connecting status if it's already trying
                if (MQTTManager.Instance != null && !MQTTManager.Instance.IsConnected)
                {
                    GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
                    btnStyle.fontSize = 36;
                    
                    if (GUI.Button(new Rect(x, y + 40, width, height), "RECONNECT MANUALLY", btnStyle))
                    {
                        MQTTManager.Instance.Connect();
                    }
                }
                else
                {
                    GUI.Label(new Rect(x, y + 40, width, height), "Reconnected! Resuming...", labelStyle);
                }
            }
        }

        #endregion
    }
}
