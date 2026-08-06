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
    void GenerateSceneSafe(Result result)
    {
        try
        {
            GenerateScene(result);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
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
}
