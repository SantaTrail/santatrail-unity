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


public class SceneLoaderArcgis : MonoBehaviour
{
    [Header("Camera")]
    public Camera droneCamera;
    public float scale = 1000f;
    ArcGISMapComponent arcGISMap;
    Transform arcGISRoot;
    [Header("Level Origin")]
    [Tooltip("When enabled, this scene sets the ArcGIS map origin from the values below.")]
    [SerializeField] bool overrideMapOrigin = false;
    [SerializeField] double mapOriginLongitude;
    [SerializeField] double mapOriginLatitude;
    [SerializeField] double mapOriginAltitude;
    [SerializeField] bool limitMapExtent = false;
    [SerializeField] double mapExtentSizeMeters = 1000f;
    [Header("ArcGIS")]
    [SerializeField] ArcGISConverter arcGISConverter;
    [Header("Fallback")]
    [SerializeField] bool allowLocalGpsFallback = true;
    [Header("Generation Mode")]
    [SerializeField] bool useArcGISTerrainOnly = true;
    [Header("Building Spawn")]
    [SerializeField] float buildingSpawnRadius = 500f;
    [SerializeField] bool showSpawnRadius = true;
    [SerializeField] bool hideOsmBuildingLayer = true;
    [SerializeField] bool logLayerVisibilityChanges = true;
    float waterHeight = -1f;

    [Header("Python Script")]
    public string pythonPath = "/Users/notebook/.pyenv/versions/3.10.18/bin/python3";

    string scriptPath;
    string outputPath;
    [Header("Prefabs")]
    public GameObject treePrefab;
    public GameObject buildingPrefab;
    public GameObject[] randomHousePrefabs;
    [Header("Village Buildings")]
    public GameObject[] smallHousePrefabs;
    public GameObject[] mediumHousePrefabs;
    public GameObject[] largeBuildingPrefabs;
    [SerializeField] bool spawnVisibleVillageClusterOnly = false;
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

    Terrain terrain;
    bool fallbackOriginSet;
    double fallbackOriginLat;
    double fallbackOriginLon;
    Transform generatedObjectsRoot;

    void Awake()
    {
        TryResolveArcGISConverter();
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
        ArcGISMapComponent mapComponent = FindFirstObjectByType<ArcGISMapComponent>(FindObjectsInactive.Include);
        if (mapComponent == null)
        {
            Debug.LogWarning("⚠️ SceneLoaderArcgis could not find ArcGISMapComponent to override origin.");
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
                ApplyConfiguredMapExtent(mapComponent, mapComponent.OriginPosition);
                Debug.Log($"🧭 SceneLoaderArcgis kept selected home location: {GameManager.homeLat}, {GameManager.homeLon}, {GameManager.homeAlt}");
            }
            else if (mapComponent.OriginPosition != null)
            {
                GameManager.SetHomeLocation(
                    mapComponent.OriginPosition.Y,
                    mapComponent.OriginPosition.X,
                    mapComponent.OriginPosition.Z
                );
                ApplyConfiguredMapExtent(mapComponent, mapComponent.OriginPosition);
                Debug.Log("🧭 SceneLoaderArcgis initialized home from scene map origin.");
            }

            return;
        }

        mapComponent.OriginPosition = new ArcGISPoint(
            mapOriginLongitude,
            mapOriginLatitude,
            mapOriginAltitude,
            ArcGISSpatialReference.WGS84()
        );
        ApplyConfiguredMapExtent(mapComponent, mapComponent.OriginPosition);
        GameManager.SetHomeLocation(mapOriginLatitude, mapOriginLongitude, mapOriginAltitude);

        Debug.Log($"🧭 SceneLoaderArcgis applied map origin: {mapOriginLatitude}, {mapOriginLongitude}, {mapOriginAltitude}");
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

        string projectRoot = Application.dataPath + "/../";
        string backendPath = ResolveBackendPath(projectRoot);

        scriptPath = Path.Combine(backendPath, "main.py");
        outputPath = Path.Combine(backendPath, "output.json");

