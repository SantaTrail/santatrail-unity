using UnityEngine;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using Debug = UnityEngine.Debug;
using System.Collections;
using Esri.ArcGISMapsSDK.Components;
using Esri.ArcGISMapsSDK.Utils;
using Esri.ArcGISMapsSDK.Utils.GeoCoord;
using Esri.GameEngine.Geometry;
using Esri.GameEngine.Map;
using Unity.Mathematics;

public partial class SceneLoaderArcgis
{
    void Awake()
    {
        TryResolveArcGISConverter();
        ResolveModularHouseGenerator();
        ApplyConfiguredMapOrigin();

        arcGISMap = FindFirstObjectByType<ArcGISMapComponent>();

        if (arcGISMap != null)
        {
            arcGISRoot = arcGISMap.transform;

            Debug.Log(
                $"✅ ArcGIS root found: {arcGISRoot.name}"
            );

            ApplyLayerVisibilityOverrides();
        }
        else
        {
            Debug.LogError(
                "❌ ArcGISMapComponent not found"
            );
        }
        if (arcGISMap != null)
        {
            Debug.Log(
                $"MAP ORIGIN = " +
                $"{arcGISMap.OriginPosition.Y}, " +
                $"{arcGISMap.OriginPosition.X}"
            );
        }
        if (arcGISConverter == null)
            Debug.LogWarning("⚠️ ArcGISConverter not found in Awake (will keep retrying)");
    }

    void OnDrawGizmosSelected()
    {
        if (!showSpawnRadius || droneTransform == null)
            return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(
            droneTransform.position,
            buildingSpawnRadius
        );
    }

    void ApplyConfiguredMapOrigin()
    {
        ArcGISMapComponent mapComponent =
            FindFirstObjectByType<ArcGISMapComponent>(
                FindObjectsInactive.Include
            );

        if (mapComponent == null)
        {
            Debug.LogWarning(
                "⚠️ SceneLoaderArcgis could not find ArcGISMapComponent."
            );
            return;
        }

        if (lockSceneMapOriginForManualBuildings)
        {
            if (mapComponent.OriginPosition != null)
            {
                GameManager.SetHomeLocation(
                    mapComponent.OriginPosition.Y,
                    mapComponent.OriginPosition.X,
                    mapComponent.OriginPosition.Z
                );

                ApplyConfiguredMapExtent(
                    mapComponent,
                    mapComponent.OriginPosition
                );

                Debug.Log(
                    $"🔒 Manual-building map locked to scene origin: " +
                    $"{mapComponent.OriginPosition.Y}, " +
                    $"{mapComponent.OriginPosition.X}, " +
                    $"{mapComponent.OriginPosition.Z}"
                );
            }
            else
            {
                Debug.LogWarning(
                    "⚠️ Manual-building origin lock is enabled, " +
                    "but the ArcGIS map has no saved Origin Position."
                );
            }

            return;
        }

        if (!overrideMapOrigin)
        {
            if (GameManager.hasHomeLocation)
            {
                mapComponent.OriginPosition = new ArcGISPoint(
                    GameManager.homeLon,
                    GameManager.homeLat,
                    GameManager.homeAlt,
                    ArcGISSpatialReference.WGS84()
                );

                ApplyConfiguredMapExtent(
                    mapComponent,
                    mapComponent.OriginPosition
                );

                Debug.Log(
                    $"🧭 SceneLoaderArcgis kept selected home location: " +
                    $"{GameManager.homeLat}, " +
                    $"{GameManager.homeLon}, " +
                    $"{GameManager.homeAlt}"
                );
            }
            else if (mapComponent.OriginPosition != null)
            {
                GameManager.SetHomeLocation(
                    mapComponent.OriginPosition.Y,
                    mapComponent.OriginPosition.X,
                    mapComponent.OriginPosition.Z
                );

                ApplyConfiguredMapExtent(
                    mapComponent,
                    mapComponent.OriginPosition
                );

                Debug.Log(
                    "🧭 SceneLoaderArcgis initialized home from scene map origin."
                );
            }

            return;
        }

        mapComponent.OriginPosition = new ArcGISPoint(
            mapOriginLongitude,
            mapOriginLatitude,
            mapOriginAltitude,
            ArcGISSpatialReference.WGS84()
        );

        ApplyConfiguredMapExtent(
            mapComponent,
            mapComponent.OriginPosition
        );

        GameManager.SetHomeLocation(
            mapOriginLatitude,
            mapOriginLongitude,
            mapOriginAltitude
        );

        Debug.Log(
            $"🧭 SceneLoaderArcgis applied map origin: " +
            $"{mapOriginLatitude}, " +
            $"{mapOriginLongitude}, " +
            $"{mapOriginAltitude}"
        );
    }

