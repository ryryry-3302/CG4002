using UnityEngine;
using UnityEngine.Audio;

namespace OrchestraMaestro
{
    /// <summary>
    /// Controls the audio mixer for Orchestra Maestro.
    /// Manages per-section volume and effects based on conducting gestures.
    /// 
    /// Setup required:
    /// 1. Create an AudioMixer asset in Unity
    /// 2. Create 4 AudioMixerGroups: Strings, Woodwinds, Brass, Percussion
    /// 3. Expose volume parameters: StringsVolume, WoodwindsVolume, BrassVolume, PercussionVolume
    /// 4. Assign the mixer to this component
    /// </summary>
    public class AudioMixerController : MonoBehaviour
    {
        [Header("Audio Mixer")]
        public AudioMixer audioMixer;

        [Header("Volume Settings")]
        [SerializeField] private float minVolume = -40f;  // dB
        [SerializeField] private float maxVolume = 0f;    // dB
        [SerializeField] private float defaultVolume = -10f; // dB
        [SerializeField] private float volumeStep = 3f;   // dB per gesture

        [Header("Effect Settings")]
        [SerializeField] private float accentBoost = 6f;  // dB boost for accent
        [SerializeField] private float accentDuration = 0.2f;
        [SerializeField] private float cutoffVolume = -80f; // Essentially mute
        [SerializeField] private float cutoffFadeDuration = 0.3f;

        [Header("Exposed Parameter Names")]
        [SerializeField] private string drumVolumeParam = "DrumVolume";
        [SerializeField] private string fluteVolumeParam = "FluteVolume";
        [SerializeField] private string pipeVolumeParam = "PipeVolume";
        [SerializeField] private string xylophoneVolumeParam = "XylophoneVolume";

        // Current volume levels (before effects)
        private float[] sectionVolumes = new float[4];

        // Active coroutines for effects
        private Coroutine[] activeEffects = new Coroutine[4];

        // Singleton
        public static AudioMixerController Instance { get; private set; }

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Auto-load mixer if not assigned
            if (audioMixer == null)
            {
                audioMixer = Resources.Load<UnityEngine.Audio.AudioMixer>("OrchestraMixer");
                if (audioMixer != null)
                {
                    Debug.Log("[AudioMixerController] Loaded OrchestraMixer from Resources");
                }
                else
                {
                    Debug.LogWarning("[AudioMixerController] No AudioMixer assigned and OrchestraMixer not found in Resources folder. Audio control disabled.");
                }
            }
        }

        private void Start()
        {
            ResetAllVolumes();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        #endregion

        #region Volume Control

        /// <summary>Reset all sections to default volume</summary>
        public void ResetAllVolumes()
        {
            for (int i = 0; i < 4; i++)
            {
                sectionVolumes[i] = defaultVolume;
                ApplyVolume((OrchestraSection)i, defaultVolume);
            }
            Debug.Log("[AudioMixer] All volumes reset to default");
        }

        /// <summary>Get current volume for a section</summary>
        public float GetSectionVolume(OrchestraSection section)
        {
            return sectionVolumes[(int)section];
        }

        /// <summary>Set volume for a section (in dB)</summary>
        public void SetSectionVolume(OrchestraSection section, float volumeDb)
        {
            volumeDb = Mathf.Clamp(volumeDb, minVolume, maxVolume);
            sectionVolumes[(int)section] = volumeDb;
            ApplyVolume(section, volumeDb);
        }

        /// <summary>Increase volume for a section (UP/V_SHAPE gestures)</summary>
        public void IncreaseSectionVolume(OrchestraSection section)
        {
            float currentVolume = sectionVolumes[(int)section];
            float newVolume = Mathf.Min(currentVolume + volumeStep, maxVolume);
            SetSectionVolume(section, newVolume);
            
            Debug.Log($"[AudioMixer] {section} volume increased to {newVolume}dB");
        }

        /// <summary>Decrease volume for a section (DOWN/LAMBDA_SHAPE gestures)</summary>
        public void DecreaseSectionVolume(OrchestraSection section)
        {
            float currentVolume = sectionVolumes[(int)section];
            float newVolume = Mathf.Max(currentVolume - volumeStep, minVolume);
            SetSectionVolume(section, newVolume);
            
            Debug.Log($"[AudioMixer] {section} volume decreased to {newVolume}dB");
        }

        private void ApplyVolume(OrchestraSection section, float volumeDb)
        {
            if (audioMixer == null) return;

            string paramName = GetVolumeParamName(section);
            audioMixer.SetFloat(paramName, volumeDb);
        }

        private string GetVolumeParamName(OrchestraSection section)
        {
            return section switch
            {
                OrchestraSection.Drum => drumVolumeParam,
                OrchestraSection.Flute => fluteVolumeParam,
                OrchestraSection.Pipe => pipeVolumeParam,
                OrchestraSection.Xylophone => xylophoneVolumeParam,
                _ => drumVolumeParam
            };
        }

        #endregion

        #region Effects

        /// <summary>Trigger accent effect (PUNCH/STRONG_ACCENT gestures)</summary>
        public void TriggerAccent(OrchestraSection section)
        {
            int index = (int)section;
            
            // Cancel any existing effect
            if (activeEffects[index] != null)
            {
                StopCoroutine(activeEffects[index]);
            }

            activeEffects[index] = StartCoroutine(AccentEffect(section));
            Debug.Log($"[AudioMixer] Accent triggered on {section}");
        }

        private System.Collections.IEnumerator AccentEffect(OrchestraSection section)
        {
            int index = (int)section;
            float baseVolume = sectionVolumes[index];
            float peakVolume = Mathf.Min(baseVolume + accentBoost, maxVolume);

            // Quick boost
            ApplyVolume(section, peakVolume);

            // Hold briefly
            yield return new WaitForSeconds(accentDuration * 0.3f);

            // Fade back to base
            float elapsed = 0f;
            float fadeDuration = accentDuration * 0.7f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fadeDuration;
                float volume = Mathf.Lerp(peakVolume, baseVolume, t);
                ApplyVolume(section, volume);
                yield return null;
            }

            ApplyVolume(section, baseVolume);
            activeEffects[index] = null;
        }

