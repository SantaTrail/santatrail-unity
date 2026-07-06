using System;
using System.Collections.Generic;
using UnityEngine;

public class ModularHouseGenerator : MonoBehaviour
{
    [Header("Wall Module Prefabs")]
    [Tooltip("Basic wall module. A cube is used when this is empty.")]
    [SerializeField] GameObject plainWallPrefab;

    [Tooltip("Optional wall module containing a window.")]
    [SerializeField] GameObject windowWallPrefab;

    [Tooltip("Optional wall module containing a door.")]
    [SerializeField] GameObject doorWallPrefab;

    [Tooltip("Only the Y value is used. X and Z are always forced to 0 so wall modules remain upright.")]
    [SerializeField] Vector3 moduleLocalRotationEuler = Vector3.zero;

    [Header("Wall Dimensions")]
    [Min(0.25f)]
    [SerializeField] float preferredModuleWidth = 2f;

    [Min(0.05f)]
    [SerializeField] float wallThickness = 0.25f;

    [Min(0.5f)]
    [SerializeField] float floorHeight = 3f;

    [Min(1)]
    [SerializeField] int minimumFloors = 1;

    [Min(1)]
    [SerializeField] int maximumFloors = 2;

    [Tooltip("Scale the prefab thickness to Wall Thickness. Disable this when the prefab already has the desired depth.")]
    [SerializeField] bool fitPrefabThickness = true;

    [Tooltip("Automatically move each module so its measured bottom sits on the floor and its measured centre is aligned to the segment.")]
    [SerializeField] bool normalizePrefabBounds = true;

    [Header("Wall Variety")]
    [Range(0f, 1f)]
    [SerializeField] float groundFloorWindowChance = 0.45f;

    [Range(0f, 1f)]
    [SerializeField] float upperFloorWindowChance = 0.70f;

    [Tooltip("Place one door on the longest footprint edge when a Door Wall Prefab is assigned.")]
    [SerializeField] bool createDoor = true;

    [Header("Roof")]
    [SerializeField] Material roofMaterial;

    [Min(0f)]
    [SerializeField] float roofVerticalOffset = 0.05f;

    [Tooltip("Adds a MeshCollider to the generated polygon roof.")]
    [SerializeField] bool addRoofCollider = true;

    [Header("Delivery Target")]
    [Tooltip("Height above the roof used for the DeliveryTarget child.")]
    [Min(0f)]
    [SerializeField] float deliveryTargetVerticalOffset = 0.10f;

    [Header("Generated Object Options")]
    [SerializeField] bool addWallColliders = true;

    [Tooltip("Disable colliders that already exist inside module prefabs. The generator creates predictable wall colliders instead.")]
    [SerializeField] bool disablePrefabColliders = true;

    public float FloorHeight => floorHeight;

