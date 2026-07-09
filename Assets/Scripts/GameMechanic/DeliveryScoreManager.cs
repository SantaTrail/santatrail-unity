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
    [Tooltip("Horizontal radius around the selected roof center where a present can be delivered when roof-footprint checking is disabled.")]
    [Min(0.1f)] public float deliveryRadius = 5f;
    [Tooltip("Require the drone to be horizontally above the detected roof before delivery starts. This keeps the gift directly below the drone.")]
    public bool requireDroneAboveRoofForDelivery = true;
    [Tooltip("Extra horizontal tolerance around the detected roof footprint.")]
    [Min(0f)] public float deliveryRoofPadding = 0.35f;
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

    [Header("Delivery Chimney")]
    [Tooltip("Add all chimney prefabs here. Every selected house receives one random chimney.")]
    public GameObject[] chimneyPrefabs;
    [Tooltip("Optional child object inside a chimney prefab that marks the opening. Name it PresentDropPoint.")]
    public string chimneyDropPointName = "PresentDropPoint";
    [Tooltip("Place the chimney at a slightly different roof position for every target house.")]
    public bool randomizeChimneyPosition = true;
    [Range(0f, 0.45f)]
    [Tooltip("How far the chimney may move away from the roof center, as a fraction of the usable roof half-size.")]
    public float chimneyRandomPositionRange = 0.25f;
    [Min(0f)]
    [Tooltip("Keeps the chimney away from the detected roof bounds edges.")]
    public float chimneyRoofEdgeInset = 0.6f;
    [Tooltip("Moves the chimney slightly into the roof so it does not appear to float.")]
    public float chimneyRoofSink = 0.08f;
    [Tooltip("Use downward raycasts to find and validate a real roof surface before placing the chimney.")]
    public bool useRoofRaycastForChimney = true;
    [Tooltip("Layers that may be detected as a roof by the chimney placement raycast.")]
    public LayerMask chimneyRoofRaycastMask = ~0;
    [Min(0.1f)]
    public float chimneyRoofRaycastExtraHeight = 5f;
    [Min(1)]
    [Tooltip("How many random roof positions are tested before using a safe fallback position.")]
    public int chimneyPlacementAttempts = 24;
    [Min(0f)]
    [Tooltip("Checks the chimney center and four surrounding points. Increase this for wider chimney prefabs.")]
    public float chimneyFootprintCheckRadius = 0.28f;
    [Range(0f, 1f)]
    [Tooltip("Rejects wall or nearly vertical collider surfaces. Sloped roofs usually work well around 0.2 to 0.35.")]
    public float chimneyMinimumRoofNormalY = 0.2f;
    [Min(0f)]
    [Tooltip("Maximum allowed roof-height difference across the chimney footprint. Increase slightly for steep roofs.")]
    public float chimneyMaxFootprintHeightDifference = 0.5f;
    [Tooltip("Extra rotation applied after matching the building's Y rotation.")]
    public Vector3 chimneyRotationOffset = Vector3.zero;
    [Min(0.01f)]
    public float chimneyScaleMultiplier = 1f;
    [Min(0f)]
    [Tooltip("Automatically resize each chimney to this visible world height. Set to 0 to keep each prefab's original height.")]
    public float chimneyTargetWorldHeight = 1.6f;
    [Min(0.05f)]
    [Tooltip("Fallback opening width used when the prefab has no PresentDropPoint child.")]
    public float defaultChimneyOpeningWidth = 0.8f;
    [Tooltip("Extra height above the detected or authored chimney opening.")]
    public float chimneyOpeningHeightOffset = 0.02f;
    [Min(0.1f)]
    [Tooltip("The drone must hover this close horizontally to the chimney opening.")]
    public float chimneyDeliveryRadius = 0.75f;
    [Tooltip("Creates a simple square chimney when no chimney prefab is assigned.")]
    public bool createFallbackChimney = true;
    public Vector3 fallbackChimneySize = new Vector3(0.9f, 1.5f, 0.9f);
    public Color fallbackChimneyColor = new Color(0.35f, 0.08f, 0.05f, 1f);
    [Tooltip("Keep the chimney as part of the house after the delivery is complete.")]
    public bool keepChimneyAfterDelivery = true;

    [Header("UI")]
    public string progressMessage = "Sending present...";
    public string completeMessage = "Present send complete!";
    public string level1CompleteMessage = "Level 1 Complete!";
    public float completeMessageDuration = 2f;


    [Header("Present Drop Animation")]
    [Tooltip("Add all present prefabs here. One random prefab is selected for each delivery.")]
    public GameObject[] presentPrefabs;
    [Tooltip("Offset from the drone where the present is released. Only the drone's Y rotation is used, so roll and pitch cannot throw the present sideways.")]
    public Vector3 presentSpawnOffset = new Vector3(0f, -0.5f, 0f);
    [Tooltip("Height above the target roof where the present finishes.")]
    [Min(0f)] public float presentLandingHeight = 0.25f;
    [Tooltip("Minimum vertical distance between the release point and roof. This prevents the present spawning inside the roof.")]
    [Min(0.05f)] public float presentMinimumDropHeight = 0.5f;
    [Tooltip("Time taken for the present to fall vertically from the drone to the roof.")]
    [Min(0.05f)] public float presentDropDuration = 0.8f;
    [Tooltip("Keeps the landing point slightly inside the detected roof bounds.")]
    [Min(0f)] public float presentRoofEdgeInset = 0.15f;
    [Tooltip("Automatically resize each prefab so its largest visible dimension matches this world size. Set to 0 to keep the prefab's original size.")]
    [Min(0f)] public float presentTargetWorldSize = 1.2f;
    [Tooltip("Keep the box upright while it falls. Recommended for gift-box prefabs.")]
    public bool keepPresentUpright = true;
    [Tooltip("How quickly the present spins while falling. For an upright gift, normally use only the Y value.")]
    public Vector3 presentSpinDegreesPerSecond = new Vector3(0f, 90f, 0f);
    [Tooltip("Small upward bounce after touching the roof. Set to 0 to disable.")]
    [Min(0f)] public float presentLandingBounceHeight = 0.06f;
    [Tooltip("Duration of the landing bounce.")]
    [Min(0f)] public float presentLandingBounceDuration = 0.22f;
    [Tooltip("Temporary squash amount when the gift lands.")]
    [Range(0f, 0.4f)] public float presentLandingSquash = 0.08f;
    [Tooltip("How long the present remains on the roof before disappearing. Set to 0 to keep it.")]
    [Min(0f)] public float presentStayDuration = 2f;
    [Tooltip("Scale multiplier applied to the selected present prefab.")]
    [Min(0.01f)] public float presentScaleMultiplier = 1.25f;
    [Tooltip("Prevent prefab colliders and rigidbodies from affecting the drone/building during animation.")]
    public bool disablePresentPhysics = true;

    [Header("Present Into Chimney")]
    [Tooltip("Resize the present so it fits through the chimney opening.")]
    public bool fitPresentToChimneyOpening = true;
    [Min(0f)]
    [Tooltip("Small vertical gap before the present begins entering the chimney.")]
    public float presentAboveChimneyOpening = 0.02f;
    [Range(0.1f, 1f)]
    [Tooltip("How much of the chimney opening width the present may use.")]
    public float presentOpeningFill = 0.65f;
    [Min(0.05f)]
    [Tooltip("How long the present pauses at the opening before entering.")]
    public float presentOpeningPause = 0.08f;
    [Min(0.05f)]
    [Tooltip("How long the present takes to disappear down the chimney.")]
    public float presentChimneyEntryDuration = 0.28f;
    [Min(0.05f)]
    [Tooltip("How far below the chimney opening the present moves.")]
    public float presentChimneyEntryDepth = 0.9f;
    [Range(0.01f, 1f)]
    [Tooltip("Final scale while the present disappears inside the chimney.")]
    public float presentInsideChimneyScale = 0.08f;

    [Header("Delivered Building")]
    [Tooltip("Hide the target building after the present is delivered.")]
    public bool hideDeliveredBuilding = true;
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
        public GameObject chimneyObject;
        public Transform chimneyDropPoint;
        public float chimneyOpeningWidth;
    }

    private readonly HashSet<string> deliveredBuildingKeys = new HashSet<string>();
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
    private readonly Dictionary<string, GameObject> targetChimneys = new Dictionary<string, GameObject>();
    private Vector3 lastDronePos;
    private float currentSpeed;

    void Start()
    {
        AutoBindDrone();
        CleanupLegacyDeliveryRings();
        CleanupLegacyTargetBeacons();
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
        RefreshTargetChimneys();
        RefreshDeliveryRings();
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

            bool generatedWorldModularName =
                t.name.IndexOf("ModularHouse_WorldBuilding_", StringComparison.OrdinalIgnoreCase) >= 0;

            // ArcGIS-anchored generated buildings use ArcGISLocationComponent.
            // House-marked world-space modular roots are also accepted as a
            // compatibility fallback for scenes created with an older version.
            if ((hasArcGISLocation && (generatedBuildingName || hasHouseMarker)) ||
                (hasHouseMarker && (generatedBuildingName || generatedWorldModularName)))
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

        target = new SpawnedBuildingTarget
        {
            root = root,
            representativeRenderer = renderers[0],
            bounds = combinedBounds,
            roofTarget = new Vector3(combinedBounds.center.x, combinedBounds.max.y, combinedBounds.center.z),
            key = MakeBuildingKey(combinedBounds.center)
        };

        return true;
    }

    Vector3 GetDeliveryPoint(SpawnedBuildingTarget target)
    {
        if (target == null)
        {
            return Vector3.zero;
        }

        if (target.chimneyDropPoint != null)
        {
            return target.chimneyDropPoint.position +
                Vector3.up * chimneyOpeningHeightOffset;
        }

        return target.roofTarget;
    }

    float GetTargetDeliveryRadius(SpawnedBuildingTarget target)
    {
        return target != null && target.chimneyDropPoint != null
            ? Mathf.Max(0.1f, chimneyDeliveryRadius)
            : Mathf.Max(0.1f, deliveryRadius);
    }

    bool IsDroneWithinDeliveryRadius(SpawnedBuildingTarget target)
    {
        if (target == null || drone == null)
        {
            return false;
        }

        Vector3 deliveryPoint = GetDeliveryPoint(target);
        float horizontalDistance = Vector2.Distance(
            new Vector2(drone.position.x, drone.position.z),
            new Vector2(deliveryPoint.x, deliveryPoint.z)
        );

        if (target.chimneyDropPoint != null)
        {
            return horizontalDistance <= GetTargetDeliveryRadius(target);
        }

        if (requireDroneAboveRoofForDelivery)
        {
            float padding = Mathf.Max(0f, deliveryRoofPadding);
            Vector3 dronePosition = drone.position;

            return
                dronePosition.x >= target.bounds.min.x - padding &&
                dronePosition.x <= target.bounds.max.x + padding &&
                dronePosition.z >= target.bounds.min.z - padding &&
                dronePosition.z <= target.bounds.max.z + padding;
        }

        return horizontalDistance <= GetTargetDeliveryRadius(target);
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

            Vector3 deliveryPoint = GetDeliveryPoint(target);
            float horizontalDistance = Vector2.Distance(
                new Vector2(dronePos.x, dronePos.z),
                new Vector2(deliveryPoint.x, deliveryPoint.z)
            );
            float heightAboveTarget = dronePos.y - deliveryPoint.y;

            if (verboseDebugLogs)
            {
                Debug.Log($"Drone = {drone.position}");
                Debug.Log($"Delivery point = {deliveryPoint}");
            }

            if (verboseDebugLogs && logCandidateBuildings)
            {
                Debug.Log(
                    $"Candidate root: {target.root.name} | Delivery point: {deliveryPoint} | " +
                    $"HorizontalDistance: {horizontalDistance:F2} | HeightAboveTarget: {heightAboveTarget:F2}"
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

            if (heightAboveTarget < 0f || heightAboveTarget > maxHeightAboveRoof)
            {
                continue;
            }

            if (heightAboveTarget < nearestDeliverableHeight)
            {
                nearestDeliverableHeight = heightAboveTarget;
                deliverableTarget = target;
            }
        }

        if (verboseDebugLogs)
        {
            if (deliverableTarget != null)
            {
                Debug.Log(
                    $"Delivery candidate selected: {deliverableTarget.root.name} | Point: {GetDeliveryPoint(deliverableTarget)}"
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

            Vector3 center = GetDeliveryPoint(target) + Vector3.up * deliveryRingVerticalOffset;
            float ringRadius = GetTargetDeliveryRadius(target);
            int segmentCount = ring.positionCount;

            for (int segment = 0; segment < segmentCount; segment++)
            {
                float angle = (Mathf.PI * 2f * segment) / segmentCount;
                Vector3 point = center +
                    new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ringRadius;
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


    void CleanupLegacyTargetBeacons()
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

            if (sceneObject.name == "DeliveryBeacon" ||
                sceneObject.name.StartsWith("DeliveryBeacon_"))
            {
                Destroy(sceneObject);
            }
        }
    }

    void RefreshTargetChimneys()
    {
        RemoveUnusedTargetChimneys();

        for (int i = 0; i < activeTargets.Count; i++)
        {
            SpawnedBuildingTarget target = activeTargets[i];
            if (target == null || target.root == null ||
                targetChimneys.ContainsKey(target.key))
            {
                continue;
            }

            GameObject chimney = CreateChimneyForTarget(target);
            if (chimney == null)
            {
                continue;
            }

            target.chimneyObject = chimney;
            targetChimneys[target.key] = chimney;

            Transform authoredDropPoint =
                FindChildRecursive(chimney.transform, chimneyDropPointName);

            if (authoredDropPoint != null)
            {
                target.chimneyDropPoint = authoredDropPoint;

                float authoredWidth = Mathf.Min(
                    Mathf.Abs(authoredDropPoint.lossyScale.x),
                    Mathf.Abs(authoredDropPoint.lossyScale.z)
                );

                target.chimneyOpeningWidth =
                    authoredWidth > 0.05f
                        ? authoredWidth
                        : Mathf.Max(0.05f, defaultChimneyOpeningWidth);
            }
            else
            {
                GameObject dropPointObject = new GameObject(chimneyDropPointName);
                dropPointObject.transform.SetParent(chimney.transform, true);

                if (TryGetCombinedRendererBounds(chimney, out Bounds chimneyBounds))
                {
                    dropPointObject.transform.position = new Vector3(
                        chimneyBounds.center.x,
                        chimneyBounds.max.y,
                        chimneyBounds.center.z
                    );
                }
                else
                {
                    dropPointObject.transform.position =
                        chimney.transform.position + Vector3.up;
                }

                target.chimneyDropPoint = dropPointObject.transform;
                target.chimneyOpeningWidth =
                    Mathf.Max(0.05f, defaultChimneyOpeningWidth);
            }
        }
    }

    GameObject CreateChimneyForTarget(SpawnedBuildingTarget target)
    {
        if (target == null || target.root == null)
        {
            return null;
        }

        Vector3 roofPosition = ChooseChimneyRoofPosition(target);
        Quaternion buildingYaw =
            Quaternion.Euler(0f, target.root.eulerAngles.y, 0f);
        Quaternion chimneyRotation =
            buildingYaw * Quaternion.Euler(chimneyRotationOffset);

        GameObject selectedPrefab = GetRandomValidPrefab(chimneyPrefabs);
        GameObject chimney;

        if (selectedPrefab != null)
        {
            chimney = Instantiate(
                selectedPrefab,
                roofPosition,
                chimneyRotation,
                transform
            );
        }
        else if (createFallbackChimney)
        {
            chimney = CreateFallbackChimney(
                roofPosition,
                chimneyRotation
            );
        }
        else
        {
            Debug.LogWarning(
                "No chimney prefab is assigned and fallback chimney creation is disabled."
            );
            return null;
        }

        chimney.name = $"DeliveryChimney_{target.key}";

        ResizeChimney(chimney);

        if (TryGetCombinedRendererBounds(chimney, out Bounds chimneyBounds))
        {
            float desiredBottomY =
                roofPosition.y - Mathf.Max(0f, chimneyRoofSink);

            chimney.transform.position += Vector3.up *
                (desiredBottomY - chimneyBounds.min.y);
        }
        else
        {
            chimney.transform.position = roofPosition;
        }

        return chimney;
    }

    Vector3 ChooseChimneyRoofPosition(SpawnedBuildingTarget target)
    {
        if (target == null)
        {
            return Vector3.zero;
        }

        Bounds roofBounds = target.bounds;
        float inset = Mathf.Max(0f, chimneyRoofEdgeInset);
        float usableHalfX = Mathf.Max(0f, roofBounds.extents.x - inset);
        float usableHalfZ = Mathf.Max(0f, roofBounds.extents.z - inset);

        // With raycast validation enabled, a candidate is accepted only when
        // the chimney center and its four corners all land on this building.
        // This prevents positions inside the building's world AABB but outside
        // the real roof, which happens often with rotated and L-shaped houses.
        if (useRoofRaycastForChimney)
        {
            int attempts = Mathf.Max(1, chimneyPlacementAttempts);
            float requestedRange = randomizeChimneyPosition
                ? Mathf.Clamp(chimneyRandomPositionRange, 0f, 0.45f)
                : 0f;

            for (int i = 0; i < attempts; i++)
            {
                float offsetX = requestedRange > 0f
                    ? UnityEngine.Random.Range(
                        -usableHalfX * requestedRange,
                        usableHalfX * requestedRange)
                    : 0f;
                float offsetZ = requestedRange > 0f
                    ? UnityEngine.Random.Range(
                        -usableHalfZ * requestedRange,
                        usableHalfZ * requestedRange)
                    : 0f;

                float candidateX = roofBounds.center.x + offsetX;
                float candidateZ = roofBounds.center.z + offsetZ;

                if (TryValidateChimneyRoofFootprint(
                    target,
                    candidateX,
                    candidateZ,
                    out float roofY))
                {
                    return new Vector3(candidateX, roofY, candidateZ);
                }
            }

            // The center can be outside an L-shaped roof, so after testing it,
            // search the complete inset bounds for any valid roof surface.
            if (TryValidateChimneyRoofFootprint(
                target,
                roofBounds.center.x,
                roofBounds.center.z,
                out float centerRoofY))
            {
                return new Vector3(
                    roofBounds.center.x,
                    centerRoofY,
                    roofBounds.center.z);
            }

            for (int i = 0; i < attempts; i++)
            {
                float candidateX = roofBounds.center.x +
                    UnityEngine.Random.Range(-usableHalfX, usableHalfX);
                float candidateZ = roofBounds.center.z +
                    UnityEngine.Random.Range(-usableHalfZ, usableHalfZ);

                if (TryValidateChimneyRoofFootprint(
                    target,
                    candidateX,
                    candidateZ,
                    out float roofY))
                {
                    return new Vector3(candidateX, roofY, candidateZ);
                }
            }
        }

        // Collider-free fallback: prefer an actual high roof renderer instead
        // of the combined bounds center of the complete building.
        if (TryGetRoofRendererFallbackPosition(target, out Vector3 fallbackPosition))
        {
            return fallbackPosition;
        }

        return target.roofTarget;
    }

    bool TryValidateChimneyRoofFootprint(
        SpawnedBuildingTarget target,
        float centerX,
        float centerZ,
        out float roofY)
    {
        roofY = target != null ? target.roofTarget.y : 0f;

        if (target == null || target.root == null)
        {
            return false;
        }

        float radius = Mathf.Max(0f, chimneyFootprintCheckRadius);
        Vector2[] sampleOffsets =
        {
            Vector2.zero,
            new Vector2(-radius, -radius),
            new Vector2(-radius, radius),
            new Vector2(radius, -radius),
            new Vector2(radius, radius)
        };

        float minimumY = float.PositiveInfinity;
        float maximumY = float.NegativeInfinity;
        float totalY = 0f;

        for (int i = 0; i < sampleOffsets.Length; i++)
        {
            float sampleX = centerX + sampleOffsets[i].x;
            float sampleZ = centerZ + sampleOffsets[i].y;

            if (!TryRaycastTargetRoof(target, sampleX, sampleZ, out RaycastHit hit))
            {
                return false;
            }

            minimumY = Mathf.Min(minimumY, hit.point.y);
            maximumY = Mathf.Max(maximumY, hit.point.y);
            totalY += hit.point.y;
        }

        if (maximumY - minimumY >
            Mathf.Max(0f, chimneyMaxFootprintHeightDifference))
        {
            return false;
        }

        roofY = totalY / sampleOffsets.Length;
        return true;
    }

    bool TryRaycastTargetRoof(
        SpawnedBuildingTarget target,
        float worldX,
        float worldZ,
        out RaycastHit validHit)
    {
        validHit = default;

        if (target == null || target.root == null)
        {
            return false;
        }

        Bounds bounds = target.bounds;
        float extraHeight = Mathf.Max(0.1f, chimneyRoofRaycastExtraHeight);
        Vector3 rayOrigin = new Vector3(
            worldX,
            bounds.max.y + extraHeight,
            worldZ);
        float rayDistance = bounds.size.y + extraHeight * 2f + 2f;

        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            rayDistance,
            chimneyRoofRaycastMask,
            QueryTriggerInteraction.Ignore);

        Array.Sort(
            hits,
            (first, second) => first.distance.CompareTo(second.distance));

        // Ignore low floors and terrain that happen to be inside the AABB.
        float minimumAcceptedRoofY =
            bounds.min.y + bounds.size.y * 0.45f;
        float minimumNormalY =
            Mathf.Clamp01(chimneyMinimumRoofNormalY);

        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == null)
            {
                continue;
            }

            bool belongsToTarget =
                hitTransform == target.root ||
                hitTransform.IsChildOf(target.root);

            if (!belongsToTarget)
            {
                continue;
            }

            if (hits[i].point.y < minimumAcceptedRoofY)
            {
                continue;
            }

            if (hits[i].normal.y < minimumNormalY)
            {
                continue;
            }

            validHit = hits[i];
            return true;
        }

        return false;
    }

    bool TryGetRoofRendererFallbackPosition(
        SpawnedBuildingTarget target,
        out Vector3 position)
    {
        position = target != null ? target.roofTarget : Vector3.zero;

        if (target == null || target.root == null)
        {
            return false;
        }

        Renderer[] renderers =
            target.root.GetComponentsInChildren<Renderer>(true);
        Renderer bestRenderer = null;
        float bestScore = float.NegativeInfinity;
        float highPartThreshold =
            target.bounds.max.y - Mathf.Max(0.25f, target.bounds.size.y * 0.4f);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            if (bounds.max.y < highPartThreshold)
            {
                continue;
            }

            // A roof normally has useful size on both horizontal axes.
            if (bounds.size.x < 0.2f || bounds.size.z < 0.2f)
            {
                continue;
            }

            bool namedRoof =
                renderer.name.IndexOf("roof", StringComparison.OrdinalIgnoreCase) >= 0 ||
                renderer.transform.parent != null &&
                renderer.transform.parent.name.IndexOf(
                    "roof",
                    StringComparison.OrdinalIgnoreCase) >= 0;

            float horizontalArea = bounds.size.x * bounds.size.z;
            float score = horizontalArea + (namedRoof ? 10000f : 0f);

            if (score > bestScore)
            {
                bestScore = score;
                bestRenderer = renderer;
            }
        }

        if (bestRenderer == null)
        {
            return false;
        }

        Bounds bestBounds = bestRenderer.bounds;
        position = new Vector3(
            bestBounds.center.x,
            bestBounds.max.y,
            bestBounds.center.z);
        return true;
    }

    GameObject GetRandomValidPrefab(GameObject[] prefabs)
    {
        if (prefabs == null || prefabs.Length == 0)
        {
            return null;
        }

        List<GameObject> validPrefabs = new List<GameObject>();
        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] != null)
            {
                validPrefabs.Add(prefabs[i]);
            }
        }

        if (validPrefabs.Count == 0)
        {
            return null;
        }

        return validPrefabs[
            UnityEngine.Random.Range(0, validPrefabs.Count)
        ];
    }

    GameObject CreateFallbackChimney(
        Vector3 position,
        Quaternion rotation)
    {
        GameObject chimney =
            GameObject.CreatePrimitive(PrimitiveType.Cube);
        chimney.transform.SetParent(transform, true);
        chimney.transform.position = position;
        chimney.transform.rotation = rotation;
        chimney.transform.localScale = fallbackChimneySize;

        Collider collider = chimney.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        Renderer renderer = chimney.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader != null)
            {
                Material material = new Material(shader);
                material.color = fallbackChimneyColor;
                renderer.material = material;
            }
        }

        return chimney;
    }

    void ResizeChimney(GameObject chimney)
    {
        if (chimney == null)
        {
            return;
        }

        if (chimneyTargetWorldHeight > 0f &&
            TryGetCombinedRendererBounds(chimney, out Bounds visibleBounds) &&
            visibleBounds.size.y > 0.0001f)
        {
            float fitScale =
                chimneyTargetWorldHeight / visibleBounds.size.y;
            chimney.transform.localScale *= fitScale;
        }

        chimney.transform.localScale *=
            Mathf.Max(0.01f, chimneyScaleMultiplier);
    }

    Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        if (root.name.Equals(
            childName,
            StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found =
                FindChildRecursive(root.GetChild(i), childName);

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    void RemoveUnusedTargetChimneys()
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
        foreach (KeyValuePair<string, GameObject> pair in targetChimneys)
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
            ClearTargetChimneyReference(keysToRemove[i]);
            targetChimneys.Remove(keysToRemove[i]);
        }
    }

    void RemoveTargetChimney(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (targetChimneys.TryGetValue(
            key,
            out GameObject chimney) &&
            chimney != null)
        {
            Destroy(chimney);
        }

        ClearTargetChimneyReference(key);
        targetChimneys.Remove(key);
    }

    void FinishTargetChimney(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (keepChimneyAfterDelivery)
        {
            if (targetChimneys.TryGetValue(
                key,
                out GameObject chimney) &&
                chimney != null)
            {
                chimney.name = $"HouseChimney_{key}";
            }

            targetChimneys.Remove(key);
            ClearTargetChimneyReference(key);
        }
        else
        {
            RemoveTargetChimney(key);
        }
    }

    void ClearTargetChimneyReference(string key)
    {
        for (int i = 0; i < cachedBuildings.Count; i++)
        {
            SpawnedBuildingTarget target = cachedBuildings[i];
            if (target != null && target.key == key)
            {
                target.chimneyObject = null;
                target.chimneyDropPoint = null;
                target.chimneyOpeningWidth = 0f;
                return;
            }
        }
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
            Gizmos.DrawWireSphere(
                GetDeliveryPoint(target),
                GetTargetDeliveryRadius(target)
            );
        }
    }

    void ProcessBuildingHover(SpawnedBuildingTarget building)
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

        if (currentSpeed > maxHoverSpeed)
        {
            hoverTimers[key] = 0f;

            SetDeliveryStatus("Hold position...");

            return;
        }

        if (hoverTimers.Count > 1 || (hoverTimers.Count == 1 && !hoverTimers.ContainsKey(key)))
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
            float remain = Mathf.Max(0f, hoverSecondsRequired - hoverTimers[key]);
            SetDeliveryStatus($"{progressMessage} {remain:0.0}s");
            return;
        }

        bool hasPresentPrefab = false;
        if (presentPrefabs != null)
        {
            for (int i = 0; i < presentPrefabs.Length; i++)
            {
                if (presentPrefabs[i] != null)
                {
                    hasPresentPrefab = true;
                    break;
                }
            }
        }

        if (hasPresentPrefab)
        {
            StartCoroutine(PlayPresentDrop(building));
        }
        else
        {
            FinishTargetChimney(building.key);
        }

        score += rewardPerDelivery;
        deliveredBuildingKeys.Add(key);
        completedDeliveries++;
        hoverTimers.Remove(key);
        activeTargets.Remove(building);
        RemoveDeliveryRing(key);
        TargetsRemainingChanged?.Invoke(RemainingTargets);
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


        if (!level1Completed && completedDeliveries >= Mathf.Max(1, totalTargets))
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


    IEnumerator PlayPresentDrop(SpawnedBuildingTarget building)
    {
        if (building == null || drone == null)
        {
            if (building != null)
            {
                FinishTargetChimney(building.key);
            }

            yield break;
        }

        GameObject selectedPrefab =
            GetRandomValidPrefab(presentPrefabs);

        if (selectedPrefab == null)
        {
            FinishTargetChimney(building.key);
            yield break;
        }

        bool hasChimney = building.chimneyDropPoint != null;
        Vector3 deliveryPoint = GetDeliveryPoint(building);

        float releaseYaw = drone.eulerAngles.y;
        Quaternion droneYaw = Quaternion.Euler(0f, releaseYaw, 0f);
        Vector3 droneReleasePosition =
            drone.position + droneYaw * presentSpawnOffset;

        float landingOffset = hasChimney
            ? Mathf.Max(0f, presentAboveChimneyOpening)
            : Mathf.Max(0f, presentLandingHeight);

        Vector3 endPosition = deliveryPoint +
            Vector3.up * landingOffset;

        Vector3 startPosition = new Vector3(
            deliveryPoint.x,
            Mathf.Max(
                droneReleasePosition.y,
                endPosition.y + Mathf.Max(
                    0.05f,
                    presentMinimumDropHeight
                )
            ),
            deliveryPoint.z
        );

        Quaternion startRotation = keepPresentUpright
            ? Quaternion.Euler(0f, releaseYaw, 0f)
            : UnityEngine.Random.rotation;

        GameObject present = Instantiate(
            selectedPrefab,
            startPosition,
            startRotation
        );
        present.name = selectedPrefab.name + "_Dropped";

        ResizePresentForDrop(present, building);
        Vector3 normalScale = present.transform.localScale;

        if (disablePresentPhysics)
        {
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

        float elapsed = 0f;
        float dropDuration = Mathf.Max(0.05f, presentDropDuration);

        while (elapsed < dropDuration && present != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dropDuration);

            float fallT = t * t;
            float y = Mathf.Lerp(
                startPosition.y,
                endPosition.y,
                fallT
            );

            present.transform.position = new Vector3(
                deliveryPoint.x,
                y,
                deliveryPoint.z
            );

            RotatePresentDuringDrop(
                present,
                releaseYaw,
                elapsed
            );

            yield return null;
        }

        if (present == null)
        {
            FinishTargetChimney(building.key);
            yield break;
        }

        present.transform.position = endPosition;
        present.transform.localScale = normalScale;

        if (hasChimney)
        {
            float pause = Mathf.Max(0f, presentOpeningPause);
            if (pause > 0f)
            {
                yield return new WaitForSeconds(pause);
            }

            if (present == null)
            {
                FinishTargetChimney(building.key);
                yield break;
            }

            Vector3 entryStart = endPosition;
            Vector3 entryEnd =
                deliveryPoint -
                Vector3.up * Mathf.Max(
                    0.05f,
                    presentChimneyEntryDepth
                );

            float entryDuration =
                Mathf.Max(0.05f, presentChimneyEntryDuration);
            Vector3 insideScale =
                normalScale *
                Mathf.Clamp(
                    presentInsideChimneyScale,
                    0.01f,
                    1f
                );

            elapsed = 0f;
            while (elapsed < entryDuration && present != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(
                    elapsed / entryDuration
                );
                float enterT =
                    1f - Mathf.Pow(1f - t, 3f);

                present.transform.position = Vector3.Lerp(
                    entryStart,
                    entryEnd,
                    enterT
                );
                present.transform.localScale = Vector3.Lerp(
                    normalScale,
                    insideScale,
                    enterT
                );

                RotatePresentDuringDrop(
                    present,
                    releaseYaw,
                    dropDuration + elapsed
                );

                yield return null;
            }

            if (present != null)
            {
                Destroy(present);
            }

            FinishTargetChimney(building.key);
            yield break;
        }

        float bounceDuration =
            Mathf.Max(0f, presentLandingBounceDuration);

        if (bounceDuration > 0f &&
            (presentLandingBounceHeight > 0f ||
             presentLandingSquash > 0f))
        {
            elapsed = 0f;

            while (elapsed < bounceDuration && present != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(
                    elapsed / bounceDuration
                );
                float bounce =
                    Mathf.Sin(t * Mathf.PI) *
                    presentLandingBounceHeight;
                float squash =
                    (1f - t) * presentLandingSquash;

                present.transform.position =
                    endPosition + Vector3.up * bounce;
                present.transform.localScale = new Vector3(
                    normalScale.x * (1f + squash),
                    normalScale.y * (1f - squash),
                    normalScale.z * (1f + squash)
                );

                yield return null;
            }
        }

        if (present == null)
        {
            FinishTargetChimney(building.key);
            yield break;
        }

        present.transform.position = endPosition;
        present.transform.localScale = normalScale;

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

        FinishTargetChimney(building.key);
    }

    void RotatePresentDuringDrop(
        GameObject present,
        float releaseYaw,
        float elapsed)
    {
        if (present == null)
        {
            return;
        }

        if (keepPresentUpright)
        {
            float yaw =
                releaseYaw +
                presentSpinDegreesPerSecond.y * elapsed;

            present.transform.rotation =
                Quaternion.Euler(0f, yaw, 0f);
        }
        else
        {
            present.transform.Rotate(
                presentSpinDegreesPerSecond *
                Time.deltaTime,
                Space.Self
            );
        }
    }

    void ResizePresentForDrop(
        GameObject present,
        SpawnedBuildingTarget building)
    {
        if (present == null)
        {
            return;
        }

        float requestedWorldSize =
            Mathf.Max(0f, presentTargetWorldSize);

        if (fitPresentToChimneyOpening &&
            building != null &&
            building.chimneyDropPoint != null)
        {
            float openingWidth =
                building.chimneyOpeningWidth > 0.05f
                    ? building.chimneyOpeningWidth
                    : Mathf.Max(
                        0.05f,
                        defaultChimneyOpeningWidth
                    );

            requestedWorldSize =
                openingWidth *
                Mathf.Clamp(presentOpeningFill, 0.1f, 1f);
        }

        float scaleMultiplier =
            Mathf.Max(0.01f, presentScaleMultiplier);

        if (requestedWorldSize > 0f &&
            TryGetCombinedRendererBounds(
                present,
                out Bounds visibleBounds))
        {
            float largestDimension = Mathf.Max(
                visibleBounds.size.x,
                visibleBounds.size.y,
                visibleBounds.size.z
            );

            if (largestDimension > 0.0001f)
            {
                float fitScale =
                    requestedWorldSize / largestDimension;

                present.transform.localScale *=
                    fitScale * scaleMultiplier;
                return;
            }
        }

        present.transform.localScale *= scaleMultiplier;
    }

    bool TryGetCombinedRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = new Bounds();

        if (root == null)
        {
            return false;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool foundRenderer = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (!foundRenderer)
            {
                bounds = renderer.bounds;
                foundRenderer = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return foundRenderer;
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
