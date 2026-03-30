using System;

/// <summary>
/// Core type definitions for Orchestra Maestro conducting game.
/// Based on Section 6 of design report - 11 basic gestures + 7 combo gestures.
/// </summary>
namespace OrchestraMaestro
{
    /// <summary>
    /// All gesture types recognized by the system.
    /// Gestures (0-10): Left-hand glove only
    /// </summary>
    public enum GestureType
    {
        // === Left-Hand Gestures ===
        ERROR = 0,
        UP = 1,                         // Crescendo - increase section volume
        DOWN = 2,                       // Decrescendo - decrease section volume
        LEFT = 3,                       // Section cue - switch to left section
        RIGHT = 4,                      // Section cue - switch to right section
        PUNCH = 5,                      // Accent - strong emphasized hit
        WITHDRAW = 6,                   // Cutoff - stop sustained sound cleanly
        W_SHAPE = 7,                    // Phrasing flourish - wave-like melodic passage
        HOURGLASS_SHAPE = 8,            // Tempo reset/loop - prepare for phrase repeat
        LIGHTNING_BOLT_SHAPE = 9,       // Subito change - sudden dramatic shift in dynamics
        TRIPLE_CLOCKWISE_CIRCLE = 10    // Ritardando - gradual slowing over three measures
    }

    /// <summary>
    /// Orchestra instruments that can be targeted for conducting gestures.
    /// Index 0-3 for array access, wrap-around navigation via LEFT/RIGHT.
    /// </summary>
    public enum OrchestraSection
    {
        Drum = 0,
        Flute = 1,
        Pipe = 2,
        Xylophone = 3
    }

    /// <summary>
    /// Timing judgement result based on ±1 second window.
    /// </summary>
    public enum JudgementType
    {
        Perfect,    // ≤0.3s from target
        Good,       // ≤0.7s from target
        Miss        // >0.7s or wrong gesture
    }

    /// <summary>
    /// A single cue in the rhythm map timeline.
    /// </summary>
    [Serializable]
    public struct RhythmCue
    {
        /// <summary>Timestamp in seconds from song start</summary>
        public float timestamp;
        
        /// <summary>Expected gesture type</summary>
        public GestureType gestureType;
        
        /// <summary>Target orchestra section (null for section-independent gestures)</summary>
        public OrchestraSection? targetSection;
        
        /// <summary>Whether this cue has been hit/missed already</summary>
        public bool consumed;

        public RhythmCue(float timestamp, GestureType gestureType, OrchestraSection? targetSection = null)
        {
            this.timestamp = timestamp;
            this.gestureType = gestureType;
            this.targetSection = targetSection;
            this.consumed = false;
        }
    }

    /// <summary>
    /// Left-hand gesture event received from Ultra96 via MQTT.
    /// Topic: orchestra/left_gesture_event
    /// </summary>
    [Serializable]
    public struct LeftGestureEvent
    {
        public string gestureId;
        public string inference;
        public bool isClenched;
        public long timestamp;
        public float confidence;

        public void Normalize()
        {
            if (string.IsNullOrEmpty(gestureId) && !string.IsNullOrEmpty(inference))
            {
                gestureId = inference switch
                {
                    "0" => "IDLE",
                    "1" => "UP",
                    "2" => "DOWN",
                    "3" => "LEFT",
                    "4" => "RIGHT",
                    "5" => "PUNCH",
                    "6" => "WITHDRAW",
                    "7" => "W_SHAPE",
                    "8" => "HOURGLASS_SHAPE",
                    "9" => "LIGHTNING_BOLT_SHAPE",
                    "10" => "TRIPLE_CLOCKWISE_CIRCLE",
                    _ => "UP"
                };
                
                isClenched = true;
                confidence = 1.0f;
            }
        }

        /// <summary>Parse gestureId string to GestureType enum</summary>
        public GestureType GetGestureType()
        {
            if (string.IsNullOrEmpty(gestureId)) return GestureType.UP;
            return gestureId.ToUpper() switch
            {
                "UP" => GestureType.UP,
                "DOWN" => GestureType.DOWN,
                "LEFT" => GestureType.LEFT,
                "RIGHT" => GestureType.RIGHT,
                "PUNCH" => GestureType.PUNCH,
                "WITHDRAW" => GestureType.WITHDRAW,
                "W_SHAPE" => GestureType.W_SHAPE,
                "HOURGLASS_SHAPE" => GestureType.HOURGLASS_SHAPE,
                "LIGHTNING_BOLT_SHAPE" => GestureType.LIGHTNING_BOLT_SHAPE,
                "TRIPLE_CLOCKWISE_CIRCLE" => GestureType.TRIPLE_CLOCKWISE_CIRCLE,
                _ => GestureType.UP // Default fallback
            };
        }
    }

    /// <summary>
    /// Right-hand stick stroke event received via MQTT.
    /// Topic: orchestra/stick_stroke
    /// </summary>
    [Serializable]
    public struct RightStickEvent
    {
        public string type;     // "DOWNSTROKE"
        public long timestamp;
    }

    /// <summary>
    /// Scoring result for a single gesture attempt.
    /// </summary>
    public struct ScoringResult
    {
        public JudgementType judgement;
        public float timingOffset;      // Negative = early, Positive = late
        public int scoreAwarded;
        public OrchestraSection targetSection;
        public RhythmCue? matchedCue;   // The cue that was matched (if any)
        public GestureType gestureType; // The gesture that was performed

        public static int GetScoreForJudgement(JudgementType judgement)
        {
            return judgement switch
            {
                JudgementType.Perfect => 100,
                JudgementType.Good => 50,
                JudgementType.Miss => 0,
                _ => 0
            };
        }
    }

    /// <summary>
    /// Utility methods for gesture classification.
    /// </summary>
    public static class GestureUtils
    {
        /// <summary>Check if gesture is a section navigation gesture (LEFT/RIGHT)</summary>
        public static bool IsSectionNavigation(GestureType gesture)
        {
            return gesture == GestureType.LEFT || gesture == GestureType.RIGHT;
        }

        /// <summary>Check if gesture requires combo validation with stick pattern</summary>
        public static bool IsComboGesture(GestureType gesture)
        {
            return false; // No combo gestures in new gesture set
        }

        /// <summary>Check if gesture affects section volume</summary>
        public static bool IsVolumeGesture(GestureType gesture)
        {
            return gesture == GestureType.UP || 
                   gesture == GestureType.DOWN || 
                   gesture == GestureType.W_SHAPE;
        }

        /// <summary>Check if gesture is a cutoff/release type</summary>
        public static bool IsCutoffGesture(GestureType gesture)
        {
            return gesture == GestureType.WITHDRAW;
        }

        /// <summary>Check if gesture affects tempo</summary>
        public static bool IsTempoGesture(GestureType gesture)
        {
            return gesture == GestureType.HOURGLASS_SHAPE || 
                   gesture == GestureType.TRIPLE_CLOCKWISE_CIRCLE;
        }

        /// <summary>
        /// Get the display string for a gesture type.
        /// </summary>
        public static string ToDisplayString(this GestureType gesture)
        {
            return gesture switch
            {
                GestureType.W_SHAPE => "W",
                GestureType.HOURGLASS_SHAPE => "⏳",
                GestureType.LIGHTNING_BOLT_SHAPE => "⚡",
                GestureType.TRIPLE_CLOCKWISE_CIRCLE => "⭕",
                _ => gesture.ToString().Replace("_", " ") // Default for others
            };
        }
    }
}
