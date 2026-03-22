using UnityEngine;
using System.Collections.Generic;

namespace OrchestraMaestro
{
    /// <summary>
    /// Spawns visual effects or models corresponding to gestures when cues appear.
    /// This is purely for visual feedback and does not affect gameplay mechanics.
    /// </summary>
    public class GestureVisualSpawner : MonoBehaviour
    {
        [System.Serializable]
        public struct GestureVisualMapping
        {
            public GestureType gestureType;
            public GameObject prefab;
            public float visualDuration; // How long the visual lasts
            public Vector3 positionOffset; // Offset from instrument center
            public Vector3 rotationOffset; // Rotation offset
            public float scaleMultiplier;
        }

        [Header("References")]
        [SerializeField] private RhythmMap rhythmMap;
        [SerializeField] private OrchestraPlacement orchestraPlacement;

        [Header("Configuration")]
        [SerializeField] private List<GestureVisualMapping> gestureMappings;
        [Tooltip("Default duration if not specified in mapping")]
        [SerializeField] private float defaultDuration = 2.0f;
        [Tooltip("Height above instrument to spawn")]
        [SerializeField] private float defaultHeightOffset = 1.5f;

        private Dictionary<GestureType, GestureVisualMapping> mappingDict;

        private void Awake()
        {
            // Build dictionary for fast lookup
            mappingDict = new Dictionary<GestureType, GestureVisualMapping>();
            foreach (var mapping in gestureMappings)
            {
                if (!mappingDict.ContainsKey(mapping.gestureType))
                {
                    mappingDict.Add(mapping.gestureType, mapping);
                }
            }
        }

        private void Start()
        {
            if (rhythmMap == null) rhythmMap = FindObjectOfType<RhythmMap>();
            if (orchestraPlacement == null) orchestraPlacement = FindObjectOfType<OrchestraPlacement>();

            if (rhythmMap != null)
            {
                rhythmMap.OnCueApproaching += HandleCueApproaching;
            }
        }

        private void OnDestroy()
        {
            if (rhythmMap != null)
            {
                rhythmMap.OnCueApproaching -= HandleCueApproaching;
            }
        }

        private void HandleCueApproaching(RhythmCue cue)
        {
            // Only spawn if we have a target section and a mapping for this gesture
            if (!cue.targetSection.HasValue) return;
            
            if (mappingDict.TryGetValue(cue.gestureType, out GestureVisualMapping mapping))
            {
                SpawnVisual(mapping, cue.targetSection.Value);
            }
        }

        private void SpawnVisual(GestureVisualMapping mapping, OrchestraSection section)
        {
            if (mapping.prefab == null) return;

            // Determine spawn position
            Vector3 spawnPosition = Vector3.zero;
            bool positionFound = false;

            if (orchestraPlacement != null)
            {
                Vector3? sectionPos = orchestraPlacement.GetSectionPosition(section);
                if (sectionPos.HasValue)
                {
                    spawnPosition = sectionPos.Value;
                    positionFound = true;
                }
            }

            if (!positionFound)
            {
                // Fallback position logic if needed, or just return
                // Debug.LogWarning($"Could not find position for section {section}");
                return; 
            }

            // Apply offsets
            Vector3 finalPosition = spawnPosition + Vector3.up * defaultHeightOffset + mapping.positionOffset;
            Quaternion finalRotation = Quaternion.Euler(mapping.rotationOffset);
            
            // If prefab has a specific rotation, use it combined with offset? 
            // Usually prefabs are identity, so just using offset is fine.
            // But let's respect prefab's rotation + offset.
            finalRotation = mapping.prefab.transform.rotation * Quaternion.Euler(mapping.rotationOffset);

            // Instantiate
            GameObject visual = Instantiate(mapping.prefab, finalPosition, finalRotation);
            
            // Apply scale
            float scale = mapping.scaleMultiplier > 0 ? mapping.scaleMultiplier : 1.0f;
            visual.transform.localScale *= scale;
            
            // Should the visual face the camera?
            // If it's a 2D effect (billboard), the shader handles it.
            // If it's a 3D model, maybe we want it to face the user?
            // For now, let's leave rotation as defined in mapping.
            
            // Destroy after duration
            float duration = mapping.visualDuration > 0 ? mapping.visualDuration : defaultDuration;
            Destroy(visual, duration);
        }
    }
}
