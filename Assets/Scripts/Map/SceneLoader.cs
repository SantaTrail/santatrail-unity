using UnityEngine;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using Debug = UnityEngine.Debug;
using System.Collections;
using UnityEngine.SceneManagement;
using Esri.ArcGISMapsSDK.Components;


public class SceneLoader : MonoBehaviour
{
    [Header("Camera")]
public Camera droneCamera;
    public float scale = 5000f;
    [Header("Level Origin")]
    [Tooltip("When enabled, this scene sets the shared home location before terrain generation.")]
    [SerializeField] bool overrideHomeLocation = false;
    [SerializeField] double homeLatitude;
    [SerializeField] double homeLongitude;
    [SerializeField] double homeAltitude;
    [SerializeField] double homeYaw;
    GPSConverter gps;
    
    float waterHeight = -1f;

    [Header("Python Script")]
    public string pythonPath = "/Users/notebook/.pyenv/versions/3.10.18/bin/python3";

    string scriptPath;
    string outputPath;
    [Header("Prefabs")]
    public GameObject treePrefab;
    public GameObject buildingPrefab;
    public GameObject[] randomHousePrefabs;
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
Process pythonProcess;
bool pythonDone;
int pythonExitCode = -1;
void Awake()
{
    gps = Object.FindFirstObjectByType<GPSConverter>();
    ApplyConfiguredHomeLocation();

    if (gps == null)
        Debug.LogError("❌ GPSConverter not found in scene");
}

void ApplyConfiguredHomeLocation()
{
    if (!overrideHomeLocation)
    {
        return;
    }

    GameManager.SetHomeLocation(homeLatitude, homeLongitude, homeAltitude, homeYaw);
    Debug.Log($"🧭 SceneLoader applied home location: {homeLatitude}, {homeLongitude}, {homeAltitude}, {homeYaw}");
}

public void LoadLevel1()
{
    GameManager.SetHomeLocation(52.15603852403063, 4.963989431212162, 0, 0);
    SceneManager.LoadScene("LV1");
}

void Start()
{
    string projectRoot = Application.dataPath + "/../";
    string backendPath = ResolveBackendPath(projectRoot);

    scriptPath = Path.Combine(backendPath, "main.py");
    outputPath = Path.Combine(backendPath, "output.json");
    if (!File.Exists(scriptPath))
    {
        Debug.LogError("❌ Python script NOT FOUND at: " + scriptPath);
        return;
    }
    
    Random.InitState(System.DateTime.Now.Millisecond);
    SetupDroneReference();

    if (droneCamera != null)
    {
        droneCamera.farClipPlane = 20000f;
        Debug.Log("✅ Using drone camera: " + droneCamera.name);
    }
    else
    {
        Debug.LogError("❌ Drone camera not assigned!");
    }
    Debug.Log("Running Python at: " + scriptPath);
    Debug.Log("Expecting JSON at: " + outputPath);

    if (File.Exists(outputPath))
    {
        File.Delete(outputPath);
        Debug.Log("🧹 Old JSON deleted");
    }

    RunPython();
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
    float timeout = 120f;
    float timer = 0f;
    bool jsonReady = false;

    Debug.Log("⏳ Waiting for JSON (validated)...");

    while (timer < timeout)
    {
        if (!jsonReady && File.Exists(outputPath))
        {
            try
            {
                string content = File.ReadAllText(outputPath);
                if (IsValidJson(content))
                {
                    jsonReady = true;
                    Debug.Log("✅ JSON READY (validated)");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("⚠️ JSON read error: " + e.Message);
            }
        }

        if (jsonReady)
        {
            LoadAndGenerate();
            yield break;
        }

        if (pythonDone && pythonExitCode != 0)
        {
            Debug.LogError("❌ Python failed before JSON was produced. Aborting.");
            yield break;
        }

        timer += 0.5f;
        yield return new WaitForSeconds(0.5f);
    }

    Debug.LogError("❌ Initialization timeout: JSON not ready within 120s");
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
    void LoadAndGenerate()
{
    string json = File.ReadAllText(outputPath);
    Result result = JsonUtility.FromJson<Result>(json);
    if (result == null)
    {
        Debug.LogError("❌ Failed to parse JSON");
        return;
    }
    if (result.features == null)
    {
        Debug.LogError("❌ JSON missing features");
        return;
    }

    Debug.Log("🏢 JSON contains buildings: " + json.Contains("buildings"));

    Debug.Log("🏢 Parsed buildings: " + 
        (result.buildings == null ? "NULL" : result.buildings.Length.ToString()));

    if (result.features.building_density == 0 &&
        result.features.vegetation_density == 0)
    {
        result.scene = "urban";
        Debug.Log("⚠️ OSM failed → forcing URBAN scene");
    }

    Debug.Log("🌍 Scene type: " + result.scene);

    GenerateTerrain(result);
    CarveRiversIntoTerrain(result);

    bool hasWater = result.features.water_present;

    if (!hasWater && Random.value > 0.9f)
        hasWater = true;

    if (hasWater)
    {
        GenerateWater();
        Debug.Log("🌊 Water generated");
    }

    GenerateScene(result);
    DrawPath(result);
    DrawRivers(result);
}

void GenerateTerrain(Result result)
{
    int size = result.grid_size;

    TerrainData terrainData = new TerrainData();
    terrainData.heightmapResolution = size + 1;
    float heightScale = result.features.elevation_range * 2f;


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

    DroneAltitudeMapper mapper = Object.FindFirstObjectByType<DroneAltitudeMapper>();
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
    float veg = result.features.vegetation_density;
    float build = result.features.building_density;

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
    if (result.buildings == null || terrain == null) return;

    for (int i = 0; i < result.buildings.Length; i++)
    {
        var building = result.buildings[i];
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

        Vector3 center = gps.ConvertGPSToUnity(avgLat, avgLon, 0);
        center.y = terrain.SampleHeight(center);

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var p in building.points)
        {
            Vector3 pt = gps.ConvertGPSToUnity(p.lat, p.lon, 0);

            if (pt.x < minX) minX = pt.x;
            if (pt.x > maxX) maxX = pt.x;
            if (pt.z < minZ) minZ = pt.z;
            if (pt.z > maxZ) maxZ = pt.z;
        }

        float width = Mathf.Clamp(maxX - minX, 5f, 80f);
        float depth = Mathf.Clamp(maxZ - minZ, 5f, 80f);
        float height = Random.Range(10f, 40f);

        GameObject prefabToUse = GetBuildingPrefabForIndex(i, width, depth);
        GameObject b = Instantiate(prefabToUse, center, Quaternion.identity);
        if (b.GetComponent<House>() == null)
        {
            b.AddComponent<House>();
        }

        b.transform.localScale = ComputePrefabScale(prefabToUse, width, depth, height);
        if (buildingReplacements != null)
        {
            foreach (var rule in buildingReplacements)
            {
                if (rule != null && rule.prefab == prefabToUse)
                {
                    b.transform.localScale *= Mathf.Max(0.01f, rule.scaleMultiplier);
                    b.transform.rotation = Quaternion.Euler(rule.rotationEulerOffset);
                    break;
                }
            }
        }
        if (b.GetComponent<Collider>() == null)
        {
            b.AddComponent<BoxCollider>();
        }
    }

    Debug.Log("🏙 Real buildings spawned: " + result.buildings.Length);
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

    if (randomHousePrefabs != null && randomHousePrefabs.Length > 0)
    {
        GameObject randomPrefab = PickHousePrefabByFootprint(width, depth);
        if (randomPrefab != null)
        {
            return randomPrefab;
        }
    }

    return buildingPrefab;
}

GameObject PickHousePrefabByFootprint(float width, float depth)
{
    if (randomHousePrefabs == null || randomHousePrefabs.Length == 0)
        return null;

    float footprint = Mathf.Max(width, depth);
    if (footprint < 12f)
    {
        return randomHousePrefabs[Random.Range(0, randomHousePrefabs.Length)];
    }

    if (footprint < 25f)
    {
        return randomHousePrefabs[Random.Range(0, randomHousePrefabs.Length)];
    }

    return randomHousePrefabs[Random.Range(0, randomHousePrefabs.Length)];
}

Vector3 ComputePrefabScale(GameObject prefab, float width, float depth, float height)
{
    if (prefab == buildingPrefab)
    {
        return new Vector3(width, height, depth);
    }

    float footprint = Mathf.Max(width, depth);
    float houseScale = Mathf.Clamp(footprint / 10f, 0.5f, 6f);
    return new Vector3(houseScale, Mathf.Clamp(height / 12f, 0.5f, 4f), houseScale);
}

    // =========================
    // PATH
    // =========================
    void DrawPath(Result result)
    {
        if (result.waypoints == null || result.waypoints.Length == 0) return;

        LineRenderer line = new GameObject("Path").AddComponent<LineRenderer>();

        line.positionCount = result.waypoints.Length;
        line.widthMultiplier = 8f;
        line.material = new Material(Shader.Find("Sprites/Default"));

        float originLat = result.waypoints[0].lat;
        float originLon = result.waypoints[0].lon;

        for (int i = 0; i < result.waypoints.Length; i++)
        {
            var wp = result.waypoints[i];

            Vector3 pos = gps.ConvertGPSToUnity(wp.lat, wp.lon, wp.alt);

            pos.y = terrain.SampleHeight(pos) + 2f;

            line.SetPosition(i, pos);
        }
    }

    // =========================
    // RIVERS
    // =========================
    void DrawRivers(Result result)
    {
        if (result.rivers == null || terrain == null) return;

        float originLat = result.waypoints[0].lat;
        float originLon = result.waypoints[0].lon;

        foreach (var river in result.rivers)
        {
            LineRenderer line = new GameObject("River").AddComponent<LineRenderer>();

            line.positionCount = river.Length;
            line.widthMultiplier = 8f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = Color.blue;
            line.endColor = Color.blue;

            for (int i = 0; i < river.Length; i++)
            {
                var p = river[i];

                Vector3 pos = gps.ConvertGPSToUnity(p.lat, p.lon, p.alt);

                pos.y = terrain.SampleHeight(pos) + 0.05f;

                line.SetPosition(i, pos);
            }
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

    pythonDone = false;
    pythonExitCode = -1;
    pythonProcess = new Process();
    pythonProcess.StartInfo = psi;
    pythonProcess.EnableRaisingEvents = true;

    pythonProcess.OutputDataReceived += (sender, args) =>
    {
        if (!string.IsNullOrEmpty(args.Data))
            Debug.Log("[PYTHON] " + args.Data);
    };

    pythonProcess.ErrorDataReceived += (sender, args) =>
    {
        if (!string.IsNullOrEmpty(args.Data))
            Debug.LogError("🐍 ERROR: " + args.Data);
    };

    pythonProcess.Exited += (sender, args) =>
    {
        pythonDone = true;
        pythonExitCode = pythonProcess.ExitCode;
        Debug.Log($"🐍 Python exited with code {pythonExitCode}");
    };

    pythonProcess.Start();
    pythonProcess.BeginOutputReadLine();
    pythonProcess.BeginErrorReadLine();
}
    void CarveRiversIntoTerrain(Result result)
{
    if (terrain == null || result.rivers == null) return;

    TerrainData data = terrain.terrainData;
    int res = data.heightmapResolution;

    float[,] heights = data.GetHeights(0, 0, res, res);

    float originLat = result.waypoints[0].lat;
    float originLon = result.waypoints[0].lon;

    float riverWidth = 6f;
    float depth = 0.02f;

    foreach (var river in result.rivers)
    {
        foreach (var point in river)
        {
Vector3 pos = gps.ConvertGPSToUnity(point.lat, point.lon, 0);
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

}
