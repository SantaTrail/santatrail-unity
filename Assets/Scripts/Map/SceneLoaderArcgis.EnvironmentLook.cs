using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Esri.ArcGISMapsSDK.Components;
using Esri.ArcGISMapsSDK.Utils.GeoCoord;
using Esri.GameEngine.Geometry;

public partial class SceneLoaderArcgis
{
    [Header("Custom Map Texture")]
    [SerializeField] bool useCustomMapTexture = true;
    [SerializeField] Texture2D customMapTexture;
    [SerializeField] string arcGISFallbackBasemapURL =
        "https://basemapstyles-api.arcgis.com/arcgis/rest/services/styles/v2/webmaps/arcgis/imagery/standard";
    [SerializeField] int customMapMeshResolution = 64;
    [SerializeField] float customMapSurfaceOffsetMeters = 0.18f;
    [SerializeField] bool flattenCustomMapGround;

    [Header("Night Lighting")]
    [SerializeField] bool useNightLevelLook = true;
    [SerializeField] Texture2D nightSkyTexture;
    [SerializeField] Color nightAmbientColor = new Color(0.20f, 0.23f, 0.34f, 1f);
    [SerializeField, Range(0.1f, 1f)] float nightAmbientIntensity = 0.82f;

    static Material customGroundMaterial;
    static Material nightSkyMaterial;

    void ApplyNightLevelLook()
    {
        if (!enabled || !useNightLevelLook)
        {
            return;
        }

        GameObject mainLightObject = GameObject.Find("Directional Light");
        Light mainLight = mainLightObject != null
            ? mainLightObject.GetComponent<Light>()
            : null;
        if (mainLight != null)
        {
            mainLight.enabled = false;
        }

        if (nightSkyTexture != null)
        {
            Shader skyShader = Shader.Find("Skybox/Panoramic");
            if (skyShader != null)
            {
                if (nightSkyMaterial == null)
                {
                    nightSkyMaterial = new Material(skyShader)
                    {
                        name = "SantaTrail Temporary Night Sky"
                    };
                }

                nightSkyMaterial.SetTexture("_MainTex", nightSkyTexture);
                if (nightSkyMaterial.HasProperty("_Tint"))
                {
                    nightSkyMaterial.SetColor("_Tint", Color.white);
                }
                if (nightSkyMaterial.HasProperty("_Exposure"))
                {
                    nightSkyMaterial.SetFloat("_Exposure", 1f);
                }

                RenderSettings.skybox = nightSkyMaterial;
            }
            else
            {
                Debug.LogWarning("Night sky texture was assigned, but Skybox/Panoramic shader is unavailable.");
            }
        }

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = nightAmbientColor;
        RenderSettings.ambientIntensity = nightAmbientIntensity;
        RenderSettings.reflectionIntensity = 0.3f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.025f, 0.04f, 0.09f, 1f);
        RenderSettings.fogDensity = 0.0012f;
        DynamicGI.UpdateEnvironment();