    /// <summary>
    /// Generates a modular house under buildingRoot.
    /// localFootprintPoints must be in buildingRoot local X/Z metre coordinates.
    /// </summary>
    public bool GenerateHouse(
        Transform buildingRoot,
        List<Vector3> localFootprintPoints,
        int deterministicSeed,
        out Transform deliveryTarget)
    {
        deliveryTarget = null;

        if (buildingRoot == null)
        {
            Debug.LogWarning("ModularHouseGenerator: Building root is missing.");
            return false;
        }

        List<Vector3> footprint = CleanFootprint(localFootprintPoints);
        if (footprint.Count < 3)
        {
            Debug.LogWarning(
                $"ModularHouseGenerator: {buildingRoot.name} has fewer than three valid footprint points."
            );
            return false;
        }

        float signedArea = CalculateSignedArea(footprint);
        if (Mathf.Abs(signedArea) <= 0.001f)
        {
            Debug.LogWarning(
                $"ModularHouseGenerator: {buildingRoot.name} has a degenerate footprint."
            );
            return false;
        }

        int safeMinimumFloors = Mathf.Max(1, minimumFloors);
        int safeMaximumFloors = Mathf.Max(safeMinimumFloors, maximumFloors);
        System.Random random = new System.Random(deterministicSeed);
        int floorCount = random.Next(safeMinimumFloors, safeMaximumFloors + 1);
        float totalWallHeight = floorCount * Mathf.Max(0.5f, floorHeight);

        GameObject generatedRootObject = new GameObject("GeneratedModules");
        generatedRootObject.transform.SetParent(buildingRoot, false);
        generatedRootObject.transform.localPosition = Vector3.zero;
        generatedRootObject.transform.localRotation = Quaternion.identity;
        generatedRootObject.transform.localScale = Vector3.one;

        Transform generatedRoot = generatedRootObject.transform;

        GameObject wallsObject = new GameObject("Walls");
        wallsObject.transform.SetParent(generatedRoot, false);
        Transform wallsRoot = wallsObject.transform;

        int doorEdgeIndex = FindLongestEdgeIndex(footprint);
        bool isCounterClockwise = signedArea > 0f;

        for (int edgeIndex = 0; edgeIndex < footprint.Count; edgeIndex++)
        {
            Vector3 edgeStart = footprint[edgeIndex];
            Vector3 edgeEnd = footprint[(edgeIndex + 1) % footprint.Count];
            Vector3 edge = edgeEnd - edgeStart;
            edge.y = 0f;

            float edgeLength = edge.magnitude;
            if (edgeLength <= 0.01f)
            {
                continue;
            }

            Vector3 tangent = edge / edgeLength;
            Vector3 outward = isCounterClockwise
                ? new Vector3(tangent.z, 0f, -tangent.x)
                : new Vector3(-tangent.z, 0f, tangent.x);

            if (outward.sqrMagnitude <= 0.0001f)
            {
                continue;
            }

            outward.Normalize();

            int moduleCount = Mathf.Max(
                1,
                Mathf.CeilToInt(edgeLength / Mathf.Max(0.25f, preferredModuleWidth))
            );

            float actualModuleWidth = edgeLength / moduleCount;
            int doorModuleIndex = moduleCount / 2;

            for (int floorIndex = 0; floorIndex < floorCount; floorIndex++)
            {
                float floorBaseHeight = floorIndex * floorHeight;

                for (int moduleIndex = 0; moduleIndex < moduleCount; moduleIndex++)
                {
                    float distanceAlongEdge =
                        (moduleIndex + 0.5f) * actualModuleWidth;

                    Vector3 modulePosition =
                        edgeStart + tangent * distanceAlongEdge;
                    modulePosition.y = floorBaseHeight;

                    bool isDoor =
                        createDoor &&
                        doorWallPrefab != null &&
                        floorIndex == 0 &&
                        edgeIndex == doorEdgeIndex &&
                        moduleIndex == doorModuleIndex;

                    float windowChance = floorIndex == 0
                        ? groundFloorWindowChance
                        : upperFloorWindowChance;

                    bool useWindow =
                        !isDoor &&
                        windowWallPrefab != null &&
                        random.NextDouble() < windowChance;

                    GameObject selectedPrefab = isDoor
                        ? doorWallPrefab
                        : useWindow
                            ? windowWallPrefab
                            : plainWallPrefab;

                    string moduleType = isDoor
                        ? "Door"
                        : useWindow
                            ? "Window"
                            : "Wall";

                    CreateWallModule(
                        wallsRoot,
                        selectedPrefab,
                        moduleType,
                        edgeIndex,
                        floorIndex,
                        moduleIndex,
                        modulePosition,
                        outward,
                        actualModuleWidth,
                        floorHeight
                    );
                }
            }
        }

        if (!TryCreateRoof(
                generatedRoot,
                footprint,
                totalWallHeight + roofVerticalOffset,
                out Vector3 deliveryPoint))
        {
            Destroy(generatedRootObject);
            return false;
        }

        GameObject deliveryTargetObject = new GameObject("DeliveryTarget");
        deliveryTargetObject.transform.SetParent(buildingRoot, false);
        deliveryTargetObject.transform.localPosition =
            deliveryPoint + Vector3.up * deliveryTargetVerticalOffset;
        deliveryTargetObject.transform.localRotation = Quaternion.identity;
        deliveryTargetObject.transform.localScale = Vector3.one;
        deliveryTarget = deliveryTargetObject.transform;

        Debug.Log(
            $"🏗 Modular house generated: {buildingRoot.name} | " +
            $"Corners={footprint.Count} | Floors={floorCount} | " +
            $"Height={totalWallHeight:F1}m"
        );

        return true;
    }

