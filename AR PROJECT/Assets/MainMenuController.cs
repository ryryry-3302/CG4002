using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using OrchestraMaestro;

public class MainMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject modeSelectionPanel;
    [SerializeField] private SettingsPanelController settingsPanel;

    [Header("Settings")]
    [SerializeField] private Button settingsButton;

    [Header("Scene Settings")]
    [SerializeField] private string gameSceneName = "SampleScene";

    private bool showLeaderboardOverlay;
    private int leaderboardSongIndex;
    private Vector2 leaderboardScrollPos;

    private bool leaderboardStylesInitialized;
    private Texture2D leaderboardDimBg;
    private Texture2D leaderboardPanelBg;
    private Texture2D leaderboardButtonBg;
    private Texture2D leaderboardButtonHover;
    private Texture2D leaderboardRowAltBg;
    private GUIStyle leaderboardHeaderStyle;
    private GUIStyle leaderboardSongStyle;
    private GUIStyle leaderboardLabelStyle;
    private GUIStyle leaderboardButtonStyle;
    private GUIStyle leaderboardRowStyle;
    private GUIStyle leaderboardAltRowStyle;
    private bool cachedMainPanelActive;
    private bool cachedModePanelActive;
    private bool cachedSettingsPanelActive;
    private bool hasCachedPanelState;

    private void Start()
    {
        if (mainPanel != null) mainPanel.SetActive(true);
        if (modeSelectionPanel != null) modeSelectionPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.Hide();

        if (settingsButton == null)
        {
            var allButtons = FindObjectsOfType<Button>(true);
            foreach (var b in allButtons)
                if (b.name.Contains("Settings")) { settingsButton = b; break; }
        }
        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettingsButtonClicked);

        if (settingsPanel == null)
            settingsPanel = FindObjectOfType<SettingsPanelController>(true);
    }

    public void OnPlayButtonClicked()
    {
        if (showLeaderboardOverlay)
            CloseLeaderboardOverlay();

        if (mainPanel != null) mainPanel.SetActive(false);
        if (modeSelectionPanel != null) modeSelectionPanel.SetActive(true);
    }

    public void OnBackButtonClicked()
    {
        if (showLeaderboardOverlay)
            CloseLeaderboardOverlay();

        if (modeSelectionPanel != null) modeSelectionPanel.SetActive(false);
        if (mainPanel != null) mainPanel.SetActive(true);
    }

    public void OnSettingsButtonClicked()
    {
        if (showLeaderboardOverlay)
            CloseLeaderboardOverlay();

        if (mainPanel != null) mainPanel.SetActive(false);
        if (modeSelectionPanel != null) modeSelectionPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.Show();
    }

    public void OnSettingsBackClicked()
    {
        if (settingsPanel != null) settingsPanel.Hide();
        if (mainPanel != null) mainPanel.SetActive(true);
    }

    public void OnLeaderboardButtonClicked()
    {
        if (showLeaderboardOverlay)
        {
            CloseLeaderboardOverlay();
            return;
        }

        showLeaderboardOverlay = true;
        leaderboardScrollPos = Vector2.zero;

        cachedMainPanelActive = mainPanel != null && mainPanel.activeInHierarchy;
        cachedModePanelActive = modeSelectionPanel != null && modeSelectionPanel.activeInHierarchy;
        cachedSettingsPanelActive = settingsPanel != null && settingsPanel.gameObject.activeInHierarchy;
        hasCachedPanelState = true;

        if (mainPanel != null) mainPanel.SetActive(false);
        if (modeSelectionPanel != null) modeSelectionPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.Hide();
    }

    public void OnTutorialClicked()
    {
        GameSettings.CurrentMode = GameMode.Tutorial;
        LoadGame();
    }

    public void OnRegularPlayClicked()
    {
        GameSettings.CurrentMode = GameMode.Regular;
        LoadGame();
    }

    public void OnCheatsPlayClicked()
    {
        GameSettings.CurrentMode = GameMode.Cheats;
        LoadGame();
    }

    private void LoadGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    private void OnGUI()
    {
        if (!ShouldShowMainMenuLeaderboard()) return;

        InitLeaderboardStyles();

        leaderboardHeaderStyle.fontSize = 36;
        leaderboardSongStyle.fontSize = 24;
        leaderboardLabelStyle.fontSize = 22;
        leaderboardButtonStyle.fontSize = 20;

        if (!showLeaderboardOverlay) return;

        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), leaderboardDimBg);

        List<string> songKeys = LeaderboardService.GetSongKeys();
        if (songKeys.Count == 0) leaderboardSongIndex = 0;
        else leaderboardSongIndex = Mathf.Clamp(leaderboardSongIndex, 0, songKeys.Count - 1);

        float panelW = Screen.width - 24f;
        float panelH = Screen.height - 36f;
        panelW = Mathf.Min(panelW, Screen.width - 16f);
        panelH = Mathf.Min(panelH, Screen.height - 20f);
        float panelX = (Screen.width - panelW) * 0.5f;
        float panelY = Mathf.Max(10f, (Screen.height - panelH) * 0.5f);

        float columnContentWidth = panelW - 32f;
        float rankWidth = columnContentWidth * 0.18f;
        float nameWidth = columnContentWidth * 0.42f;
        float scoreWidth = columnContentWidth * 0.30f;

        GUILayout.BeginArea(new Rect(panelX, panelY, panelW, panelH), GUIContent.none, leaderboardRowStyle);
        GUI.DrawTexture(new Rect(0f, 0f, panelW, panelH), leaderboardPanelBg);

        GUILayout.Space(10);
        GUILayout.Label("ARCADE LEADERBOARDS", leaderboardHeaderStyle);
        GUILayout.Space(6);

        if (songKeys.Count == 0)
        {
            GUILayout.Space(16);
            GUILayout.Label("No scores saved yet.", leaderboardLabelStyle);
        }
        else
        {
            GUILayout.BeginHorizontal();
            float navButtonW = 84f;
            float navButtonH = 48f;
            if (GUILayout.Button("◀", leaderboardButtonStyle, GUILayout.Width(navButtonW), GUILayout.Height(navButtonH)))
            {
                leaderboardSongIndex = (leaderboardSongIndex - 1 + songKeys.Count) % songKeys.Count;
                leaderboardScrollPos = Vector2.zero;
            }

            string key = songKeys[leaderboardSongIndex];
            string songName = LeaderboardService.GetSongDisplayNameForKey(key);
            GUILayout.Label(songName, leaderboardSongStyle, GUILayout.Height(navButtonH));

            if (GUILayout.Button("▶", leaderboardButtonStyle, GUILayout.Width(navButtonW), GUILayout.Height(navButtonH)))
            {
                leaderboardSongIndex = (leaderboardSongIndex + 1) % songKeys.Count;
                leaderboardScrollPos = Vector2.zero;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("RANK", leaderboardLabelStyle, GUILayout.Width(rankWidth));
            GUILayout.Label("NAME", leaderboardLabelStyle, GUILayout.Width(nameWidth));
            GUILayout.Label("SCORE", leaderboardLabelStyle, GUILayout.Width(scoreWidth));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            List<LeaderboardEntry> entries = LeaderboardService.GetEntries(key);
            float listHeight = Mathf.Max(240f, panelH - 250f);
            leaderboardScrollPos = GUILayout.BeginScrollView(leaderboardScrollPos, GUILayout.Height(listHeight));
            for (int i = 0; i < entries.Count; i++)
            {
                LeaderboardEntry entry = entries[i];
                GUIStyle rowStyle = (i % 2 == 0) ? leaderboardRowStyle : leaderboardAltRowStyle;
                float rowHeight = 42f;
                GUILayout.BeginHorizontal(rowStyle, GUILayout.Height(rowHeight));
                GUILayout.Label((i + 1).ToString("D2"), leaderboardLabelStyle, GUILayout.Width(rankWidth));
                GUILayout.Label(entry.playerName, leaderboardLabelStyle, GUILayout.Width(nameWidth));
                GUILayout.Label(entry.score.ToString("N0"), leaderboardLabelStyle, GUILayout.Width(scoreWidth));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        GUILayout.Space(12);
        if (GUILayout.Button("Back", leaderboardButtonStyle, GUILayout.Height(52f)))
        {
            CloseLeaderboardOverlay();
        }

        GUILayout.EndArea();
    }

    private void InitLeaderboardStyles()
    {
        if (leaderboardStylesInitialized) return;
        leaderboardStylesInitialized = true;

        leaderboardDimBg = new Texture2D(1, 1);
        leaderboardDimBg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
        leaderboardDimBg.Apply();

        leaderboardPanelBg = new Texture2D(1, 1);
        leaderboardPanelBg.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.12f, 0.94f));
        leaderboardPanelBg.Apply();

        leaderboardButtonBg = new Texture2D(1, 1);
        leaderboardButtonBg.SetPixel(0, 0, new Color(0.22f, 0.24f, 0.42f, 0.95f));
        leaderboardButtonBg.Apply();

        leaderboardButtonHover = new Texture2D(1, 1);
        leaderboardButtonHover.SetPixel(0, 0, new Color(0.3f, 0.33f, 0.58f, 0.95f));
        leaderboardButtonHover.Apply();

        leaderboardRowAltBg = new Texture2D(1, 1);
        leaderboardRowAltBg.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.06f));
        leaderboardRowAltBg.Apply();

        leaderboardHeaderStyle = new GUIStyle(GUI.skin.label);
        leaderboardHeaderStyle.fontSize = 22;
        leaderboardHeaderStyle.fontStyle = FontStyle.Bold;
        leaderboardHeaderStyle.alignment = TextAnchor.MiddleCenter;
        leaderboardHeaderStyle.normal.textColor = new Color(0.95f, 0.96f, 1f);

        leaderboardSongStyle = new GUIStyle(GUI.skin.label);
        leaderboardSongStyle.fontSize = 16;
        leaderboardSongStyle.fontStyle = FontStyle.Bold;
        leaderboardSongStyle.alignment = TextAnchor.MiddleCenter;
        leaderboardSongStyle.normal.textColor = new Color(0.8f, 0.9f, 1f);

        leaderboardLabelStyle = new GUIStyle(GUI.skin.label);
        leaderboardLabelStyle.fontSize = 14;
        leaderboardLabelStyle.fontStyle = FontStyle.Bold;
        leaderboardLabelStyle.alignment = TextAnchor.MiddleLeft;
        leaderboardLabelStyle.normal.textColor = new Color(0.93f, 0.95f, 1f);

        leaderboardButtonStyle = new GUIStyle(GUI.skin.button);
        leaderboardButtonStyle.fontSize = 13;
        leaderboardButtonStyle.fontStyle = FontStyle.Bold;
        leaderboardButtonStyle.normal.background = leaderboardButtonBg;
        leaderboardButtonStyle.hover.background = leaderboardButtonHover;
        leaderboardButtonStyle.active.background = leaderboardButtonHover;
        leaderboardButtonStyle.normal.textColor = Color.white;
        leaderboardButtonStyle.hover.textColor = Color.white;
        leaderboardButtonStyle.active.textColor = Color.white;
        leaderboardButtonStyle.padding = new RectOffset(10, 10, 8, 8);

        leaderboardRowStyle = new GUIStyle(GUI.skin.box);
        leaderboardRowStyle.normal.background = Texture2D.blackTexture;
        leaderboardRowStyle.normal.textColor = Color.white;
        leaderboardRowStyle.padding = new RectOffset(12, 12, 6, 6);
        leaderboardRowStyle.margin = new RectOffset(0, 0, 0, 0);

        leaderboardAltRowStyle = new GUIStyle(leaderboardRowStyle);
        leaderboardAltRowStyle.normal.background = leaderboardRowAltBg;
    }

    private bool ShouldShowMainMenuLeaderboard()
    {
        if (showLeaderboardOverlay)
            return true;

        if (mainPanel == null || !mainPanel.activeInHierarchy) return false;
        if (modeSelectionPanel != null && modeSelectionPanel.activeInHierarchy) return false;
        if (settingsPanel != null && settingsPanel.gameObject.activeInHierarchy) return false;
        return true;
    }

    private void CloseLeaderboardOverlay()
    {
        showLeaderboardOverlay = false;

        if (!hasCachedPanelState)
        {
            if (mainPanel != null) mainPanel.SetActive(true);
            if (modeSelectionPanel != null) modeSelectionPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.Hide();
            return;
        }

        if (mainPanel != null) mainPanel.SetActive(cachedMainPanelActive);
        if (modeSelectionPanel != null) modeSelectionPanel.SetActive(cachedModePanelActive);

        if (settingsPanel != null)
        {
            if (cachedSettingsPanelActive) settingsPanel.Show();
            else settingsPanel.Hide();
        }

        hasCachedPanelState = false;
    }
}
