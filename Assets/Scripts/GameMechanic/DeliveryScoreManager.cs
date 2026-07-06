using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Esri.ArcGISMapsSDK.Components;

public class DeliveryScoreManager : MonoBehaviour
{
    public event Action<int> ScoreChanged;
    public event Action<int> TargetsRemainingChanged;
    public event Action LevelCompleted;

    [Header("References")]
    public Transform drone;
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI statusText;
    [Tooltip("Optional separate canvas text for delivery messages. If empty, Status Text is used.")]
    public TextMeshProUGUI deliveryStatusText;

    [Header("Level Target Setup")]
    [Tooltip("How many random spawned buildings become delivery targets in this level.")]
    [Min(1)] public int targetBuildingCount = 5;
    [Tooltip("How often the manager retries while waiting for ArcGIS buildings to spawn.")]
    [Min(0.1f)] public float buildingScanRetrySeconds = 1f;
    [Tooltip("Maximum time spent waiting for spawned buildings. Use 0 to keep retrying forever.")]
    [Min(0f)] public float buildingScanTimeoutSeconds = 60f;

    [Header("OSM Delivery")]
    public float deliveryRange = 2f;
    public float hoverSecondsRequired = 1.5f;
    public int rewardPerDelivery = 100;
    public float scanInterval = 1f;
    public float minBuildingHeight = 0.1f;
    public float minBuildingFootprint = 3f;
    public bool includeInactiveRenderers = false;

    [Header("Delivery Precision")]
    [Tooltip("Horizontal radius around the selected roof center where a present can be delivered.")]
    [Min(0.1f)] public float deliveryRadius = 5f;
    public float maxHoverSpeed = 100f;
    public float maxHeightAboveRoof = 3f;
    public bool showSpawnRadius = true;
    [Tooltip("Editor-only debug gizmos. Keep this off to avoid extra colored circles in Scene view.")]
    public bool showDebugGizmos = false;
    public bool logCandidateBuildings = false;
    public bool logAcceptedBuildingRoots = false;
    [Tooltip("Enable only while debugging. Per-frame logs can freeze or crash the Unity Editor.")]
    public bool verboseDebugLogs = false;

    [Header("Delivery Ring")]
    public float deliveryRingVerticalOffset = 0.2f;
    [Range(12, 128)] public int deliveryRingSegments = 48;
    public float deliveryRingWidth = 0.18f;
    public Color deliveryRingReadyColor = new Color(0.2f, 1f, 0.3f, 0.9f);
    public Color deliveryRingSearchColor = new Color(1f, 0.9f, 0.2f, 0.9f);

    [Header("Target Beacon")]
    [Tooltip("Optional prefab shown above every selected delivery house. A simple light pillar is created when this is empty.")]
    public GameObject targetBeaconPrefab;
    public Vector3 targetBeaconOffset = new Vector3(0f, 8f, 0f);
    public Vector3 targetBeaconScale = new Vector3(1.5f, 8f, 1.5f);
    public Color targetBeaconColor = new Color(1f, 0.15f, 0.1f, 0.65f);
    public bool rotateTargetBeacons = true;
    public float targetBeaconRotationSpeed = 45f;

    [Header("UI")]
    public string progressMessage = "Sending present...";
    public string completeMessage = "Present send complete!";
    public string level1CompleteMessage = "Level 1 Complete!";
    public float completeMessageDuration = 2f;


    [Header("Present Drop Animation")]
    [Tooltip("Add one or more present prefabs. One random prefab is selected for each delivery.")]
    public GameObject[] presentPrefabs;

    [Tooltip("Message shown while the present is travelling to the house.")]
    public string droppingPresentMessage = "Dropping present...";

    [Tooltip("Local offset from the drone where the present begins.")]
    public Vector3 presentSpawnOffset = new Vector3(0f, -0.5f, 0f);

    [Tooltip("Height above the target roof where the present finishes.")]
    [Min(0f)] public float presentLandingHeight = 0.25f;

    [Tooltip("Time taken for the present to travel from the drone to the roof.")]
    [Min(0.05f)] public float presentDropDuration = 1.2f;

    [Tooltip("Controls the travel speed over time. The default gives a smooth start and finish.")]
    public AnimationCurve presentDropCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Extra upward curve during the drop. Set to 0 for a straight fall.")]
    [Min(0f)] public float presentDropArcHeight = 0.8f;

    [Tooltip("How quickly the present spins while falling.")]
    public Vector3 presentSpinDegreesPerSecond =
        new Vector3(180f, 240f, 120f);

