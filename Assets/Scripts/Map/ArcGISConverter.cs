using UnityEngine;
using Esri.ArcGISMapsSDK.Components;
using Esri.GameEngine;
using Esri.GameEngine.Geometry;
using Esri.GameEngine.MapView;
using System.Collections;

public class ArcGISConverter : MonoBehaviour
{
    [Header("Visible Map Readiness")]
    [Min(0f)]
    [Tooltip(
        "Minimum settling time after ArcGIS first reports DrawStatus.Completed " +
        "before the level may hide its loading screen. The final readiness " +
        "sample must also be Completed."
    )]
    [SerializeField] float requiredStableDrawSeconds = 2f;
    [Tooltip(
        "Allow the level to open when the map and basemap are loaded and " +
        "coordinate projection has remained usable, even if ArcGIS keeps " +
        "DrawStatus.InProgress because its streaming camera is moving."
    )]
    [SerializeField] bool allowLoadedMapFallback = false;
    [Min(1f)]
    [Tooltip(
        "Continuous usable-map time required after the final camera placement " +
        "before accepting a persistent DrawStatus.InProgress."
    )]
    [SerializeField] float loadedMapFallbackSeconds = 15f;

    private ArcGISMapComponent arcGISMap;
    private string lastProbeError = "";
    private bool hasLoggedCameraState = false;

    private bool hasLoggedReady = false;
    private float nextMapRetryAt = 0f;
    private float drawCompletedSinceRealtime = -1f;
    private float projectableSinceRealtime = -1f;
    private bool acceptedLoadedMapFallback = false;
    private bool hasLoggedLoadedMapFallback = false;
    private ArcGISDroneMapDriver heldMapDriver;

    void Awake()
    {
        LogRuntimeEnvironment();

        arcGISMap = FindFirstObjectByType<ArcGISMapComponent>();

        if (arcGISMap == null)
        {
            Debug.LogError("❌ ArcGISMapComponent NOT found in scene");
            return;
        }

        Debug.Log("🧭 ArcGISConverter initialized");
        LogArcGISCameraState("Awake");

        StartCoroutine(WaitForArcGISReady());
    }

