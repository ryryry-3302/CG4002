using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace OrchestraMaestro
{
    /// <summary>
    /// Queue-based OnGUI tutorial dialog system. Shows messages with an optional looping VideoClip.
    /// </summary>
    public class TutorialDialogController : MonoBehaviour
    {
        public static TutorialDialogController Instance { get; private set; }

        private struct DialogItem
        {
            public string message;
            public Action onContinue;
            public VideoClip videoClip;
            public bool isVideoView;
        }

        private Queue<DialogItem> queue = new Queue<DialogItem>();
        private Queue<string> continueLabelQueue = new Queue<string>();
        private Queue<Texture2D> fallbackImageQueue = new Queue<Texture2D>();
        private bool isShowingVideoMode = false;
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;
        private GUIStyle buttonStyle;
        private bool stylesInitialized;
        
        private VideoPlayer videoPlayer;
        private RenderTexture renderTexture;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            // Setup VideoPlayer for Portrait (9:16)
            // Width 180, Height 320 (common portrait size for previews)
            renderTexture = new RenderTexture(180, 320, 16, RenderTextureFormat.ARGB32);
            renderTexture.Create();

            var go = new GameObject("TutorialVideoPlayer");
            go.transform.SetParent(transform);
            videoPlayer = go.AddComponent<VideoPlayer>();
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = renderTexture;
            videoPlayer.isLooping = true;
            videoPlayer.playOnAwake = false;
            // Ensure audio is muted (we don't want video audio to play)
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            videoPlayer.prepareCompleted += OnVideoPrepared;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (videoPlayer != null)
                videoPlayer.prepareCompleted -= OnVideoPrepared;
            if (renderTexture != null) renderTexture.Release();
        }

        private void OnVideoPrepared(VideoPlayer player)
        {
            if (player != null && !player.isPlaying)
            {
                player.Play();
            }
        }

        /// <summary>Show a dialog. Callback invoked when user clicks the action button.</summary>
        public void Show(string message, Action onContinue = null, VideoClip videoClip = null, string continueLabel = "Continue", Texture2D fallbackImage = null)
        {
            queue.Enqueue(new DialogItem { message = message, onContinue = onContinue, videoClip = videoClip, isVideoView = false });
            continueLabelQueue.Enqueue(string.IsNullOrEmpty(continueLabel) ? "Continue" : continueLabel);
            fallbackImageQueue.Enqueue(fallbackImage);
        }

        public void ShowVideo(string message, VideoClip videoClip, Action onProceed)
        {
            queue.Enqueue(new DialogItem { message = message, onContinue = onProceed, videoClip = videoClip, isVideoView = true });
            continueLabelQueue.Enqueue("Proceed");
            fallbackImageQueue.Enqueue(null);
        }

        public void Clear()
        {
            queue.Clear();
            continueLabelQueue.Clear();
            fallbackImageQueue.Clear();
            if (videoPlayer != null && videoPlayer.isPlaying)
            {
                videoPlayer.Stop();
            }
        }

        public bool IsShowing()
        {
            return queue.Count > 0;
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;
            stylesInitialized = true;

            boxStyle = new GUIStyle(GUI.skin.box);
            // Reduced opacity from 0.95f to 0.75f for better background visibility
            boxStyle.normal.background = MakeTex(1, 1, new Color(0.05f, 0.05f, 0.15f, 0.75f));
            boxStyle.padding = new RectOffset(16, 16, 16, 16);

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 12;
            labelStyle.normal.textColor = new Color(0.9f, 0.9f, 1f);
            labelStyle.wordWrap = true;
            labelStyle.alignment = TextAnchor.MiddleCenter;

            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 12;
            buttonStyle.fontStyle = FontStyle.Bold;
        }

        private static Texture2D MakeTex(int w, int h, Color color)
        {
            var tex = new Texture2D(w, h);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    tex.SetPixel(x, y, color);
            tex.Apply();
            return tex;
        }

        private void OnGUI()
        {
            if (queue.Count == 0) 
            {
                if (videoPlayer.isPlaying) videoPlayer.Stop();
                return;
            }

            InitStyles();
            GUI.depth = -100; // Draw on top of all other OnGUI elements
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * 3);

            var item = queue.Peek();
            string buttonLabel = continueLabelQueue.Count > 0 ? continueLabelQueue.Peek() : "Continue";
            Texture2D fallbackImage = fallbackImageQueue.Count > 0 ? fallbackImageQueue.Peek() : null;
            
            bool hasVideo = item.videoClip != null && item.isVideoView;
            bool hasImage = !hasVideo && fallbackImage != null && !item.isVideoView;
            
            if (hasVideo)
            {
                if (videoPlayer.clip != item.videoClip)
                {
                    videoPlayer.clip = item.videoClip;
                    videoPlayer.Prepare();
                }
                else if (!videoPlayer.isPlaying && videoPlayer.isPrepared)
                {
                    videoPlayer.Play();
                }
            }
            else
            {
                if (videoPlayer.isPlaying) videoPlayer.Stop();
            }

            float panelW = 280f;
            // Reduced Height from 420f to 380f to fit portrait video better without excess space
            float panelH = item.isVideoView ? 380f : (hasImage ? 280f : 180f);
            float x = (Screen.width / 3f - panelW) / 2f;
            float y = (Screen.height / 3f - panelH) / 2f;

            GUILayout.BeginArea(new Rect(x, y, panelW, panelH), boxStyle);
            
            if (hasVideo || hasImage)
            {
                // Align to center roughly
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                
                // Adjust video rectangle for portrait (9:16 aspect ratio)
                // Width 160 -> Height 284
                float videoW = 160f;
                float videoH = item.isVideoView ? 284f : 160f;
                
                Rect videoRect = GUILayoutUtility.GetRect(videoW, videoH, GUILayout.Width(videoW), GUILayout.Height(videoH));
                if (hasVideo)
                    GUI.DrawTexture(videoRect, renderTexture, ScaleMode.ScaleToFit);
                else if (hasImage)
                    GUI.DrawTexture(videoRect, fallbackImage, ScaleMode.ScaleToFit);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                
                GUILayout.Space(4);
            }
            
            // Only show message if NOT in specifically designated video view mode
            if (!item.isVideoView)
            {
                GUILayout.Label(item.message, labelStyle, GUILayout.ExpandHeight(true));
                GUILayout.Space(8);
            }
            else
            {
                // In video view, we just want the video and the proceed button
                GUILayout.FlexibleSpace();
            }

            if (!item.isVideoView && item.videoClip != null)
            {
                if (GUILayout.Button("Watch Video", buttonStyle, GUILayout.Height(32)))
                {
                    // Action for viewing video: Show current clip in a video-specific dialog
                    string msg = item.message;
                    var clip = item.videoClip;
                    var onCont = item.onContinue;
                    
                    // Dequeue current one and replace with video version
                    queue.Dequeue();
                    continueLabelQueue.Dequeue();
                    fallbackImageQueue.Dequeue();
                    
                    ShowVideo(msg, clip, onCont);
                }
                GUILayout.Space(4);
            }

            if (GUILayout.Button(buttonLabel, buttonStyle, GUILayout.Height(32)))
            {
                queue.Dequeue();
                if (continueLabelQueue.Count > 0) continueLabelQueue.Dequeue();
                if (fallbackImageQueue.Count > 0) fallbackImageQueue.Dequeue();
                item.onContinue?.Invoke();
            }
            GUILayout.EndArea();

            GUI.depth = 0; // Reset so other UI isn't affected
        }
    }
}
