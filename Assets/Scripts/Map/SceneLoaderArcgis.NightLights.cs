using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class SceneLoaderArcgis
{
    [Header("Night Window and Drone Lighting")]
    [SerializeField, Range(0f, 1f)] float nightWindowLitChance = 1f;
    [SerializeField] float nightWindowGlowStrength = 3.5f;
    [SerializeField, Range(0f, 1f)] float nightWindowLightChance = 1f;
    [SerializeField] float nightWindowLightIntensity = 0.8f;
    [SerializeField] float nightWindowLightRange = 8f;
    [SerializeField] float droneNavigationLightSize = 0.16f;
    [SerializeField] float droneHeadlightIntensity = 4f;

    static Material nightWindowMaterial;
    static Material droneMarkerMaterialRed;
    static Material droneMarkerMaterialGreen;
    static Material droneMarkerMaterialWhite;

    void AddNightWindowGlows(
        Transform buildingRoot,
        GameObject buildingModel,
        int seed)
    {
        if (!useNightLevelLook || buildingRoot == null || buildingModel == null ||
            buildingRoot.Find("NightWindowGlows") != null)
        {
            return;
        }

        Renderer[] renderers = buildingModel.GetComponentsInChildren<Renderer>(true);
        Bounds localBounds = default;
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            Bounds rendererBounds = renderer.localBounds;
            Vector3 min = rendererBounds.min;
            Vector3 max = rendererBounds.max;
            Matrix4x4 toBuildingRoot =
                buildingRoot.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 rendererPoint = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z
                );
                Vector3 localPoint = toBuildingRoot.MultiplyPoint3x4(rendererPoint);
                if (!hasBounds)
                {
                    localBounds = new Bounds(localPoint, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(localPoint);
                }
            }
        }

        if (!hasBounds || localBounds.size.y < 2.4f ||
            localBounds.size.x < 1.8f || localBounds.size.z < 1.8f)
        {
            return;
        }

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        System.Random random = new System.Random(seed ^ 0x4E1D);
        int rows = Mathf.Clamp(Mathf.FloorToInt(localBounds.size.y / 2.8f), 1, 6);
        int columnsX = Mathf.Clamp(Mathf.FloorToInt(localBounds.size.x / 2.5f), 1, 8);
        int columnsZ = Mathf.Clamp(Mathf.FloorToInt(localBounds.size.z / 2.5f), 1, 8);
        float floorStep = localBounds.size.y / (rows + 1f);
        float windowHeight = Mathf.Clamp(floorStep * 0.42f, 0.48f, 0.92f);
        float windowWidthX = Mathf.Min(0.82f, localBounds.size.x / columnsX * 0.42f);
        float windowWidthZ = Mathf.Min(0.82f, localBounds.size.z / columnsZ * 0.42f);

        for (int row = 0; row < rows; row++)
        {
            float y = localBounds.min.y + floorStep * (row + 1);
            AddWindowFacade(
                vertices,
                triangles,
                random,
                localBounds,
                row,
                columnsX,
                y,
                windowWidthX,
                windowHeight,
                true
            );
            AddWindowFacade(
                vertices,
                triangles,
                random,
                localBounds,
                row,
                columnsX,
                y,
                windowWidthX,
                windowHeight,
                false
            );
            AddSideWindowFacade(
                vertices,
                triangles,
                random,
                localBounds,
                row,
                columnsZ,
                y,
                windowWidthZ,
                windowHeight,
                true
            );
            AddSideWindowFacade(
                vertices,
                triangles,
                random,
                localBounds,
                row,
                columnsZ,
                y,
                windowWidthZ,
                windowHeight,
                false
            );
        }

        if (vertices.Count == 0)
        {
            return;
        }

        GameObject glowObject = new GameObject("NightWindowGlows");
        glowObject.transform.SetParent(buildingRoot, false);
        Mesh mesh = new Mesh { name = "Warm Building Windows" };
        if (vertices.Count > 65535)
        {
            mesh.indexFormat = IndexFormat.UInt32;
        }
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        glowObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer glowRenderer = glowObject.AddComponent<MeshRenderer>();
        glowRenderer.sharedMaterial = GetNightWindowMaterial();
        glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        glowRenderer.receiveShadows = false;

        AddNightWindowLight(buildingRoot, localBounds, seed);
    }

    void AddWindowFacade(
        List<Vector3> vertices,
        List<int> triangles,
        System.Random random,
        Bounds bounds,
        int row,
        int columns,
        float y,
        float windowWidth,
        float windowHeight,
        bool positiveZ)
    {
        float spacing = bounds.size.x / (columns + 1f);
        float z = positiveZ ? bounds.max.z + 0.035f : bounds.min.z - 0.035f;
        float normalSign = positiveZ ? 1f : -1f;

        for (int column = 0; column < columns; column++)
        {
            if (random.NextDouble() > nightWindowLitChance)
            {
                continue;
            }

            float x = bounds.min.x + spacing * (column + 1);
            AddWindowQuad(
                vertices,
                triangles,
                new Vector3(x, y, z),
                new Vector3(windowWidth, windowHeight, 0f),
                normalSign
            );
        }
    }

    void AddSideWindowFacade(
        List<Vector3> vertices,
        List<int> triangles,
        System.Random random,
        Bounds bounds,
        int row,
        int columns,
        float y,
        float windowWidth,
        float windowHeight,
        bool positiveX)
    {
        float spacing = bounds.size.z / (columns + 1f);
        float x = positiveX ? bounds.max.x + 0.035f : bounds.min.x - 0.035f;
        float normalSign = positiveX ? 1f : -1f;

        for (int column = 0; column < columns; column++)
        {
            if (random.NextDouble() > nightWindowLitChance)
            {
                continue;
            }

            float z = bounds.min.z + spacing * (column + 1);
            AddSideWindowQuad(
                vertices,
                triangles,
                new Vector3(x, y, z),
                new Vector3(0f, windowHeight, windowWidth),
                normalSign
            );
        }
    }

    static void AddWindowQuad(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 center,
        Vector3 size,
        float normalSign)
    {
        int index = vertices.Count;
        float halfX = size.x * 0.5f;
        float halfY = size.y * 0.5f;
        vertices.Add(center + new Vector3(-halfX, -halfY, 0f));
        vertices.Add(center + new Vector3(-halfX, halfY, 0f));
        vertices.Add(center + new Vector3(halfX, halfY, 0f));
        vertices.Add(center + new Vector3(halfX, -halfY, 0f));
        AddQuadTriangles(triangles, index, normalSign);
    }

    static void AddSideWindowQuad(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 center,
        Vector3 size,
        float normalSign)
    {
        int index = vertices.Count;
        float halfZ = size.z * 0.5f;
        float halfY = size.y * 0.5f;
        vertices.Add(center + new Vector3(0f, -halfY, -halfZ));
        vertices.Add(center + new Vector3(0f, -halfY, halfZ));
        vertices.Add(center + new Vector3(0f, halfY, halfZ));
        vertices.Add(center + new Vector3(0f, halfY, -halfZ));
        AddQuadTriangles(triangles, index, normalSign);
    }

    static void AddQuadTriangles(List<int> triangles, int index, float normalSign)
    {
        if (normalSign >= 0f)
        {
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
        }
        else
        {
            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 1);
            triangles.Add(index);
            triangles.Add(index + 3);
            triangles.Add(index + 2);
        }
    }

    Material GetNightWindowMaterial()
    {
        if (nightWindowMaterial != null)
        {
            return nightWindowMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }
        if (shader == null)
        {
            return null;
        }

        Color glow = new Color(1f, 0.63f, 0.27f, 1f);
        nightWindowMaterial = new Material(shader)
        {
            name = "SantaTrail Warm Window Glow"
        };
        if (nightWindowMaterial.HasProperty("_BaseColor"))
        {
            nightWindowMaterial.SetColor(
                "_BaseColor",
                new Color(
                    glow.r * nightWindowGlowStrength,
                    glow.g * nightWindowGlowStrength,
                    glow.b * nightWindowGlowStrength,
                    1f
                )
            );
        }
        else if (nightWindowMaterial.HasProperty("_Color"))
        {
            nightWindowMaterial.SetColor(
                "_Color",
                new Color(
                    glow.r * nightWindowGlowStrength,
                    glow.g * nightWindowGlowStrength,
                    glow.b * nightWindowGlowStrength,
                    1f
                )
            );
        }
        if (nightWindowMaterial.HasProperty("_Cull"))
        {
            nightWindowMaterial.SetFloat("_Cull", 0f);
        }
        return nightWindowMaterial;
    }

    void AddNightWindowLight(Transform buildingRoot, Bounds bounds, int seed)
    {
        System.Random random = new System.Random(seed ^ 0x71A7);
        if (random.NextDouble() > nightWindowLightChance)
        {
            return;
        }

        int facade = random.Next(4);
        Vector3 position = bounds.center;
        switch (facade)
        {
            case 0:
                position.z = bounds.max.z + 0.12f;
                break;
            case 1:
                position.z = bounds.min.z - 0.12f;
                break;
            case 2:
                position.x = bounds.max.x + 0.12f;
                break;
            default:
                position.x = bounds.min.x - 0.12f;
                break;
        }

        GameObject windowLightObject = new GameObject("Warm Window Light");
        windowLightObject.transform.SetParent(buildingRoot, false);
        windowLightObject.transform.localPosition = position;
        Light windowLight = windowLightObject.AddComponent<Light>();
        windowLight.type = LightType.Point;
        windowLight.color = new Color(1f, 0.62f, 0.32f, 1f);
        windowLight.intensity = Mathf.Max(0f, nightWindowLightIntensity);
        windowLight.range = Mathf.Max(0f, nightWindowLightRange);
        windowLight.shadows = LightShadows.None;
        windowLight.renderMode = LightRenderMode.Auto;
    }

    void AddNightDroneVisibility(GameObject droneRoot)
    {
        if (!useNightLevelLook || droneRoot == null ||
            droneRoot.transform.Find("NightNavigationLights") != null)
        {
            return;
        }

        Renderer[] renderers = droneRoot.GetComponentsInChildren<Renderer>(true);
        Bounds localBounds = default;
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            Bounds rendererBounds = renderer.localBounds;
            Vector3 min = rendererBounds.min;
            Vector3 max = rendererBounds.max;
            Matrix4x4 toDroneRoot =
                droneRoot.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 rendererPoint = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z
                );
                Vector3 localPoint = toDroneRoot.MultiplyPoint3x4(rendererPoint);
                if (!hasBounds)
                {
                    localBounds = new Bounds(localPoint, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(localPoint);
                }
            }
        }

        if (!hasBounds)
        {
            return;
        }

        Transform lightsRoot = new GameObject("NightNavigationLights").transform;
        lightsRoot.SetParent(droneRoot.transform, false);
        float x = Mathf.Max(0.45f, localBounds.extents.x * 0.82f);
        float z = Mathf.Max(0.45f, localBounds.extents.z * 0.82f);
        float y = localBounds.center.y + Mathf.Max(0.08f, localBounds.extents.y * 0.12f);

        AddDroneMarker(lightsRoot, "PortRed", new Vector3(-x, y, -z),
            new Color(1f, 0.08f, 0.05f, 1f), ref droneMarkerMaterialRed);
        AddDroneMarker(lightsRoot, "StarboardGreen", new Vector3(x, y, -z),
            new Color(0.05f, 1f, 0.32f, 1f), ref droneMarkerMaterialGreen);
        AddDroneMarker(lightsRoot, "FrontWhiteLeft", new Vector3(-x, y, z),
            Color.white, ref droneMarkerMaterialWhite);
        AddDroneMarker(lightsRoot, "FrontWhiteRight", new Vector3(x, y, z),
            Color.white, ref droneMarkerMaterialWhite);

        GameObject headlightObject = new GameObject("DroneForwardLight");
        headlightObject.transform.SetParent(lightsRoot, false);
        headlightObject.transform.localPosition = new Vector3(0f, y, localBounds.max.z);
        Light headlight = headlightObject.AddComponent<Light>();
        headlight.type = LightType.Spot;
        headlight.color = new Color(0.72f, 0.84f, 1f, 1f);
        headlight.intensity = droneHeadlightIntensity;
        headlight.range = 24f;
        headlight.spotAngle = 70f;
        headlight.shadows = LightShadows.None;
        headlight.renderMode = LightRenderMode.Auto;
    }

    void AddDroneMarker(
        Transform parent,
        string markerName,
        Vector3 localPosition,
        Color color,
        ref Material sharedMaterial)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = markerName;
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = localPosition;
        marker.transform.localScale = Vector3.one * droneNavigationLightSize;
        Collider collider = marker.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        if (sharedMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                return;
            }

            sharedMaterial = new Material(shader)
            {
                name = $"SantaTrail {markerName} Material"
            };
            if (sharedMaterial.HasProperty("_BaseColor"))
            {
                sharedMaterial.SetColor("_BaseColor", color * 2f);
            }
            else if (sharedMaterial.HasProperty("_Color"))
            {
                sharedMaterial.SetColor("_Color", color * 2f);
            }
        }

        Renderer renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        Light point = marker.AddComponent<Light>();
        point.type = LightType.Point;
        point.color = color;
        point.intensity = 0.8f;
        point.range = 3.5f;
        point.shadows = LightShadows.None;
    }
}