    [Tooltip("Small bounce after the present reaches the roof.")]
    [Min(0f)] public float presentLandingBounceHeight = 0.25f;

    [Tooltip("Duration of the landing bounce.")]
    [Min(0f)] public float presentLandingBounceDuration = 0.2f;

    [Tooltip("Keep the landed present attached to the delivered building.")]
    public bool parentPresentToBuildingAfterLanding = true;

    [Tooltip("Optional particle or sparkle prefab spawned when the present lands.")]
    public GameObject presentLandingEffectPrefab;

    [Tooltip("How long the present remains on the roof before disappearing. Set to 0 to keep it.")]
    [Min(0f)] public float presentStayDuration = 2f;

    [Tooltip("Scale multiplier applied to the selected present prefab.")]
    [Min(0.01f)] public float presentScaleMultiplier = 1f;

    [Tooltip("Prevent prefab colliders and rigidbodies from affecting the drone/building during animation.")]
    public bool disablePresentPhysics = true;

    [Header("Delivered Building")]
    [Tooltip("Hide the target building after the present is delivered.")]
    public bool hideDeliveredBuilding = false;
    [Min(0f)] public float hideDeliveredBuildingDelay = 0.2f;

    [Header("Level Progress")]
    [Tooltip("Legacy value kept for existing scenes. Completion now uses the number of randomly selected targets.")]
    public int level1RequiredDeliveries = 5;

    public int score = 0;
    private bool buildingsInitialized = false;
    private sealed class SpawnedBuildingTarget
    {
        public Transform root;
        public Renderer representativeRenderer;
        public Bounds bounds;
        public Vector3 roofTarget;
        public string key;
    }

    private readonly HashSet<string> deliveredBuildingKeys = new HashSet<string>();
    private readonly HashSet<string> deliveriesInProgressKeys = new HashSet<string>();
    private readonly Dictionary<string, float> hoverTimers = new Dictionary<string, float>();
    private readonly List<SpawnedBuildingTarget> cachedBuildings = new List<SpawnedBuildingTarget>();
    private readonly List<SpawnedBuildingTarget> activeTargets = new List<SpawnedBuildingTarget>();

    private int totalTargets = 0;
    public int RemainingTargets => activeTargets.Count;
    public int CompletedTargets => completedDeliveries;
    public int TotalTargets => totalTargets;
    private string latestStatus = "";
    private float statusUntilTime = -1f;
    private int completedDeliveries = 0;
    private bool level1Completed = false;
    private MAVLinkReceiver mavReceiver;
    private SpawnedBuildingTarget currentNearestBuilding;
    private SpawnedBuildingTarget currentDeliverableTarget;
    private readonly Dictionary<string, LineRenderer> deliveryRings = new Dictionary<string, LineRenderer>();
    private readonly Dictionary<string, GameObject> deliveryRingObjects = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, GameObject> targetBeacons = new Dictionary<string, GameObject>();
    private Vector3 lastDronePos;
    private float currentSpeed;

    void Start()
    {
        AutoBindDrone();
        CleanupLegacyDeliveryRings();
        UpdateScoreUI();
        ScoreChanged?.Invoke(score);

        if (drone != null)
        {
            lastDronePos = drone.position;
        }

        StartCoroutine(InitializeBuildingTargets());
    }

    IEnumerator InitializeBuildingTargets()
    {
        float elapsed = 0f;

        while (!buildingsInitialized)
        {
            AutoBindDrone();
            ScanBuildings();

            if (cachedBuildings.Count > 0)
            {
                int requestedCount = Mathf.Max(1, targetBuildingCount);
                SelectRandomTargets(requestedCount);
                buildingsInitialized = activeTargets.Count > 0;

                if (buildingsInitialized)
                {
                    Debug.Log(
                        $"Delivery system initialized with {activeTargets.Count} random targets " +
                        $"from {cachedBuildings.Count} detected buildings."
                    );
                    yield break;
                }
            }

            if (buildingScanTimeoutSeconds > 0f && elapsed >= buildingScanTimeoutSeconds)
            {
                Debug.LogWarning(
                    $"DeliveryScoreManager found no spawned building targets after " +
                    $"{buildingScanTimeoutSeconds:0.0} seconds."
                );
                yield break;
            }

            float delay = Mathf.Max(0.1f, buildingScanRetrySeconds);
            yield return new WaitForSeconds(delay);
            elapsed += delay;
        }
    }

