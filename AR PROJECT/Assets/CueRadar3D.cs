using UnityEngine;
using TMPro;

namespace OrchestraMaestro
{
    /// <summary>
    /// Simple 3D-based cue radar that works reliably in AR.
    /// Uses a Quad with material instead of Canvas UI.
    /// </summary>
    public class CueRadar3D : MonoBehaviour
    {
        [Header("Visual Components")]
        [SerializeField] private SpriteRenderer backgroundSprite;
        [SerializeField] private SpriteRenderer ringSprite;
        [SerializeField] private TextMeshPro gestureText;
        
        [Header("Colors")]
        [SerializeField] private Color farColor = Color.white;
        [SerializeField] private Color readyColor = Color.green;
        [SerializeField] private Color closeColor = Color.yellow;
        [SerializeField] private Color urgentColor = Color.red;
        [SerializeField] private Color perfectColor = new Color(0f, 1f, 0.5f);
        [SerializeField] private Color goodColor = new Color(1f, 0.9f, 0f);
        [SerializeField] private Color missColor = new Color(1f, 0.2f, 0.2f);
        
        [Header("Timing Thresholds")]
        [SerializeField] private float readyThreshold = 1.5f;
        [SerializeField] private float closeThreshold = 0.7f;
        [SerializeField] private float urgentThreshold = 0.3f;
        
        [Header("Animation")]
        [SerializeField] private float maxRingScale = 1.5f;
        [SerializeField] private float minRingScale = 1.0f;
        
        private bool isActive = false;
        private float totalTime = 0f;
        private GestureType currentGesture;
        
        public bool IsActive => isActive;

        /// <summary>
        /// Create a radar programmatically (no prefab needed)
        /// </summary>
        public static CueRadar3D Create(Vector3 position, float size)
        {
            GameObject radarObj = new GameObject("CueRadar3D");
            radarObj.transform.position = position;
            
            CueRadar3D radar = radarObj.AddComponent<CueRadar3D>();
            radar.CreateVisuals(size);
            radar.Hide();
            
            return radar;
        }

        private void CreateVisuals(float size)
        {
            // Create background quad
            GameObject bgObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgObj.name = "Background";
            bgObj.transform.SetParent(transform, false);
            bgObj.transform.localScale = Vector3.one * size;
            Destroy(bgObj.GetComponent<Collider>());
            
            Renderer bgRenderer = bgObj.GetComponent<Renderer>();
            bgRenderer.material = new Material(Shader.Find("Sprites/Default"));
            bgRenderer.material.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            backgroundSprite = null; // Using renderer directly
            
            // Create ring quad (slightly in front)
            GameObject ringObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ringObj.name = "Ring";
            ringObj.transform.SetParent(transform, false);
            ringObj.transform.localPosition = new Vector3(0, 0, -0.01f);
            ringObj.transform.localScale = Vector3.one * size * maxRingScale;
            Destroy(ringObj.GetComponent<Collider>());
            
            Renderer ringRenderer = ringObj.GetComponent<Renderer>();
            ringRenderer.material = new Material(Shader.Find("Sprites/Default"));
            ringRenderer.material.color = farColor;
            
            // Store ring reference for animation
            ringTransform = ringObj.transform;
            ringMaterial = ringRenderer.material;
            baseSize = size;
            
            // Create text
            GameObject textObj = new GameObject("GestureText");
            textObj.transform.SetParent(transform, false);
            textObj.transform.localPosition = new Vector3(0, 0, -0.02f);
            
            gestureText = textObj.AddComponent<TextMeshPro>();
            gestureText.text = "?";
            gestureText.fontSize = 4;
            gestureText.alignment = TextAlignmentOptions.Center;
            gestureText.color = Color.white;
            
            RectTransform textRect = gestureText.GetComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(size, size);
        }

        private Transform ringTransform;
        private Material ringMaterial;
        private float baseSize;

        public void ShowCue(GestureType gesture, float timeUntilHit)
        {
            currentGesture = gesture;
            totalTime = timeUntilHit;
            isActive = true;
            
            // Set gesture text
            if (gestureText != null)
            {
                gestureText.text = GetGestureText(gesture);
            }
            
            // Reset ring
            if (ringTransform != null)
            {
                ringTransform.localScale = Vector3.one * baseSize * maxRingScale;
            }
            if (ringMaterial != null)
            {
                ringMaterial.color = farColor;
            }
            
            gameObject.SetActive(true);
            Debug.Log($"[CueRadar3D] ShowCue: {gesture}, time={timeUntilHit}");
        }

        public void UpdateTimer(float remaining)
        {
            if (!isActive) return;
            
            // Animate ring scale
            float progress = Mathf.Clamp01(remaining / totalTime);
            float scale = Mathf.Lerp(minRingScale, maxRingScale, progress);
            
            if (ringTransform != null)
            {
                ringTransform.localScale = Vector3.one * baseSize * scale;
            }
            
            // Animate ring color
            if (ringMaterial != null)
            {
                ringMaterial.color = GetColorForTime(remaining);
            }
        }

        public void ShowResult(JudgementType judgement)
        {
            Color resultColor = judgement switch
            {
                JudgementType.Perfect => perfectColor,
                JudgementType.Good => goodColor,
                _ => missColor
            };
            
            if (ringMaterial != null)
            {
                ringMaterial.color = resultColor;
            }
            
            // Hide after brief delay
            Invoke(nameof(Hide), 0.3f);
        }

        public void Hide()
        {
            isActive = false;
            gameObject.SetActive(false);
        }

        private Color GetColorForTime(float remaining)
        {
            if (remaining > readyThreshold) return farColor;
            if (remaining > closeThreshold) return Color.Lerp(readyColor, farColor, (remaining - closeThreshold) / (readyThreshold - closeThreshold));
            if (remaining > urgentThreshold) return Color.Lerp(closeColor, readyColor, (remaining - urgentThreshold) / (closeThreshold - urgentThreshold));
            return Color.Lerp(urgentColor, closeColor, remaining / urgentThreshold);
        }

        private string GetGestureText(GestureType gesture)
        {
            return gesture switch
            {
                GestureType.UP => "UP",
                GestureType.DOWN => "DN",
                GestureType.LEFT => "LT",
                GestureType.RIGHT => "RT",
                GestureType.PUNCH => "HIT",
                GestureType.WITHDRAW => "OUT",
                GestureType.V_SHAPE => "V",
                GestureType.LAMBDA_SHAPE => "^",
                GestureType.TRIANGLE => "TRI",
                GestureType.CIRCLE => "O",
                GestureType.S_SHAPE => "S",
                _ => "?"
            };
        }

        private void Update()
        {
            // Always face camera
            if (Camera.main != null)
            {
                transform.LookAt(Camera.main.transform);
                transform.Rotate(0, 180, 0);
            }
        }
    }
}
