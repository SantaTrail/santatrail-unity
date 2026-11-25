using System.Collections.Generic;
using UnityEngine;
using Esri.ArcGISMapsSDK.Components;
using Esri.ArcGISMapsSDK.Utils.GeoCoord;
using Esri.GameEngine.Geometry;

public partial class SceneLoaderArcgis
{
    [Header("OSM Road Surfaces")]
    [SerializeField] bool spawnOsmRoadSurfaces = true;
    [SerializeField] float roadSurfaceShoulderMeters = 0.65f;
    [SerializeField] float roadSurfaceGroundOffsetMeters = 0.12f;

    static Material generatedRoadSurfaceMaterial;
    static Material generatedRoadShoulderMaterial;

    void SpawnOsmRoadSurfaces(Result result)
    {
        if (!spawnOsmRoadSurfaces || result == null || result.roads == null ||
            result.roads.Length == 0)
        {
            return;
        }

        Transform environmentRoot = GetGeneratedEnvironmentRoot();
        int roadCount = 0;

        for (int roadIndex = 0; roadIndex < result.roads.Length; roadIndex++)
        {
            Road road = result.roads[roadIndex];
            if (road == null || road.points == null || road.points.Length < 2)
            {
                continue;
            }

            float roadWidth = GetRoadSurfaceWidth(road.highway);
            if (roadWidth <= 0f)
            {
                continue;
            }

            GPSPoint anchor = null;
            Vector3 anchorUnity = Vector3.zero;
            List<Vector3> localPoints = new List<Vector3>();

            for (int pointIndex = 0; pointIndex < road.points.Length; pointIndex++)
            {
                GPSPoint point = road.points[pointIndex];
                if (!IsValidRoadPoint(point))
                {
                    continue;
                }

                if (limitMapExtent &&
                    !IsInsideConfiguredMapExtent(point.lat, point.lon, out _))
                {
                    continue;
                }

                if (!TryConvertGPS(point.lat, point.lon, 0, out Vector3 converted))
                {
                    continue;
                }

                if (anchor == null)
                {
                    anchor = point;
                    anchorUnity = converted;
                    localPoints.Add(Vector3.zero);
                    continue;
                }

                Vector3 local = converted - anchorUnity;
                local.y = 0f;
                if (localPoints.Count == 0 ||
                    (local - localPoints[localPoints.Count - 1]).sqrMagnitude > 0.25f)
                {
                    localPoints.Add(local);
                }
            }

            if (anchor == null || localPoints.Count < 2)
            {
                continue;
            }

            GameObject roadObject = new GameObject($"OSMRoadSurface_{roadIndex}");
            if (environmentRoot != null)
            {
                roadObject.transform.SetParent(environmentRoot, false);
            }

            AddConfiguredArcGISLocation(
                roadObject,
                anchor.lon,
                anchor.lat,
                0,
                ArcGISSurfacePlacementMode.OnTheGround,
                roadSurfaceGroundOffsetMeters,
                new ArcGISRotation(0, 0, 0)
            );

            GameObject visual = new GameObject("RoadMesh");
            visual.transform.SetParent(roadObject.transform, false);
            visual.AddComponent<ArcGISUprightVisualRoot>().Configure(0f);

            Mesh mesh = BuildRoadSurfaceMesh(
                localPoints,
                roadWidth,
                roadSurfaceShoulderMeters
            );
            if (mesh == null)
            {
                Destroy(roadObject);
                continue;
            }

            MeshFilter filter = visual.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[]
            {
                GetRoadShoulderMaterial(),
                GetRoadSurfaceMaterial()
            };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            roadCount++;
        }

        Debug.Log($"🛣 Spawned {roadCount} OSM road surface sections.");
    }

    float GetRoadSurfaceWidth(string highway)
    {
        switch ((highway ?? "").Trim().ToLowerInvariant())
        {
            case "motorway":
            case "trunk":
                return 8f;
            case "primary":
                return 6.5f;
            case "secondary":
                return 5.5f;
            case "tertiary":
                return 4.8f;
            case "residential":
            case "unclassified":
                return 4.1f;
            case "living_street":
                return 3.6f;
            case "service":
                return 2.8f;
            default:
                return 0f;
        }
    }

    Mesh BuildRoadSurfaceMesh(
        List<Vector3> points,
        float roadWidth,
        float shoulderWidth)
    {
        if (points == null || points.Count < 2)
        {
            return null;
        }

        List<Vector3> vertices = new List<Vector3>();
        List<int> shoulderTriangles = new List<int>();
        List<int> roadTriangles = new List<int>();
        float halfRoad = roadWidth * 0.5f;
        float halfShoulder = halfRoad + Mathf.Max(0f, shoulderWidth);
        float shoulderY = 0f;
        float roadY = 0.035f;

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 start = points[i];
            Vector3 end = points[i + 1];
            Vector3 direction = end - start;
            direction.y = 0f;
            float length = direction.magnitude;
            if (length < 0.5f)
            {
                continue;
            }

            direction /= length;
            Vector3 side = new Vector3(direction.z, 0f, -direction.x);

            // Short overlaps at segment ends prevent visible gaps at OSM bends.
            Vector3 extension = direction * Mathf.Min(0.35f, length * 0.08f);
            AddRoadQuad(
                vertices,
                shoulderTriangles,
                start - extension,
                end + extension,
                side,
                halfShoulder,
                shoulderY
            );
            AddRoadQuad(
                vertices,
                roadTriangles,
                start - extension,
                end + extension,
                side,
                halfRoad,
                roadY
            );
        }

        if (vertices.Count == 0)
        {
            return null;
        }

        Mesh mesh = new Mesh();
        mesh.name = "OSMRoadSurfaceMesh";
        if (vertices.Count > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        mesh.SetVertices(vertices);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(shoulderTriangles, 0);
        mesh.SetTriangles(roadTriangles, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AddRoadQuad(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 start,
        Vector3 end,
        Vector3 side,
        float halfWidth,
        float height)
    {
        int first = vertices.Count;
        vertices.Add(new Vector3(start.x - side.x * halfWidth, height, start.z - side.z * halfWidth));
        vertices.Add(new Vector3(start.x + side.x * halfWidth, height, start.z + side.z * halfWidth));
        vertices.Add(new Vector3(end.x + side.x * halfWidth, height, end.z + side.z * halfWidth));
        vertices.Add(new Vector3(end.x - side.x * halfWidth, height, end.z - side.z * halfWidth));

        triangles.Add(first);
        triangles.Add(first + 2);
        triangles.Add(first + 1);
        triangles.Add(first);
        triangles.Add(first + 3);
        triangles.Add(first + 2);
    }

    static Material GetRoadSurfaceMaterial()
    {
        if (generatedRoadSurfaceMaterial == null)
        {
            generatedRoadSurfaceMaterial = CreateRoadMaterial(
                "SantaTrail Road Asphalt",
                new Color32(58, 63, 67, 255)
            );
        }

        return generatedRoadSurfaceMaterial;
    }

    static Material GetRoadShoulderMaterial()
    {
        if (generatedRoadShoulderMaterial == null)
        {
            generatedRoadShoulderMaterial = CreateRoadMaterial(
                "SantaTrail Road Shoulder",
                new Color32(147, 145, 136, 255)
            );
        }

        return generatedRoadShoulderMaterial;
    }

    static Material CreateRoadMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            return null;
        }

        Material material = new Material(shader);
        material.name = materialName;
        material.color = color;
        return material;
    }
}
