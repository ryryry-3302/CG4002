using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class PrefabCreator : MonoBehaviour
{
    [Tooltip("The prefab to spawn when an image is detected")]
    [SerializeField] private GameObject orchestraPrefab;
    
    [Tooltip("Offset position from the center of the image")]
    [SerializeField] private Vector3 prefabOffset = Vector3.zero;

    // Hardcoded rotation to ensure it faces the camera (180 deg)
    // Removed serialized field to prevent build layout errors
    
    private ARTrackedImageManager aRtrackedImageManager;
    private Dictionary<string, GameObject> spawnedObjects = new Dictionary<string, GameObject>();

    private void Awake()
    {
        aRtrackedImageManager = GetComponent<ARTrackedImageManager>();
    }

    private void OnEnable()
    {
        if (aRtrackedImageManager != null)
        {
            aRtrackedImageManager.trackedImagesChanged += OnImageChanged;
        }
    }

    private void OnDisable()
    {
        if (aRtrackedImageManager != null)
        {
            aRtrackedImageManager.trackedImagesChanged -= OnImageChanged;
        }
    }

    private void OnImageChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        Debug.Log($"[PrefabCreator] OnImageChanged called. Added: {eventArgs.added.Count}, Updated: {eventArgs.updated.Count}");

        foreach (ARTrackedImage image in eventArgs.added)
        {
            SpawnOrUpdatePrefab(image);
        }
        
        foreach (ARTrackedImage image in eventArgs.updated)
        {
            // sometimes detection happens late, so we check if we missed spawning it
            if (image.trackingState == TrackingState.Tracking || image.trackingState == TrackingState.Limited)
            {
                SpawnOrUpdatePrefab(image);
            }
            
            // Update visibility based on tracking state
            if (spawnedObjects.TryGetValue(image.referenceImage.name, out GameObject spawnedObject))
            {
                // Show it even if tracking is LIMITED, so we can see if it exists at all
                bool isVisible = image.trackingState == TrackingState.Tracking || image.trackingState == TrackingState.Limited;
                if (spawnedObject.activeSelf != isVisible)
                {
                    spawnedObject.SetActive(isVisible);
                    Debug.Log($"[PrefabCreator] {image.referenceImage.name} visibility set to {isVisible} (State: {image.trackingState})");
                }
            }
        }
        
        foreach (ARTrackedImage image in eventArgs.removed)
        {
            if (spawnedObjects.TryGetValue(image.referenceImage.name, out GameObject spawnedObject))
            {
                Destroy(spawnedObject);
                spawnedObjects.Remove(image.referenceImage.name);
            }
        }
    }

    private void SpawnOrUpdatePrefab(ARTrackedImage image)
    {
        if (orchestraPrefab == null) return;
        
        // If we haven't spawned it yet, do it now
        if (!spawnedObjects.ContainsKey(image.referenceImage.name))
        {
            Debug.Log($"[PrefabCreator] Spawning prefab for {image.referenceImage.name}");
            GameObject newObject = Instantiate(orchestraPrefab, image.transform);
            
            // Set position and rotation relative to the image
            newObject.transform.localPosition = prefabOffset;
            newObject.transform.localScale = new Vector3(1f, 1f, 1f); // Set Scale here (0.1 = 10% size)
            
            // Standard upright rotation for AR (0, 180, 0)
            // If model is still lying down, try (90, 180, 0) or (-90, 180, 0)
            newObject.transform.localRotation = Quaternion.Euler(-90, 0, 180);
            
            spawnedObjects[image.referenceImage.name] = newObject;
        }
    }
}