    void CreateWallModule(
        Transform wallsRoot,
        GameObject selectedPrefab,
        string moduleType,
        int edgeIndex,
        int floorIndex,
        int moduleIndex,
        Vector3 localPosition,
        Vector3 outward,
        float targetWidth,
        float targetHeight)
    {
        GameObject holderObject = new GameObject(
            $"{moduleType}_E{edgeIndex}_F{floorIndex}_M{moduleIndex}"
        );

        holderObject.transform.SetParent(wallsRoot, false);
        holderObject.transform.localPosition = localPosition;

        // Use a calculated yaw instead of LookRotation so X and Z stay exactly 0.
        float wallYawDegrees = Mathf.Atan2(
            outward.x,
            outward.z
        ) * Mathf.Rad2Deg;

        holderObject.transform.localRotation =
            Quaternion.Euler(0f, wallYawDegrees, 0f);
        holderObject.transform.localScale = Vector3.one;

        if (addWallColliders)
        {
            BoxCollider wallCollider = holderObject.AddComponent<BoxCollider>();
            wallCollider.center = new Vector3(0f, targetHeight * 0.5f, 0f);
            wallCollider.size = new Vector3(
                Mathf.Max(0.05f, targetWidth),
                Mathf.Max(0.05f, targetHeight),
                Mathf.Max(0.05f, wallThickness)
            );
        }

        if (selectedPrefab == null)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = moduleType + "_FallbackCube";
            cube.transform.SetParent(holderObject.transform, false);
            cube.transform.localPosition = new Vector3(0f, targetHeight * 0.5f, 0f);
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = new Vector3(
                targetWidth,
                targetHeight,
                wallThickness
            );

            Collider cubeCollider = cube.GetComponent<Collider>();
            if (cubeCollider != null)
            {
                Destroy(cubeCollider);
            }

            return;
        }

        GameObject module = Instantiate(selectedPrefab, holderObject.transform);
        module.name = selectedPrefab.name;
        module.transform.localPosition = Vector3.zero;

        // The module can have an additional Y-axis correction, but never X/Z rotation.
        module.transform.localRotation = Quaternion.Euler(
            0f,
            moduleLocalRotationEuler.y,
            0f
        );
        module.transform.localScale = Vector3.one;

