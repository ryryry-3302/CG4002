using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrchestraMaestro
{
    /// <summary>
    /// Queue-based OnGUI tutorial dialog system. Shows messages with Continue button.
    /// </summary>
    public class TutorialDialogController : MonoBehaviour
    {
        public static TutorialDialogController Instance { get; private set; }

        private struct DialogItem
        {
            public string message;
            public Action onContinue;
        }

        private Queue<DialogItem> queue = new Queue<DialogItem>();
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;
        private GUIStyle buttonStyle;
        private bool stylesInitialized;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Show a dialog. Callback invoked when user clicks Continue.</summary>
        public void Show(string message, Action onContinue = null)
        {
            queue.Enqueue(new DialogItem { message = message, onContinue = onContinue });
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;
            stylesInitialized = true;

            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = MakeTex(1, 1, new Color(0.05f, 0.05f, 0.15f, 0.95f));
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
            if (queue.Count == 0) return;

            InitStyles();
            GUI.depth = -100; // Draw on top of all other OnGUI elements
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * 3);

            var item = queue.Peek();
            float panelW = 260f;
            float panelH = 140f;
            float x = (Screen.width / 3f - panelW) / 2f;
            float y = (Screen.height / 3f - panelH) / 2f;

            GUILayout.BeginArea(new Rect(x, y, panelW, panelH), boxStyle);
            GUILayout.Label(item.message, labelStyle, GUILayout.Height(70));
            GUILayout.Space(8);
            if (GUILayout.Button("Continue", buttonStyle, GUILayout.Height(32)))
            {
                queue.Dequeue();
                item.onContinue?.Invoke();
            }
            GUILayout.EndArea();

            GUI.depth = 0; // Reset so other UI isn't affected
        }
    }
}