        Debug.Log("🌙 Applied SantaTrail night lighting and sky.");
    }

    void SpawnCustomMapGround(Result result)
    {
        if (arcGISMap != null)
        {
            // Keep ArcGIS elevation and georeferencing, while replacing its
            // imagery/style layer with the local editable Unity texture.
            arcGISMap.Basemap = useCustomMapTexture && customMapTexture != null
                ? string.Empty
                : arcGISFallbackBasemapURL;
        }

        if (!useCustomMapTexture || customMapTexture == null)
        {
            return;
        }

        int resolution = Mathf.Clamp(customMapMeshResolution, 16, 128);
        float widthMeters = Mathf.Max(50f, (float)mapExtentSizeMeters * 2f);
        float centerElevation = SampleCustomGroundElevation(result, 0f, 0f);
        List<Vector3> vertices = new List<Vector3>((resolution + 1) * (resolution + 1));
        List<Vector2> uvs = new List<Vector2>((resolution + 1) * (resolution + 1));
        List<int> triangles = new List<int>(resolution * resolution * 6);

        for (int z = 0; z <= resolution; z++)
        {
            float v = z / (float)resolution;
            float northMeters = (v - 0.5f) * widthMeters;

            for (int x = 0; x <= resolution; x++)
            {
                float u = x / (float)resolution;
                float eastMeters = (u - 0.5f) * widthMeters;
                float elevationOffset = flattenCustomMapGround
                    ? 0f
                    : SampleCustomGroundElevation(result, eastMeters, northMeters) - centerElevation;

                vertices.Add(new Vector3(
                    eastMeters,
                    elevationOffset + customMapSurfaceOffsetMeters,
                    northMeters
                ));
                uvs.Add(new Vector2(u, v));

                if (x == resolution || z == resolution)
                {
                    continue;
                }

                int southwest = z * (resolution + 1) + x;
                int southeast = southwest + 1;
                int northwest = southwest + resolution + 1;
                int northeast = northwest + 1;

                triangles.Add(southwest);
                triangles.Add(northwest);
                triangles.Add(southeast);
                triangles.Add(southeast);
                triangles.Add(northwest);
                triangles.Add(northeast);
            }
        }

        Mesh mesh = new Mesh
        {
            name = "Temporary Custom Map Ground"
        };
        mesh.indexFormat = vertices.Count > 65535
            ? IndexFormat.UInt32
            : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Transform environmentRoot = GetGeneratedEnvironmentRoot();
        GameObject groundAnchor = new GameObject("CustomMapGround");
        if (environmentRoot != null)
        {
            groundAnchor.transform.SetParent(environmentRoot, false);
        }

        AddConfiguredArcGISLocation(
            groundAnchor,
            mapOriginLongitude,
            mapOriginLatitude,
            0,
            ArcGISSurfacePlacementMode.OnTheGround,
            0,
            new ArcGISRotation(0, 0, 0)
        );

        GameObject groundVisual = new GameObject("CustomMapTextureMesh");
        groundVisual.transform.SetParent(groundAnchor.transform, false);
        groundVisual.AddComponent<ArcGISUprightVisualRoot>().Configure(0f);
        groundVisual.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = groundVisual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GetCustomGroundMaterial();
        if (renderer.sharedMaterial != null)
        {
            renderer.sharedMaterial.mainTexture = customMapTexture;
        }
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;

        Debug.Log(
            $"🗺 Replaced ArcGIS imagery with editable local texture " +
            $"({widthMeters:F0}m × {widthMeters:F0}m)."
        );
    }

    float SampleCustomGroundElevation(Result result, float eastMeters, float northMeters)
    {
        if (result == null || result.elevation_data == null ||
            result.grid_size < 2 ||
            result.elevation_data.Length < result.grid_size * result.grid_size)
        {
            return 0f;
        }

        float halfSource = Mathf.Max(1f, scale * 0.5f);
        float sourceU = Mathf.Clamp01(0.5f + eastMeters / (halfSource * 2f));
        float sourceV = Mathf.Clamp01(0.5f + northMeters / (halfSource * 2f));
        float gridX = sourceU * (result.grid_size - 1);
        float gridY = sourceV * (result.grid_size - 1);
        int x0 = Mathf.FloorToInt(gridX);
        int y0 = Mathf.FloorToInt(gridY);
        int x1 = Mathf.Min(x0 + 1, result.grid_size - 1);
        int y1 = Mathf.Min(y0 + 1, result.grid_size - 1);
        float tx = gridX - x0;
        float ty = gridY - y0;

        float southwest = result.elevation_data[y0 * result.grid_size + x0];
        float southeast = result.elevation_data[y0 * result.grid_size + x1];
        float northwest = result.elevation_data[y1 * result.grid_size + x0];
        float northeast = result.elevation_data[y1 * result.grid_size + x1];
        float south = Mathf.Lerp(southwest, southeast, tx);
        float north = Mathf.Lerp(northwest, northeast, tx);
        return Mathf.Lerp(south, north, ty);
    }

    static Material GetCustomGroundMaterial()
    {
        if (customGroundMaterial != null)
        {
            return customGroundMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            return null;
        }

        customGroundMaterial = new Material(shader)
        {
            name = "Temporary Editable Map Ground"
        };
        return customGroundMaterial;
    }
}
