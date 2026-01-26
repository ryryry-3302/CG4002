using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

public class MultiImageTracker : MonoBehaviour
{
    [Header("Prefabs (Must match order of images in library)")]
    [Tooltip("Prefabs in same order as images in your Reference Image Library")]
    public List<GameObject> prefabs = new List<GameObject>();

    private ARTrackedImageManager trackedImageManager;
    private Dictionary<string, GameObject> spawnedPrefabs = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> imageToPrefabMap = new Dictionary<string, GameObject>();

    void Awake()
    {
        // Try to get from this GameObject first, then search the scene
        trackedImageManager = GetComponent<ARTrackedImageManager>();
        if (trackedImageManager == null)
        {
            trackedImageManager = FindObjectOfType<ARTrackedImageManager>();
        }
        
        if (trackedImageManager == null)
        {
            Debug.LogError("MultiImageTracker: No ARTrackedImageManager found! Add one to your XR Origin.");
            return;
        }

        // Use the library already assigned to ARTrackedImageManager
        if (trackedImageManager.referenceLibrary != null)
        {
            Debug.Log($"Using image library with {trackedImageManager.referenceLibrary.count} images");
            BuildImageToPrefabMap();
        }
        else
        {
            Debug.LogWarning("MultiImageTracker: No Reference Image Library assigned to ARTrackedImageManager!");
        }
    }

    private void BuildImageToPrefabMap()
    {
        imageToPrefabMap.Clear();
        
        var library = trackedImageManager.referenceLibrary;
        for (int i = 0; i < library.count; i++)
        {
            string imageName = library[i].name;
            
            if (i < prefabs.Count && prefabs[i] != null)
            {
                imageToPrefabMap[imageName] = prefabs[i];
                Debug.Log($"Mapped image '{imageName}' to prefab '{prefabs[i].name}'");
            }
            else
            {
                Debug.LogWarning($"No prefab assigned for image at index {i}: '{imageName}'");
            }
        }
    }

    void OnEnable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
        }
    }

    void OnDisable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
        }
    }

    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        // Handle newly detected images
        foreach (ARTrackedImage trackedImage in eventArgs.added)
        {
            Debug.Log($"Image Added: {trackedImage.referenceImage.name}");
            SpawnOrUpdatePrefab(trackedImage);
        }

        // Handle updated images (position/rotation changes)
        foreach (ARTrackedImage trackedImage in eventArgs.updated)
        {
            UpdatePrefabTransform(trackedImage);
        }

        // Handle removed images
        foreach (ARTrackedImage trackedImage in eventArgs.removed)
        {
            Debug.Log($"Image Removed: {trackedImage.referenceImage.name}");
            RemovePrefab(trackedImage);
        }
    }

    private void SpawnOrUpdatePrefab(ARTrackedImage trackedImage)
    {
        string imageName = trackedImage.referenceImage.name;

        // Find the prefab for this image
        if (!imageToPrefabMap.TryGetValue(imageName, out GameObject prefabToSpawn))
        {
            Debug.LogWarning($"No prefab assigned for image: {imageName}");
            return;
        }

        // Check if we already spawned a prefab for this image
        if (!spawnedPrefabs.ContainsKey(imageName))
        {
            // Spawn new prefab
            GameObject spawnedObject = Instantiate(prefabToSpawn, trackedImage.transform);
            spawnedObject.name = $"Spawned_{imageName}";
            spawnedPrefabs[imageName] = spawnedObject;
            
            Debug.Log($"Spawned prefab for image: {imageName}");
        }

        UpdatePrefabTransform(trackedImage);
    }

    private void UpdatePrefabTransform(ARTrackedImage trackedImage)
    {
        string imageName = trackedImage.referenceImage.name;

        if (spawnedPrefabs.TryGetValue(imageName, out GameObject spawnedObject))
        {
            if (spawnedObject != null)
            {
                // Show/hide based on tracking state
                bool isTracking = trackedImage.trackingState == TrackingState.Tracking;
                spawnedObject.SetActive(isTracking);
            }
        }
    }

    private void RemovePrefab(ARTrackedImage trackedImage)
    {
        string imageName = trackedImage.referenceImage.name;

        if (spawnedPrefabs.TryGetValue(imageName, out GameObject spawnedObject))
        {
            if (spawnedObject != null)
            {
                Destroy(spawnedObject);
            }
            spawnedPrefabs.Remove(imageName);
        }
    }

}
