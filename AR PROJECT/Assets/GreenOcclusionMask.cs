using UnityEngine;

/// <summary>
/// Renders a visual baton mesh aligned with the detected green blob axis.
/// Also occludes AR objects behind it via depth writing.
/// Uses BatonTracker's tip/base endpoints to orient a single elongated quad.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GreenOcclusionMask : MonoBehaviour
{
    [SerializeField] private BatonTracker batonTracker;

    [Header("Baton Shape")]
    [Tooltip("Width of the baton mesh in screen pixels")]
    [SerializeField] private float batonWidthPixels = 40f;
    [Tooltip("Extra length added beyond detected endpoints (screen pixels)")]
    [SerializeField] private float lengthExtension = 15f;
    [Tooltip("Depth from camera (meters). Slightly closer than BatonTracker.estimatedDepth to ensure occlusion.")]
    [SerializeField] private float occlusionDepth = 0.65f;
    [Tooltip("Taper ratio at the tip (0=pointed, 1=uniform width)")]
    [SerializeField] private float tipTaper = 0.3f;

    [Header("Visual")]
    [Tooltip("Base color of the baton shaft")]
    [SerializeField] private Color shaftColor = new Color(0.25f, 0.13f, 0.06f);
    [Tooltip("Tip/ferrule color (metallic)")]
    [SerializeField] private Color tipColor = new Color(0.85f, 0.85f, 0.9f);
    [Tooltip("Grip color at the base")]
    [SerializeField] private Color gripColor = new Color(0.12f, 0.12f, 0.12f);
    [Tooltip("How much of the baton length is the metallic tip (0-1)")]
    [SerializeField] private float tipFraction = 0.15f;
    [Tooltip("How much of the baton length is the grip (0-1)")]
    [SerializeField] private float gripFraction = 0.12f;

    [Header("Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.06f;

    private Mesh batonMesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Camera arCamera;

    private Vector2 smoothTip, smoothBase;
    private Vector2 tipVel, baseVel;
    private bool hasSmoothedPos;

    private const int SegmentCount = 12;
    private int debugLogCooldown;

    private void Awake()
    {
        if (batonTracker == null)
            batonTracker = GetComponentInParent<BatonTracker>();
        if (batonTracker == null)
            batonTracker = FindObjectOfType<BatonTracker>();

        Debug.Log($"[GreenOcclusionMask] Awake — batonTracker={(batonTracker != null ? batonTracker.gameObject.name : "NULL")}");

        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        batonMesh = new Mesh { name = "BatonVisual" };
        batonMesh.MarkDynamic();
        meshFilter.mesh = batonMesh;

        meshRenderer.material = CreateBatonMaterial();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    private void Start()
    {
        arCamera = Camera.main;
        Debug.Log($"[GreenOcclusionMask] Start — arCamera={(arCamera != null ? "OK" : "NULL")}, meshRenderer.enabled={meshRenderer.enabled}, material={meshRenderer.material?.shader?.name}");
    }

    private Material CreateBatonMaterial()
    {
        // Try custom shader first, then guaranteed built-in fallbacks that support vertex colors
        Shader shader = Shader.Find("Hidden/BatonVisual");

        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("UI/Default");

        Debug.Log($"[GreenOcclusionMask] Shader selected: {shader?.name ?? "NONE"}");

        var mat = new Material(shader);
        mat.renderQueue = 1999;
        mat.SetFloat("_ZWrite", 1f);

        // For Particles/Standard Unlit: enable vertex color mode
        if (shader.name.Contains("Particles"))
        {
            mat.SetFloat("_ColorMode", 1f); // vertex color
            mat.SetColor("_Color", Color.white);
        }

        return mat;
    }

    private void LateUpdate()
    {
        if (batonTracker == null || arCamera == null)
        {
            if (debugLogCooldown <= 0)
            {
                Debug.LogWarning($"[GreenOcclusionMask] Missing ref — batonTracker={batonTracker != null}, arCamera={arCamera != null}");
                debugLogCooldown = 300;
            }
            debugLogCooldown--;
            batonMesh.Clear();
            return;
        }

        if (!batonTracker.IsTracking || batonTracker.BlobPixelCount == 0)
        {
            batonMesh.Clear();
            hasSmoothedPos = false;
            return;
        }

        Vector2 rawTip = batonTracker.BlobTipScreen;
        Vector2 rawBase = batonTracker.BlobBaseScreen;

        if (!hasSmoothedPos)
        {
            smoothTip = rawTip;
            smoothBase = rawBase;
            hasSmoothedPos = true;
        }
        else
        {
            smoothTip = Vector2.SmoothDamp(smoothTip, rawTip, ref tipVel, positionSmoothTime);
            smoothBase = Vector2.SmoothDamp(smoothBase, rawBase, ref baseVel, positionSmoothTime);
        }

        BuildBatonMesh(smoothTip, smoothBase);
    }

    private void BuildBatonMesh(Vector2 tip, Vector2 baseEnd)
    {
        Vector2 axis = tip - baseEnd;
        float length = axis.magnitude;
        if (length < 2f)
        {
            batonMesh.Clear();
            return;
        }

        Vector2 dir = axis / length;
        Vector2 perp = new Vector2(-dir.y, dir.x);

        Vector2 extendedTip = tip + dir * lengthExtension;
        Vector2 extendedBase = baseEnd - dir * lengthExtension;

        int vertCount = (SegmentCount + 1) * 2;
        int triCount = SegmentCount * 6;

        var verts = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var colors = new Color[vertCount];
        var tris = new int[triCount];

        float depth = occlusionDepth;
        float halfW = batonWidthPixels * 0.5f;

        for (int i = 0; i <= SegmentCount; i++)
        {
            float t = (float)i / SegmentCount;

            Vector2 center = Vector2.Lerp(extendedBase, extendedTip, t);

            float widthMult = GetWidthMultiplier(t);
            float w = halfW * widthMult;

            Vector2 left = center - perp * w;
            Vector2 right = center + perp * w;

            int vi = i * 2;
            verts[vi] = arCamera.ScreenToWorldPoint(new Vector3(left.x, left.y, depth));
            verts[vi + 1] = arCamera.ScreenToWorldPoint(new Vector3(right.x, right.y, depth));

            uvs[vi] = new Vector2(0f, t);
            uvs[vi + 1] = new Vector2(1f, t);

            Color segColor = GetSegmentColor(t);
            colors[vi] = segColor;
            colors[vi + 1] = segColor;
        }

        int ti = 0;
        for (int i = 0; i < SegmentCount; i++)
        {
            int bl = i * 2;
            int br = i * 2 + 1;
            int tl = (i + 1) * 2;
            int tr = (i + 1) * 2 + 1;

            tris[ti++] = bl;
            tris[ti++] = tl;
            tris[ti++] = br;
            tris[ti++] = br;
            tris[ti++] = tl;
            tris[ti++] = tr;
        }

        batonMesh.Clear();
        batonMesh.vertices = verts;
        batonMesh.uv = uvs;
        batonMesh.colors = colors;
        batonMesh.triangles = tris;

        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    private float GetWidthMultiplier(float t)
    {
        if (t < gripFraction)
        {
            float gt = t / gripFraction;
            return Mathf.Lerp(0.7f, 1.1f, gt);
        }

        float shaftEnd = 1f - tipFraction;
        if (t < shaftEnd)
            return Mathf.Lerp(1.1f, 0.85f, (t - gripFraction) / (shaftEnd - gripFraction));

        float tipT = (t - shaftEnd) / tipFraction;
        return Mathf.Lerp(0.85f, tipTaper, tipT);
    }

    private Color GetSegmentColor(float t)
    {
        if (t < gripFraction)
            return Color.Lerp(gripColor, shaftColor, t / gripFraction);

        float shaftEnd = 1f - tipFraction;
        if (t < shaftEnd)
        {
            float st = (t - gripFraction) / (shaftEnd - gripFraction);
            Color woodLight = new Color(
                shaftColor.r * 1.4f,
                shaftColor.g * 1.3f,
                shaftColor.b * 1.2f);
            float grain = 0.9f + 0.1f * Mathf.Sin(st * 18f);
            return Color.Lerp(shaftColor, woodLight, st * 0.3f) * grain;
        }

        float tipT = (t - shaftEnd) / tipFraction;
        return Color.Lerp(shaftColor, tipColor, tipT);
    }

    private void OnDestroy()
    {
        if (batonMesh != null)
            Destroy(batonMesh);
    }
}
