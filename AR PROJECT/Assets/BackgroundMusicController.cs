using UnityEngine;

/// <summary>
/// Plays background music in the StartScreen scene.
/// Add to any GameObject (e.g. empty "BackgroundMusic") and assign your clip.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class BackgroundMusicController : MonoBehaviour
{
    [Header("Background Music")]
    [SerializeField] private AudioClip backgroundMusic;
    [SerializeField] private bool loop = true;
    [SerializeField] [Range(0f, 1f)] private float volume = 0.5f;

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = loop;
    }

    private void Start()
    {
        if (backgroundMusic != null)
        {
            audioSource.clip = backgroundMusic;
            audioSource.volume = volume;
            audioSource.Play();
        }
    }
}