    void Update()
    {
        AutoBindDrone();
        // Debug.Log($"Drone transform = {drone.position}");
        if (mavReceiver == null)
        {
            mavReceiver = MAVLinkReceiver.Active;
        }

        if (drone == null)
        {
            SetAllDeliveryRingsActive(false);
            return;
        }

        bool farGuidedTargetActive =
            mavReceiver != null &&
            mavReceiver.IsGuidedArmed &&
            mavReceiver.HasNavTargetDistance &&
            mavReceiver.NavTargetDistanceMeters > 25f;

        if (rotateTargetBeacons)
        {
            foreach (GameObject beacon in targetBeacons.Values)
            {
                if (beacon != null)
                {
                    beacon.transform.Rotate(Vector3.up, targetBeaconRotationSpeed * Time.deltaTime, Space.World);
                }
            }
        }

        currentSpeed =
            Vector3.Distance(drone.position, lastDronePos) /
            Mathf.Max(Time.deltaTime, 0.0001f);
        lastDronePos = drone.position;

        if (statusUntilTime > 0f && Time.time > statusUntilTime)
        {
            ClearDeliveryStatus();
        }

        if (farGuidedTargetActive)
        {
            if (hoverTimers.Count > 0)
            {
                hoverTimers.Clear();
            }

            ClearProgressStatusOnly();

            UpdateAllDeliveryRings(null);
            return;
        }


        if (!buildingsInitialized || activeTargets.Count == 0)
        {
            SetAllDeliveryRingsActive(false);
            return;
        }

        EvaluateTargets(out SpawnedBuildingTarget nearestTarget, out SpawnedBuildingTarget deliverableTarget);
        currentNearestBuilding = nearestTarget;
        currentDeliverableTarget = deliverableTarget;

        UpdateAllDeliveryRings(currentDeliverableTarget);

        if (currentDeliverableTarget != null)
        {
            ProcessBuildingHover(currentDeliverableTarget);
        }
        else
        {
            if (hoverTimers.Count > 0)
            {
                hoverTimers.Clear();
            }

            ClearProgressStatusOnly();
        }

    }


    TextMeshProUGUI GetDeliveryStatusText()
    {
        return deliveryStatusText != null ? deliveryStatusText : statusText;
    }

    void SetDeliveryStatus(string message, float durationSeconds = -1f)
    {
        TextMeshProUGUI text = GetDeliveryStatusText();
        latestStatus = string.IsNullOrEmpty(message) ? "" : message;

        if (text != null)
        {
            text.text = latestStatus;
        }

        statusUntilTime = durationSeconds > 0f
            ? Time.time + durationSeconds
            : -1f;
    }

    void ClearDeliveryStatus()
    {
        latestStatus = "";
        statusUntilTime = -1f;

        TextMeshProUGUI text = GetDeliveryStatusText();
        if (text != null)
        {
            text.text = "";
        }
    }

    void ClearProgressStatusOnly()
    {
        if (latestStatus.StartsWith(progressMessage) ||
            latestStatus == "Hold position...")
        {
            ClearDeliveryStatus();
        }
    }

    void AutoBindDrone()
    {
        if (drone != null)
        {
            return;
        }

        GameObject droneObj = GameObject.FindGameObjectWithTag("Drone");
        if (droneObj != null)
        {
            ArcGISLocationComponent locationComponent = droneObj.GetComponentInChildren<ArcGISLocationComponent>(true);
            drone = locationComponent != null ? locationComponent.transform : droneObj.transform;
        }
    }

