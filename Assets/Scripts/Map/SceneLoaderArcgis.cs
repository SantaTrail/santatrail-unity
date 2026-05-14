using UnityEngine;
using System.IO;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Collections;


public class SceneLoaderArcgis : MonoBehaviour
{
    [Header("Camera")]
public Camera droneCamera;
    public float scale = 5000f;
    [Header("ArcGIS")]
    [SerializeField] ArcGISConverter arcGISConverter;
    [Header("Fallback")]
    [SerializeField] bool allowLocalGpsFallback = true;
    [Header("Generation Mode")]
    [SerializeField] bool useArcGISTerrainOnly = true;
    
    float waterHeight = -1f;

    [Header("Python Script")]
    public string pythonPath = "/Users/notebook/.pyenv/versions/3.10.18/bin/python3";

    string scriptPath;
    string outputPath;
    [Header("Prefabs")]
    public GameObject treePrefab;
    public GameObject buildingPrefab;

    Terrain terrain;
    bool fallbackOriginSet;
    double fallbackOriginLat;
    double fallbackOriginLon;
void Awake()
{
    TryResolveArcGISConverter();

    if (arcGISConverter == null)
        Debug.LogWarning("⚠️ ArcGISConverter not found in Awake (will keep retrying)");
}

void Start()
{
    string projectRoot = Application.dataPath + "/../";
    string backendPath = Path.Combine(projectRoot, "uav-terrain-ai/backend");

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
    catch {}
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
            SpawnRealBuildings(result);
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

    if (veg <= 0) veg = Random.Range(5f, 20f);
    if (build <= 0) build = Random.Range(20f, 60f);

    int treeCount = Mathf.Clamp((int)(veg * 10), 20, 300);
    SpawnTrees(treeCount);

if (result.buildings != null && result.buildings.Length > 0)
{
    SpawnRealBuildings(result);
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
            Random.Range(-scale / 2, scale / 2),
            0,
            Random.Range(-scale / 2, scale / 2)
        );

        pos.y = terrain.SampleHeight(pos);

        if (waterHeight > 0 && pos.y <= waterHeight + 2f)
            continue;

        float slope = terrain.terrainData.GetSteepness(
            (pos.x + scale / 2) / scale,
            (pos.z + scale / 2) / scale
        );

        if (slope > 35f) continue;

        Quaternion rot = Quaternion.Euler(0, Random.Range(0, 360), 0);
        float s = Random.Range(0.8f, 1.6f);

        GameObject tree = Instantiate(treePrefab, pos, rot);
        tree.transform.localScale *= s;

        spawned++;
    }

    Debug.Log($"🌳 Spawned {spawned} trees (safe)");
}
void SpawnRealBuildings(Result result)
{
    if (result.buildings == null || terrain == null || arcGISConverter == null || buildingPrefab == null) return;

    foreach (var building in result.buildings)
    {
        if (building.points == null || building.points.Length < 3)
            continue;

        float avgLat = 0;
        float avgLon = 0;

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

        if (terrain != null)
            center.y = terrain.SampleHeight(center);

        GameObject b = Instantiate(buildingPrefab, center, Quaternion.identity);
        if (b.GetComponent<House>() == null)
        {
            b.AddComponent<House>();
        }

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        int validPoints = 0;

        foreach (var p in building.points)
        {
            if (!TryConvertGPS(p.lat, p.lon, 0, out Vector3 pt))
                continue;

            if (pt.x < minX) minX = pt.x;
            if (pt.x > maxX) maxX = pt.x;
            if (pt.z < minZ) minZ = pt.z;
            if (pt.z > maxZ) maxZ = pt.z;
            validPoints++;
        }

        if (validPoints < 3)
        {
            Destroy(b);
            Debug.LogWarning("⚠️ Skipping building (not enough valid corners)");
            continue;
        }

        float width = Mathf.Clamp(maxX - minX, 5f, 80f);
        float depth = Mathf.Clamp(maxZ - minZ, 5f, 80f);
        float height = Random.Range(10f, 40f);

        b.transform.localScale = new Vector3(width, height, depth);
        if (b.GetComponent<Collider>() == null)
        {
            b.AddComponent<BoxCollider>();
        }
    }

    Debug.Log("🏙 Real buildings spawned: " + result.buildings.Length);
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
    string customLocation = "51.5074,-0.1278,35,0";

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

    pos.y += 50f;
    droneTransform.position = pos;

    Rigidbody rb = droneTransform.GetComponent<Rigidbody>();
    if (rb != null)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

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
        droneTransform = droneObj.transform;
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

}
