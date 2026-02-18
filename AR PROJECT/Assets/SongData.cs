using UnityEngine;

namespace OrchestraMaestro
{
    [CreateAssetMenu(fileName = "SongData", menuName = "Orchestra/Song Data")]
    public class SongData : ScriptableObject
    {
        [Header("Metadata")]
        public string songName;
        public string artistName;
        
        [Header("Audio")]
        public AudioClip audioClip;
        [Tooltip("Beats per minute of the track")]
        public float bpm = 120f;
        [Tooltip("Time offset in seconds where the first beat starts")]
        public float offset = 0f;

        [Header("Rhythm Map")]
        [Tooltip("JSON file containing rhythm cues")]
        public TextAsset rhythmMapJson;
    }
}