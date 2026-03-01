using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        if (mainPanel != null) mainPanel.SetActive(false);
        if (modeSelectionPanel != null) modeSelectionPanel.SetActive(true);
    }

    public void OnBackButtonClicked()
    {
        if (modeSelectionPanel != null) modeSelectionPanel.SetActive(false);
        if (mainPanel != null) mainPanel.SetActive(true);
    }

    public void OnSettingsButtonClicked()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (modeSelectionPanel != null) modeSelectionPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.Show();
    }

    public void OnSettingsBackClicked()
    {
        if (settingsPanel != null) settingsPanel.Hide();
        if (mainPanel != null) mainPanel.SetActive(true);
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
}