        if (!File.Exists(scriptPath))
        {
            Debug.LogError("❌ Python script NOT FOUND at: " + scriptPath);
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
                TryResolveArcGISConverter();

            if (!jsonReady && File.Exists(outputPath))
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
                    Debug.LogWarning("⚠️ JSON read error: " + e.Message);
                }
            }

            if (!arcgisReady && arcGISConverter != null && arcGISConverter.IsReady())
            {
                arcgisReady = true;
                Debug.Log("✅ ArcGIS READY");
            }

            if (jsonReady && (arcgisReady || CanUseFallbackProjection()))
            {
                Debug.Log("🚀 ALL READY → Stabilizing...");
                yield return new WaitForSeconds(2f);

                LoadAndGenerateSafe();
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }

        Debug.LogError("❌ Initialization timeout → aborting generation");
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

    void LoadAndGenerateSafe()
    {
        string json = File.ReadAllText(outputPath);
        Result result = JsonUtility.FromJson<Result>(json);

        if (result == null)
        {
            Debug.LogError("❌ Failed to parse JSON → abort");
            return;
        }

        Debug.Log("🌍 Scene type: " + result.scene);
        SyncMapOriginToResult(result);

        if (!useArcGISTerrainOnly)
        {
            GenerateTerrain(result);

            if (terrain == null)
            {
                Debug.LogError("❌ Terrain failed → abort");
                return;
            }

            CarveRiversIntoTerrainSafe(result);

            if (result.features != null && result.features.water_present)
                GenerateWater();
        }
        else
        {
            PlaceDroneOnArcGIS(result);
        }

        GenerateSceneSafe(result);
        DrawPathSafe(result);
        DrawRiversSafe(result);

        Debug.Log("✅ Scene generation COMPLETE (safe)");
    }

    void SyncMapOriginToResult(Result result)
    {
        if (overrideMapOrigin || arcGISMap == null || result == null || result.waypoints == null || result.waypoints.Length == 0)
        {
            return;
        }

        GPSWaypoint originWaypoint = result.waypoints[0];
        bool shouldReplaceHome = !GameManager.hasHomeLocation;

        if (!shouldReplaceHome)
        {
            double latDelta = System.Math.Abs(GameManager.homeLat - originWaypoint.lat);
            double lonDelta = System.Math.Abs(GameManager.homeLon - originWaypoint.lon);
            shouldReplaceHome = latDelta > 0.25 || lonDelta > 0.25;
        }

        if (!shouldReplaceHome)
        {
            return;
        }

        GameManager.SetHomeLocation(originWaypoint.lat, originWaypoint.lon, originWaypoint.alt, 0);
        arcGISMap.OriginPosition = new ArcGISPoint(
            originWaypoint.lon,
            originWaypoint.lat,
            originWaypoint.alt,
            ArcGISSpatialReference.WGS84()
        );
        ApplyConfiguredMapExtent(arcGISMap, arcGISMap.OriginPosition);

        Debug.Log(
            $"🧭 SceneLoaderArcgis synced map origin to JSON waypoint: {originWaypoint.lat}, {originWaypoint.lon}, {originWaypoint.alt}"
        );
    }
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

            pos.y = ground + 50f;
            droneTransform.position = pos;

            Debug.Log("🚁 Drone safely placed above terrain");
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
        if (useArcGISTerrainOnly)
        {
            if (result.buildings != null && result.buildings.Length > 0)
            {
                SpawnVillageBuildings(result);
            }
            else
            {
                Debug.LogWarning("⚠️ No buildings from JSON");
            }

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

        if (result.buildings != null && result.buildings.Length > 0)
        {
            SpawnVillageBuildings(result);
        }
        else
        {
            Debug.LogWarning("⚠️ No buildings from JSON");
        }

        Debug.Log($"🌳 Trees: {treeCount} | 🏙 Buildings: {(build > 10 ? "YES" : "NO")}");
    }

    void SpawnTrees(int count)
    {
        if (treePrefab == null || terrain == null) return;

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

    void SpawnVillageBuildings(Result result)
    {
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
            SpawnVisibleVillageCluster();
            return;
        }

        if (result.buildings == null || result.buildings.Length == 0 || arcGISConverter == null)
        {
            Debug.LogWarning("⚠️ Village spawn skipped or ArcGIS converter unavailable. Using visible fallback cluster.");
            SpawnVisibleVillageCluster();
            return;
        }

        Debug.Log($"🏙 Spawning village buildings from {result.buildings.Length} OSM footprints");
        int spawned = 0;
        for (int i = 0; i < result.buildings.Length; i++)
        {

            var building = result.buildings[i];
            Debug.Log(
    $"Building {i} has {building.points.Length} points"
);
            if (building.points == null || building.points.Length < 3)
                continue;

            float avgLat = 0;
            float avgLon = 0;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;
            int validPoints = 0;
            bool hasFirstPoint = false;
            bool hasPreviousPoint = false;
            Vector3 firstValidPoint = Vector3.zero;
            Vector3 previousValidPoint = Vector3.zero;
            Vector3 longestEdge = Vector3.forward;
            float longestEdgeSqr = 0f;

            foreach (var p in building.points)
            {
                avgLat += p.lat;
                avgLon += p.lon;
            }

            avgLat /= building.points.Length;
            avgLon /= building.points.Length;

            if (!TryConvertGPS(avgLat, avgLon, 0, out Vector3 center))
            {
                Debug.LogWarning("⚠️ Skipping building (invalid GPS)");
                continue;
            }

            Debug.Log(
    $"Building {i} GPS = {avgLat}, {avgLon}"
);

            Debug.Log(
                $"Building {i} Converted Position = {center}"
            );
            Debug.Log(
                $"Building {i} Unity Pos = {center}"
            );
            foreach (var p in building.points)
            {
                if (!TryConvertGPS(p.lat, p.lon, 0, out Vector3 pt))
                    continue;

                if (!hasFirstPoint)
                {
                    firstValidPoint = pt;
                    hasFirstPoint = true;
                }

                if (hasPreviousPoint)
                {
                    Vector3 edge = pt - previousValidPoint;
                    float edgeSqr = edge.x * edge.x + edge.z * edge.z;
                    if (edgeSqr > longestEdgeSqr)
                    {
                        longestEdgeSqr = edgeSqr;
                        longestEdge = edge;
                    }
                }

                previousValidPoint = pt;
                hasPreviousPoint = true;

                if (pt.x < minX) minX = pt.x;
                if (pt.x > maxX) maxX = pt.x;
                if (pt.z < minZ) minZ = pt.z;
                if (pt.z > maxZ) maxZ = pt.z;
                validPoints++;
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

            if (validPoints < 3)
            {
                Debug.LogWarning("⚠️ Skipping building (not enough valid corners)");
                continue;
            }

            if (terrain != null)
                center.y = terrain.SampleHeight(center);
            else
                center.y += 1f;
            if (droneTransform != null)
            {
                float dist = Vector3.Distance(
                    new Vector3(droneTransform.position.x, 0, droneTransform.position.z),
                    new Vector3(center.x, 0, center.z)
                );

                Debug.Log(
                    $"Building {i} | Drone={droneTransform.position} | Building={center} | Distance={dist:F1}"
                );

                if (dist > buildingSpawnRadius)
                {
                    Debug.LogWarning(
                        $"❌ Building {i} OUTSIDE RADIUS ({dist:F1}m)"
                    );
                    continue;
                }
            }
            float width = Mathf.Clamp(maxX - minX, 5f, 80f);
            float depth = Mathf.Clamp(maxZ - minZ, 5f, 80f);
            float area = width * depth;
            float angle = Mathf.Atan2(longestEdge.x, longestEdge.z) * Mathf.Rad2Deg;

            GameObject prefabToUse = GetBuildingPrefabForIndex(i, width, depth);
            Quaternion rotation = Quaternion.Euler(0f, angle, 0f);
            Transform arcgisRootTransform = GetArcGISRoot();
            GameObject buildingRoot = new GameObject($"{prefabToUse.name}_ArcGISBuilding");

            if (arcgisRootTransform != null)
            {
                buildingRoot.transform.SetParent(arcgisRootTransform, false);
            }
            else
            {
                buildingRoot.transform.position = center;
            }

            ArcGISLocationComponent locationComponent = buildingRoot.AddComponent<ArcGISLocationComponent>();
            locationComponent.SurfacePlacementMode = ArcGISSurfacePlacementMode.OnTheGround;
            locationComponent.SurfacePlacementOffset = 0;
            locationComponent.Position = new ArcGISPoint(
                avgLon,
                avgLat,
                0,
                ArcGISSpatialReference.WGS84()
            );
            locationComponent.Rotation = new ArcGISRotation(angle, 0, 0);

            GameObject buildingModel = Instantiate(prefabToUse, buildingRoot.transform);
            EnsureRenderable(buildingModel);
            Debug.Log(
                $"🏠 Spawned ArcGIS building {buildingRoot.name} | Geo=({avgLat}, {avgLon}) | World={center} | Heading={angle:F1}"
            );
            Debug.Log(
                $"🏠 Model local transform | Pos={buildingModel.transform.localPosition} | Rot={buildingModel.transform.localRotation.eulerAngles} | Scale={buildingModel.transform.localScale}"
            );

            {
                buildingRoot.AddComponent<House>();
            }
            Debug.Log("Drone = " + droneTransform.position);
            Debug.Log("Building = " + center);
            buildingModel.transform.localScale = Vector3.Scale(
                buildingModel.transform.localScale,
                ComputeVillageScale(width, depth, area) * UnityEngine.Random.Range(0.9f, 1.15f)
            );
            if (buildingReplacements != null)
            {
                foreach (var rule in buildingReplacements)
                {
                    if (rule != null && rule.prefab == prefabToUse)
                    {
                        buildingModel.transform.localScale *= Mathf.Max(0.01f, rule.scaleMultiplier);
                        buildingModel.transform.rotation = rotation * Quaternion.Euler(rule.rotationEulerOffset);
                        break;
                    }
                }
            }
            if (buildingRoot.GetComponent<Collider>() == null)
            {
                buildingRoot.AddComponent<BoxCollider>();
            }

            spawned++;
        }

        if (spawned == 0)
        {
            Debug.LogWarning("⚠️ No village buildings could be placed from OSM footprints. Using visible fallback cluster.");
            SpawnVisibleVillageCluster();
            return;
        }

        Debug.Log("🏙 Village buildings spawned: " + spawned);
    }

    void SpawnVisibleVillageCluster()
    {
        if ((smallHousePrefabs == null || smallHousePrefabs.Length == 0) &&
            (mediumHousePrefabs == null || mediumHousePrefabs.Length == 0) &&
            (largeBuildingPrefabs == null || largeBuildingPrefabs.Length == 0) &&
            (randomHousePrefabs == null || randomHousePrefabs.Length == 0))
        {
            Debug.LogWarning("⚠️ No village prefabs assigned for fallback cluster.");
            return;
        }

        Transform anchorTransform = droneTransform != null
            ? droneTransform
            : droneCamera != null
                ? droneCamera.transform
                : Camera.main != null ? Camera.main.transform : null;

        Vector3 center = anchorTransform != null ? anchorTransform.position : Vector3.zero;
        Vector3 forward = anchorTransform != null ? anchorTransform.forward : Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 clusterCenter = center + forward * 4f + Vector3.up * 0.5f;
        int count = 6;
        float radius = Mathf.Clamp(scale * 0.01f, 2f, 5f);
        float angleStep = 360f / Mathf.Max(1, count);

        GameObject heroPrefab = GetGuaranteedVillagePrefab();
        Vector3 heroLocalPos = new Vector3(0f, -0.5f, 4f);
        GameObject heroBuilding = CreateVisibleBuildingSpawn(anchorTransform, heroPrefab, heroLocalPos, Quaternion.identity, 1.5f);
        Debug.Log($"🏠 Hero village building spawned at {heroBuilding.transform.position}");
Debug.Log($"CENTER = {center}");
Debug.Log($"DRONE = {droneTransform.position}");
Debug.Log($"DIST = {Vector3.Distance(center, droneTransform.position)}");
        for (int i = 0; i < count; i++)
        {
            float angle = i * angleStep + UnityEngine.Random.Range(-10f, 10f);
            float distance = UnityEngine.Random.Range(radius * 0.35f, radius);
            Vector3 offset = (right * Mathf.Cos(angle * Mathf.Deg2Rad) + forward * Mathf.Sin(angle * Mathf.Deg2Rad)) * distance;

            GameObject prefab = PickBuildingPrefab(distance, distance);
            Vector3 spawnLocalPos = new Vector3(offset.x, -0.5f, 4f + offset.z);
            Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            GameObject building = CreateVisibleBuildingSpawn(anchorTransform, prefab, spawnLocalPos, rotation, UnityEngine.Random.Range(0.5f, 0.85f));
            Debug.Log($"🏠 Village building spawned at {building.transform.position}");
        }

        Debug.Log("🏙 Visible fallback village cluster spawned");
    }

    GameObject CreateVisibleBuildingSpawn(Transform anchor, GameObject prefab, Vector3 localPosition, Quaternion localRotation, float scaleMultiplier)
    {
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = prefab != null ? $"VisibleBuilding_{prefab.name}" : "VisibleBuilding";
        if (anchor != null)
        {
            root.transform.SetParent(anchor, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = localRotation;
        }
        else
        {
            root.transform.position = localPosition;
            root.transform.rotation = localRotation;
        }
        root.transform.localScale = new Vector3(1f, 1f, 1f) * Mathf.Max(0.1f, scaleMultiplier);

        Renderer rootRenderer = root.GetComponent<Renderer>();
        if (rootRenderer != null && rootRenderer.material != null && rootRenderer.material.HasProperty("_Color"))
        {
            rootRenderer.material.color = new Color(0.92f, 0.78f, 0.35f, 1f);
        }

        if (prefab != null)
        {
            GameObject child = Instantiate(prefab, root.transform);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            EnsureRenderable(child);
            if (child.GetComponent<Collider>() != null)
            {
                Destroy(child.GetComponent<Collider>());
            }
            if (child.GetComponent<House>() == null)
            {
                child.AddComponent<House>();
            }
        }

        if (root.GetComponent<Collider>() == null)
        {
            root.AddComponent<BoxCollider>();
        }
        if (root.GetComponent<House>() == null)
        {
            root.AddComponent<House>();
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

                if (material.HasProperty("_Color"))
                {
                    material.color = new Color(0.95f, 0.75f, 0.2f, 1f);
                }
            }
        }
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

    // =========================
    // PATH
    // =========================
    void DrawPath(Result result)
    {
        if (result.waypoints == null || result.waypoints.Length == 0) return;

        LineRenderer line = new GameObject("Path").AddComponent<LineRenderer>();
        line.widthMultiplier = 8f;
        line.material = new Material(Shader.Find("Sprites/Default"));
        Vector3[] positions = new Vector3[result.waypoints.Length];
        int validCount = 0;

        for (int i = 0; i < result.waypoints.Length; i++)
        {
            var wp = result.waypoints[i];
            if (!TryConvertGPS(wp.lat, wp.lon, wp.alt, out Vector3 pos))
            {
                Debug.LogWarning($"⚠️ Skipping waypoint {i}");
                continue;
            }

            if (terrain != null)
                pos.y = terrain.SampleHeight(pos) + 2f;
            positions[validCount] = pos;
            validCount++;
        }

        if (validCount < 2)
        {
            Destroy(line.gameObject);
            Debug.LogWarning("⚠️ Path skipped (not enough valid waypoints)");
            return;
        }

        line.positionCount = validCount;
        for (int i = 0; i < validCount; i++)
            line.SetPosition(i, positions[i]);
    }

    // =========================
    // RIVERS
    // =========================
    void DrawRivers(Result result)
    {
        if (result.rivers == null || terrain == null) return;

        foreach (var river in result.rivers)
        {
            if (river == null || river.Length < 2)
                continue;

            LineRenderer line = new GameObject("River").AddComponent<LineRenderer>();
            line.widthMultiplier = 8f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = Color.blue;
            line.endColor = Color.blue;
            Vector3[] positions = new Vector3[river.Length];
            int validCount = 0;

            for (int i = 0; i < river.Length; i++)
            {
                var p = river[i];

                if (!TryConvertGPS(p.lat, p.lon, p.alt, out Vector3 pos))
                    continue;

                positions[validCount] = pos;
                validCount++;
            }

            if (validCount < 2)
            {
                Destroy(line.gameObject);
                continue;
            }

            line.positionCount = validCount;
            for (int i = 0; i < validCount; i++)
                line.SetPosition(i, positions[i]);
        }
    }

    // =========================
    // UTILS
    // =========================


    void PositionCamera()
    {
        Camera.main.transform.position = new Vector3(0, scale / 5, -scale / 2);
        Camera.main.transform.rotation = Quaternion.Euler(45, 0, 0);
    }

    void RunPython()
    {
        string customLocation = string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},0",
            GameManager.homeLat,
            GameManager.homeLon,
            GameManager.homeAlt
        );

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"\"{scriptPath}\" --custom-location {customLocation}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        Process p = new Process();
        p.StartInfo = psi;

        p.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
                Debug.Log("[PYTHON] " + args.Data);
        };

        p.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
                Debug.LogError("🐍 ERROR: " + args.Data);
        };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
    }
    void CarveRiversIntoTerrain(Result result)
    {
        if (terrain == null || result.rivers == null || arcGISConverter == null) return;

        TerrainData data = terrain.terrainData;
        int res = data.heightmapResolution;

        float[,] heights = data.GetHeights(0, 0, res, res);

        float riverWidth = 6f;
        float depth = 0.02f;

        foreach (var river in result.rivers)
        {
            foreach (var point in river)
            {
                if (!TryConvertGPS(point.lat, point.lon, point.alt, out Vector3 pos))
                    continue;
                int x = (int)((pos.x + scale / 2) / scale * res);
                int y = (int)((pos.z + scale / 2) / scale * res);

                for (int i = -10; i <= 10; i++)
                {
                    for (int j = -10; j <= 10; j++)
                    {
                        int nx = x + i;
                        int ny = y + j;

                        if (nx < 0 || ny < 0 || nx >= res || ny >= res) continue;

                        float dist = Mathf.Sqrt(i * i + j * j);

                        if (dist < riverWidth)
                        {
                            float falloff = 1f - (dist / riverWidth);

                            heights[ny, nx] -= depth * falloff;
                        }
                    }
                }
            }
        }

        data.SetHeights(0, 0, heights);

        Debug.Log("🌊 Rivers carved into terrain");
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
        Debug.Log($"fallbackOriginLat = {fallbackOriginLat}");
        Debug.Log($"fallbackOriginLon = {fallbackOriginLon}");
        Debug.Log($"Building lat = {lat}");
        Debug.Log($"Building lon = {lon}");
        return new Vector3(x, y, z);

    }

    void PlaceDroneOnArcGIS(Result result)
    {
        if (droneTransform == null || result == null || result.waypoints == null || result.waypoints.Length == 0)
            return;

        var wp0 = result.waypoints[0];
        if (!TryConvertGPS(wp0.lat, wp0.lon, wp0.alt, out Vector3 pos))
            return;

        pos.y += 50f;
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

    void GenerateSceneSafe(Result result)
    {
        try
        {
            GenerateScene(result);
        }
        catch (System.Exception e)
        {
            Debug.LogError("❌ Scene generation failed: " + e.Message);
        }
    }

    void DrawPathSafe(Result result)
    {
        try
        {
            DrawPath(result);
        }
        catch (System.Exception e)
        {
            Debug.LogError("❌ Path generation failed: " + e.Message);
        }
    }

    void DrawRiversSafe(Result result)
    {
        try
        {
            DrawRivers(result);
        }
        catch (System.Exception e)
        {
            Debug.LogError("❌ River drawing failed: " + e.Message);
        }
    }

    void CarveRiversIntoTerrainSafe(Result result)
    {
        try
        {
            CarveRiversIntoTerrain(result);
        }
        catch (System.Exception e)
        {
            Debug.LogError("❌ River carving failed: " + e.Message);
        }
    }

    Transform droneTransform;

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

    Transform GetGeneratedObjectsRoot()
    {
        if (generatedObjectsRoot != null)
        {
            return generatedObjectsRoot;
        }

        GameObject root = GameObject.Find("GeneratedArcGISObjects");
        if (root == null)
        {
            root = new GameObject("GeneratedArcGISObjects");
        }

        generatedObjectsRoot = root.transform;
        return generatedObjectsRoot;
    }

}
