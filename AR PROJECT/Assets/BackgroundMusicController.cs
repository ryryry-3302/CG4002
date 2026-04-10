using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Plays background music in the StartScreen scene.
/// Add to any GameObject (e.g. empty "BackgroundMusic") and assign your clip.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class BackgroundMusicController : MonoBehaviour
{
    private static BackgroundMusicController instance;

    [Header("Background Music")]
    [SerializeField] private AudioClip backgroundMusic;
    [SerializeField] private bool loop = true;
    [SerializeField] [Range(0f, 1f)] private float volume = 0.5f;

    private AudioSource audioSource;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = loop;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Start()
    {
        ApplyScenePlayback(SceneManager.GetActiveScene().name);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyScenePlayback(scene.name);
    }

    private void ApplyScenePlayback(string sceneName)
    {
        bool shouldPlay = ShouldPlayInScene(sceneName);
        if (!shouldPlay)
        {
            if (audioSource.isPlaying)
                audioSource.Stop();
            return;
        }

        if (backgroundMusic == null)
            return;

        if (audioSource.clip != backgroundMusic)
            audioSource.clip = backgroundMusic;

        audioSource.loop = loop;
        audioSource.volume = volume;

        if (!audioSource.isPlaying)
            audioSource.Play();
    }

    private bool ShouldPlayInScene(string sceneName)
    {
        return sceneName == "StartScreen" || sceneName == "LeaderboardScreen";
    }
}
