using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a simple house directly from an OSM footprint.
///
/// Expected module orientation:
/// - local X = module width
/// - local Y = module height
/// - local Z = wall thickness / outward direction
///
/// The generator repeats wall modules instead of stretching one complete house
/// across the entire OSM rectangle. It also creates a polygon roof and an
/// explicit DeliveryTarget child for DeliveryScoreManager.
/// </summary>
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

    [Header("Prefab Module Fitting")]
    [Tooltip("Choose the nearest whole number of modules for each OSM edge. This prevents windows and doors from being compressed as strongly as the old ceiling-based calculation.")]
    [SerializeField] bool useNearestModuleCount = true;

    [Tooltip("Keep the prefab's designed height. Enable this when WallPlain, WallWindow and WallDoor are already 3 metres tall.")]
    [SerializeField] bool preserveModuleHeight = true;

    [Tooltip("Keep the prefab's designed thickness. Enable this when the modules are already the same thickness as Wall Thickness.")]
    [SerializeField] bool preserveModuleThickness = true;

    [Tooltip("Print a warning when a module must be stretched or compressed strongly along X to fill an unusually short OSM edge.")]
    [SerializeField] bool warnAboutLargeWidthScaling = true;

    [Min(1f)]
    [SerializeField] float largeWidthScaleWarningRatio = 1.35f;

    [Header("Wall Variety")]
    [Range(0f, 1f)]
    [SerializeField] float groundFloorWindowChance = 0.25f;

    [Range(0f, 1f)]
    [SerializeField] float upperFloorWindowChance = 0.35f;

    [Tooltip("Do not place windows in the first or last module of an edge. This makes corners look less crowded.")]
    [SerializeField] bool avoidWindowsAtCorners = true;

    [Min(0)]
    [Tooltip("Number of non-window modules required between two windows on the same edge and floor.")]
    [SerializeField] int minimumPlainModulesBetweenWindows = 1;

    [Min(0)]
    [Tooltip("Maximum windows on one footprint edge per floor. Set to 0 for no limit.")]
    [SerializeField] int maximumWindowsPerEdgePerFloor = 2;

    [Tooltip("Place one door on the longest footprint edge when a Door Wall Prefab is assigned.")]
    [SerializeField] bool createDoor = true;

    public enum RoofStyle
    {
        Gable,
        Hip,
        Shed,
        Random
    }

    [Header("Roof")]
    [SerializeField] Material roofMaterial;

    [Tooltip("No flat roofs are generated. Gable and Hip are used for valid four-corner footprints. Irregular footprints use a Shed roof.")]
    [SerializeField] RoofStyle roofStyle = RoofStyle.Random;

    [Min(0.1f)]
    [Tooltip("Extra height of the ridge, apex, or high side above the top of the walls.")]
    [SerializeField] float slopedRoofHeight = 1.5f;

    [Min(0f)]
    [Tooltip("Moves the entire roof slightly above the walls.")]
    [SerializeField] float roofVerticalOffset = 0.05f;

    [Min(0f)]
    [Tooltip("Makes the roof wider and longer than the house body. Around 0.25 to 0.45 metres usually looks realistic.")]
    [SerializeField] float roofOverhang = 0.35f;

    [Tooltip("Generate a visible fascia/roof edge around the overhanging roof.")]
    [SerializeField] bool createRoofEdge = true;

    [Min(0.02f)]
    [Tooltip("Vertical thickness of the visible roof edge.")]
    [SerializeField] float roofEdgeHeight = 0.12f;

    [Min(0f)]
    [Tooltip("How far the lower roof edge is tucked inward to create a softer eave shape.")]
    [SerializeField] float roofEdgeInset = 0.10f;

    [Tooltip("Cover the gap between the top of the rectangular walls and a sloped Shed roof.")]
    [SerializeField] bool createShedWallFill = true;

    [Tooltip("Adds a MeshCollider to the generated sloped roof.")]
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
    /// localFootprintPoints must be in a world-upright buildingRoot local X/Z coordinate system.
    /// </summary>
    /// 
    [System.Serializable]
    public class BuildingStyle
    {
        public string styleName;

        public Material wallMaterial;
        public Material roofMaterial;
        public Material trimMaterial;
        public Material glassMaterial;
        public Material doorMaterial;
        public Material chimneyMaterial;
        [ColorUsage(false, true)]
        public Color chimneyColor = new Color(0.35f, 0.08f, 0.05f, 1f);
        [Tooltip("Extra scaling applied to chimney parts for this style. Use Y to make it taller and X/Z to make it wider.")]
        public Vector3 chimneyScaleMultiplier = Vector3.one;
    }

    [Header("Building Appearance")]
    [SerializeField] BuildingStyle[] buildingStyles;

    struct FootprintShapeInfo
    {
        public float area;
        public float perimeter;
        public float averageEdgeLength;
        public float longestEdgeLength;
        public float shortestEdgeLength;
        public float edgeAspectRatio;
        public float compactness;
    }

    int GetBuildingStyleIndex(int buildingIndex)
    {
        if (buildingStyles == null || buildingStyles.Length == 0)
        {
            return -1;
        }

        // Stable variation: the same OSM building index keeps the same style.
        return Mathf.Abs(buildingIndex * 31 + 7) % buildingStyles.Length;
    }

    BuildingStyle GetBuildingStyle(int buildingIndex)
    {
        int styleIndex = GetBuildingStyleIndex(buildingIndex);
        return styleIndex >= 0 ? buildingStyles[styleIndex] : null;
    }

    void ApplyBuildingStyle(
        GameObject buildingObject,
        BuildingStyle style)
    {
        if (buildingObject == null || style == null)
        {
            return;
        }

        Renderer[] renderers =
            buildingObject.GetComponentsInChildren<Renderer>(true);
        HashSet<Transform> scaledChimneys = new HashSet<Transform>();

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            string objectName =
                renderer.gameObject.name.ToLowerInvariant();

            Material selectedMaterial = style.wallMaterial;

            if (objectName.Contains("glass"))
            {
                selectedMaterial = style.glassMaterial;
            }
            else if (objectName.Contains("roof"))
            {
                selectedMaterial = style.roofMaterial;
            }
            else if (objectName.Contains("chimney"))
            {
                selectedMaterial = style.chimneyMaterial;
            }
            else if (objectName.Contains("doorpanel") ||
                     objectName == "door")
            {
                selectedMaterial = style.doorMaterial;
            }
            else if (objectName.Contains("frame") ||
                     objectName.Contains("trim") ||
                     objectName.Contains("sill"))
            {
                selectedMaterial = style.trimMaterial;
            }

            if (selectedMaterial != null)
            {
                renderer.sharedMaterial = selectedMaterial;
            }

            if (objectName.Contains("chimney"))
            {
                ApplyRendererTint(renderer, style.chimneyColor);
                ApplyChimneyScale(renderer.transform, style.chimneyScaleMultiplier, scaledChimneys);
            }
        }
    }

    void ApplyChimneyScale(
        Transform chimneyTransform,
        Vector3 scaleMultiplier,
        HashSet<Transform> scaledChimneys)
    {
        if (chimneyTransform == null || scaledChimneys == null)
        {
            return;
        }

        if (!scaledChimneys.Add(chimneyTransform))
        {
            return;
        }

        Vector3 safeMultiplier = new Vector3(
            Mathf.Max(0.01f, scaleMultiplier.x),
            Mathf.Max(0.01f, scaleMultiplier.y),
            Mathf.Max(0.01f, scaleMultiplier.z));

        chimneyTransform.localScale = Vector3.Scale(
            chimneyTransform.localScale,
            safeMultiplier
        );
    }
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

        FootprintShapeInfo shapeInfo =
            AnalyzeFootprintShape(
                footprint,
                signedArea
            );

        int safeMinimumFloors = Mathf.Max(1, minimumFloors);
        int safeMaximumFloors = Mathf.Max(safeMinimumFloors, maximumFloors);
        System.Random random = new System.Random(deterministicSeed);
        int floorCount = ChooseFloorCount(
            safeMinimumFloors,
            safeMaximumFloors,
            shapeInfo,
            random
        );
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
            bool isFrontEdge = edgeIndex == doorEdgeIndex;

            float safePreferredWidth =
                Mathf.Max(0.25f, preferredModuleWidth);

            float idealModuleCount =
                edgeLength / safePreferredWidth;

            int moduleCount = useNearestModuleCount
                ? Mathf.Max(1, Mathf.RoundToInt(idealModuleCount))
                : Mathf.Max(1, Mathf.CeilToInt(idealModuleCount));

            float actualModuleWidth =
                edgeLength / moduleCount;
            int doorModuleIndex = moduleCount / 2;

            for (int floorIndex = 0; floorIndex < floorCount; floorIndex++)
            {
                float floorBaseHeight = floorIndex * floorHeight;
                int windowsPlacedOnEdge = 0;
                int modulesSinceLastWindow =
                    Mathf.Max(0, minimumPlainModulesBetweenWindows);

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

                    windowChance = Mathf.Clamp01(
                        windowChance *
                        GetResidentialWindowChanceMultiplier(
                            shapeInfo,
                            isFrontEdge,
                            floorIndex,
                            moduleCount,
                            moduleIndex
                        )
                    );

                    bool cornerAllowsWindow =
                        !avoidWindowsAtCorners ||
                        moduleCount < 3 ||
                        (moduleIndex > 0 &&
                         moduleIndex < moduleCount - 1);

                    bool spacingAllowsWindow =
                        modulesSinceLastWindow >=
                        Mathf.Max(0, minimumPlainModulesBetweenWindows);

                    bool countAllowsWindow =
                        maximumWindowsPerEdgePerFloor <= 0 ||
                        windowsPlacedOnEdge <
                        maximumWindowsPerEdgePerFloor;

                    bool useWindow =
                        !isDoor &&
                        windowWallPrefab != null &&
                        cornerAllowsWindow &&
                        spacingAllowsWindow &&
                        countAllowsWindow &&
                        random.NextDouble() < windowChance;

                    if (useWindow)
                    {
                        windowsPlacedOnEdge++;
                        modulesSinceLastWindow = 0;
                    }
                    else
                    {
                        modulesSinceLastWindow++;
                    }

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

        RoofStyle selectedRoofStyle =
            ResolveRoofStyle(footprint, random);

        if (!TryCreateRoof(
                generatedRoot,
                footprint,
                totalWallHeight + roofVerticalOffset,
                selectedRoofStyle,
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

        // Apply one coordinated appearance to the entire generated house.
        // deterministicSeed is the OSM building index passed by SceneLoaderArcgis.
        int styleIndex = GetBuildingStyleIndex(deterministicSeed);
        BuildingStyle style = styleIndex >= 0 ? buildingStyles[styleIndex] : null;
        ApplyBuildingStyle(buildingRoot.gameObject, style);
        ApplyHouseMetadata(buildingRoot.gameObject, styleIndex, style);

        Debug.Log(
            $"🏗 Modular house generated: {buildingRoot.name} | " +
            $"Corners={footprint.Count} | Floors={floorCount} | " +
            $"Height={totalWallHeight:F1}m | Roof={selectedRoofStyle}"
        );

        return true;
    }

    void ApplyHouseMetadata(
        GameObject buildingRoot,
        int styleIndex,
        BuildingStyle style)
    {
        if (buildingRoot == null)
        {
            return;
        }

        House house = buildingRoot.GetComponent<House>();
        if (house == null)
        {
            house = buildingRoot.AddComponent<House>();
        }

        house.styleIndex = styleIndex;
        house.styleName = style != null &&
                          !string.IsNullOrWhiteSpace(style.styleName)
            ? style.styleName.Trim()
            : (styleIndex >= 0 ? $"Style {styleIndex}" : "");
        house.deliveryGroupName = house.styleName;
        house.model = buildingRoot.transform;
    }

    void ApplyRendererTint(Renderer renderer, Color tint)
    {
        if (renderer == null)
        {
            return;
        }

        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", tint);
        propertyBlock.SetColor("_Color", tint);
        renderer.SetPropertyBlock(propertyBlock);
    }

    FootprintShapeInfo AnalyzeFootprintShape(
        List<Vector3> footprint,
        float signedArea)
    {
        FootprintShapeInfo info = new FootprintShapeInfo();

        if (footprint == null || footprint.Count < 3)
        {
            return info;
        }

        info.area = Mathf.Abs(signedArea);

        float perimeter = 0f;
        float longestEdge = 0f;
        float shortestEdge = float.MaxValue;

        for (int i = 0; i < footprint.Count; i++)
        {
            Vector3 edge =
                footprint[(i + 1) % footprint.Count] - footprint[i];
            edge.y = 0f;

            float edgeLength = edge.magnitude;
            perimeter += edgeLength;
            longestEdge = Mathf.Max(longestEdge, edgeLength);
            shortestEdge = Mathf.Min(shortestEdge, edgeLength);
        }

        info.perimeter = perimeter;
        info.averageEdgeLength =
            footprint.Count > 0
                ? perimeter / footprint.Count
                : 0f;
        info.longestEdgeLength = longestEdge;
        info.shortestEdgeLength =
            shortestEdge == float.MaxValue ? 0f : shortestEdge;
        info.edgeAspectRatio =
            info.shortestEdgeLength > 0.001f
                ? info.longestEdgeLength / info.shortestEdgeLength
                : 1f;

        float perimeterSquared = perimeter * perimeter;
        info.compactness = perimeterSquared > 0.001f
            ? Mathf.Clamp01(
                (4f * Mathf.PI * info.area) / perimeterSquared
            )
            : 0f;

        return info;
    }

    int ChooseFloorCount(
        int safeMinimumFloors,
        int safeMaximumFloors,
        FootprintShapeInfo shapeInfo,
        System.Random random)
    {
        if (safeMaximumFloors <= safeMinimumFloors)
        {
            return safeMinimumFloors;
        }

        float extraFloorChance = 0.10f;

        if (shapeInfo.area >= 40f)
        {
            extraFloorChance += Mathf.InverseLerp(
                40f,
                180f,
                shapeInfo.area
            ) * 0.40f;
        }

        if (shapeInfo.area <= 60f)
        {
            extraFloorChance -= 0.20f;
        }

        if (shapeInfo.compactness >= 0.65f)
        {
            extraFloorChance += 0.15f;
        }
        else if (shapeInfo.compactness <= 0.45f)
        {
            extraFloorChance -= 0.10f;
        }

        if (shapeInfo.edgeAspectRatio >= 1.35f)
        {
            extraFloorChance += 0.05f;
        }

        extraFloorChance = Mathf.Clamp01(extraFloorChance);

        return random.NextDouble() < extraFloorChance
            ? safeMaximumFloors
            : safeMinimumFloors;
    }

    float GetResidentialWindowChanceMultiplier(
        FootprintShapeInfo shapeInfo,
        bool isFrontEdge,
        int floorIndex,
        int moduleCount,
        int moduleIndex)
    {
        float multiplier = 1f;

        if (floorIndex == 0)
        {
            multiplier *= isFrontEdge ? 0.70f : 0.45f;
        }
        else
        {
            multiplier *= isFrontEdge ? 1.05f : 0.90f;
        }

        if (moduleCount <= 2)
        {
            multiplier *= 0.35f;
        }
        else if (moduleCount == 3)
        {
            multiplier *= moduleIndex == 1 ? 1.0f : 0.55f;
        }
        else
        {
            float center = (moduleCount - 1) * 0.5f;
            float distanceFromCenter =
                Mathf.Abs(moduleIndex - center) /
                Mathf.Max(1f, center);

            multiplier *= Mathf.Lerp(1.0f, 0.50f, distanceFromCenter);
        }

        if (shapeInfo.area < 70f)
        {
            multiplier *= 0.75f;
        }
        else if (shapeInfo.area > 160f)
        {
            multiplier *= 1.08f;
        }

        if (shapeInfo.compactness < 0.50f)
        {
            multiplier *= 0.88f;
        }

        return multiplier;
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

        if (preserveModuleHeight &&
            Mathf.Abs(initialBounds.size.y - targetHeight) > 0.05f)
        {
            Debug.LogWarning(
                $"ModularHouseGenerator: {selectedPrefab.name} is " +
                $"{initialBounds.size.y:F2}m tall, but Floor Height is " +
                $"{targetHeight:F2}m. The prefab height is being preserved."
            );
        }

        if (preserveModuleThickness &&
            Mathf.Abs(initialBounds.size.z - wallThickness) > 0.05f)
        {
            Debug.LogWarning(
                $"ModularHouseGenerator: {selectedPrefab.name} is " +
                $"{initialBounds.size.z:F2}m thick, but Wall Thickness is " +
                $"{wallThickness:F2}m. The prefab thickness is being preserved."
            );
        }

        float scaleX =
            targetWidth / initialBounds.size.x;

        float scaleY = preserveModuleHeight
            ? 1f
            : targetHeight / initialBounds.size.y;

        float scaleZ = 1f;

        if (!preserveModuleThickness &&
            fitPrefabThickness &&
            initialBounds.size.z > 0.001f)
        {
            scaleZ =
                wallThickness / initialBounds.size.z;
        }

        if (warnAboutLargeWidthScaling)
        {
            float safeScaleX = Mathf.Max(0.0001f, scaleX);
            float widthChangeRatio = Mathf.Max(
                safeScaleX,
                1f / safeScaleX
            );

            if (widthChangeRatio > largeWidthScaleWarningRatio)
            {
                Debug.LogWarning(
                    $"ModularHouseGenerator: {selectedPrefab.name} on " +
                    $"edge {edgeIndex} is being width-scaled by {scaleX:F2}. " +
                    "Consider changing Preferred Module Width or creating " +
                    "a second narrow/wide module variant."
                );
            }
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

    RoofStyle ResolveRoofStyle(
        List<Vector3> footprint,
        System.Random random)
    {
        FootprintShapeInfo shapeInfo =
            AnalyzeFootprintShape(
                footprint,
                CalculateSignedArea(footprint)
            );

        bool supportsFourSidedRoof =
            IsValidConvexQuadrilateral(footprint);

        if (!supportsFourSidedRoof)
        {
            return RoofStyle.Shed;
        }

        if (roofStyle == RoofStyle.Random)
        {
            float gableBias = 0.50f;

            if (shapeInfo.edgeAspectRatio >= 1.35f)
            {
                gableBias += 0.20f;
            }
            else if (shapeInfo.edgeAspectRatio <= 1.10f)
            {
                gableBias -= 0.10f;
            }

            if (shapeInfo.compactness >= 0.70f)
            {
                gableBias += 0.10f;
            }

            gableBias = Mathf.Clamp01(gableBias);

            return random.NextDouble() < gableBias
                ? RoofStyle.Gable
                : RoofStyle.Hip;
        }

        if (roofStyle == RoofStyle.Shed)
        {
            return RoofStyle.Shed;
        }

        return roofStyle;
    }

    bool TryCreateRoof(
        Transform generatedRoot,
        List<Vector3> footprint,
        float roofBaseHeight,
        RoofStyle selectedStyle,
        out Vector3 deliveryPoint)
    {
        deliveryPoint = Vector3.zero;

        List<Vector3> cleanRoofFootprint =
            new List<Vector3>(footprint);

        if (CalculateSignedArea(cleanRoofFootprint) < 0f)
        {
            cleanRoofFootprint.Reverse();
        }

        List<Vector3> overhangFootprint =
            BuildRoofOverhangFootprint(
                cleanRoofFootprint,
                roofOverhang
            );

        if (selectedStyle == RoofStyle.Gable &&
            IsValidConvexQuadrilateral(cleanRoofFootprint))
        {
            return TryCreateGableRoof(
                generatedRoot,
                cleanRoofFootprint,
                overhangFootprint,
                roofBaseHeight,
                out deliveryPoint
            );
        }

        if (selectedStyle == RoofStyle.Hip &&
            IsValidConvexQuadrilateral(cleanRoofFootprint))
        {
            return TryCreateHipRoof(
                generatedRoot,
                overhangFootprint,
                roofBaseHeight,
                out deliveryPoint
            );
        }

        return TryCreateShedRoof(
            generatedRoot,
            cleanRoofFootprint,
            overhangFootprint,
            roofBaseHeight,
            out deliveryPoint
        );
    }

    bool TryCreateGableRoof(
        Transform generatedRoot,
        List<Vector3> bodyFootprint,
        List<Vector3> roofFootprint,
        float roofBaseHeight,
        out Vector3 deliveryPoint)
    {
        deliveryPoint = Vector3.zero;

        Vector3 p0 = SetHeight(roofFootprint[0], roofBaseHeight);
        Vector3 p1 = SetHeight(roofFootprint[1], roofBaseHeight);
        Vector3 p2 = SetHeight(roofFootprint[2], roofBaseHeight);
        Vector3 p3 = SetHeight(roofFootprint[3], roofBaseHeight);

        float edgesZeroAndTwoLength =
            ((p1 - p0).magnitude +
             (p3 - p2).magnitude) * 0.5f;

        float edgesOneAndThreeLength =
            ((p2 - p1).magnitude +
             (p0 - p3).magnitude) * 0.5f;

        bool ridgeRunsAlongEdgesZeroAndTwo =
            edgesZeroAndTwoLength >=
            edgesOneAndThreeLength;

        Vector3 ridgeA;
        Vector3 ridgeB;
        Vector3[] roofVertices;
        List<int> roofTriangles = new List<int>();

        if (ridgeRunsAlongEdgesZeroAndTwo)
        {
            ridgeA =
                (p3 + p0) * 0.5f +
                Vector3.up * slopedRoofHeight;

            ridgeB =
                (p1 + p2) * 0.5f +
                Vector3.up * slopedRoofHeight;

            roofVertices = new[]
            {
                p0, p1, p2, p3, ridgeA, ridgeB
            };

            AddUpwardTriangle(
                roofTriangles,
                roofVertices,
                0, 1, 5
            );
            AddUpwardTriangle(
                roofTriangles,
                roofVertices,
                0, 5, 4
            );
            AddUpwardTriangle(
                roofTriangles,
                roofVertices,
                2, 3, 4
            );
            AddUpwardTriangle(
                roofTriangles,
                roofVertices,
                2, 4, 5
            );

            CreateGableWallFill(
                generatedRoot,
                bodyFootprint,
                roofBaseHeight,
                true
            );
        }
        else
        {
            ridgeA =
                (p0 + p1) * 0.5f +
                Vector3.up * slopedRoofHeight;

            ridgeB =
                (p2 + p3) * 0.5f +
                Vector3.up * slopedRoofHeight;

            roofVertices = new[]
            {
                p0, p1, p2, p3, ridgeA, ridgeB
            };

            AddUpwardTriangle(
                roofTriangles,
                roofVertices,
                1, 2, 5
            );
            AddUpwardTriangle(
                roofTriangles,
                roofVertices,
                1, 5, 4
            );
            AddUpwardTriangle(
                roofTriangles,
                roofVertices,
                3, 0, 4
            );
            AddUpwardTriangle(
                roofTriangles,
                roofVertices,
                3, 4, 5
            );

            CreateGableWallFill(
                generatedRoot,
                bodyFootprint,
                roofBaseHeight,
                false
            );
        }

        CreateRoofMeshObject(
            generatedRoot,
            "Roof_Gable",
            roofVertices,
            roofTriangles.ToArray(),
            BuildPlanarUv(roofVertices),
            addRoofCollider
        );

        if (createRoofEdge)
        {
            CreateRoofEdgeMesh(
                generatedRoot,
                new[] { p0, p1, p2, p3 }
            );
        }

        deliveryPoint =
            (ridgeA + ridgeB) * 0.5f;

        return true;
    }

    bool TryCreateHipRoof(
        Transform generatedRoot,
        List<Vector3> roofFootprint,
        float roofBaseHeight,
        out Vector3 deliveryPoint)
    {
        deliveryPoint = Vector3.zero;

        Vector3[] roofVertices = new Vector3[5];

        for (int i = 0; i < 4; i++)
        {
            roofVertices[i] =
                SetHeight(
                    roofFootprint[i],
                    roofBaseHeight
                );
        }

        Vector3 apex =
            (roofVertices[0] +
             roofVertices[1] +
             roofVertices[2] +
             roofVertices[3]) * 0.25f;

        apex.y =
            roofBaseHeight + slopedRoofHeight;

        roofVertices[4] = apex;

        List<int> triangles =
            new List<int>();

        for (int edgeIndex = 0;
             edgeIndex < 4;
             edgeIndex++)
        {
            int next =
                (edgeIndex + 1) % 4;

            AddUpwardTriangle(
                triangles,
                roofVertices,
                edgeIndex,
                next,
                4
            );
        }

        CreateRoofMeshObject(
            generatedRoot,
            "Roof_Hip",
            roofVertices,
            triangles.ToArray(),
            BuildPlanarUv(roofVertices),
            addRoofCollider
        );

        if (createRoofEdge)
        {
            CreateRoofEdgeMesh(
                generatedRoot,
                new[]
                {
                    roofVertices[0],
                    roofVertices[1],
                    roofVertices[2],
                    roofVertices[3]
                }
            );
        }

        deliveryPoint = apex;
        return true;
    }

    bool TryCreateShedRoof(
        Transform generatedRoot,
        List<Vector3> bodyFootprint,
        List<Vector3> roofFootprint,
        float roofBaseHeight,
        out Vector3 deliveryPoint)
    {
        deliveryPoint = Vector3.zero;

        List<Vector3> triangulationFootprint =
            new List<Vector3>(roofFootprint);

        if (CalculateSignedArea(triangulationFootprint) < 0f)
        {
            triangulationFootprint.Reverse();
        }

        if (!TryTriangulatePolygon(
                triangulationFootprint,
                out List<int> triangleIndices))
        {
            Debug.LogWarning(
                "ModularHouseGenerator: Shed roof triangulation failed."
            );
            return false;
        }

        int longestEdgeIndex =
            FindLongestEdgeIndex(
                triangulationFootprint
            );

        Vector3 longestEdge =
            triangulationFootprint[
                (longestEdgeIndex + 1) %
                triangulationFootprint.Count
            ] -
            triangulationFootprint[
                longestEdgeIndex
            ];

        longestEdge.y = 0f;

        Vector3 slopeDirection =
            new Vector3(
                -longestEdge.z,
                0f,
                longestEdge.x
            ).normalized;

        float minimumProjection =
            float.MaxValue;

        float maximumProjection =
            float.MinValue;

        for (int i = 0;
             i < triangulationFootprint.Count;
             i++)
        {
            float projection =
                Vector3.Dot(
                    triangulationFootprint[i],
                    slopeDirection
                );

            minimumProjection =
                Mathf.Min(
                    minimumProjection,
                    projection
                );

            maximumProjection =
                Mathf.Max(
                    maximumProjection,
                    projection
                );
        }

        float projectionRange =
            Mathf.Max(
                0.001f,
                maximumProjection -
                minimumProjection
            );

        Vector3[] roofVertices =
            new Vector3[
                triangulationFootprint.Count
            ];

        for (int i = 0;
             i < triangulationFootprint.Count;
             i++)
        {
            Vector3 point =
                triangulationFootprint[i];

            float projection =
                Vector3.Dot(
                    point,
                    slopeDirection
                );

            float normalizedHeight =
                Mathf.Clamp01(
                    (projection -
                     minimumProjection) /
                    projectionRange
                );

            point.y =
                roofBaseHeight +
                normalizedHeight *
                slopedRoofHeight;

            roofVertices[i] = point;
        }

        if (createShedWallFill)
        {
            Vector3[] bodyRoofTopPoints =
                new Vector3[bodyFootprint.Count];

            for (int i = 0;
                 i < bodyFootprint.Count;
                 i++)
            {
                Vector3 point =
                    bodyFootprint[i];

                float projection =
                    Vector3.Dot(
                        point,
                        slopeDirection
                    );

                float normalizedHeight =
                    Mathf.Clamp01(
                        (projection -
                         minimumProjection) /
                        projectionRange
                    );

                point.y =
                    roofBaseHeight +
                    normalizedHeight *
                    slopedRoofHeight;

                bodyRoofTopPoints[i] = point;
            }

            CreateShedWallFillMesh(
                generatedRoot,
                bodyFootprint,
                bodyRoofTopPoints,
                roofBaseHeight
            );
        }

        List<int> upwardTriangles =
            new List<int>();

        float bestDeliveryHeight =
            float.MinValue;

        Vector3 bestDeliveryPoint =
            CalculateAveragePoint(
                roofVertices
            );

        for (int triangleIndex = 0;
             triangleIndex <
             triangleIndices.Count;
             triangleIndex += 3)
        {
            int a =
                triangleIndices[triangleIndex];
            int b =
                triangleIndices[
                    triangleIndex + 1
                ];
            int c =
                triangleIndices[
                    triangleIndex + 2
                ];

            AddUpwardTriangle(
                upwardTriangles,
                roofVertices,
                a,
                b,
                c
            );

            Vector3 triangleCenter =
                (roofVertices[a] +
                 roofVertices[b] +
                 roofVertices[c]) / 3f;

            if (triangleCenter.y >
                bestDeliveryHeight)
            {
                bestDeliveryHeight =
                    triangleCenter.y;

                bestDeliveryPoint =
                    triangleCenter;
            }
        }

        CreateRoofMeshObject(
            generatedRoot,
            "Roof_Shed",
            roofVertices,
            upwardTriangles.ToArray(),
            BuildPlanarUv(roofVertices),
            addRoofCollider
        );

        if (createRoofEdge)
        {
            CreateRoofEdgeMesh(
                generatedRoot,
                roofVertices
            );
        }

        deliveryPoint =
            bestDeliveryPoint;

        return true;
    }

    void CreateShedWallFillMesh(
        Transform generatedRoot,
        List<Vector3> bodyFootprint,
        Vector3[] roofTopPoints,
        float roofBaseHeight)
    {
        if (generatedRoot == null ||
            bodyFootprint == null ||
            roofTopPoints == null ||
            bodyFootprint.Count < 3 ||
            roofTopPoints.Length != bodyFootprint.Count)
        {
            return;
        }

        List<Vector3> vertices =
            new List<Vector3>();

        List<int> triangles =
            new List<int>();

        List<Vector2> uv =
            new List<Vector2>();

        for (int edgeIndex = 0;
             edgeIndex < bodyFootprint.Count;
             edgeIndex++)
        {
            int nextIndex =
                (edgeIndex + 1) %
                bodyFootprint.Count;

            Vector3 bottomA =
                SetHeight(
                    bodyFootprint[edgeIndex],
                    roofBaseHeight
                );

            Vector3 bottomB =
                SetHeight(
                    bodyFootprint[nextIndex],
                    roofBaseHeight
                );

            Vector3 topA =
                roofTopPoints[edgeIndex];

            Vector3 topB =
                roofTopPoints[nextIndex];

            float heightA =
                topA.y - roofBaseHeight;

            float heightB =
                topB.y - roofBaseHeight;

            // The lowest roof edge can already touch the wall top.
            // Skip only edges that have no visible fill area at either end.
            if (heightA <= 0.001f &&
                heightB <= 0.001f)
            {
                continue;
            }

            float edgeLength =
                Vector3.Distance(
                    bottomA,
                    bottomB
                );

            int vertexStart =
                vertices.Count;

            // Front face vertices.
            vertices.Add(bottomA);
            vertices.Add(bottomB);
            vertices.Add(topB);
            vertices.Add(topA);

            // Separate back-face vertices prevent opposite normals from
            // cancelling when Unity recalculates normals.
            vertices.Add(bottomA);
            vertices.Add(bottomB);
            vertices.Add(topB);
            vertices.Add(topA);

            // Front.
            triangles.Add(vertexStart + 0);
            triangles.Add(vertexStart + 1);
            triangles.Add(vertexStart + 2);

            triangles.Add(vertexStart + 0);
            triangles.Add(vertexStart + 2);
            triangles.Add(vertexStart + 3);

            // Back, reversed.
            triangles.Add(vertexStart + 6);
            triangles.Add(vertexStart + 5);
            triangles.Add(vertexStart + 4);

            triangles.Add(vertexStart + 7);
            triangles.Add(vertexStart + 6);
            triangles.Add(vertexStart + 4);

            float maximumHeight =
                Mathf.Max(
                    heightA,
                    heightB
                );

            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(edgeLength, 0f));
            uv.Add(new Vector2(edgeLength, heightB));
            uv.Add(new Vector2(0f, heightA));

            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(edgeLength, 0f));
            uv.Add(new Vector2(edgeLength, heightB));
            uv.Add(new Vector2(0f, heightA));
        }

        if (vertices.Count == 0)
        {
            return;
        }

        GameObject fillObject =
            new GameObject("ShedWallFill");

        fillObject.transform.SetParent(
            generatedRoot,
            false
        );
        fillObject.transform.localPosition =
            Vector3.zero;
        fillObject.transform.localRotation =
            Quaternion.identity;
        fillObject.transform.localScale =
            Vector3.one;

        Mesh mesh = new Mesh();
        mesh.name =
            "GeneratedShedWallFillMesh";
        mesh.SetVertices(vertices);
        mesh.SetTriangles(
            triangles,
            0
        );
        mesh.SetUVs(0, uv);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter filter =
            fillObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer =
            fillObject.AddComponent<MeshRenderer>();

        Material wallMaterial =
            FindGeneratedWallMaterial(
                generatedRoot
            );

        if (wallMaterial != null)
        {
            renderer.sharedMaterial =
                wallMaterial;
        }
        else
        {
            ApplyRoofMaterial(renderer);
        }
    }

    void CreateGableWallFill(
        Transform generatedRoot,
        List<Vector3> bodyFootprint,
        float roofBaseHeight,
        bool ridgeRunsAlongEdgesZeroAndTwo)
    {
        if (generatedRoot == null ||
            bodyFootprint == null ||
            bodyFootprint.Count != 4)
        {
            return;
        }

        Vector3 b0 =
            SetHeight(
                bodyFootprint[0],
                roofBaseHeight
            );
        Vector3 b1 =
            SetHeight(
                bodyFootprint[1],
                roofBaseHeight
            );
        Vector3 b2 =
            SetHeight(
                bodyFootprint[2],
                roofBaseHeight
            );
        Vector3 b3 =
            SetHeight(
                bodyFootprint[3],
                roofBaseHeight
            );

        Vector3 endA0;
        Vector3 endA1;
        Vector3 endATop;

        Vector3 endB0;
        Vector3 endB1;
        Vector3 endBTop;

        if (ridgeRunsAlongEdgesZeroAndTwo)
        {
            endA0 = b1;
            endA1 = b2;
            endATop =
                (b1 + b2) * 0.5f +
                Vector3.up * slopedRoofHeight;

            endB0 = b3;
            endB1 = b0;
            endBTop =
                (b3 + b0) * 0.5f +
                Vector3.up * slopedRoofHeight;
        }
        else
        {
            endA0 = b0;
            endA1 = b1;
            endATop =
                (b0 + b1) * 0.5f +
                Vector3.up * slopedRoofHeight;

            endB0 = b2;
            endB1 = b3;
            endBTop =
                (b2 + b3) * 0.5f +
                Vector3.up * slopedRoofHeight;
        }

        GameObject fillRootObject =
            new GameObject("GableWallFill");

        fillRootObject.transform.SetParent(
            generatedRoot,
            false
        );
        fillRootObject.transform.localPosition =
            Vector3.zero;
        fillRootObject.transform.localRotation =
            Quaternion.identity;
        fillRootObject.transform.localScale =
            Vector3.one;

        Material wallMaterial =
            FindGeneratedWallMaterial(
                generatedRoot
            );

        CreateDoubleSidedTriangleObject(
            fillRootObject.transform,
            "GableWallFill_A",
            endA0,
            endA1,
            endATop,
            wallMaterial
        );

        CreateDoubleSidedTriangleObject(
            fillRootObject.transform,
            "GableWallFill_B",
            endB0,
            endB1,
            endBTop,
            wallMaterial
        );
    }

    void CreateDoubleSidedTriangleObject(
        Transform parent,
        string objectName,
        Vector3 pointA,
        Vector3 pointB,
        Vector3 pointC,
        Material material)
    {
        if (parent == null)
        {
            return;
        }

        // Give each end its own sensible pivot instead of creating one mesh
        // whose vertices can be tens of metres away from its Transform.
        Vector3 centre =
            (pointA + pointB + pointC) / 3f;

        GameObject triangleObject =
            new GameObject(objectName);

        triangleObject.transform.SetParent(
            parent,
            false
        );
        triangleObject.transform.localPosition =
            centre;
        triangleObject.transform.localRotation =
            Quaternion.identity;
        triangleObject.transform.localScale =
            Vector3.one;

        Vector3 localA = pointA - centre;
        Vector3 localB = pointB - centre;
        Vector3 localC = pointC - centre;

        // Duplicate the vertices for the back face. Reusing the same three
        // vertices with reversed triangles makes RecalculateNormals average
        // opposite normals together, producing black or invalid-looking faces.
        Vector3[] vertices =
        {
            localA,
            localB,
            localC,

            localA,
            localB,
            localC
        };

        int[] triangles =
        {
            0, 1, 2,
            5, 4, 3
        };

        Vector2[] uv =
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0.5f, 1f),

            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0.5f, 1f)
        };

        Mesh mesh = new Mesh();
        mesh.name =
            objectName + "_Mesh";
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter filter =
            triangleObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer =
            triangleObject.AddComponent<MeshRenderer>();

        if (material != null)
        {
            renderer.sharedMaterial = material;
        }
        else
        {
            ApplyRoofMaterial(renderer);
        }
    }

    Material FindGeneratedWallMaterial(
        Transform generatedRoot)
    {
        Renderer[] renderers =
            generatedRoot.GetComponentsInChildren<
                Renderer
            >(true);

        for (int i = 0;
             i < renderers.Length;
             i++)
        {
            Renderer renderer =
                renderers[i];

            if (renderer == null)
            {
                continue;
            }

            string lowerName =
                renderer.gameObject.name
                    .ToLowerInvariant();

            if (lowerName.Contains(
                    "wallbody") &&
                renderer.sharedMaterial != null)
            {
                return renderer.sharedMaterial;
            }
        }

        for (int i = 0;
             i < renderers.Length;
             i++)
        {
            if (renderers[i] != null &&
                renderers[i].sharedMaterial != null)
            {
                return renderers[i]
                    .sharedMaterial;
            }
        }

        return null;
    }

    void CreateRoofEdgeMesh(
        Transform generatedRoot,
        Vector3[] topEdgePoints)
    {
        if (topEdgePoints == null ||
            topEdgePoints.Length < 3 ||
            roofEdgeHeight <= 0f)
        {
            return;
        }

        int pointCount =
            topEdgePoints.Length;

        Vector3 centre = Vector3.zero;
        for (int i = 0; i < pointCount; i++)
        {
            centre += topEdgePoints[i];
        }
        centre /= pointCount;

        Vector3[] vertices =
            new Vector3[pointCount * 2];

        for (int i = 0;
             i < pointCount;
             i++)
        {
            vertices[i] =
                topEdgePoints[i];

            Vector3 inward =
                centre - topEdgePoints[i];
            inward.y = 0f;

            if (inward.sqrMagnitude <= 0.0001f)
            {
                int next = (i + 1) % pointCount;
                Vector3 edge =
                    topEdgePoints[next] - topEdgePoints[i];
                edge.y = 0f;
                inward = new Vector3(
                    -edge.z,
                    0f,
                    edge.x
                );
            }

            inward.y = 0f;

            float inwardDistance =
                Mathf.Min(
                    roofEdgeInset,
                    Vector3.Distance(topEdgePoints[i], centre) * 0.25f
                );

            if (inward.sqrMagnitude > 0.0001f)
            {
                inward.Normalize();
            }

            vertices[i + pointCount] =
                topEdgePoints[i] +
                inward * inwardDistance -
                Vector3.up * roofEdgeHeight;
        }

        List<int> triangles =
            new List<int>();

        for (int i = 0;
             i < pointCount;
             i++)
        {
            int next =
                (i + 1) % pointCount;

            int topCurrent = i;
            int topNext = next;
            int bottomCurrent =
                i + pointCount;
            int bottomNext =
                next + pointCount;

            triangles.Add(topCurrent);
            triangles.Add(topNext);
            triangles.Add(bottomNext);

            triangles.Add(topCurrent);
            triangles.Add(bottomNext);
            triangles.Add(bottomCurrent);
        }

        GameObject edgeObject =
            new GameObject("RoofEdge");

        edgeObject.transform.SetParent(
            generatedRoot,
            false
        );

        Mesh edgeMesh = new Mesh();
        edgeMesh.name =
            "GeneratedRoofEdgeMesh";
        edgeMesh.vertices = vertices;
        edgeMesh.triangles =
            triangles.ToArray();
        edgeMesh.uv =
            BuildPlanarUv(vertices);
        edgeMesh.RecalculateNormals();
        edgeMesh.RecalculateBounds();

        MeshFilter filter =
            edgeObject.AddComponent<
                MeshFilter
            >();
        filter.sharedMesh = edgeMesh;

        MeshRenderer renderer =
            edgeObject.AddComponent<
                MeshRenderer
            >();

        ApplyRoofMaterial(renderer);
    }

    List<Vector3> BuildRoofOverhangFootprint(
        List<Vector3> footprint,
        float overhang)
    {
        List<Vector3> result =
            new List<Vector3>();

        if (footprint == null ||
            footprint.Count < 3 ||
            overhang <= 0f)
        {
            if (footprint != null)
            {
                result.AddRange(footprint);
            }

            return result;
        }

        Vector3 centre =
            Vector3.zero;

        for (int i = 0;
             i < footprint.Count;
             i++)
        {
            centre += footprint[i];
        }

        centre /=
            footprint.Count;

        for (int i = 0;
             i < footprint.Count;
             i++)
        {
            Vector3 previous =
                footprint[
                    (i - 1 +
                     footprint.Count) %
                    footprint.Count
                ];

            Vector3 current =
                footprint[i];

            Vector3 next =
                footprint[
                    (i + 1) %
                    footprint.Count
                ];

            Vector3 previousEdge =
                current - previous;

            Vector3 nextEdge =
                next - current;

            previousEdge.y = 0f;
            nextEdge.y = 0f;

            Vector3 previousOutward =
                new Vector3(
                    previousEdge.z,
                    0f,
                    -previousEdge.x
                ).normalized;

            Vector3 nextOutward =
                new Vector3(
                    nextEdge.z,
                    0f,
                    -nextEdge.x
                ).normalized;

            bool convex =
                Cross2D(
                    previous,
                    current,
                    next
                ) > 0f;

            Vector3 offsetDirection;

            if (convex)
            {
                offsetDirection =
                    previousOutward +
                    nextOutward;

                if (offsetDirection.sqrMagnitude <
                    0.0001f)
                {
                    offsetDirection =
                        nextOutward;
                }

                offsetDirection.Normalize();

                float denominator =
                    Mathf.Abs(
                        Vector3.Dot(
                            offsetDirection,
                            nextOutward
                        )
                    );

                float miterDistance =
                    overhang /
                    Mathf.Max(
                        0.35f,
                        denominator
                    );

                miterDistance =
                    Mathf.Min(
                        miterDistance,
                        overhang * 2.5f
                    );

                result.Add(
                    current +
                    offsetDirection *
                    miterDistance
                );
            }
            else
            {
                offsetDirection =
                    current - centre;

                offsetDirection.y = 0f;

                if (offsetDirection.sqrMagnitude <
                    0.0001f)
                {
                    offsetDirection =
                        nextOutward;
                }

                result.Add(
                    current +
                    offsetDirection.normalized *
                    overhang
                );
            }
        }

        return result;
    }

    bool IsValidConvexQuadrilateral(
        List<Vector3> footprint)
    {
        if (footprint == null ||
            footprint.Count != 4)
        {
            return false;
        }

        float expectedSign = 0f;

        for (int i = 0; i < 4; i++)
        {
            float cross =
                Cross2D(
                    footprint[i],
                    footprint[(i + 1) % 4],
                    footprint[(i + 2) % 4]
                );

            if (Mathf.Abs(cross) <
                0.0001f)
            {
                return false;
            }

            float sign =
                Mathf.Sign(cross);

            if (expectedSign == 0f)
            {
                expectedSign = sign;
            }
            else if (sign != expectedSign)
            {
                return false;
            }
        }

        return true;
    }

    GameObject CreateRoofMeshObject(
        Transform parent,
        string objectName,
        Vector3[] vertices,
        int[] triangles,
        Vector2[] uv,
        bool shouldAddCollider)
    {
        GameObject roofObject =
            new GameObject(objectName);

        roofObject.transform.SetParent(
            parent,
            false
        );
        roofObject.transform.localPosition =
            Vector3.zero;
        roofObject.transform.localRotation =
            Quaternion.identity;
        roofObject.transform.localScale =
            Vector3.one;

        Mesh roofMesh = new Mesh();
        roofMesh.name =
            "GeneratedOsmRoofMesh";
        roofMesh.vertices = vertices;
        roofMesh.triangles = triangles;
        roofMesh.uv = uv;
        roofMesh.RecalculateNormals();
        roofMesh.RecalculateBounds();

        MeshFilter meshFilter =
            roofObject.AddComponent<
                MeshFilter
            >();
        meshFilter.sharedMesh =
            roofMesh;

        MeshRenderer meshRenderer =
            roofObject.AddComponent<
                MeshRenderer
            >();

        ApplyRoofMaterial(
            meshRenderer
        );

        if (shouldAddCollider)
        {
            MeshCollider meshCollider =
                roofObject.AddComponent<
                    MeshCollider
                >();
            meshCollider.sharedMesh =
                roofMesh;
        }

        return roofObject;
    }

    void ApplyRoofMaterial(
        MeshRenderer renderer)
    {
        if (renderer == null)
        {
            return;
        }

        if (roofMaterial != null)
        {
            renderer.sharedMaterial =
                roofMaterial;
            return;
        }

        Shader shader =
            Shader.Find(
                "Universal Render Pipeline/Lit"
            );

        if (shader == null)
        {
            shader =
                Shader.Find("Standard");
        }

        if (shader == null)
        {
            return;
        }

        Material fallbackRoofMaterial =
            new Material(shader);

        fallbackRoofMaterial.color =
            new Color(
                0.35f,
                0.12f,
                0.08f,
                1f
            );

        renderer.material =
            fallbackRoofMaterial;
    }

    Vector3 SetHeight(
        Vector3 point,
        float y)
    {
        point.y = y;
        return point;
    }

    Vector2[] BuildPlanarUv(
        Vector3[] vertices)
    {
        Vector2[] uv =
            new Vector2[
                vertices.Length
            ];

        for (int i = 0;
             i < vertices.Length;
             i++)
        {
            uv[i] =
                new Vector2(
                    vertices[i].x,
                    vertices[i].z
                );
        }

        return uv;
    }

    void AddUpwardTriangle(
        List<int> triangles,
        Vector3[] vertices,
        int a,
        int b,
        int c)
    {
        Vector3 normal =
            Vector3.Cross(
                vertices[b] -
                vertices[a],
                vertices[c] -
                vertices[a]
            );

        if (normal.y >= 0f)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }
        else
        {
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
        }
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
