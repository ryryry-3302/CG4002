using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;

/// <summary>
/// Tracks a fluorescent green baton in the AR camera feed using color detection.
/// Uses flood-fill connected components to find distinct green blobs, then picks
/// the largest elongated one as the baton.
/// </summary>
public class BatonTracker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARCameraManager cameraManager;

    [Header("Color Detection")]
    [Tooltip("HSV range for fluorescent green - tune in Inspector if baton not detected")]
    [SerializeField] private float hueMin = 0.22f;   // ~80° - wider range for lighting variance
    [SerializeField] private float hueMax = 0.45f;   // ~160°
    [SerializeField] private float saturationMin = 0.45f;  // Relaxed for different lighting
    [SerializeField] private float valueMin = 0.4f;        // Relaxed for different lighting
    [SerializeField] private int minGreenPixels = 40;
    [SerializeField] private float greenDominanceMin = 0.1f;  // G must exceed R and B

    [Header("Calibration")]
    [SerializeField] private bool showCalibrator;

    [Header("World Space")]
    [SerializeField] private float estimatedDepth = 0.7f;

    [Header("Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.08f;
    [SerializeField] private int processEveryNFrames = 3;

    [Header("Debug")]
    [SerializeField] private bool debugLogging;

    // Output
    public Vector3 TipWorldPosition { get; private set; }
    public Vector2 TipScreenPosition { get; private set; }
    public bool IsTracking { get; private set; }
    public float TrackingConfidence { get; private set; }

    private Camera arCamera;
    private int frameCounter;
    private Vector2 smoothedScreenPos;
    private Vector2 smoothVelocity;
    private bool hasValidPosition;

    // Reusable buffers to avoid allocations
    private bool[] greenGrid;
    private bool[] visited;
    private List<Blob> blobs = new List<Blob>();
    private Queue<Vector2Int> floodQueue = new Queue<Vector2Int>();

    // Calibrator debug stats
    private int lastGreenPixelCount;
    private int lastBlobCount;

    private struct Blob
    {
        public List<Vector2Int> pixels;
        public int minX, maxX, minY, maxY;

        public float AspectRatio
        {
            get
            {
                int boxW = maxX - minX + 1;
                int boxH = maxY - minY + 1;
                return (float)Math.Max(boxW, boxH) / Math.Max(1, Math.Min(boxW, boxH));
            }
        }

        public Vector2 Centroid
        {
            get
            {
                if (pixels == null || pixels.Count == 0) return Vector2.zero;
                Vector2 sum = Vector2.zero;
                foreach (var p in pixels)
                    sum += new Vector2(p.x, p.y);
                return sum / pixels.Count;
            }
        }
    }

    private void Awake()
    {
        if (cameraManager == null)
            cameraManager = FindObjectOfType<ARCameraManager>();

        if (cameraManager != null)
            arCamera = cameraManager.GetComponent<Camera>();

        if (arCamera == null)
            arCamera = Camera.main;
    }

    private void Update()
    {
        if (cameraManager == null || arCamera == null)
        {
            IsTracking = false;
            return;
        }

        frameCounter++;
        if (frameCounter % processEveryNFrames != 0)
        {
            if (hasValidPosition)
            {
                TipScreenPosition = Vector2.SmoothDamp(
                    TipScreenPosition, smoothedScreenPos, ref smoothVelocity, positionSmoothTime);
                TipWorldPosition = ScreenToWorld(TipScreenPosition);
            }
            return;
        }

        if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            return;

        try
        {
            int outW = image.width / 4;
            int outH = image.height / 4;
            int bufferSize = image.GetConvertedDataSize(
                new Vector2Int(outW, outH), TextureFormat.RGBA32);

            using (var buffer = new NativeArray<byte>(bufferSize, Allocator.Temp))
            {
                var conversionParams = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, image.width, image.height),
                    outputDimensions = new Vector2Int(outW, outH),
                    outputFormat = TextureFormat.RGBA32,
                    transformation = XRCpuImage.Transformation.MirrorY
                };

                image.Convert(conversionParams, buffer);

                if (DetectBatonTip(buffer, outW, outH, out Vector2 tipScreen))
                {
                    float scaleX = (float)Screen.width / outW;
                    float scaleY = (float)Screen.height / outH;
                    tipScreen.x *= scaleX;
                    tipScreen.y *= scaleY;

                    smoothedScreenPos = tipScreen;
                    TipScreenPosition = Vector2.SmoothDamp(
                        TipScreenPosition, smoothedScreenPos, ref smoothVelocity, positionSmoothTime);
                    TipWorldPosition = ScreenToWorld(TipScreenPosition);
                    IsTracking = true;
                    hasValidPosition = true;
                }
                else
                {
                    IsTracking = false;
                    if (!hasValidPosition)
                        TrackingConfidence = 0f;
                }
            }
        }
        finally
        {
            image.Dispose();
        }
    }

    private bool DetectBatonTip(NativeArray<byte> pixels, int width, int height, out Vector2 tipScreen)
    {
        tipScreen = Vector2.zero;

        // 1. Build green pixel grid
        int gridSize = width * height;
        if (greenGrid == null || greenGrid.Length < gridSize)
        {
            greenGrid = new bool[gridSize];
            visited = new bool[gridSize];
        }
        else
        {
            Array.Clear(greenGrid, 0, gridSize);
            Array.Clear(visited, 0, gridSize);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                float r = pixels[i] / 255f;
                float g = pixels[i + 1] / 255f;
                float blue = pixels[i + 2] / 255f;

                Color.RGBToHSV(new Color(r, g, blue), out float h, out float s, out float v);
                bool greenDominates = (g - r >= greenDominanceMin) && (g - blue >= greenDominanceMin);
                if (greenDominates && h >= hueMin && h <= hueMax && s >= saturationMin && v >= valueMin)
                    greenGrid[y * width + x] = true;
            }
        }

        lastGreenPixelCount = 0;
        for (int i = 0; i < gridSize; i++) if (greenGrid[i]) lastGreenPixelCount++;

        // 2. Flood-fill to find connected blobs
        blobs.Clear();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                if (!greenGrid[idx] || visited[idx]) continue;

                var blobPixels = new List<Vector2Int>();
                int minX = x, maxX = x, minY = y, maxY = y;

                floodQueue.Clear();
                floodQueue.Enqueue(new Vector2Int(x, y));
                visited[idx] = true;

                while (floodQueue.Count > 0)
                {
                    var p = floodQueue.Dequeue();
                    blobPixels.Add(p);
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;

                    // 4-connected neighbors
                    TryEnqueueNeighbor(p.x - 1, p.y, width, height, visited, floodQueue);
                    TryEnqueueNeighbor(p.x + 1, p.y, width, height, visited, floodQueue);
                    TryEnqueueNeighbor(p.x, p.y - 1, width, height, visited, floodQueue);
                    TryEnqueueNeighbor(p.x, p.y + 1, width, height, visited, floodQueue);
                }

                blobs.Add(new Blob
                {
                    pixels = blobPixels,
                    minX = minX, maxX = maxX, minY = minY, maxY = maxY
                });
            }
        }

        lastBlobCount = blobs.Count;

        // 3. Pick largest blob above threshold (tiebreak: higher aspect ratio)
        Blob? bestBlob = null;
        int bestScore = 0;
        float bestAspect = 0f;

        foreach (var blob in blobs)
        {
            if (blob.pixels.Count < minGreenPixels) continue;

            int score = blob.pixels.Count;
            float aspect = blob.AspectRatio;

            if (score > bestScore || (score == bestScore && aspect > bestAspect))
            {
                bestScore = score;
                bestAspect = aspect;
                bestBlob = blob;
            }
        }

        if (!bestBlob.HasValue)
            return false;

        var best = bestBlob.Value;
        Vector2 centroid = best.Centroid;

        // 4. Find tip: extremity furthest from centroid
        Vector2 tip = centroid;
        float maxDistSq = 0f;
        foreach (var p in best.pixels)
        {
            Vector2 v = new Vector2(p.x, p.y) - centroid;
            float dSq = v.sqrMagnitude;
            if (dSq > maxDistSq)
            {
                maxDistSq = dSq;
                tip = new Vector2(p.x, p.y);
            }
        }

        tipScreen = tip;
        TrackingConfidence = Mathf.Clamp01((float)best.pixels.Count / 500f);
        return true;
    }

    private void TryEnqueueNeighbor(int x, int y, int width, int height, bool[] vis, Queue<Vector2Int> queue)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return;
        int idx = y * width + x;
        if (!greenGrid[idx] || vis[idx]) return;
        vis[idx] = true;
        queue.Enqueue(new Vector2Int(x, y));
    }

    private Vector3 ScreenToWorld(Vector2 screenPos)
    {
        Ray ray = arCamera.ScreenPointToRay(screenPos);
        return ray.origin + ray.direction * estimatedDepth;
    }

    private void OnGUI()
    {
        if (!showCalibrator) return;

        float scale = 2f;
        GUI.matrix = Matrix4x4.TRS(Vector2.zero, Quaternion.identity, Vector2.one * scale);

        float panelW = 280;
        float panelH = 320;
        GUILayout.BeginArea(new Rect(10, 10, panelW, panelH), GUI.skin.box);
        GUILayout.Label("HSV Calibrator", GUI.skin.label);
        GUILayout.Space(4);

        GUILayout.Label($"Green pixels: {lastGreenPixelCount}  Blobs: {lastBlobCount}");
        GUILayout.Label(IsTracking ? "Tracking" : "Not tracking", IsTracking ? GUI.skin.label : GUI.skin.box);
        GUILayout.Space(8);

        GUILayout.Label($"Hue: {hueMin:F2} - {hueMax:F2}");
        GUILayout.BeginHorizontal();
        hueMin = GUILayout.HorizontalSlider(hueMin, 0f, 1f, GUILayout.Width(100));
        hueMax = GUILayout.HorizontalSlider(hueMax, 0f, 1f, GUILayout.Width(100));
        GUILayout.EndHorizontal();

        GUILayout.Label($"Saturation min: {saturationMin:F2}");
        saturationMin = GUILayout.HorizontalSlider(saturationMin, 0f, 1f);

        GUILayout.Label($"Value min: {valueMin:F2}");
        valueMin = GUILayout.HorizontalSlider(valueMin, 0f, 1f);

        GUILayout.Label($"Green dominance: {greenDominanceMin:F2}");
        greenDominanceMin = GUILayout.HorizontalSlider(greenDominanceMin, 0f, 0.5f);

        GUILayout.Label($"Min pixels: {minGreenPixels}");
        minGreenPixels = (int)GUILayout.HorizontalSlider(minGreenPixels, 10, 200);

        GUILayout.Space(4);
        if (GUILayout.Button("Reset to defaults"))
        {
            hueMin = 0.22f;
            hueMax = 0.45f;
            saturationMin = 0.45f;
            valueMin = 0.4f;
            greenDominanceMin = 0.1f;
            minGreenPixels = 40;
        }

        GUILayout.EndArea();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (cameraManager == null)
            cameraManager = FindObjectOfType<ARCameraManager>();
    }
#endif
}