        /// <summary>Trigger cutoff effect (WITHDRAW/CLEAR_CUTOFF gestures)</summary>
        public void TriggerCutoff(OrchestraSection section)
        {
            int index = (int)section;
            
            // Cancel any existing effect
            if (activeEffects[index] != null)
            {
                StopCoroutine(activeEffects[index]);
            }

            activeEffects[index] = StartCoroutine(CutoffEffect(section));
            Debug.Log($"[AudioMixer] Cutoff triggered on {section}");
        }

        private System.Collections.IEnumerator CutoffEffect(OrchestraSection section)
        {
            int index = (int)section;
            float startVolume = sectionVolumes[index];

            // Quick fade to silence
            float elapsed = 0f;
            while (elapsed < cutoffFadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / cutoffFadeDuration;
                float volume = Mathf.Lerp(startVolume, cutoffVolume, t);
                ApplyVolume(section, volume);
                yield return null;
            }

            ApplyVolume(section, cutoffVolume);
            
            // Update stored volume to cutoff level
            sectionVolumes[index] = cutoffVolume;
            activeEffects[index] = null;
        }

        /// <summary>Trigger bring-out effect (BRING_OUT gesture) - subtle boost</summary>
        public void TriggerBringOut(OrchestraSection section)
        {
            // Slight volume boost that persists
            float currentVolume = sectionVolumes[(int)section];
            float newVolume = Mathf.Min(currentVolume + volumeStep * 0.5f, maxVolume);
            SetSectionVolume(section, newVolume);
            
            Debug.Log($"[AudioMixer] Bring-out on {section}, volume now {newVolume}dB");
        }

        #endregion

        #region Timbre Control (Future)

        // TODO: Implement timbre swapping for S_SHAPE gesture
        // This would require having alternate audio sources for each section
        // with different instrument samples (e.g., strings -> electric guitar)

        /// <summary>Swap timbre for a section (S_SHAPE gesture)</summary>
        public void SwapTimbre(OrchestraSection section)
        {
            // Placeholder - log for now
            string altTimbre = section switch
            {
                OrchestraSection.Drum => "Electronic Drums",
                OrchestraSection.Flute => "Synth Flute",
                OrchestraSection.Pipe => "Synth Pipe",
                OrchestraSection.Xylophone => "Electronic Xylophone",
                _ => "Alternate"
            };

            Debug.Log($"[AudioMixer] Timbre swap requested: {section} -> {altTimbre} (not implemented)");
        }

        #endregion

        #region Tempo Control (Future)

        // TODO: Implement tempo adjustments for TRIANGLE/CIRCLE/HOLD gestures
        // This would require controlling the AudioSource pitch or playback rate

        /// <summary>Adjust tempo (TRIANGLE = faster, CIRCLE = slower)</summary>
        public void AdjustTempo(float multiplier)
        {
            // Placeholder - log for now
            Debug.Log($"[AudioMixer] Tempo adjustment requested: {multiplier}x (not implemented)");
        }

        #endregion
    }
}
