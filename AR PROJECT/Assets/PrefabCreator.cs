using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class PrefabCreator : MonoBehaviour
{
    [SerializeField] private GameObject orchestraPrefab;
    [SerializeField] private Vector3 prefabOffset;
    
    private GameObject orchestra;
    private ARTrackedImageManager aRtrackedImageManager;

    private void OnEnable()
    {
        aRtrackedImageManager = gameObject.GetComponent<ARTrackedImageManager>();
        aRtrackedImageManager.trackedImagesChanged += OnImageChanged;
    }

    private void OnDisable()
    {
        if (aRtrackedImageManager != null)
        {
            aRtrackedImageManager.trackedImagesChanged -= OnImageChanged;
        }
    }

    private void OnImageChanged(ARTrackedImagesChangedEventArgs obj)
    {
        foreach (ARTrackedImage image in obj.added)
        {
            orchestra = Instantiate(orchestraPrefab, image.transform);
            orchestra.transform.position += prefabOffset;
        }
    }
}