        if (disablePrefabColliders)
        {
            Collider[] existingColliders = module.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < existingColliders.Length; i++)
            {
                existingColliders[i].enabled = false;
            }
        }

        if (!TryMeasureBoundsInSpace(
                module,
                holderObject.transform,
                out Bounds initialBounds))
        {
            return;
        }

        if (initialBounds.size.x <= 0.001f ||
            initialBounds.size.y <= 0.001f)
        {
            return;
        }

        float scaleX = targetWidth / initialBounds.size.x;
        float scaleY = targetHeight / initialBounds.size.y;
        float scaleZ = 1f;

        if (fitPrefabThickness && initialBounds.size.z > 0.001f)
        {
            scaleZ = wallThickness / initialBounds.size.z;
        }

        module.transform.localScale = Vector3.Scale(
            module.transform.localScale,
            new Vector3(scaleX, scaleY, scaleZ)
        );

        if (!normalizePrefabBounds)
        {
            return;
        }

        if (TryMeasureBoundsInSpace(
                module,
                holderObject.transform,
                out Bounds fittedBounds))
        {
            module.transform.localPosition += new Vector3(
                -fittedBounds.center.x,
                -fittedBounds.min.y,
                -fittedBounds.center.z
            );
        }
    }

    bool TryCreateRoof(
        Transform generatedRoot,
        List<Vector3> footprint,
        float roofHeight,
        out Vector3 deliveryPoint)
    {
        deliveryPoint = Vector3.zero;

        List<Vector3> triangulationFootprint =
            new List<Vector3>(footprint);

        if (CalculateSignedArea(triangulationFootprint) < 0f)
        {
            triangulationFootprint.Reverse();
        }

        if (!TryTriangulatePolygon(
                triangulationFootprint,
                out List<int> triangleIndices))
        {
            Debug.LogWarning("ModularHouseGenerator: Roof triangulation failed.");
            return false;
        }

        Vector3[] vertices = new Vector3[triangulationFootprint.Count];
        Vector2[] uv = new Vector2[triangulationFootprint.Count];

        for (int i = 0; i < triangulationFootprint.Count; i++)
        {
            Vector3 point = triangulationFootprint[i];
            point.y = roofHeight;
            vertices[i] = point;
            uv[i] = new Vector2(point.x, point.z);
        }

        int[] upwardTriangles = new int[triangleIndices.Count];
        float largestTriangleArea = -1f;
        Vector3 bestDeliveryPoint = CalculateAveragePoint(vertices);

        for (int triangle = 0; triangle < triangleIndices.Count; triangle += 3)
        {
            int a = triangleIndices[triangle];
            int b = triangleIndices[triangle + 1];
            int c = triangleIndices[triangle + 2];

            // Ear clipping returns counter-clockwise X/Z triangles. Reversing B/C
            // creates upward-facing triangles in Unity's X/Z plane.
            upwardTriangles[triangle] = a;
            upwardTriangles[triangle + 1] = c;
            upwardTriangles[triangle + 2] = b;

            float triangleArea = Mathf.Abs(Cross2D(
                triangulationFootprint[a],
                triangulationFootprint[b],
                triangulationFootprint[c]
            )) * 0.5f;

            if (triangleArea > largestTriangleArea)
            {
                largestTriangleArea = triangleArea;
                bestDeliveryPoint =
                    (vertices[a] + vertices[b] + vertices[c]) / 3f;
            }
        }

        Mesh roofMesh = new Mesh();
        roofMesh.name = "GeneratedOsmRoofMesh";
        roofMesh.vertices = vertices;
        roofMesh.triangles = upwardTriangles;
        roofMesh.uv = uv;
        roofMesh.RecalculateNormals();
        roofMesh.RecalculateBounds();

        GameObject roofObject = new GameObject("Roof");
        roofObject.transform.SetParent(generatedRoot, false);
        roofObject.transform.localPosition = Vector3.zero;
        roofObject.transform.localRotation = Quaternion.identity;
        roofObject.transform.localScale = Vector3.one;

        MeshFilter meshFilter = roofObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = roofMesh;

        MeshRenderer meshRenderer = roofObject.AddComponent<MeshRenderer>();
        if (roofMaterial != null)
        {
            meshRenderer.sharedMaterial = roofMaterial;
        }
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader != null)
            {
                Material fallbackRoofMaterial = new Material(shader);
                fallbackRoofMaterial.color = new Color(0.35f, 0.12f, 0.08f, 1f);
                meshRenderer.material = fallbackRoofMaterial;
            }
        }

        if (addRoofCollider)
        {
            MeshCollider meshCollider = roofObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = roofMesh;
        }

        deliveryPoint = bestDeliveryPoint;
        return true;
    }

    List<Vector3> CleanFootprint(List<Vector3> source)
    {
        List<Vector3> clean = new List<Vector3>();
        if (source == null)
        {
            return clean;
        }

        const float duplicateDistanceSqr = 0.0001f;

        for (int i = 0; i < source.Count; i++)
        {
            Vector3 point = source[i];
            point.y = 0f;

            if (clean.Count == 0 ||
                (clean[clean.Count - 1] - point).sqrMagnitude > duplicateDistanceSqr)
            {
                clean.Add(point);
            }
        }

        if (clean.Count > 1 &&
            (clean[0] - clean[clean.Count - 1]).sqrMagnitude <= duplicateDistanceSqr)
        {
            clean.RemoveAt(clean.Count - 1);
        }

        bool removedPoint = true;
        int safety = 0;

        while (removedPoint && clean.Count > 3 && safety < 100)
        {
            removedPoint = false;
            safety++;

            for (int i = 0; i < clean.Count; i++)
            {
                Vector3 previous = clean[(i - 1 + clean.Count) % clean.Count];
                Vector3 current = clean[i];
                Vector3 next = clean[(i + 1) % clean.Count];

                if (Mathf.Abs(Cross2D(previous, current, next)) <= 0.001f)
                {
                    clean.RemoveAt(i);
                    removedPoint = true;
                    break;
                }
            }
        }

        return clean;
    }

    int FindLongestEdgeIndex(List<Vector3> footprint)
    {
        int bestIndex = 0;
        float bestLengthSqr = -1f;

        for (int i = 0; i < footprint.Count; i++)
        {
            Vector3 edge = footprint[(i + 1) % footprint.Count] - footprint[i];
            edge.y = 0f;
            float lengthSqr = edge.sqrMagnitude;

            if (lengthSqr > bestLengthSqr)
            {
                bestLengthSqr = lengthSqr;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    float CalculateSignedArea(List<Vector3> polygon)
    {
        float area = 0f;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector3 current = polygon[i];
            Vector3 next = polygon[(i + 1) % polygon.Count];
            area += current.x * next.z - next.x * current.z;
        }

        return area * 0.5f;
    }

    bool TryTriangulatePolygon(
        List<Vector3> counterClockwisePolygon,
        out List<int> triangles)
    {
        triangles = new List<int>();

        if (counterClockwisePolygon == null ||
            counterClockwisePolygon.Count < 3)
        {
            return false;
        }

        List<int> remaining = new List<int>();
        for (int i = 0; i < counterClockwisePolygon.Count; i++)
        {
            remaining.Add(i);
        }

        int safety = 0;
        int maximumIterations = counterClockwisePolygon.Count * counterClockwisePolygon.Count;

        while (remaining.Count > 3 && safety < maximumIterations)
        {
            bool clippedEar = false;
            safety++;

            for (int i = 0; i < remaining.Count; i++)
            {
                int previousIndex = remaining[(i - 1 + remaining.Count) % remaining.Count];
                int currentIndex = remaining[i];
                int nextIndex = remaining[(i + 1) % remaining.Count];

                Vector3 a = counterClockwisePolygon[previousIndex];
                Vector3 b = counterClockwisePolygon[currentIndex];
                Vector3 c = counterClockwisePolygon[nextIndex];

                if (Cross2D(a, b, c) <= 0.0001f)
                {
                    continue;
                }

                bool containsOtherPoint = false;

                for (int candidate = 0; candidate < remaining.Count; candidate++)
                {
                    int candidateIndex = remaining[candidate];
                    if (candidateIndex == previousIndex ||
                        candidateIndex == currentIndex ||
                        candidateIndex == nextIndex)
                    {
                        continue;
                    }

                    if (PointInsideTriangle(
                            counterClockwisePolygon[candidateIndex],
                            a,
                            b,
                            c))
                    {
                        containsOtherPoint = true;
                        break;
                    }
                }

                if (containsOtherPoint)
                {
                    continue;
                }

                triangles.Add(previousIndex);
                triangles.Add(currentIndex);
                triangles.Add(nextIndex);
                remaining.RemoveAt(i);
                clippedEar = true;
                break;
            }

            if (!clippedEar)
            {
                return false;
            }
        }

        if (remaining.Count == 3)
        {
            triangles.Add(remaining[0]);
            triangles.Add(remaining[1]);
            triangles.Add(remaining[2]);
        }

        return triangles.Count >= 3;
    }

    bool PointInsideTriangle(
        Vector3 point,
        Vector3 a,
        Vector3 b,
        Vector3 c)
    {
        float cross1 = Cross2D(a, b, point);
        float cross2 = Cross2D(b, c, point);
        float cross3 = Cross2D(c, a, point);

        const float epsilon = 0.0001f;
        return cross1 >= -epsilon &&
               cross2 >= -epsilon &&
               cross3 >= -epsilon;
    }

    float Cross2D(Vector3 a, Vector3 b, Vector3 c)
    {
        return
            (b.x - a.x) * (c.z - a.z) -
            (b.z - a.z) * (c.x - a.x);
    }

    Vector3 CalculateAveragePoint(Vector3[] points)
    {
        if (points == null || points.Length == 0)
        {
            return Vector3.zero;
        }

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < points.Length; i++)
        {
            sum += points[i];
        }

        return sum / points.Length;
    }

    bool TryMeasureBoundsInSpace(
        GameObject target,
        Transform measurementSpace,
        out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);

        if (target == null || measurementSpace == null)
        {
            return false;
        }

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            return false;
        }

        bool hasBounds = false;

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null)
            {
                continue;
            }

            Vector3[] worldCorners = GetRendererWorldCorners(renderer);

            for (int cornerIndex = 0; cornerIndex < worldCorners.Length; cornerIndex++)
            {
                Vector3 localCorner =
                    measurementSpace.InverseTransformPoint(worldCorners[cornerIndex]);

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

        return hasBounds;
    }

    Vector3[] GetRendererWorldCorners(Renderer renderer)
    {
        if (renderer == null)
        {
            return Array.Empty<Vector3>();
        }

        Bounds localBounds;
        SkinnedMeshRenderer skinnedMeshRenderer =
            renderer as SkinnedMeshRenderer;
        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();

        if (skinnedMeshRenderer != null)
        {
            localBounds = skinnedMeshRenderer.localBounds;
        }
        else if (meshFilter != null &&
                 meshFilter.sharedMesh != null)
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
            worldCorners[i] =
                renderer.transform.TransformPoint(localCorners[i]);
        }

        return worldCorners;
    }

}
