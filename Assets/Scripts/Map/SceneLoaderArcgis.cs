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


public partial class SceneLoaderArcgis : MonoBehaviour
{
    [Header("Camera")]
    public Camera droneCamera;
    public float scale = 1000f;
    ArcGISMapComponent arcGISMap;
    Transform arcGISRoot;
    [Header("Level Origin")]
    [Tooltip("When enabled, this scene sets the ArcGIS map origin from the values below.")]
    [SerializeField] bool overrideMapOrigin = false;
    [Tooltip("Keep the ArcGIS origin saved in this scene. Enable this for levels with manually placed buildings.")]
    [SerializeField] bool lockSceneMapOriginForManualBuildings = false;

    [Header("Manual Building Level Runtime")]
    [Tooltip("When the scene-origin lock is enabled, keep the drone at the position saved in the Unity scene instead of moving it to the generated JSON waypoint.")]
    [SerializeField] bool keepManuallyPlacedDronePosition = true;

    [Tooltip("When the scene-origin lock is enabled, do not create generated waypoint paths or river line renderers from GPS data.")]
    [SerializeField] bool skipGeneratedGpsObjectsInManualLevel = true;

    [SerializeField] double mapOriginLongitude;
    [SerializeField] double mapOriginLatitude;
    [SerializeField] double mapOriginAltitude;
    [Tooltip("Restrict ArcGIS streaming and generated houses to a circular playable area around the map origin.")]
    [SerializeField] bool limitMapExtent = false;
    [Min(1f)]
    [Tooltip("Radius of the playable ArcGIS map area in metres.")]
    [SerializeField] double mapExtentSizeMeters = 500f;
    [Header("ArcGIS")]
    [SerializeField] ArcGISConverter arcGISConverter;

    [Header("Loading Screen")]
    [Tooltip("Full-screen loading UI shown while Python, JSON and ArcGIS initialize.")]
    [SerializeField] LoadingScreenUI loadingScreen;
    [Min(0f)]
    [SerializeField] float minimumLoadingScreenSeconds = 1.25f;
    [Tooltip("Keep loading visible until ArcGIS can convert geographic positions.")]
    [SerializeField] bool waitForArcGISBeforeHidingLoadingScreen = true;
    [Tooltip("Keep loading visible until delivery targets are selected from generated houses.")]
    [SerializeField] bool waitForDeliveryTargetsBeforeHidingLoadingScreen = true;
    [Min(1f)]
    [Tooltip("Maximum final wait for ArcGIS and delivery systems after object generation.")]
    [SerializeField] float finalLevelReadyTimeoutSeconds = 90f;
    [Range(1, 10)]
    [Tooltip("Extra completed frames rendered before the loading overlay fades out.")]
    [SerializeField] int finalRenderFrameCount = 3;

    float loadingScreenShownAt;
    [Header("Fallback")]
    [SerializeField] bool allowLocalGpsFallback = true;
    [Header("Generation Mode")]
    [SerializeField] bool useArcGISTerrainOnly = true;
    [Header("Building Spawn")]
    [Tooltip("Enable this to generate building prefabs from OSM footprints. Disable it in levels that use manually placed building assets.")]
    [SerializeField] bool spawnOsmBuildings = true;
    [SerializeField] float buildingSpawnRadius = 500f;
    [SerializeField] bool filterBuildingsByDroneDistance = false;
    [SerializeField] bool showSpawnRadius = true;
    [SerializeField] bool hideOsmBuildingLayer = true;
    [SerializeField] bool logLayerVisibilityChanges = true;
    [SerializeField] float buildingYawCorrectionDegrees = 0f;
    [Header("Placement Safety")]
    [Tooltip("Validates OSM building footprints against roads, water, and other generated buildings.")]
    [SerializeField] OsmPlacementValidator placementValidator;

    public enum BuildingGenerationMode
    {
        CompletePrefab,
        ModularFootprint,
        Hybrid
    }

    [Header("OSM Building Generation")]
    [Tooltip("Complete Prefab keeps the old stretched-house system. Modular Footprint builds every house from wall components. Hybrid now follows the same wall-based path so every OSM house gets 3D wall geometry.")]
    [SerializeField] BuildingGenerationMode buildingGenerationMode =
        BuildingGenerationMode.ModularFootprint;

    [Tooltip("Generator that owns the wall, window, door and roof component settings. Add ModularHouseGenerator to this same GameObject and assign it here. If empty at runtime, a cube-based fallback generator is added automatically.")]
    [SerializeField] ModularHouseGenerator modularHouseGenerator;

    [Tooltip("In Hybrid mode, footprints using less than this fraction of their fitted rectangle are treated as irregular.")]
    [Range(0.50f, 1f)]
    [SerializeField] float modularFootprintFillThreshold = 0.92f;

    [Tooltip("Legacy compatibility flag kept in the scene data. OSM houses now always use the modular wall generator in non-CompletePrefab modes.")]
    [SerializeField] bool replacementRulesForceCompletePrefabs = true;

    [Tooltip("Optional world Y-axis correction for modular buildings. X and Z rotation are always kept at 0.")]
    [SerializeField] float modularBuildingYawCorrectionDegrees = 0f;

    [Tooltip("Small world-space height adjustment for modular houses after GPS conversion. Increase this if walls are slightly below the map surface.")]
    [SerializeField] float modularBuildingGroundOffset = 0.05f;

    [Range(1f, 1.5f)]
    [Tooltip(
        "Uniform X/Z expansion for walls built from an OSM footprint. " +
        "1 matches the source exactly; 1.3 makes the visible house fill " +
        "the wider ArcGIS building shadow."
    )]
    [SerializeField] float osmWallFootprintScale = 1.3f;

    [System.Serializable]
    public class BuildingOrientationRule
    {
        [Header("Rule Match")]
        [Tooltip("Assign the exact prefab this rule controls. This is the recommended matching method.")]
        public GameObject prefab;
        [Tooltip("Optional fallback match when Prefab is empty. The prefab name must contain this text.")]
        public string buildingNameContains;

        [Header("1. Asset Axis Rotation")]
        [Tooltip("Exact local X/Y/Z rotation that makes this asset stand upright. Any prefab with a rule uses this value instead of the global rotation. Example: X=0, Y=90, Z=-90.")]
        public Vector3 modelLocalRotationEuler = new Vector3(0f, 90f, -90f);

        [Header("2. OSM Map Rotation")]
        [Tooltip("Fine adjustment added to the OSM geographic heading. Use this only when the whole asset is consistently rotated on the map.")]
        public float rootYawCorrectionDegrees = 0f;
        [Tooltip("Manual top-view rotation applied after the OSM heading. Normally use 0, 90, -90, or 180.")]
        public float footprintYawDegrees = 0f;
        [Tooltip("When enabled, the fitter may also test Footprint Yaw + 90 degrees and choose the less distorted result.")]
        public bool autoTryAdditionalQuarterTurn = false;

        [Header("3. OSM Footprint Size")]
        [Tooltip("Fit this prefab to the OSM width and length. Disable this to keep the prefab's own size.")]
        public bool fitToOsmFootprint = true;
        [Min(0.01f)]
        [Tooltip("Multiplies the OSM width before fitting. Use values such as 0.9 or 1.1 for asset-specific correction.")]
        public float osmWidthMultiplier = 1f;
        [Min(0.01f)]
        [Tooltip("Multiplies the OSM length before fitting. Use values such as 0.9 or 1.1 for asset-specific correction.")]
        public float osmLengthMultiplier = 1f;
        [Tooltip("Optional extra width/length in metres added after the multipliers. X = width, Y = length.")]
        public Vector2 osmSizeOffsetMeters = Vector2.zero;
        [Tooltip("Additional local scale applied to the model before OSM fitting. Y controls the asset height only.")]
        public Vector3 modelLocalScaleMultiplier = Vector3.one;

        [Header("4. Footprint Centre")]
        [Tooltip("Move the measured prefab footprint centre onto the OSM rectangle centre.")]
        public bool centerModelOnFootprint = true;
        [Tooltip("Optional final local offset after automatic centring. X/Z move the asset across the footprint; Y changes its height offset.")]
        public Vector3 modelLocalPositionOffset = Vector3.zero;

        [Header("5. Bounds Measurement")]
        [Tooltip("Optional child object used only for footprint measurement. Leave empty to use the global FootprintBounds name.")]
        public string footprintBoundsChildName = "";
    }
    [SerializeField] BuildingOrientationRule[] buildingOrientationRules;

    [Header("Building Model Axis Correction")]
    [Tooltip("Fallback import-axis correction used only by prefabs that do not have a Building Orientation Rule.")]
    [SerializeField] Vector3 globalBuildingModelAxisCorrectionEuler = new Vector3(0f, 90f, -90f);
    [Tooltip("Optional extra fallback correction used only when no per-prefab rule matches.")]
    [SerializeField] Vector3 defaultBuildingModelLocalRotationEuler = Vector3.zero;

    [SerializeField] bool useExactOsmFootprintPlacement = true;
    [SerializeField] bool fitBuildingModelToFootprint = true;
    [SerializeField] float minimumFootprintDimension = 0.05f;
    [SerializeField] bool preferDedicatedFootprintBounds = true;
    [SerializeField] string defaultFootprintBoundsChildName = "FootprintBounds";
    [SerializeField] bool preservePrefabMaterialColors = true;
    [SerializeField] bool useRandomBuildingPalette = true;
    [SerializeField]
    Color[] buildingPalette =
    {
        new Color(0.90f, 0.35f, 0.35f, 1f),
        new Color(0.38f, 0.62f, 0.92f, 1f),
        new Color(0.84f, 0.76f, 0.40f, 1f),
        new Color(0.46f, 0.52f, 0.60f, 1f),
        new Color(0.76f, 0.44f, 0.30f, 1f)
    };
    float waterHeight = -1f;

    [Header("Python Script")]
    [Tooltip(
        "Optional Python executable override. Leave empty to use the " +
        "runtime bundled in StreamingAssets/Python or python3 from PATH."
    )]
    public string pythonPath = "";

    [Header("Expanded OSM Building Query")]
    [Tooltip(
        "Runs main_with_more_buildings.py instead of main.py. The wrapper " +
        "first runs the existing backend, then adds more OSM building " +
        "footprints to output.json."
    )]
    [SerializeField] bool fetchMoreOsmBuildings = true;

    [Tooltip(
        "Python wrapper filename placed beside the existing backend main.py."
    )]
    [SerializeField] string expandedBuildingScriptName =
        "main_with_more_buildings.py";

    [Tooltip(
        "OSM query radius in metres. Keep this slightly larger than the " +
        "ArcGIS map extent so edge buildings are not missed."
    )]
    [SerializeField] double osmQueryRadiusMeters = 500.0;

    string scriptPath;
    string outputPath;

    Process pythonProcess;
    volatile bool pythonProcessCompleted;
    volatile int pythonProcessExitCode = int.MinValue;
    [Header("Prefabs")]
    public GameObject treePrefab;
    [Tooltip("Optional tree palette used for environment detail. If empty, Tree Prefab is used.")]
    [SerializeField] GameObject[] environmentTreePrefabs;
    [Header("Environment Detail")]
    [SerializeField] bool spawnEnvironmentDetail = true;
    [Header("Environment Content")]
    [Tooltip("Allow tree props in this level.")]
    [SerializeField] bool spawnEnvironmentTrees = true;
    [Tooltip("Allow bush props in this level.")]
    [SerializeField] bool spawnEnvironmentBushes = true;
    [Tooltip("Allow decorative field patches in this level.")]
    [SerializeField] bool spawnFieldPatches = true;
    [SerializeField] bool spawnBackgroundTrees = false;
    [Min(0)]
    [SerializeField] int environmentTreesPerBuildingMin = 0;
    [Min(0)]
    [SerializeField] int environmentTreesPerBuildingMax = 1;
    [Min(0)]
    [SerializeField] int environmentBackgroundTreeCount = 0;
    [Min(0.1f)]
    [SerializeField] float environmentTreeDistanceMultiplier = 1.2f;
    [Min(0.1f)]
    [SerializeField] float environmentTreeScaleMin = 0.35f;
    [Min(0.1f)]
    [SerializeField] float environmentTreeScaleMax = 0.75f;
    [Tooltip("Maximum rendered tree height in metres after prefab scaling.")]
    [Min(0.5f)]
    [SerializeField] float environmentTreeMaximumHeight = 4.5f;
    [Tooltip("Chance that a building in a field-style level receives one nearby tree.")]
    [Range(0f, 1f)]
    [SerializeField] float fieldTreeChancePerBuilding = 0.18f;
    [SerializeField] bool spawnStreetLights = true;
    [Header("Roadside Street Lights")]
    [Tooltip("Place lights from real OSM road centerlines instead of randomly around houses.")]
    [SerializeField] bool spawnStreetLightsAlongRoads = true;
    [Min(5f)]
    [SerializeField] float roadsideStreetLightSpacingMeters = 32f;
    [Min(0.5f)]
    [SerializeField] float roadsideStreetLightOffsetMeters = 4f;
    [Min(0)]
    [SerializeField] int roadsideStreetLightMaximumCount = 40;
    [SerializeField] bool roadsideStreetLightsOnBothSides = true;
    [Min(0)]
    [SerializeField] int environmentBushesPerBuildingMin = 2;
    [Min(0)]
    [SerializeField] int environmentBushesPerBuildingMax = 5;
    [Min(0.1f)]
    [SerializeField] float environmentBushScaleMin = 0.55f;
    [Min(0.1f)]
    [SerializeField] float environmentBushScaleMax = 1.05f;
    [Min(0)]
    [SerializeField] int fieldPatchCountMin = 8;
    [Min(0)]
    [SerializeField] int fieldPatchCountMax = 18;
    [Min(0.1f)]
    [SerializeField] float fieldPatchScaleMin = 0.35f;
    [Min(0.1f)]
    [SerializeField] float fieldPatchScaleMax = 1.15f;
    public GameObject buildingPrefab;
    public GameObject[] randomHousePrefabs;
    [Header("Village Buildings")]
    public GameObject[] smallHousePrefabs;
    public GameObject[] mediumHousePrefabs;
    public GameObject[] largeBuildingPrefabs;
    [SerializeField] bool spawnVisibleVillageClusterOnly = false;
    [Tooltip(
        "Creates the nearby modular fallback village only when OSM returns no " +
        "buildings or every OSM footprint is rejected. This keeps delivery " +
        "targets available without weakening normal road, water, or overlap checks."
    )]
    [SerializeField] bool spawnFallbackVillageWhenNoOsmBuildings = true;
    [SerializeField] float smallBuildingAreaMax = 120f;
    [SerializeField] float mediumBuildingAreaMax = 900f;
    [System.Serializable]
    public class BuildingReplacementRule
    {
        public int buildingIndex;
        public GameObject prefab;
        public float scaleMultiplier = 1f;
        public Vector3 rotationEulerOffset;
    }

    [Header("Building Replacements")]
    [SerializeField] BuildingReplacementRule[] buildingReplacements;

    [Header("House LOD")]
    [Tooltip("Enable a cheap far-distance proxy for every spawned house so the level can contain more buildings without rendering all of them at once.")]
    [SerializeField] bool enableHouseSimpleLod = true;
    [Min(0f)]
    [Tooltip("When the viewer is closer than this distance, the detailed house geometry is shown.")]
    [SerializeField] float houseSimpleLodDetailDistance = 600f;
    [Min(0f)]
    [Tooltip("When the viewer is farther than this distance, the house swaps to a simple proxy cube.")]
    [SerializeField] float houseSimpleLodCullDistance = 900f;
    [Min(0.05f)]
    [Tooltip("How often each house checks the viewer distance.")]
    [SerializeField] float houseSimpleLodRefreshSeconds = 0.5f;

    struct GeoCoordinate
    {
        public double latitude;
        public double longitude;

        public GeoCoordinate(double latitude, double longitude)
        {
            this.latitude = latitude;
            this.longitude = longitude;
        }
    }

    struct RoadsideStreetLightPlacement
    {
        public double latitude;
        public double longitude;
        public float heading;
        public double distanceFromMapCenter;
        public int seed;
    }