    void ApplyConfiguredMapExtent(ArcGISMapComponent mapComponent, ArcGISPoint extentCenter)
    {
        if (mapComponent == null || !limitMapExtent)
        {
            return;
        }

        mapComponent.MapType = ArcGISMapType.Local;
        mapComponent.EnableExtent = true;
        mapComponent.Extent = new ArcGISExtentInstanceData
        {
            GeographicCenter = extentCenter.ToInstanceData(),
            ExtentShape = MapExtentShapes.Circle,
            ShapeDimensions = new double2(mapExtentSizeMeters, 0),
            UseOriginAsCenter = true
        };
    }

    void ApplyLayerVisibilityOverrides()
    {
        if (!hideOsmBuildingLayer || arcGISMap == null || arcGISMap.Layers == null || arcGISMap.Layers.Count == 0)
        {
            return;
        }

        bool changed = false;
        List<ArcGISLayerInstanceData> updatedLayers = new List<ArcGISLayerInstanceData>(arcGISMap.Layers.Count);

        foreach (ArcGISLayerInstanceData layer in arcGISMap.Layers)
        {
            if (layer == null)
            {
                continue;
            }

            ArcGISLayerInstanceData clonedLayer = (ArcGISLayerInstanceData)layer.Clone();
            string source = clonedLayer.Source ?? "";
            string name = clonedLayer.Name ?? "";
            bool isOsmBuildings =
                source.Contains("OpenStreetMap3D_Buildings_v1") ||
                name.ToLowerInvariant().Contains("building");

            if (isOsmBuildings)
            {
                clonedLayer.IsVisible = false;
                changed = true;

                if (logLayerVisibilityChanges)
                {
                    Debug.Log(
                        $"🙈 Hiding ArcGIS building layer: {clonedLayer.Name} | Source={clonedLayer.Source}"
                    );
                }
            }

            updatedLayers.Add(clonedLayer);
        }

        if (changed)
        {
            arcGISMap.Layers = updatedLayers;
        }
    }

    void Start()
    {
        Debug.Log("🏘 Visible-only village test mode enabled");

        if (loadingScreen == null)
        {
            loadingScreen = FindFirstObjectByType<LoadingScreenUI>(
                FindObjectsInactive.Include
            );
        }

        loadingScreenShownAt = Time.unscaledTime;

        if (loadingScreen != null)
        {
            loadingScreen.Show("Preparing Christmas delivery mission...");
            loadingScreen.SetProgress(0.02f);
        }

        string projectRoot = Application.dataPath + "/../";
        string backendPath = ResolveBackendPath(projectRoot);

        string originalMainPath =
            Path.Combine(backendPath, "main.py");

        string expandedScriptPath =
            Path.Combine(
                backendPath,
                expandedBuildingScriptName
            );

        outputPath = Path.Combine(backendPath, "output.json");

        if (fetchMoreOsmBuildings &&
            File.Exists(expandedScriptPath))
        {
            scriptPath = expandedScriptPath;

            Debug.Log(
                "🏘 Expanded OSM building query enabled: " +
                scriptPath
            );
        }
        else
        {
            scriptPath = originalMainPath;

            if (fetchMoreOsmBuildings)
            {
                Debug.LogWarning(
                    "⚠️ Expanded building wrapper was not found at: " +
                    expandedScriptPath +
                    ". Falling back to the existing main.py."
                );
            }
        }

        if (!File.Exists(scriptPath))
        {
            string errorMessage =
                "Python level generator was not found. Check the backend path.";

            Debug.LogError(
                "❌ Python script NOT FOUND at: " + scriptPath
            );

            if (loadingScreen != null)
            {
                loadingScreen.ShowError(errorMessage);
            }

            return;
        }

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
            Debug.Log("🧹 Old JSON deleted");
        }

