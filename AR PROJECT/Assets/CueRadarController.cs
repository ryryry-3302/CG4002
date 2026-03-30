using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace OrchestraMaestro
{
    /// <summary>
    /// Controls a single cue radar display for one instrument.
    /// Shows gesture icon with shrinking ring timer and hit/miss feedback.
    /// </summary>
    public class CueRadarController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image outerRing;
        [SerializeField] private Image innerCircle;
        [SerializeField] private Image gestureIcon;
        [SerializeField] private TextMeshProUGUI gestureText; // Fallback if no sprite
        
        [Header("Ring Animation")]
        [SerializeField] private float maxRingScale = 1.5f;
        [SerializeField] private float minRingScale = 1.0f;
        
        [Header("Color Transitions")]
        [SerializeField] private Color farColor = Color.white;
        [SerializeField] private Color readyColor = Color.green;
        [SerializeField] private Color closeColor = Color.yellow;
        [SerializeField] private Color urgentColor = Color.red;
        
        [Header("Timing Thresholds (seconds)")]
        [SerializeField] private float readyThreshold = 1.5f;   // White → Green
        [SerializeField] private float closeThreshold = 0.7f;   // Green → Yellow
        [SerializeField] private float urgentThreshold = 0.3f;  // Yellow → Red
        
        [Header("Pop-in Animation")]
        [SerializeField] private float popInDuration = 0.15f;
        [SerializeField] private float popInScale = 1.2f;
        
        [Header("Feedback Animation")]
        [SerializeField] private float feedbackDuration = 0.4f;
        
        // Current state
        private bool isActive = false;
        private float totalTime = 0f;
        private float remainingTime = 0f;
        private GestureType currentGesture;
        private Coroutine currentAnimation;
        
        // Gesture sprites (set via code or inspector)
        private static Sprite[] gestureSprites;
        
        public bool IsActive => isActive;
        public GestureType CurrentGesture => currentGesture;

        #region Unity Lifecycle

        private void Awake()
        {
            // Don't auto-hide in Awake - let the manager control visibility
            // This allows the prefab to be instantiated properly
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Set UI references (for dynamically created radars)
        /// </summary>
        public void SetReferences(UnityEngine.UI.Image outer, UnityEngine.UI.Image inner, TextMeshProUGUI text)
        {
            outerRing = outer;
            innerCircle = inner;
            gestureText = text;
        }

        /// <summary>
        /// Show a cue with pop-in animation
        /// </summary>
        public void ShowCue(GestureType gesture, float timeUntilHit)
        {
            Debug.Log($"[CueRadarController] ShowCue called: gesture={gesture}, timeUntilHit={timeUntilHit}");
            
            currentGesture = gesture;
            totalTime = timeUntilHit;
            remainingTime = timeUntilHit;
            isActive = true;
            
            // Set gesture display
            SetGestureDisplay(gesture);
            
            // Reset ring
            if (outerRing != null)
            {
                outerRing.transform.localScale = Vector3.one * maxRingScale;
                outerRing.color = farColor;
            }
            
            // Show and animate pop-in
            gameObject.SetActive(true);
            Debug.Log($"[CueRadarController] GameObject activated: {gameObject.name}, active={gameObject.activeSelf}");
            
            if (currentAnimation != null) StopCoroutine(currentAnimation);
            currentAnimation = StartCoroutine(PopInAnimation());
        }

        /// <summary>
        /// Update the timer display (call each frame)
        /// </summary>
        public void UpdateTimer(float remaining)
        {
            if (!isActive) return;
            
            remainingTime = remaining;
            
            // Animate ring scale (shrinks as time runs out)
            float progress = Mathf.Clamp01(remaining / totalTime);
            float scale = Mathf.Lerp(minRingScale, maxRingScale, progress);
            
            if (outerRing != null)
            {
                outerRing.transform.localScale = Vector3.one * scale;
                outerRing.color = GetColorForTime(remaining);
            }
        }

        /// <summary>
        /// Show hit/miss result with feedback animation
        /// </summary>
        public void ShowResult(JudgementType judgement)
        {
            if (!isActive) return;
            
            if (currentAnimation != null) StopCoroutine(currentAnimation);
            currentAnimation = StartCoroutine(ResultAnimation(judgement));
        }

        /// <summary>
        /// Hide the radar immediately
        /// </summary>
        public void Hide()
        {
            isActive = false;
            if (currentAnimation != null) StopCoroutine(currentAnimation);
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Fade out the radar
        /// </summary>
        public void FadeOut(float duration = 0.2f)
        {
            if (currentAnimation != null) StopCoroutine(currentAnimation);
            currentAnimation = StartCoroutine(FadeOutAnimation(duration));
        }

        #endregion

        #region Private Methods

        private void SetGestureDisplay(GestureType gesture)
        {
            string gestureSymbol = GetGestureSymbol(gesture);
            
            // Use text fallback for now (sprites can be added later)
            if (gestureText != null)
            {
                gestureText.text = gestureSymbol;
                gestureText.gameObject.SetActive(true);
            }
            
            if (gestureIcon != null)
            {
                gestureIcon.gameObject.SetActive(false); // Hide until sprites are set up
            }
        }

        private string GetGestureSymbol(GestureType gesture)
        {
            // Use simple ASCII text to avoid font issues
            switch (gesture)
            {
                case GestureType.UP: return "UP";
                case GestureType.DOWN: return "DN";
                case GestureType.LEFT: return "LT";
                case GestureType.RIGHT: return "RT";
                case GestureType.PUNCH: return "HIT";
                case GestureType.WITHDRAW: return "OUT";
                case GestureType.W_SHAPE: return "W";
                case GestureType.HOURGLASS_SHAPE: return "HG";
                case GestureType.LIGHTNING_BOLT_SHAPE: return "LB";
                case GestureType.TRIPLE_CLOCKWISE_CIRCLE: return "TCC";
                default: return "?";
            }
        }

        private Color GetColorForTime(float remaining)
        {
            if (remaining > readyThreshold) return farColor;
            if (remaining > closeThreshold) return Color.Lerp(readyColor, farColor, (remaining - closeThreshold) / (readyThreshold - closeThreshold));
            if (remaining > urgentThreshold) return Color.Lerp(closeColor, readyColor, (remaining - urgentThreshold) / (closeThreshold - urgentThreshold));
            return Color.Lerp(urgentColor, closeColor, remaining / urgentThreshold);
        }

        #endregion

        #region Animations

        private IEnumerator PopInAnimation()
        {
            Vector3 startScale = Vector3.zero;
            Vector3 overshootScale = Vector3.one * popInScale;
            Vector3 finalScale = Vector3.one;
            
            transform.localScale = startScale;
            
            // Pop up to overshoot
            float elapsed = 0f;
            while (elapsed < popInDuration * 0.6f)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / (popInDuration * 0.6f);
                transform.localScale = Vector3.Lerp(startScale, overshootScale, EaseOutBack(t));
                yield return null;
            }
            
            // Settle to final
            elapsed = 0f;
            while (elapsed < popInDuration * 0.4f)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / (popInDuration * 0.4f);
                transform.localScale = Vector3.Lerp(overshootScale, finalScale, t);
                yield return null;
            }
            
            transform.localScale = finalScale;
            currentAnimation = null;
        }

        private IEnumerator ResultAnimation(JudgementType judgement)
        {
            Color feedbackColor;
            float burstScale;
            
            switch (judgement)
            {
                case JudgementType.Perfect:
                    feedbackColor = new Color(0f, 1f, 0.5f, 1f); // Bright green
                    burstScale = 1.5f;
                    break;
                case JudgementType.Good:
                    feedbackColor = new Color(1f, 0.9f, 0f, 1f); // Yellow
                    burstScale = 1.3f;
                    break;
                case JudgementType.Miss:
                default:
                    feedbackColor = new Color(1f, 0.2f, 0.2f, 1f); // Red
                    burstScale = 0.8f;
                    break;
            }
            
            // Flash color
            if (innerCircle != null) innerCircle.color = feedbackColor;
            if (outerRing != null) outerRing.color = feedbackColor;
            
            // Burst/shrink animation
            Vector3 startScale = transform.localScale;
            Vector3 burstScaleVec = Vector3.one * burstScale;
            
            float elapsed = 0f;
            float burstDuration = feedbackDuration * 0.3f;
            
            // Burst out
            while (elapsed < burstDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / burstDuration;
                transform.localScale = Vector3.Lerp(startScale, burstScaleVec, EaseOutQuad(t));
                yield return null;
            }
            
            // Fade out
            elapsed = 0f;
            float fadeDuration = feedbackDuration * 0.7f;
            CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
            
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fadeDuration;
                
                transform.localScale = Vector3.Lerp(burstScaleVec, Vector3.zero, EaseInQuad(t));
                
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 1f - t;
                }
                
                yield return null;
            }
            
            // Reset and hide
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            transform.localScale = Vector3.one;
            isActive = false;
            gameObject.SetActive(false);
            currentAnimation = null;
        }

        private IEnumerator FadeOutAnimation(float duration)
        {
            CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
            float elapsed = 0f;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                
                transform.localScale = Vector3.Lerp(Vector3.one, Vector3.zero, EaseInQuad(t));
                
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 1f - t;
                }
                
                yield return null;
            }
            
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            transform.localScale = Vector3.one;
            isActive = false;
            gameObject.SetActive(false);
            currentAnimation = null;
        }

        // Easing functions
        private float EaseOutBack(float t)
        {
            float c1 = 1.70158f;
            float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
        private float EaseInQuad(float t) => t * t;

        #endregion
    }
}
