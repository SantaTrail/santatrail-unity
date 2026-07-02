using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Esri.ArcGISMapsSDK.Components;

public class DeliveryScoreManager : MonoBehaviour
{
    public event Action<int> ScoreChanged;

    [Header("References")]
    public Transform drone;
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI statusText;

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

    [Header("UI")]
    public string progressMessage = "Sending present...";
    public string completeMessage = "Present send complete!";
    public string level1CompleteMessage = "Level 1 Complete!";
    public float completeMessageDuration = 2f;

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
    private readonly Dictionary<string, float> hoverTimers = new Dictionary<string, float>();
    private readonly List<SpawnedBuildingTarget> cachedBuildings = new List<SpawnedBuildingTarget>();
    private readonly List<SpawnedBuildingTarget> activeTargets = new List<SpawnedBuildingTarget>();

    private int totalTargets = 0;
    public int RemainingTargets => activeTargets.Count;
    public int CompletedTargets => completedDeliveries;
    public int TotalTargets => totalTargets;
    private float nextScanTime = 0f;
    private string latestStatus = "";
    private float statusUntilTime = -1f;
    private int completedDeliveries = 0;
    private bool level1Completed = false;
    private MAVLinkReceiver mavReceiver;
    private SpawnedBuildingTarget currentNearestBuilding;
    private SpawnedBuildingTarget currentDeliverableTarget;
    private readonly Dictionary<string, LineRenderer> deliveryRings = new Dictionary<string, LineRenderer>();
    private readonly Dictionary<string, GameObject> deliveryRingObjects = new Dictionary<string, GameObject>();
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

        currentSpeed =
            Vector3.Distance(drone.position, lastDronePos) /
            Mathf.Max(Time.deltaTime, 0.0001f);
        lastDronePos = drone.position;

        if (farGuidedTargetActive)
        {
            if (hoverTimers.Count > 0)
            {
                hoverTimers.Clear();
            }

            if (statusText != null && latestStatus.StartsWith(progressMessage))
            {
                statusText.text = "";
                latestStatus = "";
            }

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

            if (statusText != null && latestStatus.StartsWith(progressMessage))
            {
                statusText.text = "";
                latestStatus = "";
            }
        }

        if (statusText != null && statusUntilTime > 0f && Time.time > statusUntilTime)
        {
            statusText.text = "";
            latestStatus = "";
            statusUntilTime = -1f;
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
            if (t.name.EndsWith("_ArcGISBuilding",
                StringComparison.OrdinalIgnoreCase))
            {
                if (t.GetComponent<ArcGISLocationComponent>() != null)
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

            if (sceneObject.name == "DeliveryRing")
            {
                Destroy(sceneObject);
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
        if (deliveredBuildingKeys.Contains(key))
        {
            return;
        }

        if (currentSpeed > maxHoverSpeed)
        {
            hoverTimers[key] = 0f;

            if (statusText != null)
            {
                statusText.text = "Hold position...";
            }

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
            latestStatus = $"{progressMessage} {remain:0.0}s";
            if (statusText != null)
            {
                statusText.text = latestStatus;
            }
            return;
        }

        score += rewardPerDelivery;
        deliveredBuildingKeys.Add(key);
        completedDeliveries++;
        hoverTimers.Remove(key);
        activeTargets.Remove(building);
        RemoveDeliveryRing(key);
        UpdateScoreUI();
        ScoreChanged?.Invoke(score);

        latestStatus = $"{completeMessage} +{rewardPerDelivery}";
        statusUntilTime = Time.time + completeMessageDuration;
        if (statusText != null)
        {
            statusText.text = latestStatus;
        }

        Debug.Log(
            $"DELIVERY TARGET = {building.root.name} | " +
            $"Center: {building.bounds.center} | " +
            $"Key: {key}"
        );

        if (!level1Completed && completedDeliveries >= Mathf.Max(1, totalTargets))
        {
            level1Completed = true;
            latestStatus = level1CompleteMessage;
            statusUntilTime = Time.time + Mathf.Max(completeMessageDuration, 3f);
            if (statusText != null)
            {
                statusText.text = latestStatus;
            }
            Debug.Log("✅ Level 1 Complete");
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
