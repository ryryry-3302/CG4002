using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;
using OrchestraMaestro;

/// <summary>
/// Tracks a fluorescent green baton in the AR camera feed using color detection.
/// Only active during gameplay (Playing or Paused state).
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

    // Exposed for occlusion mask
    public Rect BlobScreenRect { get; private set; }
    public Vector2 BlobCentroidScreen { get; private set; }
    public int BlobPixelCount { get; private set; }
    public int DownscaledWidth { get; private set; }
    public int DownscaledHeight { get; private set; }
    public bool[] GreenGrid => greenGrid;
    public int GreenGridWidth => DownscaledWidth;
    public int GreenGridHeight => DownscaledHeight;

    // Baton axis endpoints in screen-space (full resolution pixels)
    public Vector2 BlobTipScreen { get; private set; }
    public Vector2 BlobBaseScreen { get; private set; }

    private bool trackingEnabled;
    private Camera arCamera;
    private int frameCounter;
    private Vector2 smoothedScreenPos;
    private Vector2 smoothVelocity;
    private bool hasValidPosition;

    // Image-to-screen mapping (accounts for crop/letterbox)
    private float imgToScreenScaleX, imgToScreenScaleY;
    private float imgToScreenOffsetX, imgToScreenOffsetY;

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

    private void Start()
    {
        if (RhythmGameController.Instance != null)
        {
            RhythmGameController.Instance.OnGameStateChanged += OnGameStateChanged;
            trackingEnabled = RhythmGameController.Instance.CurrentState == RhythmGameController.GameState.Playing
                || RhythmGameController.Instance.CurrentState == RhythmGameController.GameState.Paused;
        }
        else
        {
            trackingEnabled = true;
        }
    }

    private void OnDestroy()
    {
        if (RhythmGameController.Instance != null)
            RhythmGameController.Instance.OnGameStateChanged -= OnGameStateChanged;
    }

    private void OnGameStateChanged(RhythmGameController.GameState state)
    {
        trackingEnabled = state == RhythmGameController.GameState.Playing
            || state == RhythmGameController.GameState.Paused;
        if (!trackingEnabled)
            IsTracking = false;
    }

    private void Update()
    {
        if (!trackingEnabled || cameraManager == null || arCamera == null)
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
            DownscaledWidth = outW;
            DownscaledHeight = outH;

            // Compute image→screen mapping accounting for aspect ratio mismatch.
            // The AR camera fills the screen (ScaleToFill) so one axis fits exactly
            // and the other is cropped symmetrically.
            float imgAspect = (float)image.width / image.height;
            float screenAspect = (float)Screen.width / Screen.height;

            if (screenAspect > imgAspect)
            {
                // Screen is wider than image → image width fits, height is cropped
                imgToScreenScaleX = (float)Screen.width / outW;
                imgToScreenScaleY = imgToScreenScaleX;
                imgToScreenOffsetX = 0f;
                imgToScreenOffsetY = (Screen.height - outH * imgToScreenScaleY) * 0.5f;
            }
            else
            {
                // Screen is taller than image → image height fits, width is cropped
                imgToScreenScaleY = (float)Screen.height / outH;
                imgToScreenScaleX = imgToScreenScaleY;
                imgToScreenOffsetX = (Screen.width - outW * imgToScreenScaleX) * 0.5f;
                imgToScreenOffsetY = 0f;
            }

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
                    tipScreen = ImageToScreen(tipScreen);

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
        {
            BlobPixelCount = 0;
            return false;
        }

        var best = bestBlob.Value;
        Vector2 centroid = best.Centroid;
        BlobPixelCount = best.pixels.Count;
        BlobCentroidScreen = ImageToScreen(centroid);

        Vector2 rectMin = ImageToScreen(new Vector2(best.minX, best.minY));
        Vector2 rectMax = ImageToScreen(new Vector2(best.maxX + 1, best.maxY + 1));
        BlobScreenRect = new Rect(rectMin.x, rectMin.y, rectMax.x - rectMin.x, rectMax.y - rectMin.y);

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

        // 5. Find base: furthest from tip (opposite end of baton)
        Vector2 baseEnd = centroid;
        float maxDistFromTipSq = 0f;
        foreach (var p in best.pixels)
        {
            float dSq = (new Vector2(p.x, p.y) - tip).sqrMagnitude;
            if (dSq > maxDistFromTipSq)
            {
                maxDistFromTipSq = dSq;
                baseEnd = new Vector2(p.x, p.y);
            }
        }

        // Extend the tip by 1.5x the length of the green blob to account for the un-taped part of the stick
        tip = tip + (tip - baseEnd) * 1.5f;

        BlobTipScreen = ImageToScreen(tip);
        BlobBaseScreen = ImageToScreen(baseEnd);

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

    private Vector2 ImageToScreen(Vector2 imgPos)
    {
        return new Vector2(
            imgPos.x * imgToScreenScaleX + imgToScreenOffsetX,
            imgPos.y * imgToScreenScaleY + imgToScreenOffsetY);
    }

    private Vector3 ScreenToWorld(Vector2 screenPos)
    {
        Ray ray = arCamera.ScreenPointToRay(screenPos);
        return ray.origin + ray.direction * estimatedDepth;
    }

    private Texture2D debugTex;

    private void OnGUI()
    {
        if (!showCalibrator) return;

        float dpi = Screen.dpi > 0 ? Screen.dpi : 160f;
        float scale = Mathf.Max(4f, dpi / 18f);
        GUI.matrix = Matrix4x4.TRS(Vector2.zero, Quaternion.identity, Vector2.one * scale);

        float sw = Screen.width / scale;
        float sh = Screen.height / scale;
        float panelW = Mathf.Min(sw - 8, 260);
        float panelH = Mathf.Min(sh - 8, 300);
        GUILayout.BeginArea(new Rect(4, 4, panelW, panelH), GUI.skin.box);
        GUILayout.Label("HSV Calibrator");
        GUILayout.Space(2);

        GUILayout.Label($"Green px: {lastGreenPixelCount}  Blobs: {lastBlobCount}");
        GUILayout.Label(IsTracking ? "TRACKING" : "NOT TRACKING");
        GUILayout.Space(4);

        GUILayout.Label($"Hue: {hueMin:F2} - {hueMax:F2}");
        GUILayout.BeginHorizontal();
        hueMin = GUILayout.HorizontalSlider(hueMin, 0f, 1f, GUILayout.Width(panelW * 0.4f));
        hueMax = GUILayout.HorizontalSlider(hueMax, 0f, 1f, GUILayout.Width(panelW * 0.4f));
        GUILayout.EndHorizontal();

        GUILayout.Label($"Sat min: {saturationMin:F2}");
        saturationMin = GUILayout.HorizontalSlider(saturationMin, 0f, 1f);

        GUILayout.Label($"Val min: {valueMin:F2}");
        valueMin = GUILayout.HorizontalSlider(valueMin, 0f, 1f);

        GUILayout.Label($"Green dom: {greenDominanceMin:F2}");
        greenDominanceMin = GUILayout.HorizontalSlider(greenDominanceMin, 0f, 0.5f);

        GUILayout.Label($"Min px: {minGreenPixels}");
        minGreenPixels = (int)GUILayout.HorizontalSlider(minGreenPixels, 10, 200);

        GUILayout.Space(4);
        if (GUILayout.Button("Reset defaults", GUILayout.Height(30)))
        {
            hueMin = 0.22f;
            hueMax = 0.45f;
            saturationMin = 0.45f;
            valueMin = 0.4f;
            greenDominanceMin = 0.1f;
            minGreenPixels = 40;
        }

        GUILayout.EndArea();

        // Reset matrix for debug overlay drawn in actual screen coords
        GUI.matrix = Matrix4x4.identity;

        if (IsTracking)
        {
            if (debugTex == null)
            {
                debugTex = new Texture2D(1, 1);
                debugTex.SetPixel(0, 0, Color.white);
                debugTex.Apply();
            }

            // Draw crosshairs at tip and base in GUI coords (y flipped from screen coords)
            float guiTipY = Screen.height - BlobTipScreen.y;
            float guiBaseY = Screen.height - BlobBaseScreen.y;

            // Tip = magenta cross
            GUI.color = Color.magenta;
            GUI.DrawTexture(new Rect(BlobTipScreen.x - 15, guiTipY - 2, 30, 4), debugTex);
            GUI.DrawTexture(new Rect(BlobTipScreen.x - 2, guiTipY - 15, 4, 30), debugTex);

            // Base = cyan cross
            GUI.color = Color.cyan;
            GUI.DrawTexture(new Rect(BlobBaseScreen.x - 15, guiBaseY - 2, 30, 4), debugTex);
            GUI.DrawTexture(new Rect(BlobBaseScreen.x - 2, guiBaseY - 15, 4, 30), debugTex);

            // Bounding rect
            GUI.color = new Color(1, 1, 0, 0.5f);
            float guiRectY = Screen.height - BlobScreenRect.y - BlobScreenRect.height;
            GUI.DrawTexture(new Rect(BlobScreenRect.x, guiRectY, BlobScreenRect.width, 2), debugTex);
            GUI.DrawTexture(new Rect(BlobScreenRect.x, guiRectY + BlobScreenRect.height, BlobScreenRect.width, 2), debugTex);
            GUI.DrawTexture(new Rect(BlobScreenRect.x, guiRectY, 2, BlobScreenRect.height), debugTex);
            GUI.DrawTexture(new Rect(BlobScreenRect.x + BlobScreenRect.width, guiRectY, 2, BlobScreenRect.height), debugTex);

            GUI.color = Color.white;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (cameraManager == null)
            cameraManager = FindObjectOfType<ARCameraManager>();
    }
#endif
}
