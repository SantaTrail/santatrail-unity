using UnityEngine;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using Debug = UnityEngine.Debug;
using System.Collections;
using UnityEngine.SceneManagement;
using Esri.ArcGISMapsSDK.Components;
using Esri.ArcGISMapsSDK.Utils;
using Esri.ArcGISMapsSDK.Utils.GeoCoord;
using Esri.GameEngine;
using Esri.GameEngine.Geometry;
using Esri.GameEngine.Map;
using Unity.Mathematics;

public partial class SceneLoaderArcgis
{
    string validatedJsonContent;

    void Awake()
    {
        // Unity invokes Awake on disabled behaviours. Ignore the legacy loader
        // component so it cannot reconfigure the map before the active loader.
        if (!enabled)
        {
            return;
        }

        configuredMapExtentSource = this;
        // When the boundary follows the generated village, do not expose the
        // map's raw radius to MAVLink before generation has finished. A cold
        // OSM start can otherwise show (and enforce) the wrong boundary before
        // the first house has been registered.
        hasConfiguredMapExtent =
            limitMapExtent && !fitDroneBoundaryToGeneratedVillage;
        configuredMapExtentRadiusMeters =
            System.Math.Max(1.0, mapExtentSizeMeters);

        TryResolveArcGISConverter();
        ResolveModularHouseGenerator();
        GameManager.TryApplyLevelForScene(SceneManager.GetActiveScene().name);
        ApplyConfiguredMapOrigin();
        InitializeFallbackOriginFromHome();

        arcGISMap = FindFirstObjectByType<ArcGISMapComponent>();

        if (arcGISMap != null)
        {
            arcGISRoot = arcGISMap.transform;

            Debug.Log(
                $"✅ ArcGIS root found: {arcGISRoot.name}"
            );

            ApplyLayerVisibilityOverrides();
            EnsureArcGISMapLoading(arcGISMap);
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

    void OnDisable()
    {
        // A slow OSM request from the previous level must not overwrite the
        // shared cache after a different level has started loading.
        StopPythonForLocalFallback();

        if (configuredMapExtentSource != this)
        {
            return;
        }

        configuredMapExtentSource = null;
        hasConfiguredMapExtent = false;
        configuredMapExtentRadiusMeters = 0.0;
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

        // ArcGISMapComponent can expose its View before the SDK has finished
        // creating the native map used by MapType. Applying the extent in
        // Awake can therefore throw inside the SDK and stop all generation.
        if (mapExtentCoroutine != null)
        {
            return;
        }

        mapExtentCoroutine = StartCoroutine(
            ApplyConfiguredMapExtentWhenReady(mapComponent, extentCenter)
        );
    }

    IEnumerator ApplyConfiguredMapExtentWhenReady(
        ArcGISMapComponent mapComponent,
        ArcGISPoint extentCenter
    )
    {
        // Let ArcGIS finish its component initialization before changing
        // MapType or Extent. This is especially important in a standalone
        // build, where SDK startup timing differs from the Unity editor.
        yield return null;

        const int maxFrames = 300;
        for (int frame = 0; frame < maxFrames; frame++)
        {
            if (mapComponent == null || !limitMapExtent)
            {
                mapExtentCoroutine = null;
                yield break;
            }

            var map = mapComponent.View?.Map;
            if (map != null)
            {
                if (map.LoadStatus == ArcGISLoadStatus.FailedToLoad)
                {
                    map.RetryLoad();
                }
                else if (map.LoadStatus == ArcGISLoadStatus.NotLoaded)
                {
                    map.Load();
                }

                // MapType calls back into the SDK's native map state. Wait
                // until the map is fully loaded instead of only checking that
                // the managed View and Map wrappers exist.
                if (map.LoadStatus == ArcGISLoadStatus.Loaded)
                {
                    try
                    {
                        mapComponent.MapType = ArcGISMapType.Local;
                        mapComponent.EnableExtent = true;
                        mapComponent.Extent = new ArcGISExtentInstanceData
                        {
                            GeographicCenter = extentCenter.ToInstanceData(),
                            ExtentShape = MapExtentShapes.Circle,
                            ShapeDimensions = new double2(mapExtentSizeMeters, 0),
                            UseOriginAsCenter = true
                        };

                        Debug.Log(
                            "🗺 ArcGIS local map extent applied after SDK " +
                            $"initialization: {mapExtentSizeMeters:0}m radius."
                        );
                        mapExtentCoroutine = null;
                        yield break;
                    }
                    catch (System.Exception exception)
                    {
                        if (frame == maxFrames - 1)
                        {
                            Debug.LogWarning(
                                "⚠️ ArcGIS map extent could not be applied: " +
                                exception.Message
                            );
                        }
                    }
                }
            }

            yield return null;
        }

        mapExtentCoroutine = null;
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

    void EnsureArcGISMapLoading(ArcGISMapComponent mapComponent)
    {
        var map = mapComponent?.View?.Map;
        if (map == null)
        {
            Debug.LogWarning("⚠️ ArcGIS map load could not start because the SDK map is missing.");
            return;
        }

        if (map.LoadStatus == ArcGISLoadStatus.FailedToLoad)
        {
            string error = map.LoadError?.Message ?? "unknown ArcGIS load error";
            Debug.LogWarning($"⚠️ Retrying ArcGIS map load: {error}");
            map.RetryLoad();
        }
        else if (map.LoadStatus == ArcGISLoadStatus.NotLoaded)
        {
            Debug.Log("🗺 Starting ArcGIS map load after level configuration.");
            map.Load();
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

        // Request this from the active loader as well as the global scene
        // callback. This removes a startup race when the level is loaded
        // before AutoArduPilotOnPlay subscribes to sceneLoaded.
        AutoArduPilotOnPlay.RequestLaunchForScene(gameObject.scene.name);

        string projectRoot = Application.dataPath + "/../";
        string backendPath = ResolveBackendPath(projectRoot);

        string originalMainPath =
            Path.Combine(backendPath, "main.py");

        string expandedScriptPath =
            Path.Combine(
                backendPath,
                expandedBuildingScriptName
            );

        string[] requiredBackendFiles =
        {
            "main.py",
            "parser.py",
            "osm.py",
            "elevation.py",
            "features.py",
            "classifier.py"
        };
        List<string> missingBackendFiles = new List<string>();

        for (int i = 0; i < requiredBackendFiles.Length; i++)
        {
            string requiredFilePath = Path.Combine(
                backendPath,
                requiredBackendFiles[i]
            );

            if (!File.Exists(requiredFilePath))
            {
                missingBackendFiles.Add(requiredBackendFiles[i]);
            }
        }

        if (missingBackendFiles.Count > 0)
        {
            string missingFiles = string.Join(", ", missingBackendFiles);
            string errorMessage =
                "The terrain backend is incomplete. Missing: " +
                missingFiles +
                ". Rebuild the Windows player with all files from " +
                "Assets/StreamingAssets/Backend.";

            Debug.LogError(
                "❌ " + errorMessage +
                " Backend path: " + backendPath
            );

            if (loadingScreen != null)
            {
                loadingScreen.ShowError(errorMessage);
            }

            return;
        }

        outputPath = Path.Combine(
            backendPath,
            BuildLocationCacheFileName()
        );

        // Carry forward a matching cache produced by older builds. New runs
        // use a location-specific file so each level keeps its own buildings.
        string legacyOutputPath = Path.Combine(backendPath, "output.json");
        if (!File.Exists(outputPath) &&
            File.Exists(legacyOutputPath) &&
            TryReadCachedOutputForCurrentHome(
                legacyOutputPath,
                out string legacyJson
            ))
        {
            validatedJsonContent = legacyJson;

            try
            {
                File.WriteAllText(outputPath, legacyJson);
                Debug.Log("📦 Migrated matching building data to the per-location cache");
            }
            catch (System.Exception exception)
            {
                // The in-memory copy is still safe to use for this load.
                Debug.LogWarning(
                    "⚠️ Building cache migration could not be saved: " +
                    exception.Message
                );
            }
        }

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
            if (TryReadCachedOutputForCurrentHome(
                    outputPath,
                    out string cachedJson
                ))
            {
                // Hold the verified content in memory before starting Python.
                // The background refresh can now replace the disk file without
                // changing which complete dataset this load uses.
                validatedJsonContent = cachedJson;
                Debug.Log("✅ Using cached map data while OSM refreshes in the background");
            }
            else
            {
                File.Delete(outputPath);
                Debug.Log("🧹 Invalid, empty, or different-location JSON cache deleted");
            }
        }

        SetupDroneReference();
        StartCoroutine(InitializeEverything());
    }

    string ResolveBackendPath(string projectRoot)
    {
        string packagedBackendPath = Path.Combine(
            Application.streamingAssetsPath,
            "Backend"
        );

        if (File.Exists(Path.Combine(packagedBackendPath, "main.py")))
        {
            string writableBackendPath = Path.Combine(
                Application.persistentDataPath,
                "Backend"
            );

            Directory.CreateDirectory(writableBackendPath);

            CopyPackagedBackend(
                packagedBackendPath,
                writableBackendPath
            );

            Debug.Log(
                "📦 Prepared packaged Python backend at: " +
                writableBackendPath
            );
            return writableBackendPath;
        }

        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(
                projectRoot,
                "../santatrail-backend/backend"
            )),
            Path.Combine(projectRoot, "uav-terrain-ai/backend"),
            Path.Combine(System.AppContext.BaseDirectory, "Backend")
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "main.py")))
            {
                return candidate;
            }
        }

        return packagedBackendPath;
    }

    void CopyPackagedBackend(string sourceDirectory, string destinationDirectory)
    {
        string normalizedSource = Path.GetFullPath(sourceDirectory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        );

        Directory.CreateDirectory(destinationDirectory);

        foreach (string sourceFile in Directory.GetFiles(
            normalizedSource,
            "*",
            SearchOption.AllDirectories
        ))
        {
            string relativePath = sourceFile.Substring(normalizedSource.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (relativePath.EndsWith(
                    ".meta",
                    System.StringComparison.OrdinalIgnoreCase
                ) ||
                relativePath.StartsWith(
                    "__pycache__" + Path.DirectorySeparatorChar,
                    System.StringComparison.OrdinalIgnoreCase
                ) ||
                relativePath.Contains(
                    Path.DirectorySeparatorChar + "__pycache__" + Path.DirectorySeparatorChar
                ))
            {
                continue;
            }

            string destinationFile = Path.Combine(
                destinationDirectory,
                relativePath
            );

            string destinationFileName = Path.GetFileName(destinationFile);
            bool isLocationCache =
                destinationFileName.StartsWith(
                    "output_v2_",
                    System.StringComparison.OrdinalIgnoreCase
                ) &&
                destinationFileName.EndsWith(
                    ".json",
                    System.StringComparison.OrdinalIgnoreCase
                );

            // StreamingAssets contains a known-good first-launch seed for
            // configured levels. Never replace a newer live OSM refresh that
            // is already stored in the writable backend directory.
            if (isLocationCache &&
                File.Exists(destinationFile) &&
                HasReusableBuildingCache(destinationFile))
            {
                continue;
            }

            string destinationParent = Path.GetDirectoryName(destinationFile);

            if (!string.IsNullOrEmpty(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            File.Copy(sourceFile, destinationFile, true);
        }
    }

    bool HasReusableBuildingCache(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            if (!IsValidJson(json))
            {
                return false;
            }

            Result cached = JsonUtility.FromJson<Result>(json);
            return cached != null &&
                   cached.buildings != null &&
                   cached.buildings.Length > 0;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning(
                "⚠️ Existing building cache could not be validated: " +
                exception.Message
            );
            return false;
        }
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
        int backendAttempt = 1;
        const int maximumBackendAttempts = 2;
        float backendAttemptStartedAt = Time.realtimeSinceStartup;

        Debug.Log("⏳ Waiting for JSON + ArcGIS (production-safe)...");

        float timeout = 120f;
        float timer = 0f;

        bool jsonReady = !string.IsNullOrWhiteSpace(validatedJsonContent);
        bool arcgisReady = false;
        bool pythonExitWarningLogged = false;
        bool usingEmergencyLocalLevel = false;

        while (timer < timeout)
        {
            timer += 0.5f;

            if (arcGISConverter == null)
            {
                TryResolveArcGISConverter();
            }

            if (!jsonReady &&
                File.Exists(outputPath))
            {
                try
                {
                    string content = File.ReadAllText(outputPath);

                    if (CanUseJsonForCurrentHome(
                            content,
                            requireBuildingData: spawnOsmBuildings
                        ))
                    {
                        validatedJsonContent = content;
                        jsonReady = true;
                        TrySetFallbackOriginFromJson(content);
                        Debug.Log("✅ JSON READY (validated)");
                    }
                    else if (spawnOsmBuildings &&
                             pythonProcessCompleted &&
                             pythonProcessExitCode == 0 &&
                             CanUseJsonForCurrentHome(
                                 content,
                                 requireBuildingData: false
                             ))
                    {
                        if (backendAttempt < maximumBackendAttempts)
                        {
                            backendAttempt++;
                            Debug.LogWarning(
                                "⚠️ The first OSM request returned no buildings. " +
                                $"Retrying the cold-start request ({backendAttempt}/" +
                                $"{maximumBackendAttempts}) before using the local village."
                            );
                            RunPython();
                            backendAttemptStartedAt = Time.realtimeSinceStartup;
                        }
                        else
                        {
                            // Both network attempts completed normally but the
                            // location still has no building data. Keep its
                            // roads/terrain and let SpawnVillageBuildings build
                            // the guaranteed local delivery village.
                            validatedJsonContent = content;
                            jsonReady = true;
                            TrySetFallbackOriginFromJson(content);
                            Debug.LogWarning(
                                "⚠️ OSM returned no buildings after two attempts; " +
                                "using the built-in delivery village."
                            );
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning(
                        "⚠️ JSON read error: " + e.Message
                    );
                }
            }

            if (!usingEmergencyLocalLevel &&
                pythonProcessCompleted &&
                pythonProcessExitCode != 0)
            {
                if (pythonProcessExitCode == 143)
                {
                    if (!pythonExitWarningLogged)
                    {
                        Debug.LogWarning(
                            "⚠️ Python exited with code 143. " +
                            "Waiting for output.json to finish loading if it was written before shutdown."
                        );
                        pythonExitWarningLogged = true;
                    }
                }
                else
                {
                    string pythonDiagnostic =
                        string.IsNullOrEmpty(pythonProcessError)
                            ? "Exit code " + pythonProcessExitCode
                            : pythonProcessError;

                    Debug.LogError(
                        "❌ Python level generation failed with exit code " +
                        pythonProcessExitCode + ". " + pythonDiagnostic
                    );

                    if (ActivateEmergencyLocalLevel(pythonDiagnostic))
                    {
                        jsonReady = true;
                        usingEmergencyLocalLevel = true;
                        pythonProcessExitCode = 0;
                        pythonExitWarningLogged = true;
                    }
                    else
                    {
                        if (loadingScreen != null)
                        {
                            loadingScreen.ShowActionableError(
                                "The building generator could not start. Diagnostic log: " +
                                Application.consoleLogPath + ". Error: " + pythonDiagnostic
                            );
                        }

                        yield break;
                    }
                }
            }

            // Security software can leave a child process alive without
            // allowing it to read the packaged scripts or reach the network.
            // Use an in-process level after a bounded wait so the player never
            // remains on an empty loading screen.
            if (!jsonReady &&
                Time.realtimeSinceStartup - backendAttemptStartedAt >= 50f &&
                ActivateEmergencyLocalLevel("backend startup timed out"))
            {
                StopPythonForLocalFallback();
                jsonReady = true;
                usingEmergencyLocalLevel = true;
                pythonProcessExitCode = 0;
                pythonProcessCompleted = true;
            }

            bool arcGISReadyForGeneration =
                arcGISConverter != null &&
                arcGISConverter.IsReady();

            if (!arcgisReady && arcGISReadyForGeneration)
            {
                arcgisReady = true;
                Debug.Log("✅ ArcGIS READY");
            }
            else if (arcgisReady && !arcGISReadyForGeneration)
            {
                // Readiness can be lost if ArcGIS starts drawing again. Do not
                // keep a stale ready result because ArcGISLocationComponent
                // synchronizes with the native map immediately in OnEnable.
                arcgisReady = false;
                Debug.LogWarning(
                    "⚠️ ArcGIS resumed loading; delaying level-object generation."
                );
            }

            bool arcGISProjectionReady =
                arcGISConverter != null &&
                arcGISConverter.CanProjectCoordinates();
            bool fallbackProjectionReady =
                !arcGISProjectionReady &&
                CanUseFallbackProjection() &&
                timer >= Mathf.Max(5f, arcGISStartupGraceSeconds);
            bool projectionReady =
                arcGISReadyForGeneration ||
                fallbackProjectionReady;

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

                // The stabilization delay gives ArcGIS another opportunity to
                // resume drawing. Re-check immediately before any generated
                // object can add an ArcGISLocationComponent; its OnEnable path
                // calls native synchronization and is not safe against a
                // partially initialized or newly invalidated map view.
                if (!fallbackProjectionReady &&
                    (arcGISConverter == null || !arcGISConverter.IsReady()))
                {
                    arcgisReady = false;
                    Debug.LogWarning(
                        "⚠️ ArcGIS readiness changed during stabilization; " +
                        "waiting for a stable map before generating objects."
                    );
                    continue;
                }

                bool generationSucceeded = false;
                const int maximumGenerationAttempts = 2;
                for (int attempt = 1;
                     attempt <= maximumGenerationAttempts;
                     attempt++)
                {
                    generationSucceeded = LoadAndGenerateSafe();
                    if (generationSucceeded)
                    {
                        break;
                    }

                    if (attempt < maximumGenerationAttempts)
                    {
                        Debug.LogWarning(
                            "⚠️ Level object generation hit a transient map " +
                            "startup error. Waiting for a fresh ArcGIS frame and retrying."
                        );
                        arcGISConverter?.RequireFreshDrawCompletion();
                        yield return new WaitForSecondsRealtime(1f);
                        yield return new WaitForEndOfFrame();
                    }
                }

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

                DeliveryScoreManager deliveryManager =
                    FindFirstObjectByType<DeliveryScoreManager>(
                        FindObjectsInactive.Include
                    );

                if (deliveryManager != null)
                {
                    // Generation may take longer than the manager's first scan.
                    // Restart only when its earlier scan already timed out.
                    deliveryManager.EnsureInitializationStarted(
                        retryIfFinished: true
                    );
                }

                if (waitForArcGISBeforeHidingLoadingScreen &&
                    arcGISConverter != null &&
                    !localTerrainFallbackActive)
                {
                    // Generation just placed the drone and finalized the
                    // streaming camera's view. Do not reuse the earlier draw
                    // completion that occurred before those changes.
                    arcGISConverter.RequireFreshDrawCompletion();

                    // Let LateUpdate push the final visible-camera position to
                    // ArcGIS before beginning the stable-draw wait below.
                    yield return null;
                }

                float readyTimer = 0f;
                float deliveryReadyTimer = 0f;
                float nextProlongedMapLogAt =
                    Mathf.Max(1f, finalLevelReadyTimeoutSeconds);
                bool mapReady =
                    !waitForArcGISBeforeHidingLoadingScreen ||
                    (arcGISConverter != null && arcGISConverter.IsReady()) ||
                    localTerrainFallbackActive;
                bool deliveryReady =
                    !waitForDeliveryTargetsBeforeHidingLoadingScreen ||
                    deliveryManager == null ||
                    deliveryManager.IsInitializationComplete;

                // ArcGIS readiness is a strict gate. Never reveal gameplay
                // while the visible view is still drawing; a slow connection
                // should extend the loading screen, not expose an empty map.
                while (!mapReady || !deliveryReady)
                {
                    if (arcGISConverter == null)
                    {
                        TryResolveArcGISConverter();
                    }

                    if (deliveryManager == null)
                    {
                        deliveryManager =
                            FindFirstObjectByType<DeliveryScoreManager>(
                                FindObjectsInactive.Include
                            );

                        if (deliveryManager != null)
                        {
                            deliveryManager.EnsureInitializationStarted(
                                retryIfFinished: true
                            );
                        }
                    }

                    mapReady =
                        !waitForArcGISBeforeHidingLoadingScreen ||
                        (arcGISConverter != null && arcGISConverter.IsReady()) ||
                        localTerrainFallbackActive;
                    deliveryReady =
                        !waitForDeliveryTargetsBeforeHidingLoadingScreen ||
                        deliveryManager == null ||
                        deliveryManager.IsInitializationComplete;

                    if (mapReady && !deliveryReady)
                    {
                        deliveryReadyTimer += 0.2f;
                    }
                    else
                    {
                        deliveryReadyTimer = 0f;
                    }

                    if (!mapReady && readyTimer >= nextProlongedMapLogAt)
                    {
                        Debug.LogWarning(
                            "⚠️ ArcGIS is still drawing; keeping the loading " +
                            "screen visible. " +
                            (arcGISConverter != null
                                ? arcGISConverter.GetDiagnosticSummary()
                                : "ArcGISConverter is missing.")
                        );

                        nextProlongedMapLogAt += 30f;
                    }

                    // Delivery initialization has its own actionable failure
                    // state. Bound that wait only after the map is ready.
                    if (mapReady &&
                        !deliveryReady &&
                        deliveryReadyTimer >= finalLevelReadyTimeoutSeconds)
                    {
                        break;
                    }

                    if (loadingScreen != null)
                    {
                        string readinessMessage =
                            !mapReady
                                ? readyTimer >= finalLevelReadyTimeoutSeconds
                                    ? "ArcGIS is still loading map tiles..."
                                    : "Finishing ArcGIS map tiles..."
                                : "Preparing delivery targets and minimap...";
                        float readinessProgress =
                            0.88f +
                            Mathf.Clamp01(
                                readyTimer / finalLevelReadyTimeoutSeconds
                            ) * 0.10f;

                        loadingScreen.SetProgress(
                            readinessProgress,
                            readinessMessage
                        );
                    }

                    yield return new WaitForSecondsRealtime(0.2f);
                    readyTimer += 0.2f;
                }

                if (!mapReady)
                {
                    string diagnostic = arcGISConverter != null
                        ? arcGISConverter.GetDiagnosticSummary()
                        : "ArcGISConverter is missing. Log=" + Application.consoleLogPath;

                    Debug.LogError(
                        "❌ Level objects finished, but ArcGIS did not become ready. " +
                        diagnostic
                    );

                    if (loadingScreen != null)
                    {
                        loadingScreen.ShowActionableError(
                            "ArcGIS map did not finish loading. " +
                            "Check your internet connection and restart the level. " +
                            "Diagnostic log: " +
                            Application.consoleLogPath
                        );
                    }

                    yield break;
                }

                if (waitForDeliveryTargetsBeforeHidingLoadingScreen &&
                    deliveryManager != null &&
                    (!deliveryManager.IsInitializationComplete ||
                     !deliveryManager.HasInitializedTargets))
                {
                    Debug.LogError(
                        "❌ Level generation finished without delivery targets."
                    );

                    if (loadingScreen != null)
                    {
                        loadingScreen.ShowError(
                            "No delivery houses were ready. Check generated buildings."
                        );
                    }

                    yield break;
                }

                if (loadingScreen != null)
                {
                    loadingScreen.SetProgress(
                        0.99f,
                        "Finalizing camera and minimap..."
                    );
                }

                int renderFrames = Mathf.Max(1, finalRenderFrameCount);
                for (int frame = 0; frame < renderFrames; frame++)
                {
                    yield return new WaitForEndOfFrame();
                }

                Debug.Log(
                    "✅ Final ArcGIS view and delivery systems are fully ready; " +
                    "hiding the loading screen."
                );

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

                // ArcGIS has completed the held final view. It may now resume
                // following the moving drone for normal gameplay streaming.
                arcGISConverter?.ReleaseReadinessViewHold();

                yield break;
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }

        Debug.LogError(
            "❌ Initialization timeout → aborting generation"
        );

        if (loadingScreen != null)
        {
            string diagnostic = arcGISConverter != null
                ? arcGISConverter.GetDiagnosticSummary()
                : "ArcGISConverter is missing. Log=" + Application.consoleLogPath;

            loadingScreen.ShowActionableError(
                "Loading timed out. On Windows, start with Start SantaTrail.bat " +
                "and confirm the Microsoft Visual C++ 2015-2022 x64 runtime is installed. " +
                diagnostic
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

    bool ActivateEmergencyLocalLevel(string reason)
    {
        InitializeFallbackOriginFromHome();
        if (!CanUseFallbackProjection())
        {
            return false;
        }

        const int gridSize = 25;
        Result fallbackResult = new Result
        {
            scene = "plain",
            features = new Features
            {
                elevation_range = 4f,
                building_density = 0f,
                water_present = false,
                vegetation_density = 12f
            },
            waypoints = new[]
            {
                new GPSWaypoint
                {
                    lat = (float)GameManager.homeLat,
                    lon = (float)GameManager.homeLon,
                    alt = (float)GameManager.homeAlt
                }
            },
            rivers = new GPSWaypoint[0][],
            elevation_data = new float[gridSize * gridSize],
            grid_size = gridSize,
            buildings = new Building[0],
            roads = new Road[0]
        };

        validatedJsonContent = JsonUtility.ToJson(fallbackResult);
        Debug.LogWarning(
            "⚠️ Using the built-in local terrain and village because " + reason + "."
        );

        if (loadingScreen != null)
        {
            loadingScreen.SetProgress(
                0.38f,
                "Using the built-in delivery village on this device..."
            );
        }

        return IsValidJson(validatedJsonContent);
    }

    void StopPythonForLocalFallback()
    {
        try
        {
            if (pythonProcess != null && !pythonProcess.HasExited)
            {
                pythonProcess.Kill();
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning(
                "⚠️ Could not stop the timed-out terrain backend: " +
                exception.Message
            );
        }
    }

    void InitializeFallbackOriginFromHome()
    {
        if (!allowLocalGpsFallback || fallbackOriginSet)
        {
            return;
        }

        if (!GameManager.hasHomeLocation)
        {
            return;
        }

        if (double.IsNaN(GameManager.homeLat) ||
            double.IsInfinity(GameManager.homeLat) ||
            double.IsNaN(GameManager.homeLon) ||
            double.IsInfinity(GameManager.homeLon) ||
            System.Math.Abs(GameManager.homeLat) > 90.0 ||
            System.Math.Abs(GameManager.homeLon) > 180.0)
        {
            return;
        }

        fallbackOriginLat = GameManager.homeLat;
        fallbackOriginLon = GameManager.homeLon;
        fallbackOriginSet = true;
    }

    bool TryReadCachedOutputForCurrentHome(
        string path,
        out string json)
    {
        json = null;

        try
        {
            string candidate = File.ReadAllText(path);
            if (!CanUseJsonForCurrentHome(
                    candidate,
                    requireBuildingData: spawnOsmBuildings
                ))
            {
                return false;
            }

            json = candidate;
            return true;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("⚠️ Cached map data could not be read: " + exception.Message);
            return false;
        }
    }

    string BuildLocationCacheFileName()
    {
        double latitude = GameManager.hasHomeLocation
            ? GameManager.homeLat
            : mapOriginLatitude;
        double longitude = GameManager.hasHomeLocation
            ? GameManager.homeLon
            : mapOriginLongitude;

        // Four decimal places are roughly an 11m latitude cell. This absorbs
        // harmless GPS jitter while keeping neighboring level areas separate.
        long latitudeKey = (long)System.Math.Round(
            latitude * 10000.0,
            System.MidpointRounding.AwayFromZero
        );
        long longitudeKey = (long)System.Math.Round(
            longitude * 10000.0,
            System.MidpointRounding.AwayFromZero
        );
        long radiusKey = (long)System.Math.Round(
            System.Math.Max(1.0, osmQueryRadiusMeters),
            System.MidpointRounding.AwayFromZero
        );

        return string.Format(
            CultureInfo.InvariantCulture,
            "output_v2_{0}_{1}_{2}.json",
            latitudeKey,
            longitudeKey,
            radiusKey
        );
    }

    bool CanUseJsonForCurrentHome(
        string json,
        bool requireBuildingData)
    {
        if (!IsValidJson(json) || !GameManager.hasHomeLocation)
        {
            return false;
        }

        try
        {
            Result cached = JsonUtility.FromJson<Result>(json);
            if (cached == null ||
                cached.waypoints == null ||
                cached.waypoints.Length == 0)
            {
                return false;
            }

            if (requireBuildingData &&
                (cached.buildings == null || cached.buildings.Length == 0))
            {
                // Do not make a transient empty OSM response sticky. With no
                // usable cache, this run will refresh and can still generate
                // the built-in fallback village if the network remains down.
                return false;
            }

            GPSWaypoint origin = cached.waypoints[0];
            double latitudeMeters =
                (origin.lat - GameManager.homeLat) * 111320.0;
            double longitudeMeters =
                (origin.lon - GameManager.homeLon) *
                111320.0 *
                System.Math.Cos(GameManager.homeLat * System.Math.PI / 180.0);
            double distanceMeters = System.Math.Sqrt(
                latitudeMeters * latitudeMeters +
                longitudeMeters * longitudeMeters
            );

            double locationToleranceMeters = System.Math.Min(
                25.0,
                System.Math.Max(5.0, osmQueryRadiusMeters * 0.05)
            );

            return distanceMeters <= locationToleranceMeters;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("⚠️ Map data location could not be validated: " + exception.Message);
            return false;
        }
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
        string json = validatedJsonContent;
        if (string.IsNullOrEmpty(json))
        {
            json = File.ReadAllText(outputPath);
        }

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

        bool hasArcGISProjection =
            arcGISConverter != null &&
            arcGISConverter.CanProjectCoordinates();
        bool useLocalTerrainFallback =
            useArcGISTerrainOnly &&
            !hasArcGISProjection &&
            CanUseFallbackProjection();
        localTerrainFallbackActive = useLocalTerrainFallback;

        if (!useArcGISTerrainOnly || useLocalTerrainFallback)
        {
            if (useLocalTerrainFallback)
            {
                Debug.LogWarning(
                    "⚠️ ArcGIS is unavailable; generating the local terrain fallback."
                );
            }

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

        if (!GenerateSceneSafe(result))
        {
            return false;
        }

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
