using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace OrchestraMaestro
{
    /// <summary>
    /// HUD Controller for Orchestra Maestro.
    /// Displays gesture prompts, score, combo, section indicator, and judgement feedback.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [Header("Score Display")]
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private TextMeshProUGUI comboText;
        
        [Header("Section Indicator")]
        [SerializeField] private TextMeshProUGUI sectionLabel;
        [SerializeField] private Image[] sectionIndicators; // 4 indicators for each section
        [SerializeField] private Color activeColor = new Color(1f, 0.84f, 0f, 1f); // Gold
        [SerializeField] private Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        
        [Header("Gesture Prompt")]
        [SerializeField] private TextMeshProUGUI promptText;
        [SerializeField] private Image promptBackground;
        [SerializeField] private float promptFadeDuration = 0.3f;
        
        [Header("Judgement Feedback")]
        [SerializeField] private TextMeshProUGUI judgementText;
        [SerializeField] private float judgementDisplayDuration = 0.8f;
        [SerializeField] private Color perfectColor = new Color(0f, 1f, 0.5f);
        [SerializeField] private Color goodColor = new Color(1f, 1f, 0f);
        [SerializeField] private Color missColor = new Color(1f, 0.3f, 0.3f);
        
        [Header("Beat Indicator")]
        [SerializeField] private Image beatPulseImage;
        [SerializeField] private float beatPulseDuration = 0.15f;
        [SerializeField] private float beatPulseScale = 1.3f;
        
        [Header("Game State")]
        [SerializeField] private GameObject playingUI;
        [SerializeField] private GameObject resultsUI;
        [SerializeField] private TextMeshProUGUI finalScoreText;
        [SerializeField] private TextMeshProUGUI perfectCountText;
        [SerializeField] private TextMeshProUGUI goodCountText;
        [SerializeField] private TextMeshProUGUI missCountText;
        [SerializeField] private TextMeshProUGUI maxComboText;
        
        // Runtime state
        private Coroutine judgementCoroutine;
        private Coroutine beatPulseCoroutine;
        private Coroutine promptCoroutine;
        
        // Singleton
        public static HUDController Instance { get; private set; }
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }
        
        private void Start()
        {
            // Subscribe to game events
            if (RhythmGameController.Instance != null)
            {
                RhythmGameController.Instance.OnScoreChanged += OnScoreChanged;
                RhythmGameController.Instance.OnSectionChanged += OnSectionChanged;
                RhythmGameController.Instance.OnGameStateChanged += OnGameStateChanged;
            }
            
            // Initial state
            UpdateScore(0, 0);
            UpdateSectionIndicator(0);
            HideJudgement();
            
            if (playingUI != null) playingUI.SetActive(true);
            if (resultsUI != null) resultsUI.SetActive(false);
        }
        
        private void OnDestroy()
        {
            if (RhythmGameController.Instance != null)
            {
                RhythmGameController.Instance.OnScoreChanged -= OnScoreChanged;
                RhythmGameController.Instance.OnSectionChanged -= OnSectionChanged;
                RhythmGameController.Instance.OnGameStateChanged -= OnGameStateChanged;
            }
            
            if (Instance == this) Instance = null;
        }
        
        #endregion
        
        #region Score Display
        
        /// <summary>Update the score and combo display</summary>
        public void UpdateScore(int score, int combo)
        {
            if (scoreText != null)
            {
                scoreText.text = score.ToString("N0");
            }
            
            if (comboText != null)
            {
                if (combo > 1)
                {
                    comboText.text = $"{combo}x COMBO";
                    comboText.gameObject.SetActive(true);
                    
                    // Pulse animation for combo
                    StartCoroutine(PulseText(comboText));
                }
                else
                {
                    comboText.gameObject.SetActive(false);
                }
            }
        }
        
        private void OnScoreChanged(int score, int combo)
        {
            UpdateScore(score, combo);
        }
        
        private IEnumerator PulseText(TextMeshProUGUI text)
        {
            Vector3 originalScale = Vector3.one;
            Vector3 pulsedScale = Vector3.one * 1.2f;
            
            text.transform.localScale = pulsedScale;
            
            float elapsed = 0f;
            float duration = 0.15f;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                text.transform.localScale = Vector3.Lerp(pulsedScale, originalScale, elapsed / duration);
                yield return null;
            }
            
            text.transform.localScale = originalScale;
        }
        
        #endregion
        
        #region Section Indicator
        
        /// <summary>Update the section indicator display</summary>
        public void UpdateSectionIndicator(int sectionIndex)
        {
            // Update label
            if (sectionLabel != null)
            {
                OrchestraSection section = (OrchestraSection)sectionIndex;
                sectionLabel.text = section.ToString();
            }
            
            // Update indicator images
            if (sectionIndicators != null)
            {
                for (int i = 0; i < sectionIndicators.Length && i < 4; i++)
                {
                    if (sectionIndicators[i] != null)
                    {
                        sectionIndicators[i].color = (i == sectionIndex) ? activeColor : inactiveColor;
                    }
                }
            }
        }
        
        private void OnSectionChanged(OrchestraSection section)
        {
            UpdateSectionIndicator((int)section);
        }
        
        #endregion
        
        #region Gesture Prompt
        
        /// <summary>Show a gesture prompt with the expected gesture</summary>
        public void ShowPrompt(GestureType gesture, float timeUntilHit)
        {
            if (promptText == null) return;
            
            string gestureName = FormatGestureName(gesture);
            promptText.text = gestureName;
            
            if (promptCoroutine != null)
            {
                StopCoroutine(promptCoroutine);
            }
            
            promptCoroutine = StartCoroutine(PromptAnimation(timeUntilHit));
        }
        
        /// <summary>Hide the current prompt</summary>
        public void HidePrompt()
        {
            if (promptText != null)
            {
                promptText.gameObject.SetActive(false);
            }
            if (promptBackground != null)
            {
                promptBackground.gameObject.SetActive(false);
            }
        }
        
        private IEnumerator PromptAnimation(float duration)
        {
            if (promptText != null) promptText.gameObject.SetActive(true);
            if (promptBackground != null) promptBackground.gameObject.SetActive(true);
            
            // Fade in
            float fadeIn = Mathf.Min(promptFadeDuration, duration * 0.3f);
            float elapsed = 0f;
            
            while (elapsed < fadeIn)
            {
                elapsed += Time.deltaTime;
                float alpha = elapsed / fadeIn;
                SetPromptAlpha(alpha);
                yield return null;
            }
            
            SetPromptAlpha(1f);
            
            // Hold
            yield return new WaitForSeconds(duration - fadeIn - promptFadeDuration);
            
            // Fade out
            elapsed = 0f;
            while (elapsed < promptFadeDuration)
            {
                elapsed += Time.deltaTime;
                float alpha = 1f - (elapsed / promptFadeDuration);
                SetPromptAlpha(alpha);
                yield return null;
            }
            
            HidePrompt();
        }
        
        private void SetPromptAlpha(float alpha)
        {
            if (promptText != null)
            {
                Color c = promptText.color;
                c.a = alpha;
                promptText.color = c;
            }
            if (promptBackground != null)
            {
                Color c = promptBackground.color;
                c.a = alpha * 0.8f;
                promptBackground.color = c;
            }
        }
        
        private string FormatGestureName(GestureType gesture)
        {
            return gesture switch
            {
                GestureType.UP => "↑ UP",
                GestureType.DOWN => "↓ DOWN",
                GestureType.LEFT => "← LEFT",
                GestureType.RIGHT => "→ RIGHT",
                GestureType.PUNCH => "👊 PUNCH",
                GestureType.WITHDRAW => "✋ WITHDRAW",
                GestureType.V_SHAPE => "V SHAPE",
                GestureType.LAMBDA_SHAPE => "∧ SHAPE",
                GestureType.TRIANGLE => "△ TRIANGLE",
                GestureType.CIRCLE => "○ CIRCLE",
                GestureType.S_SHAPE => "S SHAPE",
                GestureType.HOLD => "⏸ HOLD",
                GestureType.READY => "🎯 READY",
                GestureType.STRONG_ACCENT => "💥 ACCENT",
                GestureType.CLEAR_CUTOFF => "✂ CUTOFF",
                GestureType.SUBDIVIDE => "⚡ SUBDIVIDE",
                GestureType.BRING_OUT => "🔊 BRING OUT",
                GestureType.TRANSITION => "➡ TRANSITION",
                _ => gesture.ToString()
            };
        }
        
        #endregion
        
        #region Judgement Display
        
        /// <summary>Show judgement result (Perfect/Good/Miss)</summary>
        public void ShowJudgement(JudgementType judgement, float timingOffset)
        {
            if (judgementText == null) return;
            
            // Set text and color
            switch (judgement)
            {
                case JudgementType.Perfect:
                    judgementText.text = "PERFECT!";
                    judgementText.color = perfectColor;
                    break;
                case JudgementType.Good:
                    judgementText.text = "GOOD";
                    judgementText.color = goodColor;
                    break;
                case JudgementType.Miss:
                    judgementText.text = "MISS";
                    judgementText.color = missColor;
                    break;
            }
            
            // Add timing indicator
            if (judgement != JudgementType.Miss && Mathf.Abs(timingOffset) > 0.1f)
            {
                string timing = timingOffset < 0 ? " (Early)" : " (Late)";
                judgementText.text += timing;
            }
            
            // Start animation
            if (judgementCoroutine != null)
            {
                StopCoroutine(judgementCoroutine);
            }
            judgementCoroutine = StartCoroutine(JudgementAnimation());
        }
        
        private void HideJudgement()
        {
            if (judgementText != null)
            {
                judgementText.gameObject.SetActive(false);
            }
        }
        
        private IEnumerator JudgementAnimation()
        {
            judgementText.gameObject.SetActive(true);
            
            // Pop in
            Vector3 startScale = Vector3.one * 1.5f;
            Vector3 endScale = Vector3.one;
            
            float popDuration = 0.1f;
            float elapsed = 0f;
            
            while (elapsed < popDuration)
            {
                elapsed += Time.deltaTime;
                judgementText.transform.localScale = Vector3.Lerp(startScale, endScale, elapsed / popDuration);
                yield return null;
            }
            
            judgementText.transform.localScale = endScale;
            
            // Hold
            yield return new WaitForSeconds(judgementDisplayDuration - popDuration - 0.2f);
            
            // Fade out
            float fadeElapsed = 0f;
            Color originalColor = judgementText.color;
            
            while (fadeElapsed < 0.2f)
            {
                fadeElapsed += Time.deltaTime;
                Color c = originalColor;
                c.a = 1f - (fadeElapsed / 0.2f);
                judgementText.color = c;
                yield return null;
            }
            
            HideJudgement();
            judgementText.color = originalColor;
        }
        
        #endregion
        
        #region Beat Pulse
        
        /// <summary>Show beat pulse indicator (called on each downstroke)</summary>
        public void ShowBeatPulse()
        {
            if (beatPulseImage == null) return;
            
            if (beatPulseCoroutine != null)
            {
                StopCoroutine(beatPulseCoroutine);
            }
            beatPulseCoroutine = StartCoroutine(BeatPulseAnimation());
        }
        
        private IEnumerator BeatPulseAnimation()
        {
            beatPulseImage.gameObject.SetActive(true);
            
            Vector3 startScale = Vector3.one * beatPulseScale;
            Vector3 endScale = Vector3.one;
            Color startColor = beatPulseImage.color;
            startColor.a = 1f;
            Color endColor = startColor;
            endColor.a = 0f;
            
            float elapsed = 0f;
            
            while (elapsed < beatPulseDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / beatPulseDuration;
                
                beatPulseImage.transform.localScale = Vector3.Lerp(startScale, endScale, t);
                beatPulseImage.color = Color.Lerp(startColor, endColor, t);
                
                yield return null;
            }
            
            beatPulseImage.gameObject.SetActive(false);
        }
        
        #endregion
        
        #region Game State UI
        
        private void OnGameStateChanged(RhythmGameController.GameState state)
        {
            switch (state)
            {
                case RhythmGameController.GameState.Playing:
                    if (playingUI != null) playingUI.SetActive(true);
                    if (resultsUI != null) resultsUI.SetActive(false);
                    break;
                    
                case RhythmGameController.GameState.Results:
                    ShowResults();
                    break;
            }
        }
        
        /// <summary>Show the results screen</summary>
        public void ShowResults()
        {
            if (playingUI != null) playingUI.SetActive(false);
            if (resultsUI != null) resultsUI.SetActive(true);
            
            var controller = RhythmGameController.Instance;
            if (controller == null) return;
            
            if (finalScoreText != null)
                finalScoreText.text = controller.TotalScore.ToString("N0");
            
            if (perfectCountText != null)
                perfectCountText.text = controller.PerfectCount.ToString();
            
            if (goodCountText != null)
                goodCountText.text = controller.GoodCount.ToString();
            
            if (missCountText != null)
                missCountText.text = controller.MissCount.ToString();
            
            if (maxComboText != null)
                maxComboText.text = $"Max Combo: {controller.MaxCombo}";
        }
        
        #endregion
    }
}