    IEnumerator WaitForArcGISReady()
    {
        Debug.Log("⏳ Waiting for ArcGIS to fully initialize...");
        float timer = 0f;
        float nextStateLogAt = 2f;

        EnsureMapIsLoading(0f);

        while (true)
        {
            timer += 0.5f;

            if (arcGISMap == null)
            {
                Debug.LogError("❌ ArcGISMapComponent became null while waiting");
                yield break;
            }

            if (IsReady())
            {
                if (!hasLoggedReady)
                {
                    Debug.Log(
                        acceptedLoadedMapFallback
                            ? "✅ ArcGIS MAP READY FOR PLAY"
                            : "✅ ArcGIS FULLY READY"
                    );
                    LogRenderCameraState();
                    hasLoggedReady = true;
                }

                yield break;
            }

            if (timer >= nextStateLogAt)
            {
                EnsureMapIsLoading(timer);
                string viewState = arcGISMap.View == null ? "NULL" : "OK";
                string srState = (arcGISMap.View != null && arcGISMap.View.SpatialReference != null) ? "OK" : "NULL";
                bool probe = CanConvertProbePoint();
                string probeErrorPart = string.IsNullOrEmpty(lastProbeError) ? "" : $" | ProbeError={lastProbeError}";
                Debug.LogWarning($"⚠️ ArcGIS still loading... t={timer:0.0}s | View={viewState} | SpatialReference={srState} | {DescribeMapLoadState()} | ProbeConvert={(probe ? "OK" : "FAIL")}{probeErrorPart}");
                LogArcGISCameraState($"t={timer:0.0}s");
                nextStateLogAt += timer < 90f ? 5f : 15f;
            }

            // Map streaming must continue while menus or tutorial UI affect time scale.
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    void Update()
    {
        // Lose the completion observation only if the map itself unloads.
        // A live ArcGIS camera can alternate between Completed and InProgress
        // as adjacent tiles stream, so resetting on every InProgress frame can
        // otherwise keep a completely usable map behind the loading screen.
        if (!IsMapContentLoaded())
        {
            drawCompletedSinceRealtime = -1f;
            projectableSinceRealtime = -1f;
            acceptedLoadedMapFallback = false;
        }
    }

    void LogRuntimeEnvironment()
    {
        Debug.Log(
            "🖥 SantaTrail map diagnostics | " +
            $"Unity={Application.unityVersion} | " +
            $"OS={SystemInfo.operatingSystem} | " +
            $"CPU={SystemInfo.processorType} | " +
            $"RAM={SystemInfo.systemMemorySize} MB | " +
            $"GPU={SystemInfo.graphicsDeviceName} | " +
            $"GraphicsAPI={SystemInfo.graphicsDeviceType} | " +
            $"GraphicsMemory={SystemInfo.graphicsMemorySize} MB | " +
            $"ShaderLevel={SystemInfo.graphicsShaderLevel} | " +
            $"ComputeShaders={SystemInfo.supportsComputeShaders} | " +
            $"Log={Application.consoleLogPath}"
        );
    }

    void EnsureMapIsLoading(float timer)
    {
        var map = arcGISMap?.View?.Map;
        if (map == null || timer < nextMapRetryAt)
        {
            return;
        }

        if (map.LoadStatus == ArcGISLoadStatus.NotLoaded)
        {
            map.Load();
            nextMapRetryAt = timer + 5f;
        }
        else if (map.LoadStatus == ArcGISLoadStatus.FailedToLoad)
        {
            string error = map.LoadError?.Message ?? "unknown ArcGIS load error";
            Debug.LogWarning($"⚠️ Retrying failed ArcGIS map load: {error}");
            map.RetryLoad();
            nextMapRetryAt = timer + 10f;
        }
    }

    bool IsMapContentLoaded()
    {
        var map = arcGISMap?.View?.Map;
        if (map == null || map.LoadStatus != ArcGISLoadStatus.Loaded)
        {
            return false;
        }

        var basemap = map.Basemap;
        return basemap == null ||
               basemap.LoadStatus == ArcGISLoadStatus.Loaded;
    }

    bool IsMapDrawComplete()
    {
        // A loaded map only means its definition is available. The player should
        // not enter LV1 until ArcGIS has finished drawing the current basemap.
        return arcGISMap?.View != null &&
               arcGISMap.View.DrawStatus == ArcGISDrawStatus.Completed;
    }

    string DescribeMapLoadState()
    {
        var map = arcGISMap?.View?.Map;
        if (map == null)
        {
            return "Map=NULL";
        }

        string state = $"Map={map.LoadStatus}";
        if (map.LoadError != null)
        {
            state += $" ({map.LoadError.Message})";
        }

        var basemap = map.Basemap;
        if (basemap != null)
        {
            state += $" | Basemap={basemap.LoadStatus}";
            if (basemap.LoadError != null)
            {
                state += $" ({basemap.LoadError.Message})";
            }
        }

        if (arcGISMap?.View != null)
        {
            state += $" | Draw={arcGISMap.View.DrawStatus}";
        }

        return state;
    }

    public string GetDiagnosticSummary()
    {
        string probeError = string.IsNullOrEmpty(lastProbeError)
            ? "none"
            : lastProbeError;

        return
            $"{DescribeMapLoadState()} | " +
            $"GraphicsAPI={SystemInfo.graphicsDeviceType} | " +
            $"GPU={SystemInfo.graphicsDeviceName} | " +
            $"OS={SystemInfo.operatingSystem} | " +
            $"ProbeError={probeError} | " +
            $"StableDraw={(drawCompletedSinceRealtime >= 0f ? (Time.realtimeSinceStartup - drawCompletedSinceRealtime).ToString("0.0") + "s" : "waiting")} | " +
            $"UsableMap={(projectableSinceRealtime >= 0f ? (Time.realtimeSinceStartup - projectableSinceRealtime).ToString("0.0") + "s" : "waiting")} | " +
            $"Log={Application.consoleLogPath}";
    }

    public bool IsReady()
    {
        // Evaluate the live view every time because the level loader can reach
        // this final phase before the startup coroutine observes completion.
        if (HasStableVisibleMap())
        {
            acceptedLoadedMapFallback = false;
            return true;
        }

        return HasSustainedUsableMap();
    }

    public bool CanProjectCoordinates()
    {
        // Coordinate conversion becomes usable before the renderer finishes
        // streaming every visible tile. Level generation may safely start at
        // this point, while IsReady() continues to guard removal of the
        // loading screen until drawing completes or the loaded view has stayed
        // continuously usable for the configured fallback window.
        return arcGISMap != null &&
               arcGISMap.View != null &&
               IsMapContentLoaded() &&
               CanConvertProbePoint();
    }

    public void RequireFreshDrawCompletion()
    {
        // Object generation and drone placement change the streaming camera's
        // final view. Require ArcGIS to complete that view instead of reusing
        // the earlier startup-ready result.
        drawCompletedSinceRealtime = -1f;
        projectableSinceRealtime = -1f;
        acceptedLoadedMapFallback = false;
        hasLoggedLoadedMapFallback = false;

        heldMapDriver = FindFirstObjectByType<ArcGISDroneMapDriver>(
            FindObjectsInactive.Include
        );
        heldMapDriver?.HoldCurrentViewForReadiness();
    }

    public void ReleaseReadinessViewHold()
    {
        if (heldMapDriver == null)
        {
            heldMapDriver = FindFirstObjectByType<ArcGISDroneMapDriver>(
                FindObjectsInactive.Include
            );
        }

        heldMapDriver?.ReleaseReadinessHold();
        heldMapDriver = null;
    }

    bool HasSustainedUsableMap()
    {
        if (!allowLoadedMapFallback || !CanProjectCoordinates())
        {
            projectableSinceRealtime = -1f;
            acceptedLoadedMapFallback = false;
            return false;
        }

        float now = Time.realtimeSinceStartup;
        if (projectableSinceRealtime < 0f)
        {
            projectableSinceRealtime = now;
            return false;
        }

        if (now - projectableSinceRealtime <
            Mathf.Max(1f, loadedMapFallbackSeconds))
        {
            return false;
        }

        acceptedLoadedMapFallback = true;
        if (!hasLoggedLoadedMapFallback)
        {
            Debug.LogWarning(
                "⚠️ ArcGIS DrawStatus is still InProgress, but the map, " +
                "basemap and coordinate projection have remained usable for " +
                $"{loadedMapFallbackSeconds:0.#}s. Opening the level while " +
                "background tile refinement continues."
            );
            hasLoggedLoadedMapFallback = true;
        }

        return true;
    }

    bool HasStableVisibleMap()
    {
        if (!CanProjectCoordinates())
        {
            drawCompletedSinceRealtime = -1f;
            return false;
        }

        // The final streaming camera is held still while the loading screen is
        // up, so require a genuinely continuous Completed interval. Any new
        // InProgress sample means ArcGIS resumed work and restarts the timer.
        if (!IsMapDrawComplete())
        {
            drawCompletedSinceRealtime = -1f;
            return false;
        }

        float now = Time.realtimeSinceStartup;
        if (drawCompletedSinceRealtime < 0f)
        {
            drawCompletedSinceRealtime = now;
        }

        return now - drawCompletedSinceRealtime >=
            Mathf.Max(0f, requiredStableDrawSeconds);
    }

    bool CanConvertProbePoint()
    {
        if (arcGISMap == null || arcGISMap.View == null)
        {
            lastProbeError = "View is null";
            return false;
        }

        try
        {
            var probe = new ArcGISPoint(
                GameManager.homeLon,
                GameManager.homeLat,
                GameManager.homeAlt,
                ArcGISSpatialReference.WGS84()
            );
            Vector3 enginePosition = arcGISMap.GeographicToEngine(probe);

            if (float.IsNaN(enginePosition.x) ||
                float.IsInfinity(enginePosition.x) ||
                float.IsNaN(enginePosition.y) ||
                float.IsInfinity(enginePosition.y) ||
                float.IsNaN(enginePosition.z) ||
                float.IsInfinity(enginePosition.z))
            {
                lastProbeError = "Engine result NaN/Infinity";
                return false;
            }

            lastProbeError = "";
            return true;
        }
        catch (System.Exception e)
        {
            lastProbeError = e.GetType().Name + ": " + e.Message;
            return false;
        }
    }

    void LogArcGISCameraState(string phase)
    {
        ArcGISCameraComponent[] arcCams = FindObjectsByType<ArcGISCameraComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        string mainCam = Camera.main != null ? Camera.main.name : "NONE";

        if (arcCams == null || arcCams.Length == 0)
        {
            if (!hasLoggedCameraState || phase != "Awake")
            {
                Debug.LogWarning($"⚠️ ArcGIS camera check ({phase}): ArcGISCameraComponent NOT found | MainCamera={mainCam}");
            }
            return;
        }

        string names = "";
        for (int i = 0; i < arcCams.Length; i++)
        {
            if (i > 0) names += ", ";
            Camera unityCamera = arcCams[i].GetComponent<Camera>();
            bool cameraEnabled = unityCamera != null && unityCamera.enabled;
            names += $"{arcCams[i].name} (ArcGIS={arcCams[i].enabled}, Camera={cameraEnabled}, Active={arcCams[i].gameObject.activeInHierarchy})";
        }

        Debug.Log($"✅ ArcGIS camera check ({phase}): Found {arcCams.Length} ArcGISCameraComponent(s): {names} | MainCamera={mainCam}");
        hasLoggedCameraState = true;
    }

    void LogRenderCameraState()
    {
        if (arcGISMap?.View == null)
        {
            return;
        }

        ArcGISCameraComponent[] cameras = FindObjectsByType<ArcGISCameraComponent>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        for (int i = 0; i < cameras.Length; i++)
        {
            ArcGISCameraComponent camera = cameras[i];
            if (camera == null || !camera.enabled)
            {
                continue;
            }

            Debug.Log(
                $"🗺 ArcGIS render camera: {camera.name} | " +
                $"World={camera.transform.position} | " +
                $"Local={camera.transform.localPosition} | " +
                $"ViewGeo={arcGISMap.View.Camera.Location}"
            );
            return;
        }

        Debug.LogWarning("⚠️ ArcGIS is ready, but no ArcGIS camera is enabled.");
    }

    public Vector3 GPSToUnity(double lat, double lon, double alt)
    {
        if (!CanProjectCoordinates())
        {
            Debug.LogWarning($"⚠️ ArcGIS projection is not ready yet → skipping conversion ({lat}, {lon})");
            return Vector3.zero;
        }

        if (double.IsNaN(lat) || double.IsInfinity(lat) ||
            double.IsNaN(lon) || double.IsInfinity(lon) ||
            double.IsNaN(alt) || double.IsInfinity(alt))
        {
            Debug.LogError($"❌ INVALID GPS input → NaN/Infinity ({lat}, {lon}, {alt})");
            return Vector3.zero;
        }

        try
        {
            var geo = new ArcGISPoint(
                lon,
                lat,
                alt,
                ArcGISSpatialReference.WGS84()
            );

            // GeographicToWorld returns ArcGIS's high-precision Cartesian
            // coordinate (ECEF for a global map), not a Unity scene position.
            // Casting that million-unit value to float loses footprint detail
            // and placing a Transform at it bypasses the HP root entirely.
            // GeographicToEngine applies the map's high-precision world matrix
            // and returns the correct Unity-space position near the map origin.
            Vector3 pos = arcGISMap.GeographicToEngine(geo);

            // 🔥 VALIDATION CHECK
            if (float.IsNaN(pos.x) || float.IsInfinity(pos.x) ||
                float.IsNaN(pos.y) || float.IsInfinity(pos.y) ||
                float.IsNaN(pos.z) || float.IsInfinity(pos.z))
            {
                Debug.LogError($"❌ INVALID ArcGIS conversion → NaN/Infinity ({lat}, {lon})");
                return Vector3.zero;
            }

            // Engine coordinates should remain local to the ArcGIS map origin.
            if (pos.magnitude > 10000000f)
            {
                Debug.LogError($"❌ ArcGIS engine position is unexpectedly large ({pos})");
                return Vector3.zero;
            }

            return pos;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ ArcGIS conversion exception: {e.Message}");
            return Vector3.zero;
        }
    }
}
