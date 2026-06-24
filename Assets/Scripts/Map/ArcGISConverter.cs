using UnityEngine;
using Esri.ArcGISMapsSDK.Components;
using Esri.GameEngine.Geometry;
using System.Collections;

public class ArcGISConverter : MonoBehaviour
{
    private ArcGISMapComponent arcGISMap;
    private string lastProbeError = "";
    private bool hasLoggedCameraState = false;

    private bool isReady = false;
    private bool hasLoggedReady = false;

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
        

        // 🔥 Start readiness watcher
        StartCoroutine(WaitForArcGISReady());
    }

    IEnumerator WaitForArcGISReady()
    {
        Debug.Log("⏳ Waiting for ArcGIS to fully initialize...");
        float timer = 0f;
        const float timeout = 90f;
        float nextStateLogAt = 2f;

        while (timer < timeout)
        {
            timer += 0.5f;

            if (arcGISMap == null)
            {
                Debug.LogError("❌ ArcGISMapComponent became null while waiting");
                yield break;
            }

            if (arcGISMap.View != null && CanConvertProbePoint())
            {
                isReady = true;

                if (!hasLoggedReady)
                {
                    Debug.Log("✅ ArcGIS FULLY READY");
                    hasLoggedReady = true;
                }

                yield break;
            }

            if (timer >= nextStateLogAt)
            {
                string viewState = arcGISMap.View == null ? "NULL" : "OK";
                string srState = (arcGISMap.View != null && arcGISMap.View.SpatialReference != null) ? "OK" : "NULL";
                bool probe = CanConvertProbePoint();
                string probeErrorPart = string.IsNullOrEmpty(lastProbeError) ? "" : $" | ProbeError={lastProbeError}";
                Debug.LogWarning($"⚠️ ArcGIS still loading... t={timer:0.0}s | View={viewState} | SpatialReference={srState} | ProbeConvert={(probe ? "OK" : "FAIL")}{probeErrorPart}");
                LogArcGISCameraState($"t={timer:0.0}s");
                nextStateLogAt += 5f;
            }

            yield return new WaitForSeconds(0.5f);
        }
Debug.Log($"MapComponent = {arcGISMap != null}");
Debug.Log($"View = {arcGISMap?.View != null}");

if (arcGISMap?.View != null)
{
    Debug.Log($"SpatialRef = {arcGISMap.View.SpatialReference}");
}
        Debug.LogError("❌ ArcGIS readiness timeout (90s). Check API key/authentication and ArcGIS Map settings.");
    }

    public bool IsReady()
    {
        return isReady;
    }

    bool CanConvertProbePoint()
    {
        Debug.Log(
    $"Probe GPS: lat={GameManager.homeLat}, " +
    $"lon={GameManager.homeLon}, " +
    $"alt={GameManager.homeAlt}"
);
Debug.Log($"ArcGIS View = {arcGISMap.View}");
Debug.Log($"SpatialRef = {arcGISMap.View.SpatialReference}");
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
            names += arcCams[i].name;
        }

        Debug.Log($"✅ ArcGIS camera check ({phase}): Found {arcCams.Length} ArcGISCameraComponent(s): {names} | MainCamera={mainCam}");
        hasLoggedCameraState = true;
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
