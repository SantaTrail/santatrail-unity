using UnityEngine;
using Esri.ArcGISMapsSDK.Components;
using Esri.GameEngine;
using Esri.GameEngine.Geometry;
using Esri.GameEngine.MapView;
using System.Collections;

public class ArcGISConverter : MonoBehaviour
{
    private ArcGISMapComponent arcGISMap;
    private string lastProbeError = "";
    private bool hasLoggedCameraState = false;

    private bool isReady = false;
    private bool hasLoggedReady = false;
    private float nextMapRetryAt = 0f;

    void Awake()
    {
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
        const float timeout = 90f;
        float nextStateLogAt = 2f;

        EnsureMapIsLoading(0f);

        while (timer < timeout)
        {
            timer += 0.5f;

            if (arcGISMap == null)
            {
                Debug.LogError("❌ ArcGISMapComponent became null while waiting");
                yield break;
            }

            if (arcGISMap.View != null &&
                CanConvertProbePoint() &&
                IsMapContentLoaded() &&
                IsMapDrawComplete())
            {
                isReady = true;

                if (!hasLoggedReady)
                {
                    Debug.Log("✅ ArcGIS FULLY READY");
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
                nextStateLogAt += 5f;
            }

            // Map streaming must continue while menus or tutorial UI affect time scale.
            yield return new WaitForSecondsRealtime(0.5f);
        }
Debug.Log($"MapComponent = {arcGISMap != null}");
Debug.Log($"View = {arcGISMap?.View != null}");

if (arcGISMap?.View != null)
{
    Debug.Log($"SpatialRef = {arcGISMap.View.SpatialReference}");
}
        Debug.LogError($"❌ ArcGIS readiness timeout (90s). {DescribeMapLoadState()}");
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

    public bool IsReady()
    {
        return isReady;
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
            var world = arcGISMap.View.GeographicToWorld(probe);

            if (double.IsNaN(world.x) || double.IsInfinity(world.x) ||
                double.IsNaN(world.y) || double.IsInfinity(world.y) ||
                double.IsNaN(world.z) || double.IsInfinity(world.z))
            {
                lastProbeError = "World result NaN/Infinity";
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
        if (!isReady)
        {
            Debug.LogWarning($"⚠️ ArcGIS not ready yet → skipping conversion ({lat}, {lon})");
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

            var world = arcGISMap.View.GeographicToWorld(geo);

            Vector3 pos = new Vector3(
                (float)world.x,
                (float)world.y,
                (float)world.z
            );

            // 🔥 VALIDATION CHECK
            if (float.IsNaN(pos.x) || float.IsInfinity(pos.x) ||
                float.IsNaN(pos.y) || float.IsInfinity(pos.y) ||
                float.IsNaN(pos.z) || float.IsInfinity(pos.z))
            {
                Debug.LogError($"❌ INVALID ArcGIS conversion → NaN/Infinity ({lat}, {lon})");
                return Vector3.zero;
            }

            // 🔥 SAFETY: ignore absurd values
            if (pos.magnitude > 10000000f)
            {
                Debug.LogError($"❌ ArcGIS position too large → likely not initialized properly ({pos})");
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
