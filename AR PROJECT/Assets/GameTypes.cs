using System;

/// <summary>
/// Core type definitions for Orchestra Maestro conducting game.
/// Based on Section 6 of design report - 11 basic gestures + 7 combo gestures.
/// </summary>
namespace OrchestraMaestro
{
    /// <summary>
    /// All gesture types recognized by the system.
    /// Basic gestures (0-10): Left-hand glove only
    /// Combo gestures (11-17): Left-hand + right-stick pattern
    /// </summary>
    public enum GestureType
    {
        // === Basic Left-Hand Gestures ===
        UP = 0,             // Crescendo - increase section volume
        DOWN = 1,           // Decrescendo - decrease section volume
        LEFT = 2,           // Section cue - switch to left section
        RIGHT = 3,          // Section cue - switch to right section
        PUNCH = 4,          // Accent (sforzando) - strong emphasized hit
        WITHDRAW = 5,       // Cutoff (release) - stop sustained sound
        V_SHAPE = 6,        // Open/broaden - fuller, louder
        LAMBDA_SHAPE = 7,   // Close/tighten - softer, controlled
        TRIANGLE = 8,       // Accelerando - increase tempo
        CIRCLE = 9,         // Ritardando - decrease tempo
        S_SHAPE = 10,       // Colour change - timbre swap

        // === Combo Gestures (Left + Right Stick Pattern) ===
        HOLD = 11,          // Fermata - freeze tempo, sustain chord
        READY = 12,         // Prep cue - confirm devices stable
        STRONG_ACCENT = 13, // Emphasized hit (Down-Up-Down stick pattern)
        CLEAR_CUTOFF = 14,  // Clean stop (Down then hold stick)
        SUBDIVIDE = 15,     // Widen timing tolerance (Down-Up-Down-Up fast)
        BRING_OUT = 16,     // Highlight section (Up-Down-Up stick pattern)
        TRANSITION = 17     // Advance to next phrase (Down-Up-Down-Up then hold)
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
        public bool isClenched;
        public long timestamp;
        public float confidence;

        /// <summary>Parse gestureId string to GestureType enum</summary>
        public GestureType GetGestureType()
        {
            return gestureId.ToUpper() switch
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
            return (int)gesture >= 11; // HOLD and above are combo gestures
        }

        /// <summary>Check if gesture affects section volume</summary>
        public static bool IsVolumeGesture(GestureType gesture)
        {
            return gesture == GestureType.UP || 
                   gesture == GestureType.DOWN || 
                   gesture == GestureType.V_SHAPE || 
                   gesture == GestureType.LAMBDA_SHAPE;
        }

        /// <summary>Check if gesture is a cutoff/release type</summary>
        public static bool IsCutoffGesture(GestureType gesture)
        {
            return gesture == GestureType.WITHDRAW || gesture == GestureType.CLEAR_CUTOFF;
        }

        /// <summary>Check if gesture affects tempo</summary>
        public static bool IsTempoGesture(GestureType gesture)
        {
            return gesture == GestureType.TRIANGLE || 
                   gesture == GestureType.CIRCLE ||
                   gesture == GestureType.HOLD;
        }
    }
}
