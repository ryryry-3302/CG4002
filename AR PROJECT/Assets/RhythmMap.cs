using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrchestraMaestro
{
    /// <summary>
    /// Manages rhythm map loading, cue scheduling, and timing judgement.
    /// Uses phone audio clock (AudioSettings.dspTime) as timing authority.
    /// Timing window: ±1 second with Perfect/Good/Miss tiers.
    /// </summary>
    public class RhythmMap : MonoBehaviour
    {
        [Header("Timing Windows (seconds)")]
        [SerializeField] private float perfectWindow = 0.3f;
        [SerializeField] private float goodWindow = 0.7f;
        [SerializeField] private float missWindow = 1.0f;

        [Header("Cue Display")]
        [SerializeField] private float cueLeadTime = 0.3f; // Show cue 300ms before target

        // Runtime state
        private List<RhythmCue> cues = new List<RhythmCue>();
        private int nextCueIndex = 0;
        private double songStartDspTime;
        private bool isPlaying = false;

        // Events
        public event Action<RhythmCue> OnCueApproaching;
        public event Action<RhythmCue> OnCueMissed;
        public event Action OnSongFinished;
        
        private bool songFinished = false;

        /// <summary>Whether the rhythm map is currently playing</summary>
        public bool IsPlaying => isPlaying;

        /// <summary>Current song time in seconds</summary>
        public float CurrentSongTime => isPlaying ? (float)(AudioSettings.dspTime - songStartDspTime) : 0f;

        /// <summary>Total number of cues in the map</summary>
        public int TotalCues => cues.Count;

        /// <summary>Number of remaining cues</summary>
        public int RemainingCues => cues.Count - nextCueIndex;

        #region Map Loading

        /// <summary>
        /// Load rhythm map from JSON string.
        /// Expected format: { "cues": [ { "timestamp": 1.5, "gestureId": "PUNCH", "section": "Strings" }, ... ] }
        /// </summary>
        public void LoadFromJson(string json)
        {
            cues.Clear();
            nextCueIndex = 0;

            try
            {
                RhythmMapData data = JsonUtility.FromJson<RhythmMapData>(json);
                
                foreach (var cueData in data.cues)
                {
                    GestureType gestureType = ParseGestureType(cueData.gestureId);
                    OrchestraSection? section = string.IsNullOrEmpty(cueData.section) 
                        ? null 
                        : ParseSection(cueData.section);

                    cues.Add(new RhythmCue(cueData.timestamp, gestureType, section));
                }

                // Sort by timestamp
                cues.Sort((a, b) => a.timestamp.CompareTo(b.timestamp));
                
                Debug.Log($"[RhythmMap] Loaded {cues.Count} cues");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RhythmMap] Failed to parse JSON: {e.Message}");
            }
        }

        /// <summary>
        /// Load rhythm map from a TextAsset containing JSON.
        /// </summary>
        public void LoadFromAsset(TextAsset jsonAsset)
        {
            if (jsonAsset == null)
            {
                Debug.LogError("[RhythmMap] Cannot load from null TextAsset");
                return;
            }
            LoadFromJson(jsonAsset.text);
        }


        /// <summary>
        /// Load a test/demo rhythm map for development.
        /// One cue at a time, cycling through sections. Only UP, DOWN, PUNCH gestures.
        /// </summary>
        public void LoadTestMap()
        {
            cues.Clear();
            nextCueIndex = 0;

            // Only the 3 mapped gestures
            GestureType[] gesturePool = { 
                GestureType.UP, GestureType.DOWN, GestureType.PUNCH
            };
            
            // Cycle through sections one at a time
            OrchestraSection[] sections = {
                OrchestraSection.Drum,
                OrchestraSection.Flute,
                OrchestraSection.Pipe,
                OrchestraSection.Xylophone
            };

            int totalCues = 16;          // 16 cues total (4 full cycles through sections)
            float startTime = 3f;        // First cue at 3s
            float cueInterval = 3f;      // 3s between each cue

            for (int i = 0; i < totalCues; i++)
            {
                float timestamp = startTime + i * cueInterval;
                OrchestraSection section = sections[i % sections.Length];
                GestureType gesture = gesturePool[i % gesturePool.Length];
                
                cues.Add(new RhythmCue(timestamp, gesture, section));
                
                Debug.Log($"[RhythmMap] TestMap: cue {i}, t={timestamp}, {section} => {gesture}");
            }

            Debug.Log($"[RhythmMap] Loaded test map with {cues.Count} cues (one at a time, cycling sections)");
        }

        #endregion

        #region Playback Control

        /// <summary>Start the rhythm map playback, synced to audio clock</summary>
        public void StartPlayback()
        {
            songStartDspTime = AudioSettings.dspTime;
            nextCueIndex = 0;
            isPlaying = true;
            songFinished = false;
            
            // Reset consumed flags
            for (int i = 0; i < cues.Count; i++)
            {
                var cue = cues[i];
                cue.consumed = false;
                cues[i] = cue;
            }

            Debug.Log($"[RhythmMap] Playback started at DSP time {songStartDspTime}");
        }

        /// <summary>Stop playback</summary>
        public void StopPlayback()
        {
            isPlaying = false;
            Debug.Log("[RhythmMap] Playback stopped");
        }

        /// <summary>Pause playback (preserves position)</summary>
        public void PausePlayback()
        {
            isPlaying = false;
        }

        #endregion

        #region Cue Scheduling

        private void Update()
        {
            if (!isPlaying) return;

            float currentTime = CurrentSongTime;

            // Check for upcoming cues to display
            for (int i = nextCueIndex; i < cues.Count; i++)
            {
                var cue = cues[i];
                float timeUntilCue = cue.timestamp - currentTime;

                // Cue is approaching - notify for display
                if (timeUntilCue <= cueLeadTime && timeUntilCue > 0 && !cue.consumed)
                {
                    OnCueApproaching?.Invoke(cue);
                }

                // Cue has passed miss window without being hit
                if (timeUntilCue < -missWindow && !cue.consumed)
                {
                    cue.consumed = true;
                    cues[i] = cue;
                    OnCueMissed?.Invoke(cue);
                    
                    // Advance next cue index
                    if (i == nextCueIndex) nextCueIndex++;
                }
            }
            
            // Check if song is finished (all cues consumed + grace period)
            if (!songFinished && nextCueIndex >= cues.Count)
            {
                // Check that all cues are consumed
                bool allConsumed = true;
                for (int i = 0; i < cues.Count; i++)
                {
                    if (!cues[i].consumed) { allConsumed = false; break; }
                }
                
                if (allConsumed)
                {
                    songFinished = true;
                    Debug.Log("[RhythmMap] All cues consumed - song finished!");
                    OnSongFinished?.Invoke();
                }
            }
        }

        /// <summary>
        /// Get cues within the display lead time window.
        /// </summary>
        public List<RhythmCue> GetUpcomingCues(float lookaheadSeconds = -1)
        {
            if (lookaheadSeconds < 0) lookaheadSeconds = cueLeadTime;

            List<RhythmCue> upcoming = new List<RhythmCue>();
            float currentTime = CurrentSongTime;

            for (int i = nextCueIndex; i < cues.Count; i++)
            {
                var cue = cues[i];
                float timeUntilCue = cue.timestamp - currentTime;

                if (timeUntilCue <= lookaheadSeconds && !cue.consumed)
                {
                    upcoming.Add(cue);
                }
                else if (timeUntilCue > lookaheadSeconds)
                {
                    break; // Cues are sorted, no need to check further
                }
            }

            return upcoming;
        }

        #endregion

        #region Timing Judgement

        /// <summary>
        /// Judge a gesture against the rhythm map.
        /// Returns the best matching cue and judgement, or Miss if no valid match.
        /// </summary>
        public ScoringResult JudgeGesture(GestureType performedGesture, OrchestraSection currentSection)
        {
            float currentTime = CurrentSongTime;
            RhythmCue? bestMatch = null;
            float bestOffset = float.MaxValue;

            // Find the closest matching cue within the timing window
            for (int i = nextCueIndex; i < cues.Count; i++)
            {
                var cue = cues[i];
                if (cue.consumed) continue;

                float offset = currentTime - cue.timestamp;
                float cueAbsOffset = Mathf.Abs(offset);

                // Check if within miss window
                if (cueAbsOffset > missWindow) 
                {
                    if (offset < -missWindow) break; // Future cues, stop searching
                    continue; // Past cues beyond window
                }

                // Check gesture type match
                if (cue.gestureType != performedGesture) continue;

                // Check section match (if cue specifies a section)
                if (cue.targetSection.HasValue && cue.targetSection.Value != currentSection) continue;

                // Found a valid match - check if it's the best
                if (cueAbsOffset < Mathf.Abs(bestOffset))
                {
                    bestMatch = cue;
                    bestOffset = offset;
                }
            }

            // No valid match found
            if (!bestMatch.HasValue)
            {
                return new ScoringResult
                {
                    judgement = JudgementType.Miss,
                    timingOffset = 0,
                    scoreAwarded = 0,
                    targetSection = currentSection,
                    matchedCue = null
                };
            }

            // Mark cue as consumed
            int matchIndex = cues.IndexOf(bestMatch.Value);
            if (matchIndex >= 0)
            {
                var cue = cues[matchIndex];
                cue.consumed = true;
                cues[matchIndex] = cue;

                // Advance nextCueIndex if needed
                while (nextCueIndex < cues.Count && cues[nextCueIndex].consumed)
                {
                    nextCueIndex++;
                }
            }

            // Determine judgement tier
            float finalAbsOffset = Mathf.Abs(bestOffset);
            JudgementType judgement;
            
            if (finalAbsOffset <= perfectWindow)
                judgement = JudgementType.Perfect;
            else if (finalAbsOffset <= goodWindow)
                judgement = JudgementType.Good;
            else
                judgement = JudgementType.Miss;

            return new ScoringResult
            {
                judgement = judgement,
                timingOffset = bestOffset,
                scoreAwarded = ScoringResult.GetScoreForJudgement(judgement),
                targetSection = bestMatch.Value.targetSection ?? currentSection,
                matchedCue = bestMatch
            };
        }

        #endregion

        #region Parsing Helpers

        private GestureType ParseGestureType(string id)
        {
            return id.ToUpper() switch
            {
                "UP" => GestureType.UP,
                "DOWN" => GestureType.DOWN,
                "LEFT" => GestureType.LEFT,
                "RIGHT" => GestureType.RIGHT,
                "PUNCH" => GestureType.PUNCH,
                "WITHDRAW" => GestureType.WITHDRAW,
                "V_SHAPE" => GestureType.V_SHAPE,
                "LAMBDA_SHAPE" => GestureType.LAMBDA_SHAPE,
                "TRIANGLE" => GestureType.TRIANGLE,
                "CIRCLE" => GestureType.CIRCLE,
                "S_SHAPE" => GestureType.S_SHAPE,
                "HOLD" => GestureType.HOLD,
                "READY" => GestureType.READY,
                "STRONG_ACCENT" => GestureType.STRONG_ACCENT,
                "CLEAR_CUTOFF" => GestureType.CLEAR_CUTOFF,
                "SUBDIVIDE" => GestureType.SUBDIVIDE,
                "BRING_OUT" => GestureType.BRING_OUT,
                "TRANSITION" => GestureType.TRANSITION,
                _ => GestureType.UP
            };
        }

        private OrchestraSection ParseSection(string section)
        {
            return section.ToLower() switch
            {
                "drum" => OrchestraSection.Drum,
                "flute" => OrchestraSection.Flute,
                "pipe" => OrchestraSection.Pipe,
                "xylophone" => OrchestraSection.Xylophone,
                _ => OrchestraSection.Drum
            };
        }

        #endregion

        #region JSON Data Classes

        [Serializable]
        private class RhythmMapData
        {
            public RhythmCueData[] cues;
        }

        [Serializable]
        private class RhythmCueData
        {
            public float timestamp;
            public string gestureId;
            public string section;
        }

        #endregion
    }
}