    Terrain terrain;
    bool fallbackOriginSet;
    double fallbackOriginLat;
    double fallbackOriginLon;
    Coroutine mapExtentCoroutine;
    Transform generatedObjectsRoot;
    Transform generatedBuildingsRoot;
    Transform droneTransform;
    int visibleBuildingCounter;
    Transform generatedEnvironmentRoot;

    // The current level bootstrap lives in SceneLoaderArcgis.Bootstrap.cs.
    // Keep this retired implementation out of the compiled partial class so
    // Unity invokes only the loader that waits for ArcGIS before hiding UI.
#if false
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

        // This runs after LV1 has activated and its map home was set in Awake.
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
        bool pythonExitWarningLogged = false;

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

            if (pythonProcessCompleted &&
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
    #endif
    // =========================
    // TERRAIN (FIXED)
    // =========================
    void GenerateTerrain(Result result)
    {
        int size = result.grid_size;
        float elevationRange = result.features != null ? result.features.elevation_range : 100f;

        TerrainData terrainData = new TerrainData();
        terrainData.heightmapResolution = size + 1;
        float heightScale = elevationRange * 2f;


        terrainData.size = new Vector3(scale, Mathf.Clamp(heightScale, 50f, 800f), scale);
        float[,] heights = new float[size + 1, size + 1];

        float min = float.MaxValue;
        float max = float.MinValue;

        foreach (float h in result.elevation_data)
        {
            if (h > max) max = h;
            if (h < min) min = h;
        }

        float range = max - min;
        if (range < 0.01f) range = 1f;

        for (int y = 0; y < size + 1; y++)
        {
            for (int x = 0; x < size + 1; x++)
            {
                int index = Mathf.Clamp((size - 1 - y) * size + x, 0, result.elevation_data.Length - 1);

                float normalized = (result.elevation_data[index] - min) / range;

                float nx = (float)x / size;
                float ny = (float)y / size;

                float baseHeight = Mathf.Pow(normalized, 0.6f);

                float large = Mathf.PerlinNoise(nx * 3f, ny * 3f);
                float ridge = Mathf.Abs(Mathf.PerlinNoise(nx * 8f, ny * 8f) - 0.5f);
                ridge = 1f - ridge;
                ridge *= ridge;

                float detail = Mathf.PerlinNoise(nx * 20f, ny * 20f) * 0.15f;
                float height;

                if (result.scene == "urban")
                {
                    height = baseHeight * 0.98f;

                    height += detail * 0.005f;
                }
                else if (result.scene == "plain")
                {
                    height = baseHeight * 0.95f + detail * 0.01f;
                }
                else if (result.scene == "forest")
                {
                    height = baseHeight * 0.8f + large * 0.15f + detail * 0.05f;
                }
                else
                {
                    height = baseHeight * 0.6f + large * 0.25f + ridge * 0.15f;
                }

                heights[y, x] = Mathf.Clamp01(height);
            }
        }

        terrainData.SetHeights(0, 0, heights);

        GameObject terrainObj = Terrain.CreateTerrainGameObject(terrainData);
        terrainObj.transform.position = new Vector3(-scale / 2, 0, -scale / 2);

        terrain = terrainObj.GetComponent<Terrain>();

        DroneAltitudeMapper mapper = FindFirstObjectByType<DroneAltitudeMapper>();
        if (mapper != null)
        {
            mapper.terrain = terrain;
        }

        Debug.Log($"🌄 Terrain generated | min:{min} max:{max} range:{range}");

        if (droneTransform != null)
        {
            Vector3 pos = droneTransform.position;

            float ground = terrain.SampleHeight(pos);

            // Initial placement is grounded. Flight altitude comes from MAVLink
            // only after the vehicle has actually taken off.
            pos.y = ground;
            droneTransform.position = pos;

            Debug.Log("🚁 Drone placed on terrain");
        }
    }

    void GenerateWater()
    {
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        int res = data.heightmapResolution;

        float minHeight = float.MaxValue;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float h = data.GetHeight(x, y);
                if (h < minHeight) minHeight = h;
            }
        }

        waterHeight = minHeight + 3f;

        GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.transform.localScale = new Vector3(scale / 10f, 1, scale / 10f);
        water.transform.position = new Vector3(0, waterHeight, 0);

        var renderer = water.GetComponent<Renderer>();
        renderer.material.color = new Color(0.2f, 0.4f, 0.8f, 0.6f);

