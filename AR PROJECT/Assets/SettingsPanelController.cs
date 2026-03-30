using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Wires up a manually-created Settings panel (like MainPanel/ModeSelectionPanel).
/// Add to SettingsPanel and assign the references in the Inspector.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SettingsPanelController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MainMenuController mainMenuController;
    [SerializeField] private Toggle toggleTestMode;
    [SerializeField] private Toggle toggleAutoPlace;
    [SerializeField] private Button btnEasy;
    [SerializeField] private Button btnMedium;
    [SerializeField] private Button btnHard;
    [SerializeField] private Button btnCalibrateGlove;
    [SerializeField] private Button btnBack;

    private void Awake()
    {
        if (mainMenuController == null)
            mainMenuController = FindObjectOfType<MainMenuController>();
    }

    private void Start()
    {
        gameObject.SetActive(false);

        if (toggleTestMode != null)
            toggleTestMode.onValueChanged.AddListener(v => GameSettings.TestMode = v);
        if (toggleAutoPlace != null)
            toggleAutoPlace.onValueChanged.AddListener(v => GameSettings.AutoPlace = v);

        // Clear any Inspector-assigned onClick (e.g. from duplicating Back button) so difficulty only updates selection
        if (btnEasy != null) { btnEasy.onClick.RemoveAllListeners(); btnEasy.onClick.AddListener(() => { GameSettings.DifficultyLevel = Difficulty.Easy; UpdateDifficultyButtons(); }); }
        if (btnMedium != null) { btnMedium.onClick.RemoveAllListeners(); btnMedium.onClick.AddListener(() => { GameSettings.DifficultyLevel = Difficulty.Medium; UpdateDifficultyButtons(); }); }
        if (btnHard != null) { btnHard.onClick.RemoveAllListeners(); btnHard.onClick.AddListener(() => { GameSettings.DifficultyLevel = Difficulty.Hard; UpdateDifficultyButtons(); }); }
        if (btnCalibrateGlove != null) { btnCalibrateGlove.onClick.RemoveAllListeners(); btnCalibrateGlove.onClick.AddListener(OnCalibrateGloveClicked); }
        if (btnBack != null) { btnBack.onClick.RemoveAllListeners(); btnBack.onClick.AddListener(OnBackClicked); }
    }

    public void Show()
    {
        gameObject.SetActive(true);
        SyncFromGameSettings();
        StartCoroutine(ReapplyColorsNextFrame());
    }

    private System.Collections.IEnumerator ReapplyColorsNextFrame()
    {
        yield return null; // Wait one frame so we override anything that ran after us (e.g. theme)
        UpdateDifficultyButtons();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void SyncFromGameSettings()
    {
        if (toggleTestMode != null) toggleTestMode.isOn = GameSettings.TestMode;
        if (toggleAutoPlace != null) toggleAutoPlace.isOn = GameSettings.AutoPlace;
        UpdateDifficultyButtons();
    }

    private void UpdateDifficultyButtons()
    {
        var cNormal = Color.white;
        var cSelected = Color.yellow;

        void SetButtonText(Button btn, bool selected)
        {
            if (btn == null) return;
            var tmp = btn.GetComponentInChildren<TMP_Text>();
            if (tmp != null) tmp.color = selected ? cSelected : cNormal;
        }

        SetButtonText(btnEasy, GameSettings.DifficultyLevel == Difficulty.Easy);
        SetButtonText(btnMedium, GameSettings.DifficultyLevel == Difficulty.Medium);
        SetButtonText(btnHard, GameSettings.DifficultyLevel == Difficulty.Hard);
    }

    private void OnCalibrateGloveClicked()
    {
        Debug.Log("[SettingsPanelController] Manual calibration requested");
        
        // Ensure CalibrationController exists
        if (OrchestraMaestro.CalibrationController.Instance == null)
        {
            GameObject calObj = new GameObject("CalibrationController");
            calObj.AddComponent<OrchestraMaestro.CalibrationController>();
        }
        
        // Check if MQTT is available
        if (OrchestraMaestro.MQTTManager.Instance == null)
        {
            Debug.LogWarning("[SettingsPanelController] Cannot calibrate - MQTT not available");
            // TODO: Show error message to user
            return;
        }
        
        if (!OrchestraMaestro.MQTTManager.Instance.IsConnected)
        {
            Debug.LogWarning("[SettingsPanelController] Cannot calibrate - MQTT not connected");
            // TODO: Show error message to user
            return;
        }
        
        // Subscribe to completion event
        OrchestraMaestro.CalibrationController.Instance.OnCalibrationComplete -= OnManualCalibrationComplete;
        OrchestraMaestro.CalibrationController.Instance.OnCalibrationComplete += OnManualCalibrationComplete;
        
        // Hide settings panel during calibration
        Hide();
        
        // Start calibration
        OrchestraMaestro.CalibrationController.Instance.StartCalibration();
    }
    
    private void OnManualCalibrationComplete()
    {
        Debug.Log("[SettingsPanelController] Manual calibration complete");
        
        // Unsubscribe
        if (OrchestraMaestro.CalibrationController.Instance != null)
        {
            OrchestraMaestro.CalibrationController.Instance.OnCalibrationComplete -= OnManualCalibrationComplete;
        }
        
        // Save calibration status
        PlayerPrefs.SetInt("HasCalibratedLeftGlove", 1);
        PlayerPrefs.Save();
        
        // Return to settings panel
        Show();
    }

    private void OnBackClicked()
    {
        Hide();
        mainMenuController?.OnSettingsBackClicked();
    }
}
