using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Esri.ArcGISMapsSDK.Components;
using Esri.HPFramework;

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
        ArcGISMapComponent arcGisMap =
            FindFirstObjectByType<ArcGISMapComponent>(
                FindObjectsInactive.Include
            );

        ArcGISCameraComponent droneArcGis =
            droneCamera != null
                ? droneCamera.GetComponent<ArcGISCameraComponent>()
                : null;

        ArcGISCameraComponent minimapArcGis =
            minimapCamera != null
                ? minimapCamera.GetComponent<ArcGISCameraComponent>()
                : null;

        // Disable scene alternatives before creating/enabling a driver. The
        // ArcGIS SDK warns and may select the wrong streaming view if two
        // ArcGISCameraComponents are briefly enabled during a scene restart.
        SetArcGisCameraEnabled(minimapArcGis, false, "Minimap Camera");
        SetArcGisCameraEnabled(
            droneArcGis,
            false,
            "DroneCamera (streamed through controlled map driver)"
        );

        ArcGISCameraComponent streamingArcGisCamera;
        string streamingCameraLabel;

        // Always stream through the driver, even when the visible drone camera
        // is already under ArcGISMap. This lets the driver ignore sub-metre
        // telemetry jitter, request a lighter tile quality during play, and
        // freeze the final loading viewpoint until DrawStatus is Completed.
        ArcGISCameraComponent mapDriverArcGis =
            EnsureMapDriver(arcGisMap, droneCamera);

        if (mapDriverArcGis != null)
        {
            SetArcGisCameraEnabled(
                mapDriverArcGis,
                true,
                "ArcGIS Drone Map Driver"
            );
            streamingArcGisCamera = mapDriverArcGis;
            streamingCameraLabel = "ArcGIS Drone Map Driver";
        }
        else
        {
            // Preserve a usable map if the driver cannot be created because a
            // required scene reference is missing.
            SetArcGisCameraEnabled(
                droneArcGis,
                true,
                "DroneCamera fallback"
            );
            streamingArcGisCamera = droneArcGis;
            streamingCameraLabel = "DroneCamera fallback";
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
            $"Streaming={streamingCameraLabel} " +
            $"ArcGIS={(streamingArcGisCamera != null ? streamingArcGisCamera.enabled.ToString() : "missing")} | " +
            $"MinimapCamera={(minimapCamera != null ? minimapCamera.name : "missing")} " +
            $"ArcGIS={(minimapArcGis != null ? minimapArcGis.enabled.ToString() : "missing")} " +
            $"Render={(minimapCamera != null && minimapCamera.enabled)}"
        );
    }

    private ArcGISCameraComponent EnsureMapDriver(
        ArcGISMapComponent arcGisMap,
        Camera droneCamera)
    {
        if (arcGisMap == null || droneCamera == null)
        {
            return null;
        }

        if (mapDriver == null ||
            mapDriver.transform.parent != arcGisMap.transform)
        {
            mapDriver = arcGisMap.GetComponentInChildren<ArcGISDroneMapDriver>(
                true
            );
        }

        if (mapDriver == null ||
            mapDriver.transform.parent != arcGisMap.transform)
        {
            GameObject driverObject = new GameObject("ArcGIS Drone Map Driver");
            driverObject.transform.SetParent(arcGisMap.transform, false);

            Camera driverCamera = driverObject.AddComponent<Camera>();
            driverCamera.enabled = false;
            driverObject.AddComponent<HPTransform>();
            driverObject.AddComponent<ArcGISCameraComponent>();
            mapDriver = driverObject.AddComponent<ArcGISDroneMapDriver>();

            Debug.Log(
                "Created ArcGIS Drone Map Driver under ArcGISMap to stream " +
                "the terrain from DroneCamera."
            );
        }

        mapDriver.Configure(droneCamera);
        return mapDriver.ArcGisCamera;
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