    void ScanBuildings()
    {
        cachedBuildings.Clear();

        Renderer[] all = FindObjectsByType<Renderer>(
            includeInactiveRenderers ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        Dictionary<Transform, List<Renderer>> grouped = new Dictionary<Transform, List<Renderer>>();

        for (int i = 0; i < all.Length; i++)
        {
            Renderer renderer = all[i];
            if (renderer == null)
            {
                continue;
            }

            if (!includeInactiveRenderers && !renderer.enabled)
            {
                continue;
            }

            if (renderer.gameObject.layer == 5)
            {
                continue;
            }

            if (drone != null && (renderer.transform == drone || renderer.transform.IsChildOf(drone)))
            {
                continue;
            }

            Transform root = GetSpawnedBuildingRoot(renderer);
            if (root == null)
            {
                continue;
            }

            if (!grouped.TryGetValue(root, out List<Renderer> renderers))
            {
                renderers = new List<Renderer>();
                grouped[root] = renderers;
            }

            renderers.Add(renderer);
        }

        foreach (KeyValuePair<Transform, List<Renderer>> entry in grouped)
        {
            Transform root = entry.Key;
            List<Renderer> renderers = entry.Value;
            if (root == null || renderers == null || renderers.Count == 0)
            {
                continue;
            }

            if (!TryBuildTarget(root, renderers, out SpawnedBuildingTarget target))
            {
                continue;
            }

            cachedBuildings.Add(target);
            if (verboseDebugLogs)
            {
                Debug.Log($"Detected building: {target.root.name}");
            }
            if (verboseDebugLogs && logAcceptedBuildingRoots)
            {
                Debug.Log(
                    $"Accepted building root: {root.name} | Center: {target.bounds.center} | Size: {target.bounds.size}"
                );
            }
        }
        if (verboseDebugLogs)
        {
            Debug.Log($"Cached buildings = {cachedBuildings.Count}");
        }

    }
    void SelectRandomTargets(int amount)
    {
        activeTargets.Clear();

        List<SpawnedBuildingTarget> candidates =
            new List<SpawnedBuildingTarget>(cachedBuildings);

        while (candidates.Count > 0 &&
               activeTargets.Count < amount)
        {
            int index = UnityEngine.Random.Range(0, candidates.Count);

            activeTargets.Add(candidates[index]);

            candidates.RemoveAt(index);
        }

        totalTargets = activeTargets.Count;
        RefreshDeliveryRings();
        RefreshTargetBeacons();
        TargetsRemainingChanged?.Invoke(RemainingTargets);

        Debug.Log($"Selected {totalTargets} delivery buildings.");

        if (verboseDebugLogs)
        {
            foreach (var target in activeTargets)
            {
                Debug.Log($"Delivery Target: {target.root.name}");
            }
        }
    }
    Transform GetSpawnedBuildingRoot(Renderer renderer)
    {
        if (renderer == null)
            return null;

        Transform t = renderer.transform;

        while (t != null)
        {
            bool generatedBuildingName =
                t.name.EndsWith("_ArcGISBuilding", StringComparison.OrdinalIgnoreCase) ||
                t.name.IndexOf("_ArcGISBuilding_", StringComparison.OrdinalIgnoreCase) >= 0;

            bool hasArcGISLocation =
                t.GetComponent<ArcGISLocationComponent>() != null;

            bool hasHouseMarker =
                t.GetComponent<House>() != null;

            // SceneLoaderArcgis names roots like Prefab_ArcGISBuilding_3.
            // The previous EndsWith check rejected every numbered root.
            if (hasArcGISLocation && (generatedBuildingName || hasHouseMarker))
            {
                return t;
            }

            t = t.parent;
        }

        return null;
    }

    bool TryBuildTarget(Transform root, List<Renderer> renderers, out SpawnedBuildingTarget target)
    {
        target = null;

        if (root == null || renderers == null || renderers.Count == 0)
        {
            return false;
        }

        bool hasBounds = false;
        Bounds combinedBounds = new Bounds(Vector3.zero, Vector3.zero);

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            if (!hasBounds)
            {
                combinedBounds = bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(bounds);
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        if (combinedBounds.size.y < minBuildingHeight)
        {
            return false;
        }

        if (Mathf.Max(combinedBounds.size.x, combinedBounds.size.z) < minBuildingFootprint)
        {
            return false;
        }

        Transform deliveryMarker = FindChildRecursive(
            root,
            "DeliveryTarget"
        );

        Vector3 roofTarget = deliveryMarker != null
            ? deliveryMarker.position
            : new Vector3(
                combinedBounds.center.x,
                combinedBounds.max.y,
                combinedBounds.center.z
            );

        target = new SpawnedBuildingTarget
        {
            root = root,
            representativeRenderer = renderers[0],
            bounds = combinedBounds,
            roofTarget = roofTarget,
            key = MakeBuildingKey(roofTarget)
        };

        if (verboseDebugLogs && deliveryMarker != null)
        {
            Debug.Log(
                $"Using explicit DeliveryTarget for {root.name}: " +
                $"{roofTarget}"
            );
        }

        return true;
    }

    Transform FindChildRecursive(
        Transform parent,
        string childName)
    {
        if (parent == null ||
            string.IsNullOrWhiteSpace(childName))
        {
            return null;
        }

        if (parent.name.Equals(
                childName,
                StringComparison.OrdinalIgnoreCase))
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform result = FindChildRecursive(
                parent.GetChild(i),
                childName
            );

            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    bool IsDroneWithinDeliveryRadius(SpawnedBuildingTarget target)
    {
        if (target == null || drone == null)
            return false;

        float horizontalDistance = Vector2.Distance(
            new Vector2(drone.position.x, drone.position.z),
            new Vector2(target.roofTarget.x, target.roofTarget.z)
        );

        return horizontalDistance <= deliveryRadius;
    }

    void EvaluateTargets(out SpawnedBuildingTarget nearestTarget, out SpawnedBuildingTarget deliverableTarget)
    {
        nearestTarget = null;
        deliverableTarget = null;

        if (cachedBuildings.Count == 0 || drone == null)
        {
            return;
        }

        Vector3 dronePos = drone.position;
        float nearestHorizontalDistance = float.MaxValue;
        float nearestDeliverableHeight = float.MaxValue;

        for (int i = 0; i < activeTargets.Count; i++)
        {
            SpawnedBuildingTarget target = activeTargets[i];
            if (target == null || target.root == null)
            {
                continue;
            }

            float horizontalDistance = Vector2.Distance(
                new Vector2(dronePos.x, dronePos.z),
                new Vector2(target.roofTarget.x, target.roofTarget.z)
            );
            if (verboseDebugLogs)
            {
                Debug.Log($"Drone = {drone.position}");
                Debug.Log($"Roof  = {target.roofTarget}");
            }
            float heightAboveRoof = dronePos.y - target.roofTarget.y;

            if (verboseDebugLogs && logCandidateBuildings)
            {
                Debug.Log(
                    $"Candidate root: {target.root.name} | Center: {target.bounds.center} | Roof: {target.roofTarget} | " +
                    $"HorizontalDistance: {horizontalDistance:F2} | HeightAboveRoof: {heightAboveRoof:F2}"
                );
            }

            if (horizontalDistance < nearestHorizontalDistance)
            {
                nearestHorizontalDistance = horizontalDistance;
                nearestTarget = target;
            }
            if (!IsDroneWithinDeliveryRadius(target))
            {
                continue;
            }

            if (heightAboveRoof < 0f)
            {
                continue;
            }

            if (heightAboveRoof > maxHeightAboveRoof)
            {
                continue;
            }

            if (heightAboveRoof < nearestDeliverableHeight)
            {
                nearestDeliverableHeight = heightAboveRoof;
                deliverableTarget = target;
            }
        }

        if (verboseDebugLogs)
        {
            if (deliverableTarget != null)
            {
                Debug.Log(
                    $"Delivery candidate selected: {deliverableTarget.root.name} | Roof: {deliverableTarget.roofTarget}"
                );
            }

            Debug.Log(nearestTarget == null
                ? "No nearest building found."
                : $"Nearest = {nearestTarget.root.name}");
        }
    }

    void RefreshDeliveryRings()
    {
        RemoveUnusedDeliveryRings();
        UpdateAllDeliveryRings(null);
    }

    void UpdateAllDeliveryRings(SpawnedBuildingTarget deliverableTarget)
    {
        if (!showSpawnRadius)
        {
            SetAllDeliveryRingsActive(false);
            return;
        }

        RemoveUnusedDeliveryRings();

        for (int i = 0; i < activeTargets.Count; i++)
        {
            SpawnedBuildingTarget target = activeTargets[i];
            if (target == null || target.root == null)
            {
                continue;
            }

            LineRenderer ring = EnsureDeliveryRing(target);
            if (ring == null)
            {
                continue;
            }

            GameObject ringObject = deliveryRingObjects[target.key];
            ringObject.SetActive(true);

            bool isDeliverable = target == deliverableTarget;
            Color ringColor = isDeliverable ? deliveryRingReadyColor : deliveryRingSearchColor;

            ring.loop = true;
            ring.useWorldSpace = true;
            ring.widthMultiplier = deliveryRingWidth;
            ring.positionCount = Mathf.Max(12, deliveryRingSegments);
            ring.startColor = ringColor;
            ring.endColor = ringColor;

            Vector3 center = target.roofTarget + Vector3.up * deliveryRingVerticalOffset;
            int segmentCount = ring.positionCount;

            for (int segment = 0; segment < segmentCount; segment++)
            {
                float angle = (Mathf.PI * 2f * segment) / segmentCount;
                Vector3 point = center +
                    new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * deliveryRadius;
                ring.SetPosition(segment, point);
            }
        }
    }

    LineRenderer EnsureDeliveryRing(SpawnedBuildingTarget target)
    {
        if (target == null || string.IsNullOrEmpty(target.key))
        {
            return null;
        }

        if (deliveryRings.TryGetValue(target.key, out LineRenderer existingRing) &&
            existingRing != null)
        {
            return existingRing;
        }

        GameObject ringObject = new GameObject($"DeliveryRing_{target.key}");
        ringObject.transform.SetParent(transform, false);
        // Keep normal scene flags. Some Unity/ArcGIS editor combinations are unstable
        // when runtime LineRenderer objects use DontSave flags.

        LineRenderer ring = ringObject.AddComponent<LineRenderer>();
        ring.loop = true;
        ring.useWorldSpace = true;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.alignment = LineAlignment.View;
        ring.numCornerVertices = 4;
        ring.numCapVertices = 4;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader != null)
        {
            ring.material = new Material(shader);
        }

        deliveryRings[target.key] = ring;
        deliveryRingObjects[target.key] = ringObject;
        return ring;
    }

    void RemoveUnusedDeliveryRings()
    {
        HashSet<string> activeKeys = new HashSet<string>();
        for (int i = 0; i < activeTargets.Count; i++)
        {
            if (activeTargets[i] != null)
            {
                activeKeys.Add(activeTargets[i].key);
            }
        }

        List<string> keysToRemove = new List<string>();
        foreach (KeyValuePair<string, GameObject> pair in deliveryRingObjects)
        {
            if (!activeKeys.Contains(pair.Key))
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value);
                }
                keysToRemove.Add(pair.Key);
            }
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            deliveryRingObjects.Remove(keysToRemove[i]);
            deliveryRings.Remove(keysToRemove[i]);
        }
    }

    void RemoveDeliveryRing(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (deliveryRingObjects.TryGetValue(key, out GameObject ringObject) &&
            ringObject != null)
        {
            Destroy(ringObject);
        }

        deliveryRingObjects.Remove(key);
        deliveryRings.Remove(key);
    }

    void SetAllDeliveryRingsActive(bool isActive)
    {
        foreach (GameObject ringObject in deliveryRingObjects.Values)
        {
            if (ringObject != null)
            {
                ringObject.SetActive(isActive);
            }
        }
    }

    void CleanupLegacyDeliveryRings()
    {
        GameObject[] sceneObjects = FindObjectsByType<GameObject>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < sceneObjects.Length; i++)
        {
            GameObject sceneObject = sceneObjects[i];
            if (sceneObject == null || sceneObject == gameObject)
            {
                continue;
            }

            if (sceneObject.name == "DeliveryRing" || sceneObject.name.StartsWith("DeliveryRing_"))
            {
                Destroy(sceneObject);
            }
        }
    }


    void RefreshTargetBeacons()
    {
        RemoveUnusedTargetBeacons();

        for (int i = 0; i < activeTargets.Count; i++)
        {
            SpawnedBuildingTarget target = activeTargets[i];
            if (target == null || target.root == null || targetBeacons.ContainsKey(target.key))
            {
                continue;
            }

            Vector3 position = target.roofTarget + targetBeaconOffset;
            GameObject beacon;

            if (targetBeaconPrefab != null)
            {
                beacon = Instantiate(targetBeaconPrefab, position, Quaternion.identity, transform);
            }
            else
            {
                beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                beacon.transform.SetParent(transform, true);
                beacon.transform.position = position;

                Collider collider = beacon.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                Renderer renderer = beacon.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null) shader = Shader.Find("Unlit/Color");
                    if (shader == null) shader = Shader.Find("Sprites/Default");

                    if (shader != null)
                    {
                        Material material = new Material(shader);
                        material.color = targetBeaconColor;
                        renderer.material = material;
                    }
                }
            }

            beacon.name = $"DeliveryBeacon_{target.key}";
            beacon.transform.localScale = targetBeaconScale;
            targetBeacons[target.key] = beacon;
        }
    }

    void RemoveUnusedTargetBeacons()
    {
        HashSet<string> activeKeys = new HashSet<string>();
        for (int i = 0; i < activeTargets.Count; i++)
        {
            if (activeTargets[i] != null)
            {
                activeKeys.Add(activeTargets[i].key);
            }
        }

        List<string> keysToRemove = new List<string>();
        foreach (KeyValuePair<string, GameObject> pair in targetBeacons)
        {
            if (!activeKeys.Contains(pair.Key))
            {
                if (pair.Value != null) Destroy(pair.Value);
                keysToRemove.Add(pair.Key);
            }
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            targetBeacons.Remove(keysToRemove[i]);
        }
    }

    void RemoveTargetBeacon(string key)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (targetBeacons.TryGetValue(key, out GameObject beacon) && beacon != null)
        {
            Destroy(beacon);
        }

        targetBeacons.Remove(key);
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || !showSpawnRadius)
        {
            return;
        }

        for (int i = 0; i < activeTargets.Count; i++)
        {
            SpawnedBuildingTarget target = activeTargets[i];
            if (target == null)
            {
                continue;
            }

            Gizmos.color = target == currentDeliverableTarget ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(target.roofTarget, deliveryRadius);
        }
    }

    void ProcessBuildingHover(SpawnedBuildingTarget building)
    {
        if (building == null)
        {
            return;
        }

        string key = building.key;

        if (deliveredBuildingKeys.Contains(key) ||
            deliveriesInProgressKeys.Contains(key))
        {
            return;
        }

        if (currentSpeed > maxHoverSpeed)
        {
            hoverTimers[key] = 0f;
            SetDeliveryStatus("Hold position...");
            return;
        }

        if (hoverTimers.Count > 1 ||
            (hoverTimers.Count == 1 && !hoverTimers.ContainsKey(key)))
        {
            hoverTimers.Clear();
        }

        if (!hoverTimers.ContainsKey(key))
        {
            hoverTimers[key] = 0f;
        }

        hoverTimers[key] += Time.deltaTime;

        if (hoverTimers[key] < hoverSecondsRequired)
        {
            float remainingSeconds =
                Mathf.Max(0f, hoverSecondsRequired - hoverTimers[key]);

            SetDeliveryStatus(
                $"{progressMessage} {remainingSeconds:0.0}s"
            );

            return;
        }

        // Reserve and remove the target immediately so Update cannot start
        // another delivery while the present animation is running.
        deliveriesInProgressKeys.Add(key);
        hoverTimers.Remove(key);
        activeTargets.Remove(building);

        RemoveDeliveryRing(key);
        RemoveTargetBeacon(key);
        TargetsRemainingChanged?.Invoke(RemainingTargets);

        StartCoroutine(CompleteDeliverySequence(building));
    }

    IEnumerator CompleteDeliverySequence(
        SpawnedBuildingTarget building)
    {
        if (building == null)
        {
            yield break;
        }

        string key = building.key;

        SetDeliveryStatus(droppingPresentMessage);

        bool hasPresentPrefab = HasValidPresentPrefab();

        if (hasPresentPrefab)
        {
            yield return PlayPresentDrop(building);
        }
        else
        {
            Debug.LogWarning(
                "Delivery completed without an animation because " +
                "no Present Prefab is assigned."
            );
        }

        FinalizeDelivery(building);

        deliveriesInProgressKeys.Remove(key);
    }

    bool HasValidPresentPrefab()
    {
        if (presentPrefabs == null || presentPrefabs.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < presentPrefabs.Length; i++)
        {
            if (presentPrefabs[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    void FinalizeDelivery(SpawnedBuildingTarget building)
    {
        if (building == null)
        {
            return;
        }

        string key = building.key;

        if (deliveredBuildingKeys.Contains(key))
        {
            return;
        }

        score += rewardPerDelivery;
        deliveredBuildingKeys.Add(key);
        completedDeliveries++;

        UpdateScoreUI();
        ScoreChanged?.Invoke(score);

        SetDeliveryStatus(
            $"{completeMessage} +{rewardPerDelivery}",
            completeMessageDuration
        );

        Debug.Log(
            $"DELIVERY TARGET = {building.root.name} | " +
            $"Center: {building.bounds.center} | " +
            $"Key: {key}"
        );

        if (hideDeliveredBuilding &&
            building.root != null)
        {
            StartCoroutine(
                HideDeliveredBuildingAfterDelay(building.root)
            );
        }

        if (!level1Completed &&
            completedDeliveries >= Mathf.Max(1, totalTargets))
        {
            level1Completed = true;

            SetDeliveryStatus(
                level1CompleteMessage,
                Mathf.Max(completeMessageDuration, 3f)
            );

            LevelCompleted?.Invoke();
            Debug.Log("✅ Level 1 Complete");
        }
    }

    IEnumerator HideDeliveredBuildingAfterDelay(
        Transform buildingRoot)
    {
        if (buildingRoot == null)
        {
            yield break;
        }

        if (hideDeliveredBuildingDelay > 0f)
        {
            yield return new WaitForSeconds(
                hideDeliveredBuildingDelay
            );
        }

        if (buildingRoot != null)
        {
            buildingRoot.gameObject.SetActive(false);
        }
    }

    IEnumerator PlayPresentDrop(
        SpawnedBuildingTarget building)
    {
        if (drone == null ||
            building == null ||
            presentPrefabs == null ||
            presentPrefabs.Length == 0)
        {
            yield break;
        }

        List<GameObject> validPrefabs = new List<GameObject>();

        for (int i = 0; i < presentPrefabs.Length; i++)
        {
            if (presentPrefabs[i] != null)
            {
                validPrefabs.Add(presentPrefabs[i]);
            }
        }

        if (validPrefabs.Count == 0)
        {
            yield break;
        }

        GameObject selectedPrefab =
            validPrefabs[
                UnityEngine.Random.Range(0, validPrefabs.Count)
            ];

        Vector3 startPosition =
            drone.TransformPoint(presentSpawnOffset);

        Vector3 endPosition =
            building.roofTarget +
            Vector3.up * presentLandingHeight;

        GameObject present = Instantiate(
            selectedPrefab,
            startPosition,
            UnityEngine.Random.rotation
        );

        present.name = selectedPrefab.name + "_Dropped";
        present.transform.localScale *= presentScaleMultiplier;

        DisablePresentPhysicsIfRequired(present);

        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, presentDropDuration);

        while (elapsed < duration && present != null)
        {
            elapsed += Time.deltaTime;

            float normalizedTime =
                Mathf.Clamp01(elapsed / duration);

            float movementTime =
                presentDropCurve != null
                    ? Mathf.Clamp01(
                        presentDropCurve.Evaluate(normalizedTime)
                    )
                    : Mathf.SmoothStep(
                        0f,
                        1f,
                        normalizedTime
                    );

            Vector3 position = Vector3.Lerp(
                startPosition,
                endPosition,
                movementTime
            );

            position.y +=
                Mathf.Sin(normalizedTime * Mathf.PI) *
                presentDropArcHeight;

            present.transform.position = position;

            present.transform.Rotate(
                presentSpinDegreesPerSecond *
                Time.deltaTime,
                Space.Self
            );

            yield return null;
        }

        if (present == null)
        {
            yield break;
        }

        present.transform.position = endPosition;

        // Small bounce to make the landing easier to see.
        float bounceDuration =
            Mathf.Max(0f, presentLandingBounceDuration);

        if (bounceDuration > 0f &&
            presentLandingBounceHeight > 0f)
        {
            float bounceElapsed = 0f;

            while (bounceElapsed < bounceDuration &&
                   present != null)
            {
                bounceElapsed += Time.deltaTime;

                float t = Mathf.Clamp01(
                    bounceElapsed / bounceDuration
                );

                float bounceOffset =
                    Mathf.Sin(t * Mathf.PI) *
                    presentLandingBounceHeight;

                present.transform.position =
                    endPosition +
                    Vector3.up * bounceOffset;

                yield return null;
            }
        }

        if (present == null)
        {
            yield break;
        }

        present.transform.position = endPosition;

        if (parentPresentToBuildingAfterLanding &&
            building.root != null)
        {
            present.transform.SetParent(
                building.root,
                true
            );
        }

        if (presentLandingEffectPrefab != null)
        {
            GameObject landingEffect = Instantiate(
                presentLandingEffectPrefab,
                endPosition,
                Quaternion.identity
            );

            Destroy(landingEffect, 3f);
        }

        if (presentStayDuration > 0f)
        {
            yield return new WaitForSeconds(
                presentStayDuration
            );

            if (present != null)
            {
                Destroy(present);
            }
        }
    }

    void DisablePresentPhysicsIfRequired(
        GameObject present)
    {
        if (!disablePresentPhysics || present == null)
        {
            return;
        }

        Collider[] colliders =
            present.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        Rigidbody[] rigidbodies =
            present.GetComponentsInChildren<Rigidbody>(true);

        for (int i = 0; i < rigidbodies.Length; i++)
        {
            rigidbodies[i].isKinematic = true;
            rigidbodies[i].useGravity = false;
            rigidbodies[i].linearVelocity = Vector3.zero;
            rigidbodies[i].angularVelocity = Vector3.zero;
        }
    }

    string MakeBuildingKey(Vector3 worldCenter)
    {
        int x = Mathf.RoundToInt(worldCenter.x * 10f);
        int z = Mathf.RoundToInt(worldCenter.z * 10f);
        return $"{x}:{z}";
    }

    void UpdateScoreUI()
    {
        if (scoreText != null)
        {
            scoreText.text = "Score: " + score;
        }
    }
}
