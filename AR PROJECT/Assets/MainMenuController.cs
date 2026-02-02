using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject modeSelectionPanel;

    [Header("Scene Settings")]
    // The name of the scene where the game actually happens
    [SerializeField] private string gameSceneName = "SampleScene";

    private void Start()
    {
        // Ensure correct start state
        if (mainPanel != null) mainPanel.SetActive(true);
        if (modeSelectionPanel != null) modeSelectionPanel.SetActive(false);
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

    public void OnTutorialClicked()
    {
        GameSettings.CurrentMode = GameMode.Tutorial;
        Debug.Log("Starting Tutorial Mode");
        LoadGame();
    }

    public void OnRegularPlayClicked()
    {
        GameSettings.CurrentMode = GameMode.Regular;
        Debug.Log("Starting Regular Mode");
        LoadGame();
    }

    public void OnCheatsPlayClicked()
    {
        GameSettings.CurrentMode = GameMode.Cheats;
        Debug.Log("Starting Cheats Mode");
        LoadGame();
    }

    private void LoadGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }
}