        Debug.Log($"🌊 Water height = {waterHeight} (min terrain = {minHeight})");
    }

    // =========================
    // OBJECTS
    // =========================
    void GenerateScene(Result result)
    {
        ClearGeneratedContent();

        // Roadside props use the same OSM road data in both terrain modes.
        // Keep this separate from terrain generation so the setting is reliable
        // when a level switches between ArcGIS and Unity terrain.
        SpawnStreetLightsAlongRoads(result);

        if (useArcGISTerrainOnly)
        {
            SpawnVillageBuildings(result);
            SpawnEnvironmentDetail(result);
            SpawnFieldDetail(result);

            Debug.Log("🌍 ArcGIS-only mode: skipped Unity terrain trees/water");
            return;
        }

        float veg = result.features != null ? result.features.vegetation_density : 0f;
        float build = result.features != null ? result.features.building_density : 0f;

        if (result.scene == "urban")
        {
            veg = 5f;
            build = 50f;
        }

        if (result.scene == "plain")
        {
            veg = 10f;
            build = 5f;
        }

        if (veg <= 0) veg = UnityEngine.Random.Range(5f, 20f);
        if (build <= 0) build = UnityEngine.Random.Range(20f, 60f);

        int treeCount = Mathf.Clamp((int)(veg * 10), 20, 300);
        SpawnTrees(treeCount);

        SpawnVillageBuildings(result);
        SpawnEnvironmentDetail(result);
        SpawnFieldDetail(result);

        Debug.Log($"🌳 Trees: {treeCount} | 🏙 Buildings: {(build > 10 ? "YES" : "NO")}");
    }

    void SpawnTrees(int count)
    {
        if (!spawnEnvironmentTrees || treePrefab == null || terrain == null) return;

        int spawned = 0;
        int attempts = 0;

        while (spawned < count && attempts < count * 10)
        {
            attempts++;

            Vector3 pos = new Vector3(
                UnityEngine.Random.Range(-scale / 2, scale / 2),
                0,
                UnityEngine.Random.Range(-scale / 2, scale / 2)
            );

            pos.y = terrain.SampleHeight(pos);

            if (waterHeight > 0 && pos.y <= waterHeight + 2f)
                continue;

            float slope = terrain.terrainData.GetSteepness(
                (pos.x + scale / 2) / scale,
                (pos.z + scale / 2) / scale
            );

            if (slope > 35f) continue;

            Quaternion rot = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0);
            float s = UnityEngine.Random.Range(0.8f, 1.6f);

            GameObject tree = Instantiate(treePrefab, pos, rot);
            tree.transform.localScale *= s;

            spawned++;
        }

        Debug.Log($"🌳 Spawned {spawned} trees (safe)");
    }

    void SpawnEnvironmentDetail(Result result)
    {
        if (!spawnEnvironmentDetail || !spawnEnvironmentTrees || result == null)
        {
            return;
        }

        float vegetationDensity =
            result.features != null
                ? result.features.vegetation_density
                : 0f;

        int backgroundCount = 0;

        if (spawnBackgroundTrees)
        {
            backgroundCount =
                environmentBackgroundTreeCount +
                Mathf.RoundToInt(vegetationDensity * 0.08f);

            if (result.scene == "forest")
            {
                backgroundCount += 10;
            }
            else if (result.scene == "plain")
            {
                backgroundCount = Mathf.Min(backgroundCount, 2);
            }
            else if (result.scene == "urban")
            {
                backgroundCount = Mathf.Max(0, backgroundCount - 8);
            }

            backgroundCount = Mathf.Clamp(backgroundCount, 0, 24);
        }

        Vector3 anchorCenter;
        Vector3 anchorForward;

        if (!TryGetVisibleVillageAnchor(result, out anchorCenter, out anchorForward))
        {
            Transform fallbackAnchor =
                droneTransform != null
                    ? droneTransform
                    : droneCamera != null
                        ? droneCamera.transform
                        : Camera.main != null
                            ? Camera.main.transform
                            : null;

            anchorCenter =
                fallbackAnchor != null
                    ? fallbackAnchor.position
                    : Vector3.zero;
            anchorForward =
                fallbackAnchor != null
                    ? fallbackAnchor.forward
                    : Vector3.forward;
        }

        anchorForward.y = 0f;
        if (anchorForward.sqrMagnitude < 0.0001f)
        {
            anchorForward = Vector3.forward;
        }
        anchorForward.Normalize();

        Vector3 anchorRight = Vector3.Cross(Vector3.up, anchorForward).normalized;
        float minRadius = Mathf.Clamp(buildingSpawnRadius * 0.30f, 12f, 45f);
        float maxRadius = Mathf.Clamp(buildingSpawnRadius * 0.80f, minRadius + 10f, 120f);

        Transform environmentRoot = GetGeneratedEnvironmentRoot();

        for (int i = 0; i < backgroundCount; i++)
        {
            float angle =
                (i * 137.5f +
                 UnityEngine.Random.Range(-15f, 15f)) *
                Mathf.Deg2Rad;

            float radius =
                UnityEngine.Random.Range(minRadius, maxRadius);

            Vector3 offset =
                (anchorRight * Mathf.Cos(angle) +
                 anchorForward * Mathf.Sin(angle)) *
                radius;

            Vector3 worldPosition = anchorCenter + offset;
            worldPosition.y = anchorCenter.y;

            SpawnEnvironmentTree(
                worldPosition,
                i * 7919 + 17,
                vegetationDensity,
                result.scene,
                environmentRoot
            );
        }
    }

    void SpawnEnvironmentDetailAroundBuilding(
        Vector3 buildingCenter,
        float width,
        float depth,
        float area,
        int buildingIndex,
        string sceneName)
    {
        if (!spawnEnvironmentDetail)
        {
            return;
        }

        // Keep props under a neutral root so they do not inherit a house's
        // footprint scale or delayed ArcGIS rotation.
        Transform detailParent = GetGeneratedEnvironmentRoot();

        int treeCount =
            UnityEngine.Random.Range(
                environmentTreesPerBuildingMin,
                environmentTreesPerBuildingMax + 1
            );

        int bushCount =
            UnityEngine.Random.Range(
                environmentBushesPerBuildingMin,
                environmentBushesPerBuildingMax + 1
            );

        if (sceneName == "forest")
        {
            treeCount += 1;
            bushCount += 1;
        }
        else if (sceneName == "urban")
        {
            treeCount = Mathf.Max(0, treeCount - 1);
            bushCount = Mathf.Max(1, bushCount - 1);
        }
        else
        {
            // LV1 uses a field-like map area, so most houses should not add a tree.
            treeCount = UnityEngine.Random.value <= fieldTreeChancePerBuilding
                ? 1
                : 0;
            bushCount = Mathf.Clamp(bushCount, 2, 4);
        }

        treeCount = Mathf.Clamp(treeCount, 0, 1);
        bushCount = Mathf.Clamp(bushCount, 0, 4);

        if (!spawnEnvironmentTrees)
        {
            treeCount = 0;
        }

        if (!spawnEnvironmentBushes)
        {
            bushCount = 0;
        }

        float baseRadius =
            Mathf.Max(width, depth) * environmentTreeDistanceMultiplier +
            1.5f;
        float maxRadius = baseRadius + Mathf.Clamp(area / 140f, 1.5f, 6f);

        for (int i = 0; i < treeCount; i++)
        {
            float angle =
                UnityEngine.Random.Range(0f, 360f) *
                Mathf.Deg2Rad;

            float radius =
                UnityEngine.Random.Range(baseRadius, maxRadius);

            Vector3 offset = new Vector3(
                Mathf.Cos(angle),
                0f,
                Mathf.Sin(angle)
            ) * radius;

            Vector3 worldPosition = buildingCenter + offset;
            worldPosition.y = buildingCenter.y;

            SpawnEnvironmentTree(
                worldPosition,
                buildingIndex * 104729 + i * 97,
                0f,
                sceneName,
                detailParent
            );
        }

        float houseHalfWidth = Mathf.Max(width * 0.5f, 1.5f);
        float houseHalfDepth = Mathf.Max(depth * 0.5f, 1.5f);
        float edgeMargin = Mathf.Clamp(
            Mathf.Max(width, depth) * 0.18f,
            1.2f,
            4.5f
        );

        float bushClusterRadius = Mathf.Sqrt(
            houseHalfWidth * houseHalfWidth +
            houseHalfDepth * houseHalfDepth
        ) + edgeMargin;
        float bushClusterHeading = UnityEngine.Random.Range(0f, 360f);
        for (int i = 0; i < bushCount; i++)
        {
            // Split the bushes into one or two loose clumps instead of putting
            // one at every building corner.
            float clusterSide = (i % 2) * 180f;
            float heading = bushClusterHeading + clusterSide +
                UnityEngine.Random.Range(-28f, 28f);
            Vector3 outward = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            Vector3 sideways = new Vector3(outward.z, 0f, -outward.x);
            float radialJitter = UnityEngine.Random.Range(-0.35f, 1.8f);
            float sideJitter = UnityEngine.Random.Range(-1.8f, 1.8f);

            Vector3 worldPosition = buildingCenter +
                outward * (bushClusterRadius + radialJitter) +
                sideways * sideJitter;
            worldPosition.y = buildingCenter.y + 0.05f;

            SpawnEnvironmentBush(
                worldPosition,
                buildingIndex * 65537 + i * 131,
                sceneName,
                detailParent
            );
        }

    }

    void SpawnFieldDetail(Result result)
    {
        if (!spawnEnvironmentDetail ||
            !spawnFieldPatches ||
            result == null ||
            result.buildings == null)
        {
            return;
        }

        List<Vector3> buildingCenters = new List<Vector3>();

        for (int i = 0; i < result.buildings.Length; i++)
        {
            var building = result.buildings[i];
            if (building == null || building.points == null || building.points.Length < 3)
            {
                continue;
            }

            double latSum = 0.0;
            double lonSum = 0.0;

            for (int p = 0; p < building.points.Length; p++)
            {
                latSum += building.points[p].lat;
                lonSum += building.points[p].lon;
            }

            double centerLat = latSum / building.points.Length;
            double centerLon = lonSum / building.points.Length;

            if (TryConvertGPS(centerLat, centerLon, 0, out Vector3 worldCenter))
            {
                buildingCenters.Add(worldCenter);
            }
        }

        if (buildingCenters.Count == 0)
        {
            return;
        }

        Vector3 min = buildingCenters[0];
        Vector3 max = buildingCenters[0];

        for (int i = 1; i < buildingCenters.Count; i++)
        {
            Vector3 point = buildingCenters[i];
            min.x = Mathf.Min(min.x, point.x);
            min.z = Mathf.Min(min.z, point.z);
            max.x = Mathf.Max(max.x, point.x);
            max.z = Mathf.Max(max.z, point.z);
        }

        Vector3 spread = max - min;
        float padding = Mathf.Clamp(
            Mathf.Max(spread.x, spread.z) * 0.15f,
            8f,
            30f
        );

        min.x -= padding;
        min.z -= padding;
        max.x += padding;
        max.z += padding;

        Transform root = GetGeneratedEnvironmentRoot();
        int detailCount = UnityEngine.Random.Range(
            fieldPatchCountMin,
            fieldPatchCountMax + 1
        );
        detailCount = Mathf.Clamp(detailCount, 4, 14);

        for (int i = 0; i < detailCount; i++)
        {
            Vector3 position = new Vector3(
                UnityEngine.Random.Range(min.x, max.x),
                buildingCenters[UnityEngine.Random.Range(0, buildingCenters.Count)].y,
                UnityEngine.Random.Range(min.z, max.z)
            );

            SpawnFieldPatch(
                position,
                9000 + i * 37,
                result.scene,
                root
            );
        }
    }

    void SpawnEnvironmentTree(
        Vector3 worldPosition,
        int seed,
        float vegetationDensity,
        string sceneName,
        Transform parent)
    {
        if (!spawnEnvironmentTrees)
        {
            return;
        }

        GameObject prefab = GetEnvironmentTreePrefab(seed);

        if (prefab == null)
        {
            return;
        }

        float scale = UnityEngine.Random.Range(
            environmentTreeScaleMin,
            environmentTreeScaleMax
        );

        if (sceneName == "forest")
        {
            scale *= 1.15f;
        }
        else if (sceneName == "urban")
        {
            scale *= 0.85f;
        }
        else
        {
            scale *= 0.65f;
        }

        scale *= Mathf.Lerp(0.90f, 1.15f, Mathf.Clamp01(vegetationDensity / 100f));

        GameObject tree = Instantiate(
            prefab,
            worldPosition,
            Quaternion.Euler(
                0f,
                UnityEngine.Random.Range(0f, 360f),
                0f
            )
        );

        tree.name = $"{prefab.name}_EnvDetail";
        tree.transform.localScale *= scale;

        if (parent != null)
        {
            tree.transform.SetParent(parent, true);
        }

        ClampEnvironmentTreeHeight(tree, sceneName);
    }

    void ClampEnvironmentTreeHeight(GameObject tree, string sceneName)
    {
        Renderer[] renderers = tree.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = default;
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds || bounds.size.y <= 0.01f)
        {
            return;
        }

        float maximumHeight = environmentTreeMaximumHeight;
        if (sceneName == "forest")
        {
            maximumHeight *= 1.5f;
        }

        if (bounds.size.y > maximumHeight)
        {
            tree.transform.localScale *= maximumHeight / bounds.size.y;
        }
    }

    void SpawnEnvironmentBush(
        Vector3 worldPosition,
        int seed,
        string sceneName,
        Transform parent)
    {
        if (!spawnEnvironmentBushes)
        {
            return;
        }

        GameObject bush = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        bush.name = $"Bush_{seed}";
        bush.transform.position = worldPosition;
        bush.transform.rotation = Quaternion.Euler(
            0f,
            (seed % 360 + 360) % 360,
            0f
        );
        bush.transform.localScale = new Vector3(
            UnityEngine.Random.Range(environmentBushScaleMin, environmentBushScaleMax) * 1.15f,
            UnityEngine.Random.Range(environmentBushScaleMin * 0.35f, environmentBushScaleMax * 0.55f),
            UnityEngine.Random.Range(environmentBushScaleMin, environmentBushScaleMax) * 1.15f
        );

        if (sceneName != "forest")
        {
            bush.transform.localScale *= 0.9f;
        }

        if (terrain != null)
        {
            Vector3 groundedPosition = bush.transform.position;
            groundedPosition.y = terrain.SampleHeight(groundedPosition);
            bush.transform.position = groundedPosition;
        }

        Collider collider = bush.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        Renderer renderer = bush.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader != null)
            {
                Material material = new Material(shader);
                material.color =
                    sceneName == "forest"
                        ? new Color(0.16f, 0.35f, 0.12f, 1f)
                        : new Color(0.22f, 0.38f, 0.15f, 1f);
                renderer.sharedMaterial = material;
            }
        }

        if (parent != null)
        {
            bush.transform.SetParent(parent, true);
        }
    }

    void SpawnFieldPatch(
        Vector3 worldPosition,
        int seed,
        string sceneName,
        Transform parent)
    {
        GameObject patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        patch.name = $"FieldPatch_{seed}";
        patch.transform.position = worldPosition;
        patch.transform.rotation = Quaternion.Euler(
            0f,
            (seed % 360 + 360) % 360,
            0f
        );

        float xScale = UnityEngine.Random.Range(fieldPatchScaleMin, fieldPatchScaleMax);
        float zScale = UnityEngine.Random.Range(fieldPatchScaleMin, fieldPatchScaleMax);
        float yScale = UnityEngine.Random.Range(0.03f, 0.08f);
        patch.transform.localScale = new Vector3(xScale, yScale, zScale);

        Collider collider = patch.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        Renderer renderer = patch.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader != null)
            {
                Material material = new Material(shader);
                material.color =
                    sceneName == "forest"
                        ? new Color(0.34f, 0.50f, 0.18f, 1f)
                        : new Color(0.42f, 0.58f, 0.22f, 1f);
                renderer.sharedMaterial = material;
            }
        }

        if (parent != null)
        {
            patch.transform.SetParent(parent, true);
        }
    }

    void SpawnStreetLightsAlongRoads(Result result)
    {
        if (!spawnStreetLights || !spawnStreetLightsAlongRoads)
        {
            return;
        }

        if (roadsideStreetLightMaximumCount <= 0)
        {
            Debug.LogWarning("🏮 Roadside street lights skipped: maximum count is 0.");
            return;
        }

        if (result == null || result.roads == null || result.roads.Length == 0)
        {
            Debug.LogWarning(
                "🏮 Roadside street lights skipped: OSM returned no road centerlines."
            );
            return;
        }

        float spacing = Mathf.Max(5f, roadsideStreetLightSpacingMeters);
        float roadsideOffset = Mathf.Max(0.5f, roadsideStreetLightOffsetMeters);
        List<RoadsideStreetLightPlacement> placements =
            new List<RoadsideStreetLightPlacement>();
        HashSet<string> occupiedLocations = new HashSet<string>();

        for (int roadIndex = 0; roadIndex < result.roads.Length; roadIndex++)
        {
            Road road = result.roads[roadIndex];
            if (road == null ||
                road.points == null ||
                road.points.Length < 2 ||
                !IsSupportedStreetLightRoad(road.highway))
            {
                continue;
            }

            double distanceToNextLight = spacing * 0.5;

            for (int pointIndex = 0; pointIndex < road.points.Length - 1; pointIndex++)
            {
                GPSPoint start = road.points[pointIndex];
                GPSPoint end = road.points[pointIndex + 1];
                if (!IsValidRoadPoint(start) || !IsValidRoadPoint(end))
                {
                    continue;
                }

                double averageLatitude = (start.lat + end.lat) * 0.5;
                const double metersPerDegreeLatitude = 111320.0;
                double metersPerDegreeLongitude =
                    metersPerDegreeLatitude *
                    System.Math.Cos(averageLatitude * System.Math.PI / 180.0);
                double eastMeters =
                    (end.lon - start.lon) * metersPerDegreeLongitude;
                double northMeters =
                    (end.lat - start.lat) * metersPerDegreeLatitude;
                double segmentLength = System.Math.Sqrt(
                    eastMeters * eastMeters + northMeters * northMeters
                );

                if (segmentLength < 0.5 ||
                    System.Math.Abs(metersPerDegreeLongitude) < 0.000001)
                {
                    continue;
                }

                double rightEast = northMeters / segmentLength;
                double rightNorth = -eastMeters / segmentLength;

                while (distanceToNextLight <= segmentLength)
                {
                    double t = distanceToNextLight / segmentLength;
                    double centerLatitude =
                        start.lat + (end.lat - start.lat) * t;
                    double centerLongitude =
                        start.lon + (end.lon - start.lon) * t;
                    int sideCount = roadsideStreetLightsOnBothSides ? 2 : 1;

                    for (int sideIndex = 0; sideIndex < sideCount; sideIndex++)
                    {
                        // Use one fixed side when requested. Alternating by
                        // OSM way index can put lights on both sides of the
                        // same physical street because one street is often
                        // split into many way segments.
                        int side = roadsideStreetLightsOnBothSides
                            ? (sideIndex == 0 ? -1 : 1)
                            : -1;
                        double offsetEast = rightEast * roadsideOffset * side;
                        double offsetNorth = rightNorth * roadsideOffset * side;
                        double latitude =
                            centerLatitude + offsetNorth / metersPerDegreeLatitude;
                        double longitude =
                            centerLongitude + offsetEast / metersPerDegreeLongitude;

                        if (!IsInsideConfiguredMapExtent(
                                latitude,
                                longitude,
                                out double distanceFromMapCenter))
                        {
                            continue;
                        }

                        string locationKey =
                            $"{System.Math.Round(latitude, 5):F5}:" +
                            $"{System.Math.Round(longitude, 5):F5}";
                        if (!occupiedLocations.Add(locationKey))
                        {
                            continue;
                        }

                        double towardRoadEast = -rightEast * side;
                        double towardRoadNorth = -rightNorth * side;
                        float heading = NormalizeHeadingDegrees((float)(
                            System.Math.Atan2(towardRoadEast, towardRoadNorth) *
                            180.0 / System.Math.PI
                        ));

                        placements.Add(new RoadsideStreetLightPlacement
                        {
                            latitude = latitude,
                            longitude = longitude,
                            heading = heading,
                            distanceFromMapCenter = distanceFromMapCenter,
                            seed = roadIndex * 100000 + pointIndex * 100 + sideIndex
                        });
                    }

                    distanceToNextLight += spacing;
                }

                distanceToNextLight -= segmentLength;
            }
        }

        placements.Sort((a, b) =>
            a.distanceFromMapCenter.CompareTo(b.distanceFromMapCenter));

        int spawnCount = Mathf.Min(
            roadsideStreetLightMaximumCount,
            placements.Count
        );

        if (spawnCount == 0)
        {
            Debug.LogWarning(
                $"🏮 Roadside street lights skipped: {result.roads.Length} OSM roads " +
                "were returned, but none produced valid placements inside the map extent."
            );
            return;
        }

        Transform environmentRoot = GetGeneratedEnvironmentRoot();

        for (int i = 0; i < spawnCount; i++)
        {
            RoadsideStreetLightPlacement placement = placements[i];
            SpawnArcGISStreetLight(
                placement.latitude,
                placement.longitude,
                placement.heading,
                placement.seed,
                result.scene,
                environmentRoot
            );
        }

        Debug.Log(
            $"🏮 Spawned {spawnCount} roadside street lights from " +
            $"{result.roads.Length} OSM roads."
        );
    }

    bool IsSupportedStreetLightRoad(string highway)
    {
        switch ((highway ?? "").Trim().ToLowerInvariant())
        {
            case "motorway":
            case "trunk":
            case "primary":
            case "secondary":
            case "tertiary":
            case "residential":
            case "unclassified":
            case "living_street":
                return true;
            default:
                return false;
        }
    }

    bool IsValidRoadPoint(GPSPoint point)
    {
        return point != null &&
               !float.IsNaN(point.lat) &&
               !float.IsInfinity(point.lat) &&
               !float.IsNaN(point.lon) &&
               !float.IsInfinity(point.lon);
    }

    void SpawnArcGISStreetLight(
        double latitude,
        double longitude,
        float heading,
        int seed,
        string sceneName,
        Transform parent)
    {
        GameObject lightRoot = new GameObject($"RoadStreetLight_{seed}");
        if (parent != null)
        {
            lightRoot.transform.SetParent(parent, false);
        }

        ArcGISLocationComponent location =
            lightRoot.AddComponent<ArcGISLocationComponent>();
        location.SurfacePlacementMode = ArcGISSurfacePlacementMode.OnTheGround;
        location.SurfacePlacementOffset = 0.05;
        location.Position = new ArcGISPoint(
            longitude,
            latitude,
            0,
            ArcGISSpatialReference.WGS84()
        );
        location.Rotation = new ArcGISRotation(0, 0, 0);

        GameObject uprightObject = new GameObject("UprightVisualRoot");
        uprightObject.transform.SetParent(lightRoot.transform, false);
        uprightObject.transform.localPosition = Vector3.zero;
        uprightObject.transform.localRotation = Quaternion.identity;
        uprightObject.transform.localScale = Vector3.one;

        ArcGISUprightVisualRoot uprightController =
            uprightObject.AddComponent<ArcGISUprightVisualRoot>();
        uprightController.Configure(heading);

        BuildStreetLightVisual(uprightObject, sceneName);
    }

    void BuildStreetLightVisual(GameObject lightRoot, string sceneName)
    {
        float poleHeight = sceneName == "forest" ? 5.5f : 7.5f;
        float poleWidth = 0.30f;

        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pole.name = "Pole";
        pole.transform.SetParent(lightRoot.transform, false);
        pole.transform.localPosition = new Vector3(0f, poleHeight * 0.5f, 0f);
        pole.transform.localScale = new Vector3(poleWidth, poleHeight, poleWidth);

        GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arm.name = "LampArm";
        arm.transform.SetParent(lightRoot.transform, false);
        arm.transform.localPosition = new Vector3(0f, poleHeight - 0.2f, 0.65f);
        arm.transform.localScale = new Vector3(0.20f, 0.20f, 1.30f);

        GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lamp.name = "LampHead";
        lamp.transform.SetParent(lightRoot.transform, false);
        lamp.transform.localPosition = new Vector3(0f, poleHeight - 0.3f, 1.35f);
        lamp.transform.localScale = new Vector3(0.65f, 0.25f, 0.65f);

        foreach (Transform child in lightRoot.transform)
        {
            Collider collider = child.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Renderer renderer = child.GetComponent<Renderer>();
            if (renderer == null)
            {
                continue;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader != null)
            {
                Material material = new Material(shader);
                material.color = child.name == "LampHead"
                    ? new Color(1f, 0.92f, 0.28f, 1f)
                    : new Color(0.10f, 0.10f, 0.12f, 1f);
                renderer.sharedMaterial = material;
            }
        }
    }

    GameObject GetEnvironmentTreePrefab(int seed)
    {
        if (environmentTreePrefabs != null &&
            environmentTreePrefabs.Length > 0)
        {
            int index = Mathf.Abs(seed) % environmentTreePrefabs.Length;
            GameObject prefab = environmentTreePrefabs[index];
            if (prefab != null)
            {
                return prefab;
            }
        }

        return treePrefab;
    }

    void SpawnVillageBuildings(Result result)
    {
        if (!spawnOsmBuildings)
        {
            Debug.Log("🏠 OSM building prefab spawning is disabled for this level. Manually placed buildings will remain unchanged.");
            return;
        }

        Debug.Log("=== SpawnVillageBuildings CALLED ===");

        if (result == null)
        {
            Debug.LogError("result is NULL");
            return;
        }

        if (result.buildings == null)
        {
            Debug.LogError("result.buildings is NULL");
            return;
        }

        Debug.Log($"Building count = {result.buildings.Length}");

        if (spawnVisibleVillageClusterOnly)
        {
            Debug.Log("🏙 Visible-only village mode enabled. Spawning a small prefab cluster near the drone.");
            SpawnVisibleVillageCluster(result);
            return;
        }

        if (result.buildings.Length == 0)
        {
            Debug.LogWarning(
                "⚠️ No OSM building footprints were returned."
            );

            SpawnFallbackVillageIfEnabled(
                result,
                "OSM returned no building footprints"
            );
            return;
        }

        Debug.Log($"🏙 Spawning village buildings from {result.buildings.Length} OSM footprints");
        int spawned = 0;
        ResolveOsmPlacementValidator();
        placementValidator.ResetPlacements();

        for (int i = 0; i < result.buildings.Length; i++)
        {
            try
            {
                var building = result.buildings[i];
                int pointCount = building != null && building.points != null ? building.points.Length : 0;
                Debug.Log($"Building {i} has {pointCount} points");

                if (building == null || building.points == null || building.points.Length < 3)
                {
                    continue;
                }

            List<Vector3> footprintPoints = new List<Vector3>();
            List<GeoCoordinate> geographicPoints = new List<GeoCoordinate>();

            double latitudeSum = 0.0;
            double longitudeSum = 0.0;
            int geographicPointCount = 0;

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;

            bool hasFirstPoint = false;
            bool hasPreviousPoint = false;
            Vector3 firstValidPoint = Vector3.zero;
            Vector3 previousValidPoint = Vector3.zero;
            Vector3 longestEdge = Vector3.forward;
            float longestEdgeSqr = 0f;

            foreach (var point in building.points)
            {
                if (!TryConvertGPS(point.lat, point.lon, 0, out Vector3 unityPoint))
                {
                    continue;
                }

                // Ignore the repeated closing OSM point. It otherwise biases average centers.
                if (footprintPoints.Count > 0 &&
                    HorizontalSqrDistance(footprintPoints[footprintPoints.Count - 1], unityPoint) < 0.000001f)
                {
                    continue;
                }

                if (!hasFirstPoint)
                {
                    firstValidPoint = unityPoint;
                    hasFirstPoint = true;
                }

                if (hasPreviousPoint)
                {
                    Vector3 edge = unityPoint - previousValidPoint;
                    float edgeSqr = edge.x * edge.x + edge.z * edge.z;
                    if (edgeSqr > longestEdgeSqr)
                    {
                        longestEdgeSqr = edgeSqr;
                        longestEdge = edge;
                    }
                }

                previousValidPoint = unityPoint;
                hasPreviousPoint = true;

                footprintPoints.Add(unityPoint);
                geographicPoints.Add(new GeoCoordinate(point.lat, point.lon));

                latitudeSum += point.lat;
                longitudeSum += point.lon;
                geographicPointCount++;

                minX = Mathf.Min(minX, unityPoint.x);
                maxX = Mathf.Max(maxX, unityPoint.x);
                minZ = Mathf.Min(minZ, unityPoint.z);
                maxZ = Mathf.Max(maxZ, unityPoint.z);
            }

            if (footprintPoints.Count > 1 &&
                HorizontalSqrDistance(footprintPoints[0], footprintPoints[footprintPoints.Count - 1]) < 0.000001f)
            {
                GeoCoordinate repeatedClosingPoint = geographicPoints[geographicPoints.Count - 1];
                latitudeSum -= repeatedClosingPoint.latitude;
                longitudeSum -= repeatedClosingPoint.longitude;
                footprintPoints.RemoveAt(footprintPoints.Count - 1);
                geographicPoints.RemoveAt(geographicPoints.Count - 1);
                geographicPointCount--;
            }

            if (hasFirstPoint && hasPreviousPoint)
            {
                Vector3 closingEdge = firstValidPoint - previousValidPoint;
                float closingEdgeSqr = closingEdge.x * closingEdge.x + closingEdge.z * closingEdge.z;
                if (closingEdgeSqr > longestEdgeSqr)
                {
                    longestEdgeSqr = closingEdgeSqr;
                    longestEdge = closingEdge;
                }
            }

            if (footprintPoints.Count < 3 || geographicPointCount < 3)
            {
                Debug.LogWarning($"⚠️ Skipping building {i}: not enough valid corners.");
                continue;
            }

            double averageLatitude = latitudeSum / geographicPointCount;
            double averageLongitude = longitudeSum / geographicPointCount;
            double placementLatitude = averageLatitude;
            double placementLongitude = averageLongitude;

            // Initialize every placement value before the optional exact-fit branch.
            // This is required because C# short-circuit evaluation would otherwise leave
            // the out variables unassigned when useExactOsmFootprintPlacement is false.
            Vector3 center = Vector3.zero;
            float width = 0f;
            float depth = 0f;
            float angle = 0f;

            bool exactFitSucceeded = false;

            if (useExactOsmFootprintPlacement)
            {
                // IMPORTANT: Calculate the rectangle in geographic east/north metres.
                // ArcGISRotation expects a geographic heading, so using GPSToUnity positions
                // here can produce the wrong angle or size when the map root is transformed.
                exactFitSucceeded = TryComputeGeographicFootprintFit(
                    geographicPoints,
                    out placementLatitude,
                    out placementLongitude,
                    out width,
                    out depth,
                    out angle
                );
            }

            if (exactFitSucceeded)
            {
                // Unity position is used only for spawn-radius checks and debug output.
                // The ArcGISLocationComponent below uses the geographic rectangle centre.
                if (!TryConvertGPS(placementLatitude, placementLongitude, 0, out center))
                {
                    Debug.LogWarning($"⚠️ Skipping building {i}: exact geographic center could not be converted.");
                    continue;
                }

                Debug.Log(
                    $"🧭 Exact OSM rectangle {i} | " +
                    $"Center=({placementLatitude:F7}, {placementLongitude:F7}) | " +
                    $"Heading={angle:F1}° | Width={width:F2}m | Length={depth:F2}m"
                );
            }
            else
            {
                if (!TryConvertGPS(averageLatitude, averageLongitude, 0, out center))
                {
                    Debug.LogWarning($"⚠️ Skipping building {i}: invalid average GPS center.");
                    continue;
                }

                width = maxX - minX;
                depth = maxZ - minZ;
                angle = Mathf.Atan2(longestEdge.x, longestEdge.z) * Mathf.Rad2Deg;
            }

            width = Mathf.Max(minimumFootprintDimension, width);
            depth = Mathf.Max(minimumFootprintDimension, depth);
            float area = width * depth;

            // When the ArcGIS map is limited to a local extent, use that
            // same geographic circle for building selection. This prevents
            // the moving drone from deciding which houses are generated.
            if (limitMapExtent)
            {
                if (!IsInsideConfiguredMapExtent(
                        placementLatitude,
                        placementLongitude,
                        out double distanceFromExtentCenter))
                {
                    Debug.LogWarning(
                        $"❌ Building {i} OUTSIDE MAP EXTENT " +
                        $"({distanceFromExtentCenter:F1}m > " +
                        $"{mapExtentSizeMeters:F1}m)"
                    );

                    continue;
                }
            }
            else if (filterBuildingsByDroneDistance && droneTransform != null)
            {
                float distance = Vector2.Distance(
                    new Vector2(
                        droneTransform.position.x,
                        droneTransform.position.z
                    ),
                    new Vector2(center.x, center.z)
                );

                Debug.Log(
                    $"Building {i} | Drone={droneTransform.position} | " +
                    $"BuildingCenter={center} | Distance={distance:F1}m"
                );

                if (distance > buildingSpawnRadius)
                {
                    Debug.LogWarning(
                        $"❌ Building {i} OUTSIDE RADIUS " +
                        $"({distance:F1}m)"
                    );

                    continue;
                }
            }

            bool useModularHouse = ShouldGenerateModularHouse(
                i,
                geographicPoints,
                width,
                depth
            );

            // Use one uniform map-shadow correction for all modular houses,
            // never a different collision-driven scale for each building.
            float requestedFootprintScale = useModularHouse
                ? Mathf.Clamp(osmWallFootprintScale, 1f, 1.5f)
                : 1f;

            if (!placementValidator.CanPlaceBuilding(
                    footprintPoints,
                    building.points,
                    result,
                    requestedFootprintScale,
                    out float placementFootprintScale,
                    out string placementRejection))
            {
                Debug.LogWarning(
                    $"⚠️ Skipping building {i}: {placementRejection}."
                );
                continue;
            }

            GameObject prefabToUse = useModularHouse
                ? null
                : GetBuildingPrefabForIndex(i, width, depth);

            if (!useModularHouse && prefabToUse == null)
            {
                Debug.LogWarning(
                    $"⚠️ Skipping building {i}: no complete building prefab is assigned."
                );
                continue;
            }

            Transform mapRoot = GetArcGISRoot();
            string generatedBuildingName = useModularHouse
                ? $"ModularHouse_ArcGISBuilding_{i}"
                : $"{prefabToUse.name}_ArcGISBuilding_{i}";

            GameObject buildingRoot = new GameObject(
                generatedBuildingName
            );

            if (useModularHouse)
            {
                ResolveModularHouseGenerator();

                if (modularHouseGenerator == null)
                {
                    Debug.LogError(
                        $"❌ Building {i} cannot be generated because " +
                        "ModularHouseGenerator is missing."
                    );
                    Destroy(buildingRoot);
                    continue;
                }

                // Keep geographic placement on an ArcGIS anchor so the house
                // stays attached to the map and can still be found by the
                // delivery system. The visible geometry is generated below a
                // separate child that cancels the ArcGIS X/Z tilt safely.
                Transform modularBuildingContainer = GetGeneratedBuildingsRoot();
                if (modularBuildingContainer != null)
                {
                    buildingRoot.transform.SetParent(modularBuildingContainer, false);
                }

                ArcGISLocationComponent modularLocation =
                    buildingRoot.AddComponent<ArcGISLocationComponent>();

                modularLocation.SurfacePlacementMode =
                    ArcGISSurfacePlacementMode.OnTheGround;
                modularLocation.SurfacePlacementOffset =
                    modularBuildingGroundOffset;
                modularLocation.Position = new ArcGISPoint(
                    placementLongitude,
                    placementLatitude,
                    0,
                    ArcGISSpatialReference.WGS84()
                );

                // Do not put the geographic heading on the ArcGIS anchor.
                // The upright visual child handles Y rotation in Unity space.
                modularLocation.Rotation = new ArcGISRotation(0, 0, 0);

                GameObject uprightObject = new GameObject(
                    "UprightVisualRoot"
                );
                uprightObject.transform.SetParent(
                    buildingRoot.transform,
                    false
                );
                uprightObject.transform.localPosition = Vector3.zero;
                uprightObject.transform.localRotation = Quaternion.identity;
                uprightObject.transform.localScale = Vector3.one;

                ArcGISUprightVisualRoot uprightController =
                    uprightObject.AddComponent<ArcGISUprightVisualRoot>();

                float modularVisualYaw = NormalizeHeadingDegrees(
                    modularBuildingYawCorrectionDegrees
                );
                uprightController.Configure(modularVisualYaw);

                // Build the exact OSM polygon in east/north metres. Because the
                // visual root uses a Y-only world rotation, counter-rotate the
                // footprint by the same yaw so its final world outline remains
                // aligned with OSM.
                List<Vector3> localFootprint =
                    ConvertGeographicFootprintToLocalMeters(
                        geographicPoints,
                        placementLatitude,
                        placementLongitude,
                        modularVisualYaw
                    );
                localFootprint = ScaleFootprintInXZ(
                    localFootprint,
                    placementFootprintScale
                );

                bool generated = modularHouseGenerator.GenerateHouse(
                    uprightObject.transform,
                    localFootprint,
                    i,
                    out Transform deliveryTarget
                );

                if (!generated)
                {
                    Debug.LogWarning(
                        $"⚠️ Modular generation failed for building {i}."
                    );
                    Destroy(buildingRoot);
                    continue;
                }

                House modularHouse = buildingRoot.GetComponent<House>();
                if (modularHouse == null)
                {
                    modularHouse = buildingRoot.AddComponent<House>();
                }

                modularHouse.model = buildingRoot.transform;
                ConfigureHouseSimpleLod(buildingRoot);

                Debug.Log(
                    $"🏗 Spawned ArcGIS-anchored upright modular house {buildingRoot.name} | " +
                    $"Geo=({placementLatitude:F7}, {placementLongitude:F7}) | " +
                    $"Corners={localFootprint.Count} | " +
                    $"Rectangle W×L={width:F2}×{depth:F2}m | " +
                    $"VisualYaw={modularVisualYaw:F1}° | " +
                    $"FootprintScale={placementFootprintScale:F2} | " +
                    $"DeliveryTarget={(deliveryTarget != null ? deliveryTarget.position.ToString() : "missing")}"
                );

                SpawnEnvironmentDetailAroundBuilding(
                    center,
                    width,
                    depth,
                    area,
                    i,
                    result.scene
                );

                placementValidator.RegisterBuildingFootprint(
                    footprintPoints,
                    placementFootprintScale
                );
                spawned++;
                continue;
            }

            // Complete prefabs keep the ArcGISLocationComponent path because
            // their imported model-axis correction is handled separately.
            Transform buildingContainer = GetGeneratedBuildingsRoot();
            if (buildingContainer != null)
            {
                buildingRoot.transform.SetParent(buildingContainer, false);
            }

            ArcGISLocationComponent locationComponent =
                buildingRoot.AddComponent<ArcGISLocationComponent>();

            locationComponent.SurfacePlacementMode =
                ArcGISSurfacePlacementMode.OnTheGround;
            locationComponent.SurfacePlacementOffset = 0;
            locationComponent.Position = new ArcGISPoint(
                placementLongitude,
                placementLatitude,
                0,
                ArcGISSpatialReference.WGS84()
            );

            string buildingTypeName = prefabToUse.name;
            BuildingOrientationRule orientationRule =
                GetBuildingOrientationRule(
                    prefabToUse,
                    buildingTypeName
                );

            float yawCorrectionDegrees = orientationRule != null
                ? orientationRule.rootYawCorrectionDegrees
                : buildingYawCorrectionDegrees;

            float finalHeading = NormalizeHeadingDegrees(
                angle + yawCorrectionDegrees
            );

            locationComponent.Rotation = new ArcGISRotation(
                finalHeading,
                0,
                0
            );

            // Complete-prefab hierarchy retained as the fallback/special-building path:
            // ArcGIS root             = geographic location + geographic heading
            // FootprintScaler         = X/Z size only
            // FootprintOrientation    = optional top-view quarter turn
            // Building model          = per-prefab import-axis correction
            GameObject footprintScalerObject =
                new GameObject("FootprintScaler");
            footprintScalerObject.transform.SetParent(
                buildingRoot.transform,
                false
            );
            footprintScalerObject.transform.localPosition = Vector3.zero;
            footprintScalerObject.transform.localRotation = Quaternion.identity;
            footprintScalerObject.transform.localScale = Vector3.one;

            GameObject footprintOrientationObject =
                new GameObject("FootprintOrientation");
            footprintOrientationObject.transform.SetParent(
                footprintScalerObject.transform,
                false
            );
            footprintOrientationObject.transform.localPosition = Vector3.zero;
            footprintOrientationObject.transform.localRotation = Quaternion.identity;
            footprintOrientationObject.transform.localScale = Vector3.one;

            GameObject buildingModel = Instantiate(
                prefabToUse,
                footprintOrientationObject.transform
            );
            buildingModel.name = prefabToUse.name;
            buildingModel.transform.localPosition = Vector3.zero;
            buildingModel.transform.localRotation =
                GetBuildingModelLocalRotation(orientationRule);
            buildingModel.transform.localScale =
                GetBuildingModelLocalScale(
                    orientationRule,
                    width,
                    depth,
                    area
                );

            ApplyBuildingReplacementOverrides(
                buildingModel,
                prefabToUse,
                i
            );
            EnsureRenderable(buildingModel);
            ApplyBuildingPalette(
                buildingModel,
                prefabToUse,
                buildingRoot.name
            );

            bool shouldFitThisPrefab =
                fitBuildingModelToFootprint &&
                (orientationRule == null ||
                 orientationRule.fitToOsmFootprint);

            if (shouldFitThisPrefab)
            {
                FitBuildingModelToFootprint(
                    buildingModel,
                    footprintOrientationObject.transform,
                    footprintScalerObject.transform,
                    buildingRoot.transform,
                    width,
                    depth,
                    orientationRule
                );
            }

            House completeHouse = buildingRoot.GetComponent<House>();
            if (completeHouse == null)
            {
                completeHouse = buildingRoot.AddComponent<House>();
            }

            completeHouse.model = buildingModel.transform;
            completeHouse.styleName = prefabToUse.name;
            completeHouse.deliveryGroupName = prefabToUse.name;
            ConfigureHouseSimpleLod(buildingRoot);

            ConfigureBuildingCollider(
                buildingRoot,
                buildingModel
            );
            EnsureDeliveryTargetMarker(
                buildingRoot,
                buildingModel
            );

            Debug.Log(
                $"🏠 Spawned complete prefab {buildingRoot.name} | " +
                $"Geo=({placementLatitude:F7}, {placementLongitude:F7}) | " +
                $"OSM W×L={width:F2}×{depth:F2}m | " +
                $"RootHeading={finalHeading:F1}° | " +
                $"ModelRot={buildingModel.transform.localRotation.eulerAngles} | " +
                $"Scaler={footprintScalerObject.transform.localScale}"
            );

            SpawnEnvironmentDetailAroundBuilding(
                center,
                width,
                depth,
                area,
                i,
                result.scene
            );

            placementValidator.RegisterBuildingFootprint(
                footprintPoints,
                placementFootprintScale
            );
            spawned++;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Building {i} generation failed: {e}");
            }
        }

        if (spawned == 0)
        {
            Debug.LogWarning(
                "⚠️ No village buildings could be placed from OSM footprints."
            );

            SpawnFallbackVillageIfEnabled(
                result,
                "every OSM footprint was rejected"
            );
            return;
        }

        Debug.Log("🏙 Village buildings spawned: " + spawned);
    }

    void SpawnFallbackVillageIfEnabled(Result result, string reason)
    {
        if (!spawnFallbackVillageWhenNoOsmBuildings)
        {
            Debug.LogWarning(
                "⚠️ Fallback village is disabled. The level has no delivery houses."
            );
            return;
        }

        Debug.LogWarning(
            $"⚠️ Spawning fallback delivery village because {reason}."
        );
        SpawnVisibleVillageCluster(result);
    }

    void ResolveOsmPlacementValidator()
    {
        if (placementValidator == null)
        {
            placementValidator = GetComponent<OsmPlacementValidator>();
        }

        if (placementValidator == null)
        {
            placementValidator = gameObject.AddComponent<OsmPlacementValidator>();
        }
    }

    void ResolveModularHouseGenerator()
    {
        if (modularHouseGenerator != null)
        {
            return;
        }

        modularHouseGenerator = GetComponent<ModularHouseGenerator>();

        if (modularHouseGenerator == null &&
            buildingGenerationMode != BuildingGenerationMode.CompletePrefab)
        {
            modularHouseGenerator =
                gameObject.AddComponent<ModularHouseGenerator>();

            Debug.Log(
                "🏗 ModularHouseGenerator was added automatically with cube fallbacks. " +
                "For custom components, add and configure ModularHouseGenerator in the Inspector before Play."
            );
        }
    }

    bool HasBuildingReplacementPrefab(int buildingIndex)
    {
        if (buildingReplacements == null)
        {
            return false;
        }

        for (int i = 0; i < buildingReplacements.Length; i++)
        {
            BuildingReplacementRule rule = buildingReplacements[i];
            if (rule != null &&
                rule.buildingIndex == buildingIndex &&
                rule.prefab != null)
            {
                return true;
            }
        }

        return false;
    }

    bool ShouldGenerateModularHouse(
        int buildingIndex,
        List<GeoCoordinate> geographicPoints,
        float fittedWidth,
        float fittedDepth)
    {
        if (buildingGenerationMode ==
            BuildingGenerationMode.CompletePrefab)
        {
            return false;
        }

        bool hasReplacementPrefab =
            replacementRulesForceCompletePrefabs &&
            HasBuildingReplacementPrefab(buildingIndex);

        if (hasReplacementPrefab)
        {
            return false;
        }

        if (buildingGenerationMode ==
            BuildingGenerationMode.ModularFootprint)
        {
            return true;
        }

        if (geographicPoints == null ||
            geographicPoints.Count != 4)
        {
            return true;
        }

        float rectangleArea = Mathf.Max(
            0.001f,
            fittedWidth * fittedDepth
        );

        float polygonArea = CalculateGeographicPolygonAreaMeters(
            geographicPoints
        );

        float fillRatio = Mathf.Clamp01(
            polygonArea / rectangleArea
        );

        return fillRatio < modularFootprintFillThreshold;
    }

    float CalculateGeographicPolygonAreaMeters(
        List<GeoCoordinate> geographicPoints)
    {
        if (geographicPoints == null ||
            geographicPoints.Count < 3)
        {
            return 0f;
        }

        double referenceLatitude = 0.0;
        double referenceLongitude = 0.0;

        for (int i = 0; i < geographicPoints.Count; i++)
        {
            referenceLatitude += geographicPoints[i].latitude;
            referenceLongitude += geographicPoints[i].longitude;
        }

        referenceLatitude /= geographicPoints.Count;
        referenceLongitude /= geographicPoints.Count;

        const double metersPerDegreeLatitude = 111320.0;
        double metersPerDegreeLongitude =
            metersPerDegreeLatitude *
            System.Math.Cos(
                referenceLatitude *
                System.Math.PI / 180.0
            );

        double twiceArea = 0.0;

        for (int i = 0; i < geographicPoints.Count; i++)
        {
            GeoCoordinate current = geographicPoints[i];
            GeoCoordinate next =
                geographicPoints[(i + 1) % geographicPoints.Count];

            double currentX =
                (current.longitude - referenceLongitude) *
                metersPerDegreeLongitude;
            double currentZ =
                (current.latitude - referenceLatitude) *
                metersPerDegreeLatitude;
            double nextX =
                (next.longitude - referenceLongitude) *
                metersPerDegreeLongitude;
            double nextZ =
                (next.latitude - referenceLatitude) *
                metersPerDegreeLatitude;

            twiceArea += currentX * nextZ - nextX * currentZ;
        }

        return (float)System.Math.Abs(twiceArea * 0.5);
    }

    float CalculateAverageWorldY(
        List<Vector3> worldPoints,
        float fallbackY)
    {
        if (worldPoints == null || worldPoints.Count == 0)
        {
            return fallbackY;
        }

        float sum = 0f;
        int validCount = 0;

        for (int i = 0; i < worldPoints.Count; i++)
        {
            float y = worldPoints[i].y;
            if (float.IsNaN(y) || float.IsInfinity(y))
            {
                continue;
            }

            sum += y;
            validCount++;
        }

        return validCount > 0
            ? sum / validCount
            : fallbackY;
    }

    List<Vector3> ConvertWorldFootprintToLocalXZ(
        List<Vector3> worldPoints,
        Transform worldUprightRoot)
    {
        List<Vector3> localPoints = new List<Vector3>();

        if (worldPoints == null || worldUprightRoot == null)
        {
            return localPoints;
        }

        for (int i = 0; i < worldPoints.Count; i++)
        {
            Vector3 localPoint =
                worldUprightRoot.InverseTransformPoint(worldPoints[i]);

            // The procedural house is intentionally flat and world-Y-up.
            // ArcGIS geographic tilt is not inherited by this root.
            localPoint.y = 0f;

            if (localPoints.Count == 0 ||
                HorizontalSqrDistance(
                    localPoints[localPoints.Count - 1],
                    localPoint
                ) > 0.000001f)
            {
                localPoints.Add(localPoint);
            }
        }

        if (localPoints.Count > 1 &&
            HorizontalSqrDistance(
                localPoints[0],
                localPoints[localPoints.Count - 1]
            ) <= 0.000001f)
        {
            localPoints.RemoveAt(localPoints.Count - 1);
        }

        return localPoints;
    }

    List<Vector3> ConvertGeographicFootprintToLocalMeters(
        List<GeoCoordinate> geographicPoints,
        double centerLatitude,
        double centerLongitude,
        float rootHeadingDegrees)
    {
        List<Vector3> localPoints = new List<Vector3>();

        if (geographicPoints == null)
        {
            return localPoints;
        }

        const double metersPerDegreeLatitude = 111320.0;
        double metersPerDegreeLongitude =
            metersPerDegreeLatitude *
            System.Math.Cos(
                centerLatitude *
                System.Math.PI / 180.0
            );

        Quaternion removeRootHeading = Quaternion.Euler(
            0f,
            -rootHeadingDegrees,
            0f
        );

        for (int i = 0; i < geographicPoints.Count; i++)
        {
            GeoCoordinate point = geographicPoints[i];

            float eastMeters = (float)(
                (point.longitude - centerLongitude) *
                metersPerDegreeLongitude
            );

            float northMeters = (float)(
                (point.latitude - centerLatitude) *
                metersPerDegreeLatitude
            );

            // Convert the source OSM geometry first. A single uniform map-shadow
            // correction is applied later, before the wall modules are created.
            Vector3 localPoint = removeRootHeading *
                new Vector3(
                    eastMeters,
                    0f,
                    northMeters
                );

            if (localPoints.Count == 0 ||
                HorizontalSqrDistance(
                    localPoints[localPoints.Count - 1],
                    localPoint
                ) > 0.000001f)
            {
                localPoints.Add(localPoint);
            }
        }

        if (localPoints.Count > 1 &&
            HorizontalSqrDistance(
                localPoints[0],
                localPoints[localPoints.Count - 1]
            ) <= 0.000001f)
        {
            localPoints.RemoveAt(localPoints.Count - 1);
        }

        return localPoints;
    }

    List<Vector3> ScaleFootprintInXZ(
        List<Vector3> footprint,
        float scale)
    {
        if (footprint == null || footprint.Count == 0)
        {
            return footprint;
        }

        float safeScale = Mathf.Max(0.01f, scale);
        if (Mathf.Approximately(safeScale, 1f))
        {
            return footprint;
        }

        Vector3 center = Vector3.zero;
        for (int i = 0; i < footprint.Count; i++)
        {
            center += footprint[i];
        }

        center /= footprint.Count;
        List<Vector3> scaledFootprint =
            new List<Vector3>(footprint.Count);

        for (int i = 0; i < footprint.Count; i++)
        {
            Vector3 point = footprint[i];
            scaledFootprint.Add(new Vector3(
                center.x + (point.x - center.x) * safeScale,
                point.y,
                center.z + (point.z - center.z) * safeScale
            ));
        }

        return scaledFootprint;
    }

    void EnsureDeliveryTargetMarker(
        GameObject buildingRoot,
        GameObject buildingModel)
    {
        if (buildingRoot == null || buildingModel == null)
        {
            return;
        }

        if (FindChildRecursive(
                buildingRoot.transform,
                "DeliveryTarget") != null)
        {
            return;
        }

        if (!TryMeasureRendererBounds(
                buildingModel,
                buildingRoot.transform,
                out Bounds visualBounds))
        {
            return;
        }

        GameObject marker = new GameObject("DeliveryTarget");
        marker.transform.SetParent(buildingRoot.transform, false);
        marker.transform.localPosition = new Vector3(
            visualBounds.center.x,
            visualBounds.max.y,
            visualBounds.center.z
        );
        marker.transform.localRotation = Quaternion.identity;
        marker.transform.localScale = Vector3.one;
    }

    void SpawnVisibleVillageCluster(Result result)
    {
        bool hasAnyVillagePrefab =
            (smallHousePrefabs != null && smallHousePrefabs.Length > 0) ||
            (mediumHousePrefabs != null && mediumHousePrefabs.Length > 0) ||
            (largeBuildingPrefabs != null && largeBuildingPrefabs.Length > 0) ||
            (randomHousePrefabs != null && randomHousePrefabs.Length > 0);

        if (!hasAnyVillagePrefab &&
            (modularHouseGenerator == null ||
             buildingGenerationMode == BuildingGenerationMode.CompletePrefab))
        {
            Debug.LogWarning("⚠️ No village prefabs assigned for fallback cluster.");
            return;
        }

        Vector3 center;
        Vector3 forward;

        if (!TryGetVisibleVillageAnchor(result, out center, out forward))
        {
            Transform anchorTransform = droneTransform != null
                ? droneTransform
                : droneCamera != null
                    ? droneCamera.transform
                    : Camera.main != null ? Camera.main.transform : null;

            center = anchorTransform != null
                ? anchorTransform.position
                : Vector3.zero;

            forward = anchorTransform != null
                ? anchorTransform.forward
                : Vector3.forward;
        }

        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        int count = 8;
        float radius = Mathf.Clamp(scale * 0.015f, 5f, 14f);
        float angleStep = 360f / Mathf.Max(1, count);

        Transform clusterParent = GetGeneratedBuildingsRoot();
        GameObject clusterRoot = new GameObject("VisibleVillageCluster");
        if (clusterParent != null)
        {
            clusterRoot.transform.SetParent(clusterParent, false);
        }

        if (TryGetVisibleVillageGeoAnchor(result, out double anchorLatitude, out double anchorLongitude))
        {
            ArcGISLocationComponent clusterLocation =
                clusterRoot.AddComponent<ArcGISLocationComponent>();
            clusterLocation.SurfacePlacementMode =
                ArcGISSurfacePlacementMode.OnTheGround;
            clusterLocation.SurfacePlacementOffset = 0f;
            clusterLocation.Position = new ArcGISPoint(
                anchorLongitude,
                anchorLatitude,
                0,
                ArcGISSpatialReference.WGS84()
            );
            clusterLocation.Rotation = new ArcGISRotation(0, 0, 0);
        }
        else
        {
            clusterRoot.transform.position = center;
            clusterRoot.transform.rotation = Quaternion.identity;
        }

        ResolveModularHouseGenerator();
        bool canBuildModularFallback =
            modularHouseGenerator != null &&
            buildingGenerationMode != BuildingGenerationMode.CompletePrefab;

        if (!canBuildModularFallback)
        {
            Debug.LogWarning(
                "⚠️ ModularHouseGenerator is unavailable for the fallback cluster. " +
                "Using the older prefab fallback so the level still shows buildings."
            );
        }

        Vector3 heroLocalPosition = forward * 4f + Vector3.down * 0.5f;
        SpawnVisibleFallbackBuilding(
            clusterRoot.transform,
            heroLocalPosition,
            forward,
            radius * 1.05f,
            radius * 0.85f,
            1.5f,
            count * 1000 + 1,
            "Hero"
        );
        Debug.Log(
            $"🏠 Hero fallback delivery building spawned at " +
            $"{clusterRoot.transform.position + heroLocalPosition}"
        );

        for (int i = 0; i < count; i++)
        {
            float angle =
                i * angleStep +
                UnityEngine.Random.Range(-10f, 10f);

            float distance =
                UnityEngine.Random.Range(
                    radius * 0.4f,
                    radius
                );

            Vector3 offset =
                (right * Mathf.Cos(angle * Mathf.Deg2Rad) +
                 forward * Mathf.Sin(angle * Mathf.Deg2Rad)) *
                distance;

            Vector3 spawnLocalPosition =
                forward * 4f +
                offset +
                Vector3.down * 0.5f;

            float footprintWidth = UnityEngine.Random.Range(4.5f, 9.5f);
            float footprintDepth = UnityEngine.Random.Range(3.5f, 7.5f);
            float scaleMultiplier = UnityEngine.Random.Range(0.95f, 1.25f);
            float yaw = UnityEngine.Random.Range(0f, 360f);
            Vector3 buildingForward =
                Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            SpawnVisibleFallbackBuilding(
                clusterRoot.transform,
                spawnLocalPosition,
                buildingForward,
                footprintWidth,
                footprintDepth,
                scaleMultiplier,
                count * 1000 + i + 2,
                $"Fallback_{i}"
            );

            Debug.Log(
                $"🏠 Fallback delivery building spawned at " +
                $"{clusterRoot.transform.position + spawnLocalPosition}"
            );
        }

        Debug.Log(
            "🏙 Visible fallback village cluster spawned as fixed " +
            "delivery buildings."
        );
    }

    void SpawnVisibleFallbackBuilding(
        Transform parent,
        Vector3 localPosition,
        Vector3 forward,
        float footprintWidth,
        float footprintDepth,
        float scaleMultiplier,
        int seed,
        string label)
    {
        if (parent == null)
        {
            return;
        }

        GameObject buildingRoot = new GameObject(
            $"VisibleBuilding_{label}_ArcGISBuilding_{seed}"
        );
        buildingRoot.transform.SetParent(parent, false);
        buildingRoot.transform.localPosition = localPosition;

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
        {
            flatForward = Vector3.forward;
        }
        flatForward.Normalize();

        buildingRoot.transform.localRotation = Quaternion.LookRotation(
            flatForward,
            Vector3.up
        );
        buildingRoot.transform.localScale = Vector3.one * Mathf.Max(0.1f, scaleMultiplier);

        if (modularHouseGenerator != null &&
            buildingGenerationMode != BuildingGenerationMode.CompletePrefab)
        {
            List<Vector3> localFootprint = new List<Vector3>
            {
                new Vector3(-footprintWidth * 0.5f, 0f, -footprintDepth * 0.5f),
                new Vector3( footprintWidth * 0.5f, 0f, -footprintDepth * 0.5f),
                new Vector3( footprintWidth * 0.5f, 0f,  footprintDepth * 0.5f),
                new Vector3(-footprintWidth * 0.5f, 0f,  footprintDepth * 0.5f)
            };

            if (!modularHouseGenerator.GenerateHouse(
                    buildingRoot.transform,
                    localFootprint,
                    seed,
                    out Transform deliveryTarget))
            {
                Debug.LogWarning(
                    $"⚠️ Modular fallback house generation failed for {buildingRoot.name}."
                );
                Destroy(buildingRoot);
                return;
            }

            House house = buildingRoot.GetComponent<House>();
            if (house == null)
            {
                house = buildingRoot.AddComponent<House>();
            }

            house.model = buildingRoot.transform;
            ConfigureHouseSimpleLod(buildingRoot);
            return;
        }

        GameObject prefab = GetGuaranteedVillagePrefab();
        GameObject visibleBuilding = CreateVisibleBuildingSpawn(
            prefab,
            Vector3.zero,
            Quaternion.identity,
            scaleMultiplier,
            buildingRoot.transform
        );

        visibleBuilding.transform.localPosition = Vector3.zero;
    }

    bool TryGetVisibleVillageAnchor(Result result, out Vector3 center, out Vector3 forward)
    {
        center = Vector3.zero;
        forward = Vector3.forward;

        if (result == null || result.waypoints == null || result.waypoints.Length == 0)
        {
            return false;
        }

        Vector3 sum = Vector3.zero;
        int validCount = 0;
        Vector3 first = Vector3.zero;
        Vector3 last = Vector3.zero;

        for (int i = 0; i < result.waypoints.Length; i++)
        {
            GPSWaypoint wp = result.waypoints[i];
            if (!TryConvertGPS(wp.lat, wp.lon, wp.alt, out Vector3 worldPoint))
            {
                continue;
            }

            if (validCount == 0)
            {
                first = worldPoint;
            }

            last = worldPoint;
            sum += worldPoint;
            validCount++;
        }

        if (validCount == 0)
        {
            return false;
        }

        center = sum / validCount;
        forward = last - first;
        return true;
    }

    bool TryGetVisibleVillageGeoAnchor(
        Result result,
        out double latitude,
        out double longitude)
    {
        latitude = 0.0;
        longitude = 0.0;

        if (result == null || result.waypoints == null || result.waypoints.Length == 0)
        {
            return false;
        }

        double latSum = 0.0;
        double lonSum = 0.0;
        int validCount = 0;

        for (int i = 0; i < result.waypoints.Length; i++)
        {
            GPSWaypoint wp = result.waypoints[i];
            if (double.IsNaN(wp.lat) || double.IsNaN(wp.lon) ||
                double.IsInfinity(wp.lat) || double.IsInfinity(wp.lon))
            {
                continue;
            }

            latSum += wp.lat;
            lonSum += wp.lon;
            validCount++;
        }

        if (validCount == 0)
        {
            return false;
        }

        latitude = latSum / validCount;
        longitude = lonSum / validCount;
        return true;
    }

    GameObject CreateVisibleBuildingSpawn(
        GameObject prefab,
        Vector3 localPosition,
        Quaternion worldRotation,
        float scaleMultiplier,
        Transform parent)
    {
        Transform buildingContainer =
            parent != null ? parent : GetGeneratedBuildingsRoot();

        string prefabName =
            prefab != null
                ? prefab.name
                : "Cube";

        GameObject root = new GameObject(
            $"VisibleBuilding_{prefabName}_ArcGISBuilding_" +
            $"{visibleBuildingCounter++}"
        );

        if (buildingContainer != null)
        {
            root.transform.SetParent(buildingContainer, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(
                0f,
                worldRotation.eulerAngles.y,
                0f
            );
            root.transform.localScale =
                Vector3.one * Mathf.Max(0.1f, scaleMultiplier);
        }
        else
        {
            root.transform.position = localPosition;
            root.transform.rotation = Quaternion.Euler(
                0f,
                worldRotation.eulerAngles.y,
                0f
            );
            root.transform.localScale =
                Vector3.one * Mathf.Max(0.1f, scaleMultiplier);
        }

        GameObject visibleModel;

        if (prefab != null)
        {
            visibleModel = Instantiate(
                prefab,
                root.transform
            );

            visibleModel.name = prefab.name;
            visibleModel.transform.localPosition = Vector3.zero;

            BuildingOrientationRule orientationRule =
                GetBuildingOrientationRule(
                    prefab,
                    prefab.name
                );

            visibleModel.transform.localRotation =
                GetBuildingModelLocalRotation(
                    orientationRule
                );

            visibleModel.transform.localScale = Vector3.one;

            EnsureRenderable(visibleModel);
            ApplyBuildingPalette(
                visibleModel,
                prefab,
                root.name
            );
        }
        else
        {
            visibleModel =
                GameObject.CreatePrimitive(
                    PrimitiveType.Cube
                );

            visibleModel.name = "FallbackBuildingMesh";
            visibleModel.transform.SetParent(
                root.transform,
                false
            );
            visibleModel.transform.localPosition =
                Vector3.zero;
            visibleModel.transform.localRotation =
                Quaternion.identity;
            visibleModel.transform.localScale =
                Vector3.one;
        }

        // The delivery manager can now identify fallback buildings
        // through both the House marker and the generated root name.
        House rootHouse = root.GetComponent<House>();
        if (rootHouse == null)
        {
            rootHouse = root.AddComponent<House>();
        }

        rootHouse.model = visibleModel.transform;
        rootHouse.styleName = prefabName;
        rootHouse.deliveryGroupName = prefabName;
        ConfigureHouseSimpleLod(root);

        ConfigureBuildingCollider(
            root,
            visibleModel
        );

        EnsureDeliveryTargetMarker(
            root,
            visibleModel
        );

        // Child colliders are unnecessary for the delivery check and
        // may interfere with the drone. The fitted root collider remains.
        Collider[] childColliders =
            visibleModel.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < childColliders.Length; i++)
        {
            Collider childCollider = childColliders[i];

            if (childCollider != null &&
                childCollider.gameObject != root)
            {
                Destroy(childCollider);
            }
        }

        return root;
    }

    GameObject GetGuaranteedVillagePrefab()
    {
        if (buildingPrefab != null)
        {
            return buildingPrefab;
        }

        if (smallHousePrefabs != null && smallHousePrefabs.Length > 0)
        {
            return smallHousePrefabs[0];
        }

        if (mediumHousePrefabs != null && mediumHousePrefabs.Length > 0)
        {
            return mediumHousePrefabs[0];
        }

        if (largeBuildingPrefabs != null && largeBuildingPrefabs.Length > 0)
        {
            return largeBuildingPrefabs[0];
        }

        if (randomHousePrefabs != null && randomHousePrefabs.Length > 0)
        {
            return randomHousePrefabs[0];
        }

        return GameObject.CreatePrimitive(PrimitiveType.Cube);
    }

    void EnsureRenderable(GameObject building)
    {
        if (building == null)
        {
            return;
        }

        Renderer[] renderers = building.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            return;
        }

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            foreach (Material material in renderer.materials)
            {
                if (material == null)
                {
                    continue;
                }

            }
        }
    }

    void ApplyBuildingPalette(GameObject building, GameObject prefabToUse, string buildingName)
    {
        if (building == null)
        {
            return;
        }

        if (preservePrefabMaterialColors && !useRandomBuildingPalette)
        {
            return;
        }

        Color? forcedColor = GetForcedBuildingColor(prefabToUse, buildingName);
        Color paletteColor = forcedColor ?? GetRandomBuildingPaletteColor(buildingName);

        Renderer[] renderers = building.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            return;
        }

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Material[] materials = renderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null || !material.HasProperty("_Color"))
                {
                    continue;
                }

                if (preservePrefabMaterialColors && forcedColor == null)
                {
                    continue;
                }

                material.color = paletteColor;
            }
        }
    }

    Color? GetForcedBuildingColor(GameObject prefabToUse, string buildingName)
    {
        if (!string.IsNullOrWhiteSpace(buildingName))
        {
            string lowerName = buildingName.ToLowerInvariant();
            if (lowerName.Contains("school"))
            {
                return new Color(0.90f, 0.82f, 0.40f, 1f);
            }

            if (lowerName.Contains("fire station"))
            {
                return new Color(0.86f, 0.24f, 0.22f, 1f);
            }

            if (lowerName.Contains("corner building"))
            {
                return new Color(0.72f, 0.72f, 0.78f, 1f);
            }
        }

        if (prefabToUse == null)
        {
            return null;
        }

        string prefabName = prefabToUse.name.ToLowerInvariant();
        if (prefabName.Contains("school"))
        {
            return new Color(0.90f, 0.82f, 0.40f, 1f);
        }

        if (prefabName.Contains("fire station"))
        {
            return new Color(0.86f, 0.24f, 0.22f, 1f);
        }

        if (prefabName.Contains("corner building"))
        {
            return new Color(0.72f, 0.72f, 0.78f, 1f);
        }

        return null;
    }

    Color GetRandomBuildingPaletteColor(string buildingName)
    {
        if (buildingPalette == null || buildingPalette.Length == 0)
        {
            return Color.white;
        }

        int hash = buildingName != null ? buildingName.GetHashCode() : 0;
        if (hash < 0)
        {
            hash = -hash;
        }

        return buildingPalette[hash % buildingPalette.Length];
    }

    GameObject GetBuildingPrefabForIndex(int buildingIndex, float width, float depth)
    {
        if (buildingReplacements != null)
        {
            foreach (var rule in buildingReplacements)
            {
                if (rule == null || rule.prefab == null)
                {
                    continue;
                }

                if (rule.buildingIndex != buildingIndex)
                {
                    continue;
                }

                return rule.prefab;
            }
        }

        return PickBuildingPrefab(width, depth);
    }

    GameObject PickBuildingPrefab(float width, float depth)
    {
        float area = width * depth;

        if (area < smallBuildingAreaMax)
        {
            return PickPrefabFromPool(smallHousePrefabs, randomHousePrefabs);
        }

        if (area < mediumBuildingAreaMax)
        {
            return PickPrefabFromPool(mediumHousePrefabs, randomHousePrefabs);
        }

        return PickPrefabFromPool(largeBuildingPrefabs, mediumHousePrefabs);
    }

    GameObject PickPrefabFromPool(GameObject[] primaryPool, GameObject[] fallbackPool)
    {
        if (primaryPool != null && primaryPool.Length > 0)
        {
            return primaryPool[UnityEngine.Random.Range(0, primaryPool.Length)];
        }

        if (fallbackPool != null && fallbackPool.Length > 0)
        {
            return fallbackPool[UnityEngine.Random.Range(0, fallbackPool.Length)];
        }

        return buildingPrefab;
    }

    BuildingOrientationRule GetBuildingOrientationRule(GameObject prefabToUse, string buildingTypeName)
    {
        if (buildingOrientationRules != null && buildingOrientationRules.Length > 0)
        {
            for (int i = 0; i < buildingOrientationRules.Length; i++)
            {
                BuildingOrientationRule rule = buildingOrientationRules[i];
                if (rule == null)
                {
                    continue;
                }

                if (rule.prefab != null && rule.prefab == prefabToUse)
                {
                    return rule;
                }

                if (!string.IsNullOrWhiteSpace(buildingTypeName) &&
                    !string.IsNullOrWhiteSpace(rule.buildingNameContains) &&
                    buildingTypeName.IndexOf(rule.buildingNameContains.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return rule;
                }
            }
        }

        return null;
    }

    float HorizontalSqrDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    List<Vector3> GetCleanFootprintPoints(List<Vector3> points)
    {
        List<Vector3> cleanPoints = new List<Vector3>();
        if (points == null)
        {
            return cleanPoints;
        }

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 point = points[i];
            if (cleanPoints.Count == 0 ||
                HorizontalSqrDistance(cleanPoints[cleanPoints.Count - 1], point) > 0.000001f)
            {
                cleanPoints.Add(point);
            }
        }

        if (cleanPoints.Count > 1 &&
            HorizontalSqrDistance(cleanPoints[0], cleanPoints[cleanPoints.Count - 1]) <= 0.000001f)
        {
            cleanPoints.RemoveAt(cleanPoints.Count - 1);
        }

        return cleanPoints;
    }

    // Finds the minimum-area oriented rectangle using the actual OSM polygon edges.
    // Center, width, length and yaw always come from the same rectangle.
    bool TryComputeFootprintFit(
        List<Vector3> points,
        out Vector3 center,
        out float width,
        out float depth,
        out float angleDegrees)
    {
        center = Vector3.zero;
        width = 0f;
        depth = 0f;
        angleDegrees = 0f;

        List<Vector3> cleanPoints = GetCleanFootprintPoints(points);
        if (cleanPoints.Count < 3)
        {
            return false;
        }

        float averageY = 0f;
        for (int i = 0; i < cleanPoints.Count; i++)
        {
            averageY += cleanPoints[i].y;
        }
        averageY /= cleanPoints.Count;

        bool foundRectangle = false;
        float bestArea = float.MaxValue;
        float bestDepth = 0f;
        Vector3 bestAxisX = Vector3.right;
        Vector3 bestAxisZ = Vector3.forward;
        float bestMinX = 0f;
        float bestMaxX = 0f;
        float bestMinZ = 0f;
        float bestMaxZ = 0f;

        for (int edgeIndex = 0; edgeIndex < cleanPoints.Count; edgeIndex++)
        {
            Vector3 current = cleanPoints[edgeIndex];
            Vector3 next = cleanPoints[(edgeIndex + 1) % cleanPoints.Count];
            Vector3 edge = next - current;
            edge.y = 0f;

            if (edge.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            // The tested polygon edge becomes the rectangle's local +Z direction.
            Vector3 axisZ = edge.normalized;
            Vector3 axisX = new Vector3(axisZ.z, 0f, -axisZ.x);

            float candidateMinX = float.MaxValue;
            float candidateMaxX = float.MinValue;
            float candidateMinZ = float.MaxValue;
            float candidateMaxZ = float.MinValue;

            for (int pointIndex = 0; pointIndex < cleanPoints.Count; pointIndex++)
            {
                float projectedX = Vector3.Dot(cleanPoints[pointIndex], axisX);
                float projectedZ = Vector3.Dot(cleanPoints[pointIndex], axisZ);

                candidateMinX = Mathf.Min(candidateMinX, projectedX);
                candidateMaxX = Mathf.Max(candidateMaxX, projectedX);
                candidateMinZ = Mathf.Min(candidateMinZ, projectedZ);
                candidateMaxZ = Mathf.Max(candidateMaxZ, projectedZ);
            }

            float candidateWidth = candidateMaxX - candidateMinX;
            float candidateDepth = candidateMaxZ - candidateMinZ;
            float candidateArea = candidateWidth * candidateDepth;

            // When two orientations have the same area, prefer the one whose +Z
            // direction is the longer rectangle side. This keeps width/length stable.
            bool isBetter =
                !foundRectangle ||
                candidateArea < bestArea - 0.001f ||
                (Mathf.Abs(candidateArea - bestArea) <= 0.001f && candidateDepth > bestDepth);

            if (!isBetter)
            {
                continue;
            }

            foundRectangle = true;
            bestArea = candidateArea;
            bestDepth = candidateDepth;
            bestAxisX = axisX;
            bestAxisZ = axisZ;
            bestMinX = candidateMinX;
            bestMaxX = candidateMaxX;
            bestMinZ = candidateMinZ;
            bestMaxZ = candidateMaxZ;
        }

        if (!foundRectangle)
        {
            return false;
        }

        width = Mathf.Max(minimumFootprintDimension, bestMaxX - bestMinX);
        depth = Mathf.Max(minimumFootprintDimension, bestMaxZ - bestMinZ);

        float rectangleCenterX = (bestMinX + bestMaxX) * 0.5f;
        float rectangleCenterZ = (bestMinZ + bestMaxZ) * 0.5f;
        center = bestAxisX * rectangleCenterX + bestAxisZ * rectangleCenterZ;
        center.y = averageY;

        // Same heading convention used by Quaternion.Euler(0, yaw, 0):
        // yaw 0 = +Z, yaw 90 = +X.
        angleDegrees = Mathf.Atan2(bestAxisZ.x, bestAxisZ.z) * Mathf.Rad2Deg;
        return true;
    }

    bool TryComputeGeographicFootprintFit(
        List<GeoCoordinate> geographicPoints,
        out double centerLatitude,
        out double centerLongitude,
        out float widthMeters,
        out float depthMeters,
        out float headingDegrees)
    {
        centerLatitude = 0.0;
        centerLongitude = 0.0;
        widthMeters = 0f;
        depthMeters = 0f;
        headingDegrees = 0f;

        if (geographicPoints == null || geographicPoints.Count < 3)
        {
            return false;
        }

        List<GeoCoordinate> cleanPoints = new List<GeoCoordinate>();
        for (int i = 0; i < geographicPoints.Count; i++)
        {
            GeoCoordinate point = geographicPoints[i];
            if (cleanPoints.Count == 0 ||
                !AreGeographicPointsEqual(cleanPoints[cleanPoints.Count - 1], point))
            {
                cleanPoints.Add(point);
            }
        }

        if (cleanPoints.Count > 1 &&
            AreGeographicPointsEqual(cleanPoints[0], cleanPoints[cleanPoints.Count - 1]))
        {
            cleanPoints.RemoveAt(cleanPoints.Count - 1);
        }

        if (cleanPoints.Count < 3)
        {
            return false;
        }

        double referenceLatitude = 0.0;
        double referenceLongitude = 0.0;
        for (int i = 0; i < cleanPoints.Count; i++)
        {
            referenceLatitude += cleanPoints[i].latitude;
            referenceLongitude += cleanPoints[i].longitude;
        }
        referenceLatitude /= cleanPoints.Count;
        referenceLongitude /= cleanPoints.Count;

        const double metersPerDegreeLatitude = 111320.0;
        double metersPerDegreeLongitude =
            metersPerDegreeLatitude *
            System.Math.Cos(referenceLatitude * System.Math.PI / 180.0);

        if (System.Math.Abs(metersPerDegreeLongitude) < 0.000001)
        {
            return false;
        }

        List<Vector3> localMeterPoints = new List<Vector3>();
        for (int i = 0; i < cleanPoints.Count; i++)
        {
            float eastMeters = (float)(
                (cleanPoints[i].longitude - referenceLongitude) *
                metersPerDegreeLongitude
            );
            float northMeters = (float)(
                (cleanPoints[i].latitude - referenceLatitude) *
                metersPerDegreeLatitude
            );

            localMeterPoints.Add(new Vector3(eastMeters, 0f, northMeters));
        }

        if (!TryComputeFootprintFit(
                localMeterPoints,
                out Vector3 localCenter,
                out widthMeters,
                out depthMeters,
                out headingDegrees))
        {
            return false;
        }

        centerLatitude = referenceLatitude + localCenter.z / metersPerDegreeLatitude;
        centerLongitude = referenceLongitude + localCenter.x / metersPerDegreeLongitude;
        headingDegrees = NormalizeHeadingDegrees(headingDegrees);
        return true;
    }

    bool IsInsideConfiguredMapExtent(
        double latitude,
        double longitude,
        out double distanceMeters)
    {
        distanceMeters = 0.0;

        if (!limitMapExtent ||
            arcGISMap == null ||
            arcGISMap.OriginPosition == null)
        {
            return true;
        }

        double centerLatitude = arcGISMap.OriginPosition.Y;
        double centerLongitude = arcGISMap.OriginPosition.X;

        distanceMeters = CalculateGeographicDistanceMeters(
            centerLatitude,
            centerLongitude,
            latitude,
            longitude
        );

        return distanceMeters <=
            System.Math.Max(1.0, mapExtentSizeMeters);
    }

    double CalculateGeographicDistanceMeters(
        double latitudeA,
        double longitudeA,
        double latitudeB,
        double longitudeB)
    {
        const double earthRadiusMeters = 6371000.0;
        const double degreesToRadians =
            System.Math.PI / 180.0;

        double latitudeARadians =
            latitudeA * degreesToRadians;
        double latitudeBRadians =
            latitudeB * degreesToRadians;

        double latitudeDelta =
            (latitudeB - latitudeA) * degreesToRadians;
        double longitudeDelta =
            (longitudeB - longitudeA) * degreesToRadians;

        double sinLatitude =
            System.Math.Sin(latitudeDelta * 0.5);
        double sinLongitude =
            System.Math.Sin(longitudeDelta * 0.5);

        double haversine =
            sinLatitude * sinLatitude +
            System.Math.Cos(latitudeARadians) *
            System.Math.Cos(latitudeBRadians) *
            sinLongitude * sinLongitude;

        double centralAngle =
            2.0 * System.Math.Atan2(
                System.Math.Sqrt(haversine),
                System.Math.Sqrt(
                    System.Math.Max(0.0, 1.0 - haversine)
                )
            );

        return earthRadiusMeters * centralAngle;
    }

    float NormalizeHeadingDegrees(float headingDegrees)
    {
        headingDegrees %= 360f;
        if (headingDegrees < 0f)
        {
            headingDegrees += 360f;
        }

        return headingDegrees;
    }

    bool AreGeographicPointsEqual(GeoCoordinate a, GeoCoordinate b)
    {
        return
            System.Math.Abs(a.latitude - b.latitude) <= 0.000000001 &&
            System.Math.Abs(a.longitude - b.longitude) <= 0.000000001;
    }

    Quaternion GetBuildingModelLocalRotation(BuildingOrientationRule orientationRule)
    {
        // A matching per-prefab rule always supplies the complete local rotation.
        // Prefabs without a rule use the global fallback rotation below.
        if (orientationRule != null)
        {
            return Quaternion.Euler(orientationRule.modelLocalRotationEuler);
        }

        return Quaternion.Euler(
            globalBuildingModelAxisCorrectionEuler +
            defaultBuildingModelLocalRotationEuler
        );
    }

    Vector3 ComputeVillageScale(float width, float depth, float area)
    {
        float footprintScale = Mathf.Max(width, depth);
        float xScale = Mathf.Clamp(width / 18f, 0.35f, 4f);
        float zScale = Mathf.Clamp(depth / 18f, 0.35f, 4f);
        float yScale = area < smallBuildingAreaMax
            ? UnityEngine.Random.Range(0.7f, 1.15f)
            : area < mediumBuildingAreaMax
                ? UnityEngine.Random.Range(0.9f, 1.6f)
                : Mathf.Clamp(footprintScale / 18f, 1.0f, 2.2f);

        return new Vector3(xScale, yScale, zScale);
    }

    Vector3 GetBuildingModelLocalScale(BuildingOrientationRule orientationRule, float width, float depth, float area)
    {
        Vector3 baseScale = useExactOsmFootprintPlacement
            ? Vector3.one
            : ComputeVillageScale(width, depth, area);

        if (orientationRule != null)
        {
            baseScale = Vector3.Scale(baseScale, orientationRule.modelLocalScaleMultiplier);
        }

        return baseScale;
    }

    void FitBuildingModelToFootprint(
        GameObject buildingModel,
        Transform footprintOrientation,
        Transform footprintScaler,
        Transform footprintSpace,
        float width,
        float depth,
        BuildingOrientationRule orientationRule)
    {
        if (buildingModel == null ||
            footprintOrientation == null ||
            footprintScaler == null ||
            footprintSpace == null)
        {
            return;
        }

        width = Mathf.Max(minimumFootprintDimension, width);
        depth = Mathf.Max(minimumFootprintDimension, depth);

        // Asset-specific size correction. This is useful when a prefab includes roof
        // overhangs, stairs, balconies, or other geometry outside its wall footprint.
        if (orientationRule != null)
        {
            width = Mathf.Max(
                minimumFootprintDimension,
                width * Mathf.Max(0.01f, orientationRule.osmWidthMultiplier) +
                orientationRule.osmSizeOffsetMeters.x
            );

            depth = Mathf.Max(
                minimumFootprintDimension,
                depth * Mathf.Max(0.01f, orientationRule.osmLengthMultiplier) +
                orientationRule.osmSizeOffsetMeters.y
            );
        }

        footprintScaler.localPosition = Vector3.zero;
        footprintScaler.localRotation = Quaternion.identity;
        footprintScaler.localScale = Vector3.one;

        footprintOrientation.localPosition = Vector3.zero;
        footprintOrientation.localRotation = Quaternion.identity;
        footprintOrientation.localScale = Vector3.one;

        // Keep the model's local import correction untouched. Only this separate parent
        // is allowed to rotate 90 degrees for width/length matching.
        Quaternion fixedModelRotation = buildingModel.transform.localRotation;
        Vector3 fixedModelScale = buildingModel.transform.localScale;
        buildingModel.transform.localPosition = Vector3.zero;
        buildingModel.transform.localRotation = fixedModelRotation;
        buildingModel.transform.localScale = fixedModelScale;

        float manualFootprintYaw = orientationRule != null
            ? orientationRule.footprintYawDegrees
            : 0f;

        bool tryAdditionalQuarterTurn =
            orientationRule == null || orientationRule.autoTryAdditionalQuarterTurn;

        float[] candidateYawDegrees = tryAdditionalQuarterTurn
            ? new[] { manualFootprintYaw, manualFootprintYaw + 90f }
            : new[] { manualFootprintYaw };

        float bestYaw = 0f;
        float bestScore = float.MaxValue;

        for (int i = 0; i < candidateYawDegrees.Length; i++)
        {
            footprintOrientation.localPosition = Vector3.zero;
            footprintOrientation.localRotation = Quaternion.Euler(0f, candidateYawDegrees[i], 0f);
            buildingModel.transform.localPosition = Vector3.zero;

            if (!TryMeasureFootprintBounds(
                    buildingModel,
                    footprintSpace,
                    orientationRule,
                    out Bounds candidateBounds))
            {
                continue;
            }

            if (candidateBounds.size.x <= 0.001f || candidateBounds.size.z <= 0.001f)
            {
                continue;
            }

            float requiredScaleX = width / candidateBounds.size.x;
            float requiredScaleZ = depth / candidateBounds.size.z;

            // Select the orientation needing the least shape distortion.
            float ratio = Mathf.Max(0.0001f, requiredScaleX) /
                          Mathf.Max(0.0001f, requiredScaleZ);
            float score = Mathf.Abs(Mathf.Log(ratio));

            if (score < bestScore)
            {
                bestScore = score;
                bestYaw = candidateYawDegrees[i];
            }
        }

        footprintOrientation.localPosition = Vector3.zero;
        footprintOrientation.localRotation = Quaternion.Euler(0f, bestYaw, 0f);
        buildingModel.transform.localPosition = Vector3.zero;
        buildingModel.transform.localRotation = fixedModelRotation;
        buildingModel.transform.localScale = fixedModelScale;

        if (!TryMeasureFootprintBounds(
                buildingModel,
                footprintSpace,
                orientationRule,
                out Bounds bounds))
        {
            Debug.LogWarning($"⚠️ Could not measure footprint bounds for {buildingModel.name}.");
            return;
        }

        bool centerModel = orientationRule == null || orientationRule.centerModelOnFootprint;
        if (centerModel)
        {
            // FootprintOrientation.localPosition is expressed in FootprintScaler/root axes,
            // so the root-space bounds centre can be removed directly.
            footprintOrientation.localPosition = new Vector3(
                -bounds.center.x,
                0f,
                -bounds.center.z
            );

            if (!TryMeasureFootprintBounds(
                    buildingModel,
                    footprintSpace,
                    orientationRule,
                    out bounds))
            {
                return;
            }
        }

        if (orientationRule != null)
        {
            // This is intentionally applied after automatic centring so the user can
            // fine-tune an asset with an unusual pivot directly from the Inspector.
            buildingModel.transform.localPosition += orientationRule.modelLocalPositionOffset;
        }

        if (bounds.size.x <= 0.001f || bounds.size.z <= 0.001f)
        {
            Debug.LogWarning($"⚠️ Invalid footprint size for {buildingModel.name}: {bounds.size}");
            return;
        }

        float scaleX = width / bounds.size.x;
        float scaleZ = depth / bounds.size.z;

        // Scale only map X/Z. Height remains unchanged because Y is always exactly 1.
        footprintScaler.localScale = new Vector3(scaleX, 1f, scaleZ);

        Debug.Log(
            $"📐 Footprint fitted {buildingModel.name} | " +
            $"Measured={bounds.size.x:F2}×{bounds.size.z:F2} | " +
            $"Target={width:F2}×{depth:F2}m | " +
            $"XZ Scale={scaleX:F3},{scaleZ:F3} | " +
            $"FootprintYaw={bestYaw:F0}° | " +
            $"ModelLocalEuler={buildingModel.transform.localRotation.eulerAngles} | " +
            $"Rule={(orientationRule != null ? "per-prefab" : "global")}"
        );
    }

    bool TryMeasureFootprintBounds(
        GameObject buildingModel,
        Transform footprintSpace,
        BuildingOrientationRule orientationRule,
        out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);

        if (buildingModel == null || footprintSpace == null)
        {
            return false;
        }

        if (preferDedicatedFootprintBounds)
        {
            string markerName = orientationRule != null &&
                                !string.IsNullOrWhiteSpace(orientationRule.footprintBoundsChildName)
                ? orientationRule.footprintBoundsChildName.Trim()
                : defaultFootprintBoundsChildName;

            Transform marker = FindChildRecursive(buildingModel.transform, markerName);
            if (marker != null &&
                TryMeasureMarkerBounds(marker, footprintSpace, out bounds))
            {
                return true;
            }
        }

        return TryMeasureRendererBounds(buildingModel, footprintSpace, out bounds);
    }

    bool TryMeasureMarkerBounds(Transform marker, Transform footprintSpace, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        BoxCollider boxCollider = marker.GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            Vector3[] corners = GetBoxColliderWorldCorners(boxCollider);
            EncapsulateWorldCorners(corners, footprintSpace, ref bounds, ref hasBounds);
        }

        Renderer[] markerRenderers = marker.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < markerRenderers.Length; i++)
        {
            if (markerRenderers[i] == null)
            {
                continue;
            }

            Vector3[] corners = GetRendererWorldCorners(markerRenderers[i]);
            EncapsulateWorldCorners(corners, footprintSpace, ref bounds, ref hasBounds);
        }

        return hasBounds;
    }

    bool TryMeasureRendererBounds(
        GameObject buildingModel,
        Transform footprintSpace,
        out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);

        Renderer[] renderers = buildingModel.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            return false;
        }

        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            Vector3[] worldCorners = GetRendererWorldCorners(renderer);
            EncapsulateWorldCorners(worldCorners, footprintSpace, ref bounds, ref hasBounds);
        }

        return hasBounds;
    }

    void EncapsulateWorldCorners(
        Vector3[] worldCorners,
        Transform footprintSpace,
        ref Bounds bounds,
        ref bool hasBounds)
    {
        if (worldCorners == null || footprintSpace == null)
        {
            return;
        }

        for (int i = 0; i < worldCorners.Length; i++)
        {
            Vector3 localCorner = footprintSpace.InverseTransformPoint(worldCorners[i]);
            if (!hasBounds)
            {
                bounds = new Bounds(localCorner, Vector3.zero);
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(localCorner);
            }
        }
    }

    Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
        {
            return null;
        }

        if (root.name.Equals(childName, System.StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), childName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    Vector3[] GetBoxColliderWorldCorners(BoxCollider boxCollider)
    {
        Vector3 center = boxCollider.center;
        Vector3 extents = boxCollider.size * 0.5f;
        Vector3[] localCorners =
        {
            center + new Vector3(-extents.x, -extents.y, -extents.z),
            center + new Vector3(-extents.x, -extents.y,  extents.z),
            center + new Vector3(-extents.x,  extents.y, -extents.z),
            center + new Vector3(-extents.x,  extents.y,  extents.z),
            center + new Vector3( extents.x, -extents.y, -extents.z),
            center + new Vector3( extents.x, -extents.y,  extents.z),
            center + new Vector3( extents.x,  extents.y, -extents.z),
            center + new Vector3( extents.x,  extents.y,  extents.z)
        };

        Vector3[] worldCorners = new Vector3[localCorners.Length];
        for (int i = 0; i < localCorners.Length; i++)
        {
            worldCorners[i] = boxCollider.transform.TransformPoint(localCorners[i]);
        }

        return worldCorners;
    }

    void ConfigureBuildingCollider(GameObject buildingRoot, GameObject buildingModel)
    {
        if (buildingRoot == null || buildingModel == null)
        {
            return;
        }

        if (!TryMeasureRendererBounds(buildingModel, buildingRoot.transform, out Bounds visualBounds))
        {
            return;
        }

        BoxCollider boxCollider = buildingRoot.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            boxCollider = buildingRoot.AddComponent<BoxCollider>();
        }

        // visualBounds is already measured in buildingRoot local space.
        boxCollider.center = visualBounds.center;
        boxCollider.size = visualBounds.size;
    }

    Vector3[] GetRendererWorldCorners(Renderer renderer)
    {
        if (renderer == null)
        {
            return System.Array.Empty<Vector3>();
        }

        Bounds localBounds;
        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        SkinnedMeshRenderer skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
        if (skinnedMeshRenderer != null)
        {
            localBounds = skinnedMeshRenderer.localBounds;
        }
        else if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            localBounds = meshFilter.sharedMesh.bounds;
        }
        else
        {
            Bounds worldBounds = renderer.bounds;
            Vector3 worldCenter = worldBounds.center;
            Vector3 worldExtents = worldBounds.extents;
            return new[]
            {
                worldCenter + new Vector3(-worldExtents.x, -worldExtents.y, -worldExtents.z),
                worldCenter + new Vector3(-worldExtents.x, -worldExtents.y,  worldExtents.z),
                worldCenter + new Vector3(-worldExtents.x,  worldExtents.y, -worldExtents.z),
                worldCenter + new Vector3(-worldExtents.x,  worldExtents.y,  worldExtents.z),
                worldCenter + new Vector3( worldExtents.x, -worldExtents.y, -worldExtents.z),
                worldCenter + new Vector3( worldExtents.x, -worldExtents.y,  worldExtents.z),
                worldCenter + new Vector3( worldExtents.x,  worldExtents.y, -worldExtents.z),
                worldCenter + new Vector3( worldExtents.x,  worldExtents.y,  worldExtents.z)
            };
        }

        Vector3 center = localBounds.center;
        Vector3 extents = localBounds.extents;
        Vector3[] localCorners =
        {
            center + new Vector3(-extents.x, -extents.y, -extents.z),
            center + new Vector3(-extents.x, -extents.y,  extents.z),
            center + new Vector3(-extents.x,  extents.y, -extents.z),
            center + new Vector3(-extents.x,  extents.y,  extents.z),
            center + new Vector3( extents.x, -extents.y, -extents.z),
            center + new Vector3( extents.x, -extents.y,  extents.z),
            center + new Vector3( extents.x,  extents.y, -extents.z),
            center + new Vector3( extents.x,  extents.y,  extents.z)
        };

        Vector3[] worldCorners = new Vector3[localCorners.Length];
        for (int i = 0; i < localCorners.Length; i++)
        {
            worldCorners[i] = renderer.transform.TransformPoint(localCorners[i]);
        }

        return worldCorners;
    }

    void ApplyBuildingReplacementOverrides(
        GameObject buildingModel,
        GameObject prefabToUse,
        int buildingIndex)
    {
        if (buildingModel == null || buildingReplacements == null || prefabToUse == null)
        {
            return;
        }

        foreach (var rule in buildingReplacements)
        {
            if (rule == null ||
                rule.prefab != prefabToUse ||
                rule.buildingIndex != buildingIndex)
            {
                continue;
            }

            buildingModel.transform.localScale *= Mathf.Max(0.01f, rule.scaleMultiplier);
            buildingModel.transform.localRotation =
                Quaternion.Euler(rule.rotationEulerOffset) *
                buildingModel.transform.localRotation;
            return;
        }
    }

    void ConfigureHouseSimpleLod(GameObject buildingRoot)
    {
        if (!enableHouseSimpleLod || buildingRoot == null)
        {
            return;
        }

        HouseSimpleLOD houseLod =
            buildingRoot.GetComponent<HouseSimpleLOD>();

        if (houseLod == null)
        {
            houseLod = buildingRoot.AddComponent<HouseSimpleLOD>();
        }

        Transform viewerTransform =
            droneCamera != null
                ? droneCamera.transform
                : droneTransform != null
                    ? droneTransform
                    : Camera.main != null
                        ? Camera.main.transform
                        : null;

        houseLod.Configure(
            viewerTransform,
            houseSimpleLodDetailDistance,
            houseSimpleLodCullDistance,
            houseSimpleLodRefreshSeconds
        );
    }

    void PositionCamera()
    {
        Camera.main.transform.position = new Vector3(0, scale / 5, -scale / 2);
        Camera.main.transform.rotation = Quaternion.Euler(45, 0, 0);
    }

    void RunPython()
    {
        string resolvedPythonPath = ResolvePythonExecutable();
        string customLocation = string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},0",
            GameManager.homeLat,
            GameManager.homeLon,
            GameManager.homeAlt
        );

        string radiusArgument =
            osmQueryRadiusMeters.ToString(
                CultureInfo.InvariantCulture
            );

        string arguments =
            $"\"{scriptPath}\" " +
            $"--custom-location {customLocation}";

        arguments += $" --radius-meters {radiusArgument}";

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = resolvedPythonPath,
            Arguments = arguments,
            WorkingDirectory =
                Path.GetDirectoryName(scriptPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        pythonProcessCompleted = false;
        pythonProcessExitCode = int.MinValue;

        pythonProcess = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };

        pythonProcess.OutputDataReceived +=
            (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    Debug.Log("[PYTHON] " + args.Data);
                }
            };

        pythonProcess.ErrorDataReceived +=
            (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    Debug.LogError(
                        "🐍 ERROR: " + args.Data
                    );
                }
            };

        pythonProcess.Exited +=
            (sender, args) =>
            {
                try
                {
                    pythonProcessExitCode =
                        pythonProcess.ExitCode;
                }
                catch
                {
                    pythonProcessExitCode = -1;
                }

                pythonProcessCompleted = true;
            };

        try
        {
            Debug.Log(
                "🐍 Starting Python: " +
                resolvedPythonPath + " " + arguments
            );

            pythonProcess.Start();
            pythonProcess.BeginOutputReadLine();
            pythonProcess.BeginErrorReadLine();
        }
        catch (System.Exception exception)
        {
            pythonProcessExitCode = -1;
            pythonProcessCompleted = true;

            Debug.LogError(
                "❌ Could not start Python: " +
                exception.Message
            );
        }
    }

    string ResolvePythonExecutable()
    {
        string environmentPath = (
            System.Environment.GetEnvironmentVariable(
                "SANTATRAIL_PYTHON"
            ) ?? ""
        ).Trim();
        if (!string.IsNullOrEmpty(environmentPath))
        {
            return environmentPath;
        }

        string configuredPath = (pythonPath ?? "").Trim();
        if (!string.IsNullOrEmpty(configuredPath))
        {
            bool looksLikeFilePath =
                configuredPath.Contains(Path.DirectorySeparatorChar.ToString()) ||
                configuredPath.Contains(Path.AltDirectorySeparatorChar.ToString());

            if (!looksLikeFilePath || File.Exists(configuredPath))
            {
                return configuredPath;
            }

            Debug.LogWarning(
                "⚠️ Configured Python executable was not found: " +
                configuredPath + ". Trying portable locations instead."
            );
        }

        List<string> candidates = new List<string>();

        bool useWindowsRuntime;
#if UNITY_EDITOR
        // Unity can compile for a Windows build target while Play Mode is
        // still running inside the macOS editor. Use the actual host here.
        useWindowsRuntime = Application.platform == RuntimePlatform.WindowsEditor;
#elif UNITY_STANDALONE_WIN
        useWindowsRuntime = true;
#else
        useWindowsRuntime = false;
#endif

        if (useWindowsRuntime)
        {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
            candidates.Add(Path.Combine(
                Application.streamingAssetsPath,
                "Python",
                "python.exe"
            ));
            candidates.Add(Path.Combine(
                System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.LocalApplicationData
                ),
                "SantaTrail",
                "Python",
                "python.exe"
            ));
#endif
        }
        else
        {
#if UNITY_EDITOR || UNITY_STANDALONE_OSX
            candidates.Add(Path.Combine(
                Application.streamingAssetsPath,
                "Python",
                "bin",
                "python3"
            ));
            candidates.Add("/usr/bin/python3");
            candidates.Add("/opt/homebrew/bin/python3");
            candidates.Add("/usr/local/bin/python3");
#endif
        }

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return useWindowsRuntime ? "python" : "python3";
    }

    bool TryConvertGPS(double lat, double lon, double alt, out Vector3 pos)
    {
        pos = Vector3.zero;

        if (arcGISConverter != null && arcGISConverter.IsReady())
        {
            pos = arcGISConverter.GPSToUnity(lat, lon, alt);

            Debug.Log(
                $"ArcGIS GPS->Unity: {lat},{lon} => {pos}"
            );
        }
        else if (CanUseFallbackProjection())
        {
            pos = LocalGpsToUnity(lat, lon, alt);
        }
        else
        {
            return false;
        }

        if (pos == Vector3.zero)
            return false;

        if (float.IsNaN(pos.x) || float.IsInfinity(pos.x) ||
            float.IsNaN(pos.y) || float.IsInfinity(pos.y) ||
            float.IsNaN(pos.z) || float.IsInfinity(pos.z))
            return false;

        if (pos.magnitude > 10000000f)
            return false;

        return true;
    }

    Vector3 LocalGpsToUnity(double lat, double lon, double alt)
    {
        double metersPerDegLat = 111320.0;
        double metersPerDegLon = 111320.0 * System.Math.Cos(fallbackOriginLat * System.Math.PI / 180.0);

        float x = (float)((lon - fallbackOriginLon) * metersPerDegLon);
        float z = (float)((lat - fallbackOriginLat) * metersPerDegLat);
        float y = (float)alt;
        return new Vector3(x, y, z);

    }

    void PlaceDroneOnArcGIS(Result result)
    {
        if (droneTransform == null || result == null || result.waypoints == null || result.waypoints.Length == 0)
            return;

        var wp0 = result.waypoints[0];
        if (!TryConvertGPS(wp0.lat, wp0.lon, wp0.alt, out Vector3 pos))
            return;

        // The first waypoint is the vehicle's resting position. Do not add a
        // takeoff-height offset here; MAVLink telemetry supplies altitude once
        // the pilot actually takes off.
        droneTransform.position = pos;

        Rigidbody rb = droneTransform.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = droneTransform.GetComponentInParent<Rigidbody>();
        }
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Debug.Log(
    $"Drone Unity Pos = {pos}"
);
        Debug.Log(
            $"Drone GPS = {wp0.lat}, {wp0.lon}"
        );


        Debug.Log("🚁 Drone placed over ArcGIS surface");
    }

    void SetupDroneReference()
    {
        GameObject droneObj = GameObject.FindWithTag("Drone");

        if (droneObj != null)
        {
            ArcGISLocationComponent locationComponent = droneObj.GetComponentInChildren<ArcGISLocationComponent>(true);
            droneTransform = locationComponent != null ? locationComponent.transform : droneObj.transform;
            Debug.Log("✅ Drone found");
        }
        else
        {
            Debug.LogError("❌ Drone not found (set tag = Drone)");
        }
    }
    Vector3 NEDToUnity(float north, float east, float down)
    {
        return new Vector3(
            east,
            -down,
            north
        );
    }
    IEnumerator WaitForArcGISAndGenerate()
    {
        Debug.Log("⏳ Waiting for ArcGIS map...");

        while (true)
        {
            if (arcGISConverter != null && arcGISConverter.IsReady())
            {
                Debug.Log("✅ ArcGIS READY");
                LoadAndGenerateSafe();
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    Transform GetArcGISRoot()
    {
        ArcGISMapComponent map =
            FindFirstObjectByType<ArcGISMapComponent>();

        if (map != null)
            return map.transform;

        return null;
    }

    Transform GetGeneratedBuildingsRoot()
    {
        Transform mapRoot = GetArcGISRoot();

        if (generatedBuildingsRoot != null)
        {
            if (mapRoot != null && generatedBuildingsRoot.parent != mapRoot)
            {
                generatedBuildingsRoot.SetParent(mapRoot, false);
            }

            return generatedBuildingsRoot;
        }

        Transform existing = null;

        if (mapRoot != null)
        {
            existing = mapRoot.Find("GeneratedBuildings");
        }

        if (existing == null)
        {
            GameObject existingObject =
                GameObject.Find("GeneratedBuildings");

            if (existingObject != null)
            {
                existing = existingObject.transform;
            }
        }

        if (existing == null)
        {
            GameObject container =
                new GameObject("GeneratedBuildings");

            existing = container.transform;

            if (mapRoot != null)
            {
                existing.SetParent(mapRoot, false);
            }
            else
            {
                existing.position = Vector3.zero;
                existing.rotation = Quaternion.identity;
                existing.localScale = Vector3.one;
            }
        }
        else if (mapRoot != null && existing.parent != mapRoot)
        {
            existing.SetParent(mapRoot, false);
        }

        generatedBuildingsRoot = existing;
        return generatedBuildingsRoot;
    }

    Transform GetGeneratedEnvironmentRoot()
    {
        Transform mapRoot = GetArcGISRoot();

        if (generatedEnvironmentRoot != null)
        {
            if (mapRoot != null && generatedEnvironmentRoot.parent != mapRoot)
            {
                generatedEnvironmentRoot.SetParent(mapRoot, false);
            }

            return generatedEnvironmentRoot;
        }

        Transform existing = null;

        if (mapRoot != null)
        {
            existing = mapRoot.Find("GeneratedEnvironment");
        }

        if (existing == null)
        {
            GameObject existingObject = GameObject.Find("GeneratedEnvironment");
            if (existingObject != null)
            {
                existing = existingObject.transform;
            }
        }

        if (existing == null)
        {
            GameObject container = new GameObject("GeneratedEnvironment");
            existing = container.transform;

            if (mapRoot != null)
            {
                existing.SetParent(mapRoot, false);
            }
            else
            {
                existing.position = Vector3.zero;
                existing.rotation = Quaternion.identity;
                existing.localScale = Vector3.one;
            }
        }
        else if (mapRoot != null && existing.parent != mapRoot)
        {
            existing.SetParent(mapRoot, false);
        }

        generatedEnvironmentRoot = existing;
        return generatedEnvironmentRoot;
    }

    Transform GetGeneratedObjectsRoot()
    {
        Transform mapRoot = GetArcGISRoot();

        if (generatedObjectsRoot != null)
        {
            if (mapRoot != null && generatedObjectsRoot.parent != mapRoot)
            {
                generatedObjectsRoot.SetParent(mapRoot, false);
            }

            return generatedObjectsRoot;
        }

        GameObject root = GameObject.Find("GeneratedArcGISObjects");
        if (root == null)
        {
            root = new GameObject("GeneratedArcGISObjects");
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
        }
        else if (mapRoot != null && root.transform.parent != mapRoot)
        {
            root.transform.SetParent(mapRoot, false);
        }

        generatedObjectsRoot = root.transform;
        return generatedObjectsRoot;
    }

    void ClearGeneratedContent()
    {
        Transform generatedBuildings = GetGeneratedBuildingsRoot();
        ClearLegacyDirectGeneratedBuildings(
            GetArcGISRoot(),
            generatedBuildings
        );
        ClearGeneratedChildren(generatedBuildings);
        ClearGeneratedChildren(GetGeneratedEnvironmentRoot());
        ClearGeneratedChildren(GetGeneratedObjectsRoot());
    }

    static void ClearLegacyDirectGeneratedBuildings(
        Transform mapRoot,
        Transform generatedBuildingsRoot)
    {
        if (mapRoot == null)
        {
            return;
        }

        for (int i = mapRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = mapRoot.GetChild(i);
            if (child == null || child == generatedBuildingsRoot)
            {
                continue;
            }

            // Older versions spawned generated houses directly under ArcGISMap.
            // Their naming is reserved for generated data, not manual level content.
            if (child.name.StartsWith("ModularHouse_ArcGISBuilding_") ||
                child.name.Contains("_ArcGISBuilding_"))
            {
                Object.Destroy(child.gameObject);
            }
        }
    }

    static void ClearGeneratedChildren(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child != null)
            {
                Object.Destroy(child.gameObject);
            }
        }
    }


    void OnValidate()
    {
        osmWallFootprintScale =
            Mathf.Clamp(osmWallFootprintScale, 1f, 1.5f);

        mapExtentSizeMeters =
            System.Math.Max(1.0, mapExtentSizeMeters);

        buildingSpawnRadius =
            Mathf.Max(1f, buildingSpawnRadius);

        roadsideStreetLightSpacingMeters =
            Mathf.Max(5f, roadsideStreetLightSpacingMeters);

        roadsideStreetLightOffsetMeters =
            Mathf.Max(0.5f, roadsideStreetLightOffsetMeters);

        roadsideStreetLightMaximumCount =
            Mathf.Max(0, roadsideStreetLightMaximumCount);

        osmQueryRadiusMeters =
            System.Math.Max(
                1.0,
                osmQueryRadiusMeters
            );
    }
}