        SetupDroneReference();
        StartCoroutine(InitializeEverything());
    }

    string ResolveBackendPath(string projectRoot)
    {
        string[] candidates =
        {
        Path.GetFullPath(Path.Combine(projectRoot, "../santatrail-backend/backend")),
        Path.Combine(projectRoot, "uav-terrain-ai/backend")
    };

        foreach (string candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "main.py")))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    IEnumerator InitializeEverything()
    {
        if (loadingScreen != null)
        {
            loadingScreen.SetProgress(
                0.08f,
                "Starting terrain and map generator..."
            );
        }

        RunPython();

        Debug.Log("⏳ Waiting for JSON + ArcGIS (production-safe)...");

        float timeout = 120f;
        float timer = 0f;

        bool jsonReady = false;
        bool arcgisReady = false;

        while (timer < timeout)
        {
            timer += 0.5f;

            if (arcGISConverter == null)
            {
                TryResolveArcGISConverter();
            }

            if (pythonProcessCompleted &&
                pythonProcessExitCode != 0)
            {
                Debug.LogError(
                    "❌ Python level generation failed with exit code " +
                    pythonProcessExitCode
                );

                if (loadingScreen != null)
                {
                    loadingScreen.ShowError(
                        "Python level generation failed. Check the Console."
                    );
                }

                yield break;
            }

            if (!jsonReady &&
                pythonProcessCompleted &&
                pythonProcessExitCode == 0 &&
                File.Exists(outputPath))
            {
                try
                {
                    string content = File.ReadAllText(outputPath);

                    if (IsValidJson(content))
                    {
                        jsonReady = true;
                        TrySetFallbackOriginFromJson(content);
                        Debug.Log("✅ JSON READY (validated)");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning(
                        "⚠️ JSON read error: " + e.Message
                    );
                }
            }

            if (!arcgisReady &&
                arcGISConverter != null &&
                arcGISConverter.IsReady())
            {
                arcgisReady = true;
                Debug.Log("✅ ArcGIS READY");
            }

            bool projectionReady =
                arcgisReady ||
                CanUseFallbackProjection();

            if (loadingScreen != null)
            {
                float waitingProgress =
                    0.10f +
                    (jsonReady ? 0.28f : 0f) +
                    (projectionReady ? 0.28f : 0f) +
                    Mathf.Clamp01(timer / timeout) * 0.10f;

                string loadingMessage;

                if (!jsonReady && !projectionReady)
                {
                    loadingMessage =
                        "Loading map data and ArcGIS terrain...";
                }
                else if (!jsonReady)
                {
                    loadingMessage =
                        "ArcGIS ready. Waiting for mission data...";
                }
                else if (!projectionReady)
                {
                    loadingMessage =
                        "Mission data ready. Loading ArcGIS terrain...";
                }
                else
                {
                    loadingMessage =
                        "Map data ready. Preparing the level...";
                }

                loadingScreen.SetProgress(
                    Mathf.Min(waitingProgress, 0.74f),
                    loadingMessage
                );
            }

            if (jsonReady && projectionReady)
            {
                Debug.Log("🚀 ALL READY → Stabilizing...");

                if (loadingScreen != null)
                {
                    loadingScreen.SetProgress(
                        0.76f,
                        "Stabilizing ArcGIS map..."
                    );
                }

                yield return new WaitForSecondsRealtime(1f);

                if (loadingScreen != null)
                {
                    loadingScreen.SetProgress(
                        0.84f,
                        "Placing drone and mission objects..."
                    );
                }

                // Let Unity render the loading-screen update before
                // running the synchronous generation work.
                yield return null;

                bool generationSucceeded = LoadAndGenerateSafe();

                if (!generationSucceeded)
                {
                    if (loadingScreen != null)
                    {
                        loadingScreen.ShowError(
                            "Level generation failed. Check the Console."
                        );
                    }

                    yield break;
                }

                if (loadingScreen != null)
                {
                    loadingScreen.SetProgress(
                        1f,
                        "Mission ready!"
                    );

                    float visibleTime =
                        Time.unscaledTime - loadingScreenShownAt;

                    float remainingTime =
                        Mathf.Max(
                            0f,
                            minimumLoadingScreenSeconds - visibleTime
                        );

                    if (remainingTime > 0f)
                    {
                        yield return new WaitForSecondsRealtime(
                            remainingTime
                        );
                    }

                    yield return new WaitForSecondsRealtime(0.25f);
                    loadingScreen.Hide();
                }

                yield break;
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }

        Debug.LogError(
            "❌ Initialization timeout → aborting generation"
        );

        if (loadingScreen != null)
        {
            loadingScreen.ShowError(
                "Loading timed out. Check ArcGIS, Python and the Console."
            );
        }
    }

    void TryResolveArcGISConverter()
    {
        if (arcGISConverter != null) return;

        arcGISConverter = FindFirstObjectByType<ArcGISConverter>(FindObjectsInactive.Include);

        if (arcGISConverter == null)
        {
            ArcGISConverter[] all = Resources.FindObjectsOfTypeAll<ArcGISConverter>();
            if (all != null && all.Length > 0)
                arcGISConverter = all[0];
        }

        if (arcGISConverter != null)
            Debug.Log("✅ ArcGISConverter linked: " + arcGISConverter.name);
    }

    bool IsValidJson(string content)
    {
        if (string.IsNullOrEmpty(content)) return false;

        content = content.Trim();

        if (!content.StartsWith("{") || !content.EndsWith("}"))
            return false;

        try
        {
            Result test = JsonUtility.FromJson<Result>(content);

            if (test == null) return false;
            if (test.grid_size <= 0) return false;
            if (test.elevation_data == null || test.elevation_data.Length == 0) return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    bool CanUseFallbackProjection()
    {
        return allowLocalGpsFallback && fallbackOriginSet;
    }

    void TrySetFallbackOriginFromJson(string json)
    {
        if (!allowLocalGpsFallback || fallbackOriginSet)
            return;

        try
        {
            Result parsed = JsonUtility.FromJson<Result>(json);
            if (parsed != null && parsed.waypoints != null && parsed.waypoints.Length > 0)
            {
                fallbackOriginLat = parsed.waypoints[0].lat;
                fallbackOriginLon = parsed.waypoints[0].lon;
                fallbackOriginSet = true;
                Debug.Log($"🧭 Fallback GPS origin set: {fallbackOriginLat}, {fallbackOriginLon}");
            }
        }
        catch { }
    }

    bool LoadAndGenerateSafe()
    {
        string json = File.ReadAllText(outputPath);
        Result result = JsonUtility.FromJson<Result>(json);

        if (result == null)
        {
            Debug.LogError("❌ Failed to parse JSON → abort");
            return false;
        }

        Debug.Log("🌍 Scene type: " + result.scene);
        SyncMapOriginToResult(result);

        bool isManualBuildingLevel =
            lockSceneMapOriginForManualBuildings;

        if (!useArcGISTerrainOnly)
        {
            GenerateTerrain(result);

            if (terrain == null)
            {
                Debug.LogError("❌ Terrain failed → abort");
                return false;
            }

            CarveRiversIntoTerrainSafe(result);

            if (result.features != null &&
                result.features.water_present)
            {
                GenerateWater();
            }
        }
        else
        {
            if (isManualBuildingLevel &&
                keepManuallyPlacedDronePosition)
            {
                Debug.Log(
                    "🔒 Manual level: keeping the drone at its " +
                    "scene position. JSON GPS drone placement skipped."
                );
            }
            else
            {
                PlaceDroneOnArcGIS(result);
            }
        }

        GenerateSceneSafe(result);

        if (isManualBuildingLevel &&
            skipGeneratedGpsObjectsInManualLevel)
        {
            Debug.Log(
                "🔒 Manual level: generated GPS path and river " +
                "line objects skipped."
            );
        }
        else
        {
            DrawPathSafe(result);
            DrawRiversSafe(result);
        }

        Debug.Log("✅ Scene generation COMPLETE (safe)");
        return true;
    }

    void SyncMapOriginToResult(Result result)
    {
        if (lockSceneMapOriginForManualBuildings)
        {
            Debug.Log(
                "🔒 JSON map-origin synchronization skipped " +
                "for this manual-building level."
            );
            return;
        }

        if (overrideMapOrigin ||
            arcGISMap == null ||
            result == null ||
            result.waypoints == null ||
            result.waypoints.Length == 0)
        {
            return;
        }

        GPSWaypoint originWaypoint = result.waypoints[0];
        bool shouldReplaceHome = !GameManager.hasHomeLocation;

        if (!shouldReplaceHome)
        {
            double latDelta =
                System.Math.Abs(
                    GameManager.homeLat - originWaypoint.lat
                );

            double lonDelta =
                System.Math.Abs(
                    GameManager.homeLon - originWaypoint.lon
                );

            shouldReplaceHome =
                latDelta > 0.25 ||
                lonDelta > 0.25;
        }

        if (!shouldReplaceHome)
        {
            return;
        }

        GameManager.SetHomeLocation(
            originWaypoint.lat,
            originWaypoint.lon,
            originWaypoint.alt,
            0
        );

        arcGISMap.OriginPosition = new ArcGISPoint(
            originWaypoint.lon,
            originWaypoint.lat,
            originWaypoint.alt,
            ArcGISSpatialReference.WGS84()
        );

        ApplyConfiguredMapExtent(
            arcGISMap,
            arcGISMap.OriginPosition
        );

        Debug.Log(
            $"🧭 SceneLoaderArcgis synced map origin to JSON waypoint: " +
            $"{originWaypoint.lat}, " +
            $"{originWaypoint.lon}, " +
            $"{originWaypoint.alt}"
        );
    }
}
