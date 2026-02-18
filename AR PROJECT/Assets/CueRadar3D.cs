using UnityEngine;

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
        
        // Current timing color (exposed for HUD to read)
        private Color currentTimingColor = Color.red;
        public Color CurrentTimingColor => currentTimingColor;
        
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
            ringRenderer.material.color = Color.red;
            
            // Store ring reference for animation
            ringTransform = ringObj.transform;
            ringMaterial = ringRenderer.material;
            baseSize = size;
        }

        private Transform ringTransform;
        private Material ringMaterial;
        private float baseSize;

        public void ShowCue(GestureType gesture, float timeUntilHit)
        {
            currentGesture = gesture;
            totalTime = timeUntilHit;
            isActive = true;
            currentTimingColor = Color.red;
            
            // Reset ring
            if (ringTransform != null)
            {
                ringTransform.localScale = Vector3.one * baseSize * maxRingScale;
            }
            if (ringMaterial != null)
            {
                ringMaterial.color = Color.red;
            }
            
            gameObject.SetActive(true);
            Debug.Log($"[CueRadar3D] ShowCue: {gesture}, time={timeUntilHit}");
        }

        public void UpdateTimer(float remaining)
        {
            if (!isActive) return;
            
            // Animate ring scale
            float progress = Mathf.Clamp01(remaining / totalTime);
            float scale = Mathf.Lerp(maxRingScale, minRingScale, progress); // Sinks toward character as time passes
            
            if (ringTransform != null)
            {
                ringTransform.localScale = Vector3.one * baseSize * scale;
            }
            
            // Animate ring color: red → yellow → green (perfect) → dark green (late)
            currentTimingColor = GetColorForTime(remaining);
            if (ringMaterial != null)
            {
                ringMaterial.color = currentTimingColor;
            }
        }

        public void ShowResult(JudgementType judgement)
        {
            Color resultColor = judgement switch
            {
                JudgementType.Perfect => new Color(0f, 1f, 0.5f),
                JudgementType.Good => new Color(1f, 0.9f, 0f),
                _ => new Color(1f, 0.2f, 0.2f)
            };
            
            currentTimingColor = resultColor;
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
            // Color progression: red (far) → yellow (getting closer) → green (perfect window) → dark green (late)
            Color red = new Color(0.9f, 0.2f, 0.2f);
            Color yellow = new Color(1f, 0.85f, 0.1f);
            Color green = new Color(0.1f, 0.9f, 0.3f);
            Color darkGreen = new Color(0.05f, 0.4f, 0.15f);
            
            if (remaining < 0f)
            {
                // Late — dark green fading
                float lateness = Mathf.Clamp01(-remaining / 0.5f);
                return Color.Lerp(green, darkGreen, lateness);
            }
            if (remaining > readyThreshold)
            {
                return red;
            }
            if (remaining > closeThreshold)
            {
                // red → yellow
                float t = 1f - (remaining - closeThreshold) / (readyThreshold - closeThreshold);
                return Color.Lerp(red, yellow, t);
            }
            if (remaining > urgentThreshold)
            {
                // yellow → green
                float t = 1f - (remaining - urgentThreshold) / (closeThreshold - urgentThreshold);
                return Color.Lerp(yellow, green, t);
            }
            // urgentThreshold → 0: bright green (perfect zone)
            return green;
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
