using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Esri.ArcGISMapsSDK.Components;

public class ArcGISCameraCoordinator : MonoBehaviour
{
    private static ArcGISCameraCoordinator instance;
    private ArcGISDroneMapDriver mapDriver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            instance.ApplyCameraPriority();
            return;
        }

        GameObject coordinatorObject =
            new GameObject("ArcGISCameraCoordinator");
        instance = coordinatorObject.AddComponent<ArcGISCameraCoordinator>();
        DontDestroyOnLoad(coordinatorObject);
        instance.ApplyCameraPriority();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyCameraPriority();
    }

    private void OnActiveSceneChanged(Scene previous, Scene current)
    {
        // LV1 is initially loaded additively behind LoadingScene. Re-apply the
        // camera choice after LV1 becomes active so the cold-start map cannot
        // keep the loading camera as its streaming viewpoint.
        ApplyCameraPriority();
    }

    private void ApplyCameraPriority()
    {
        Camera droneCamera = FindDroneCamera();
        Camera minimapCamera = FindMinimapCamera();

        ArcGISCameraComponent droneArcGis =
            droneCamera != null
                ? droneCamera.GetComponent<ArcGISCameraComponent>()
                : null;

        ArcGISCameraComponent minimapArcGis =
            minimapCamera != null
                ? minimapCamera.GetComponent<ArcGISCameraComponent>()
                : null;

        // Keep the ArcGIS camera attached to the camera that renders the Game
        // view. A separate disabled Unity Camera can provide a streaming
        // viewpoint, but it cannot render the map into the visible camera.
        SetArcGisCameraEnabled(minimapArcGis, false, "Minimap Camera");
        if (droneArcGis != null)
        {
            DisableAndRemoveMapDriver();
            SetArcGisCameraEnabled(droneArcGis, true, "DroneCamera");
        }
        else
        {
            Debug.LogWarning(
                "DroneCamera has no ArcGISCameraComponent; map rendering " +
                "cannot be attached to the visible Game camera."
            );
        }

        if (minimapCamera != null)
        {
            // The minimap remains a regular camera rendering only to its texture.
            EnsureRenderTexture(minimapCamera);

            if (!minimapCamera.enabled)
            {
                minimapCamera.enabled = true;
            }

            if (minimapCamera.CompareTag("MainCamera"))
            {
                minimapCamera.tag = "Untagged";
            }

            MinimapCameraFollow minimapFollow =
                minimapCamera.GetComponent<MinimapCameraFollow>();
            if (minimapFollow != null && !minimapFollow.enabled)
            {
                minimapFollow.enabled = true;
            }

            ArcGISLocationComponent fixedLocation =
                minimapCamera.GetComponent<ArcGISLocationComponent>();
            if (fixedLocation != null && fixedLocation.enabled)
            {
                fixedLocation.enabled = false;
            }

            DisableMinimapHpTransform(minimapCamera);
            BindMinimapRawImages(minimapCamera);
        }

        if (droneCamera != null && !droneCamera.enabled)
        {
            droneCamera.enabled = true;
        }

        if (droneCamera != null)
        {
            droneCamera.tag = "MainCamera";
        }

        Debug.Log(
            "🎥 ArcGIS camera priority set. " +
            $"DroneCamera={(droneCamera != null ? droneCamera.name : "missing")} " +
            $"Streaming=visible Game camera " +
            $"ArcGIS={(droneArcGis != null ? droneArcGis.enabled.ToString() : "missing")} | " +
            $"MinimapCamera={(minimapCamera != null ? minimapCamera.name : "missing")} " +
            $"ArcGIS={(minimapArcGis != null ? minimapArcGis.enabled.ToString() : "missing")} " +
            $"Render={(minimapCamera != null && minimapCamera.enabled)}"
        );
    }

    private void DisableAndRemoveMapDriver()
    {
        if (mapDriver == null)
        {
            mapDriver = FindFirstObjectByType<ArcGISDroneMapDriver>(
                FindObjectsInactive.Include
            );
        }

        if (mapDriver == null)
        {
            return;
        }

        ArcGISCameraComponent driverArcGis =
            mapDriver.ArcGisCamera != null
                ? mapDriver.ArcGisCamera
                : mapDriver.GetComponent<ArcGISCameraComponent>();

        SetArcGisCameraEnabled(
            driverArcGis,
            false,
            "unused ArcGIS Drone Map Driver"
        );

        GameObject driverObject = mapDriver.gameObject;
        mapDriver = null;

        if (Application.isPlaying)
        {
            Destroy(driverObject);
        }
        else
        {
            DestroyImmediate(driverObject);
        }
    }

    private static void EnsureRenderTexture(Camera camera)
    {
        if (camera.targetTexture != null)
        {
            return;
        }

        RenderTexture streamingTexture = new RenderTexture(512, 512, 16)
        {
            name = "ArcGIS Streaming Camera Texture",
            hideFlags = HideFlags.DontSave
        };
        streamingTexture.Create();
        camera.targetTexture = streamingTexture;
    }

    private static void DisableMinimapHpTransform(Camera minimapCamera)
    {
        MonoBehaviour[] behaviours =
            minimapCamera.GetComponents<MonoBehaviour>();

        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour != null &&
                behaviour.GetType().FullName ==
                "Esri.HPFramework.HPTransform" &&
                behaviour.enabled)
            {
                behaviour.enabled = false;
            }
        }
    }

    private static void BindMinimapRawImages(Camera minimapCamera)
    {
        if (minimapCamera.targetTexture == null)
        {
            return;
        }

        RawImage[] rawImages = Object.FindObjectsByType<RawImage>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        for (int i = 0; i < rawImages.Length; i++)
        {
            RawImage rawImage = rawImages[i];
            if (rawImage != null &&
                rawImage.name.Contains("Minimap"))
            {
                rawImage.texture = minimapCamera.targetTexture;
            }
        }
    }

    private static void SetArcGisCameraEnabled(
        ArcGISCameraComponent arcGisCamera,
        bool enabled,
        string label)
    {
        if (arcGisCamera == null || arcGisCamera.enabled == enabled)
        {
            return;
        }

        try
        {
            arcGisCamera.enabled = enabled;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning(
                $"Could not set {label} ArcGISCameraComponent to {enabled}: " +
                exception.Message
            );
        }
    }

    private static Camera FindDroneCamera()
    {
        GameObject namedDroneCamera = GameObject.Find("DroneCamera");
        if (namedDroneCamera != null &&
            namedDroneCamera.TryGetComponent(out Camera namedCamera))
        {
            return namedCamera;
        }

        GameObject droneObject =
            GameObject.FindGameObjectWithTag("Drone");
        if (droneObject != null)
        {
            Camera childCamera = droneObject.GetComponentInChildren<Camera>(true);
            if (childCamera != null)
            {
                return childCamera;
            }
        }

        return null;
    }

    private static Camera FindMinimapCamera()
    {
        GameObject minimapObject = GameObject.Find("Minimap Camera");
        if (minimapObject != null &&
            minimapObject.TryGetComponent(out Camera minimapCamera))
        {
            return minimapCamera;
        }

        Camera[] cameras = Object.FindObjectsByType<Camera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null &&
                cameras[i].targetTexture != null)
            {
                return cameras[i];
            }
        }

        return null;
    }
}
