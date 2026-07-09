using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Esri.ArcGISMapsSDK.Components;

/// <summary>
/// Delivery manager for building prefabs placed manually in a Unity scene.
/// It does not scan for or depend on SceneLoaderArcgis spawned building names.
/// </summary>
public class ManualDeliveryScoreManager : MonoBehaviour
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

    [Header("Manual Building Targets")]
    [Tooltip("Drag ManualDeliveryTarget components here. The order is also used when Target Limit is greater than zero.")]
    public List<ManualDeliveryTarget> manualTargets = new List<ManualDeliveryTarget>();

    [Tooltip("Also find every enabled ManualDeliveryTarget component in this scene automatically.")]
    public bool autoFindTargetsInScene = true;

    [Tooltip("0 uses all included targets. A value such as 5 uses only the first five targets in the list.")]
    [Min(0)] public int targetLimit = 0;

    [Tooltip("Small delay before target bounds are calculated. Useful while ArcGIS finishes positioning scene objects.")]
    [Min(0f)] public float initializationDelaySeconds = 0.25f;

    [Tooltip("Recalculate renderer bounds while playing so targets remain correct if the ArcGIS root moves.")]
    public bool refreshTargetGeometry = true;

    [Header("Delivery")]
    [Min(0.1f)] public float deliveryRadius = 5f;
    [Min(0.05f)] public float hoverSecondsRequired = 1.5f;
    public int rewardPerDelivery = 100;
    public float maxHoverSpeed = 100f;
    [Min(0.1f)] public float maxHeightAboveRoof = 3f;
    public float minBuildingHeight = 0.1f;
    public float minBuildingFootprint = 0.1f;
    public bool includeInactiveRenderers = false;
    public bool showSpawnRadius = true;
    public bool showDebugGizmos = false;
    public bool verboseDebugLogs = false;

    [Header("Delivery Ring")]
    public float deliveryRingVerticalOffset = 0.2f;
    [Range(12, 128)] public int deliveryRingSegments = 48;
    public float deliveryRingWidth = 0.18f;
    public Color deliveryRingReadyColor = new Color(0.2f, 1f, 0.3f, 0.9f);
    public Color deliveryRingSearchColor = new Color(1f, 0.9f, 0.2f, 0.9f);

    [Header("Target Beacon")]
    public GameObject targetBeaconPrefab;
    public Vector3 targetBeaconOffset = new Vector3(0f, 8f, 0f);
    public Vector3 targetBeaconScale = new Vector3(1.5f, 8f, 1.5f);
    public Color targetBeaconColor = new Color(1f, 0.15f, 0.1f, 0.65f);
    public bool rotateTargetBeacons = true;
    public float targetBeaconRotationSpeed = 45f;

    [Header("UI")]
    public string progressMessage = "Sending present...";
    public string completeMessage = "Present send complete!";
    public string levelCompleteMessage = "Level Complete!";
    public float completeMessageDuration = 2f;

    [Header("Present Drop Animation")]
    public GameObject[] presentPrefabs;
    public Vector3 presentSpawnOffset = new Vector3(0f, -0.5f, 0f);
    [Min(0f)] public float presentLandingHeight = 0.25f;
    [Min(0.05f)] public float presentDropDuration = 1.2f;
    [Min(0f)] public float presentDropArcHeight = 0.8f;
    public Vector3 presentSpinDegreesPerSecond = new Vector3(180f, 240f, 120f);
    [Min(0f)] public float presentStayDuration = 2f;
    [Min(0.01f)] public float presentScaleMultiplier = 1f;
    public bool disablePresentPhysics = true;

    [Header("Delivered Building")]
    [Tooltip("Keep this off when delivered houses should remain visible.")]
    public bool hideDeliveredBuilding = false;
    [Min(0f)] public float hideDeliveredBuildingDelay = 0.2f;

    public int score = 0;
    public int RemainingTargets => activeTargets.Count;
    public int CompletedTargets => completedDeliveries;
    public int TotalTargets => totalTargets;

    private sealed class ManualBuildingData
    {
        public ManualDeliveryTarget marker;
        public Transform root;
        public Bounds bounds;
        public Vector3 roofTarget;
        public string key;
    }

    private readonly List<ManualBuildingData> activeTargets = new List<ManualBuildingData>();
    private readonly HashSet<string> deliveredTargetKeys = new HashSet<string>();
    private readonly Dictionary<string, float> hoverTimers = new Dictionary<string, float>();
    private readonly Dictionary<string, LineRenderer> deliveryRings = new Dictionary<string, LineRenderer>();
    private readonly Dictionary<string, GameObject> deliveryRingObjects = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, GameObject> targetBeacons = new Dictionary<string, GameObject>();

    private bool targetsInitialized;
    private int totalTargets;
    private int completedDeliveries;
    private bool levelCompleted;
    private string latestStatus = "";
    private float statusUntilTime = -1f;
    private Vector3 lastDronePosition;
    private float currentSpeed;
    private MAVLinkReceiver mavReceiver;
    private ManualBuildingData currentDeliverableTarget;

    private void Start()
    {
        AutoBindDrone();
        CleanupLegacyDeliveryObjects();
        UpdateScoreUI();
        ScoreChanged?.Invoke(score);

        if (drone != null)
        {
            lastDronePosition = drone.position;
        }

        StartCoroutine(InitializeManualTargets());
    }

    private IEnumerator InitializeManualTargets()
    {
        if (initializationDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(initializationDelaySeconds);
        }
        else
        {
            yield return null;
        }

        BuildManualTargetList();
    }

    private void BuildManualTargetList()
    {
        activeTargets.Clear();

        List<ManualDeliveryTarget> candidates = new List<ManualDeliveryTarget>();
        HashSet<ManualDeliveryTarget> uniqueTargets = new HashSet<ManualDeliveryTarget>();

        for (int i = 0; i < manualTargets.Count; i++)
        {
            AddCandidate(manualTargets[i], candidates, uniqueTargets);
        }

        if (autoFindTargetsInScene)
        {
            ManualDeliveryTarget[] foundTargets = FindObjectsByType<ManualDeliveryTarget>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < foundTargets.Length; i++)
            {
                AddCandidate(foundTargets[i], candidates, uniqueTargets);
            }
        }

        int desiredCount = targetLimit <= 0 ? int.MaxValue : targetLimit;

        for (int i = 0; i < candidates.Count && activeTargets.Count < desiredCount; i++)
        {
            ManualDeliveryTarget marker = candidates[i];
            if (TryCreateTarget(marker, out ManualBuildingData target))
            {
                activeTargets.Add(target);
            }
        }

        totalTargets = activeTargets.Count;
        targetsInitialized = totalTargets > 0;

        RefreshDeliveryRings();
        RefreshTargetBeacons();
        TargetsRemainingChanged?.Invoke(RemainingTargets);

        if (!targetsInitialized)
        {
            Debug.LogWarning(
                "ManualDeliveryScoreManager found no valid targets. " +
                "Add ManualDeliveryTarget to each building and assign its Building Root/roof point."
            );
            return;
        }

        Debug.Log($"Manual delivery system initialized with {totalTargets} target buildings.");
    }

    private void AddCandidate(
        ManualDeliveryTarget marker,
        List<ManualDeliveryTarget> candidates,
        HashSet<ManualDeliveryTarget> uniqueTargets)
    {
        if (marker == null || !marker.includeInMission || uniqueTargets.Contains(marker))
        {
            return;
        }

        uniqueTargets.Add(marker);
        candidates.Add(marker);
    }

    private bool TryCreateTarget(ManualDeliveryTarget marker, out ManualBuildingData target)
    {
        target = null;

        if (marker == null || marker.Root == null)
        {
            return false;
        }

        ManualBuildingData candidate = new ManualBuildingData
        {
            marker = marker,
            root = marker.Root,
            key = BuildTargetKey(marker)
        };

        if (!UpdateTargetGeometry(candidate))
        {
            Debug.LogWarning($"Manual target '{marker.name}' has no usable Renderer bounds.", marker);
            return false;
        }

        target = candidate;
        return true;
    }

    private bool UpdateTargetGeometry(ManualBuildingData target)
    {
        if (target == null || target.marker == null || target.root == null)
        {
            return false;
        }

        Renderer[] renderers = target.root.GetComponentsInChildren<Renderer>(includeInactiveRenderers);
        bool hasBounds = false;
        Bounds combinedBounds = new Bounds();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || (!includeInactiveRenderers && !renderer.enabled))
            {
                continue;
            }

            if (!hasBounds)
            {
                combinedBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        if (combinedBounds.size.y < minBuildingHeight ||
            Mathf.Max(combinedBounds.size.x, combinedBounds.size.z) < minBuildingFootprint)
        {
            return false;
        }

        target.bounds = combinedBounds;
        target.roofTarget = target.marker.roofPoint != null
            ? target.marker.roofPoint.position
            : new Vector3(combinedBounds.center.x, combinedBounds.max.y, combinedBounds.center.z);

        return true;
    }

    private void Update()
    {
        AutoBindDrone();

        if (mavReceiver == null)
        {
            mavReceiver = MAVLinkReceiver.Active;
        }

        if (drone == null)
        {
            SetAllDeliveryRingsActive(false);
            return;
        }

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

        currentSpeed = Vector3.Distance(drone.position, lastDronePosition) /
                       Mathf.Max(Time.deltaTime, 0.0001f);
        lastDronePosition = drone.position;

        if (statusUntilTime > 0f && Time.time > statusUntilTime)
        {
            ClearDeliveryStatus();
        }

        bool farGuidedTargetActive =
            mavReceiver != null &&
            mavReceiver.IsGuidedArmed &&
            mavReceiver.HasNavTargetDistance &&
            mavReceiver.NavTargetDistanceMeters > 25f;

        if (farGuidedTargetActive)
        {
            hoverTimers.Clear();
            ClearProgressStatusOnly();
            UpdateAllDeliveryRings(null);
            return;
        }

        if (!targetsInitialized || activeTargets.Count == 0)
        {
            SetAllDeliveryRingsActive(false);
            return;
        }

        if (refreshTargetGeometry)
        {
            for (int i = activeTargets.Count - 1; i >= 0; i--)
            {
                if (!UpdateTargetGeometry(activeTargets[i]))
                {
                    Debug.LogWarning($"Manual delivery target became invalid: {activeTargets[i].root.name}");
                    RemoveTargetVisuals(activeTargets[i].key);
                    activeTargets.RemoveAt(i);
                }
            }
        }

        UpdateTargetBeaconPositions();
        EvaluateTargets(out ManualBuildingData deliverableTarget);
        currentDeliverableTarget = deliverableTarget;
        UpdateAllDeliveryRings(deliverableTarget);

        if (deliverableTarget != null)
        {
            ProcessBuildingHover(deliverableTarget);
        }
        else
        {
            hoverTimers.Clear();
            ClearProgressStatusOnly();
        }
    }

    private void EvaluateTargets(out ManualBuildingData deliverableTarget)
    {
        deliverableTarget = null;
        float nearestValidHeight = float.MaxValue;

        for (int i = 0; i < activeTargets.Count; i++)
        {
            ManualBuildingData target = activeTargets[i];
            if (target == null || target.root == null || target.marker == null)
            {
                continue;
            }

            float allowedRadius = target.marker.overrideDeliveryRadius
                ? target.marker.deliveryRadius
                : deliveryRadius;

            float allowedHeight = target.marker.overrideMaxHeightAboveRoof
                ? target.marker.maxHeightAboveRoof
                : maxHeightAboveRoof;

            float horizontalDistance = Vector2.Distance(
                new Vector2(drone.position.x, drone.position.z),
                new Vector2(target.roofTarget.x, target.roofTarget.z));

            float heightAboveRoof = drone.position.y - target.roofTarget.y;

            if (verboseDebugLogs)
            {
                Debug.Log(
                    $"Manual target {target.root.name} | Distance={horizontalDistance:F2} | " +
                    $"HeightAboveRoof={heightAboveRoof:F2} | Roof={target.roofTarget}"
                );
            }

            if (horizontalDistance > allowedRadius || heightAboveRoof < 0f || heightAboveRoof > allowedHeight)
            {
                continue;
            }

            if (heightAboveRoof < nearestValidHeight)
            {
                nearestValidHeight = heightAboveRoof;
                deliverableTarget = target;
            }
        }
    }

    private void ProcessBuildingHover(ManualBuildingData building)
    {
        if (building == null || deliveredTargetKeys.Contains(building.key))
        {
            return;
        }

        if (currentSpeed > maxHoverSpeed)
        {
            hoverTimers[building.key] = 0f;
            SetDeliveryStatus("Hold position...");
            return;
        }

        if (hoverTimers.Count > 1 ||
            (hoverTimers.Count == 1 && !hoverTimers.ContainsKey(building.key)))
        {
            hoverTimers.Clear();
        }

        if (!hoverTimers.ContainsKey(building.key))
        {
            hoverTimers[building.key] = 0f;
        }

        hoverTimers[building.key] += Time.deltaTime;

        if (hoverTimers[building.key] < hoverSecondsRequired)
        {
            float remaining = Mathf.Max(0f, hoverSecondsRequired - hoverTimers[building.key]);
            SetDeliveryStatus($"{progressMessage} {remaining:0.0}s");
            return;
        }

        bool hasPresentAnimation = HasValidPresentPrefab();
        if (hasPresentAnimation)
        {
            // Use the same drop animation as DeliveryScoreManager.
            StartCoroutine(PlayPresentDrop(building.roofTarget));
        }

        score += rewardPerDelivery;
        completedDeliveries++;
        deliveredTargetKeys.Add(building.key);
        hoverTimers.Remove(building.key);
        activeTargets.Remove(building);
        RemoveTargetVisuals(building.key);

        bool shouldHideBuilding = building.marker.overrideHideAfterDelivery
            ? building.marker.hideAfterDelivery
            : hideDeliveredBuilding;

        if (shouldHideBuilding && building.root != null)
        {
            // Do not hide the roof while the present is still falling or resting on it.
            StartCoroutine(HideBuildingAfterDelay(building.root, hasPresentAnimation));
        }

        TargetsRemainingChanged?.Invoke(RemainingTargets);
        UpdateScoreUI();
        ScoreChanged?.Invoke(score);
        SetDeliveryStatus($"{completeMessage} +{rewardPerDelivery}", completeMessageDuration);

        Debug.Log(
            $"Delivered to manual target '{building.root.name}' | " +
            $"Completed {completedDeliveries}/{totalTargets} | Score {score}"
        );

        if (!levelCompleted && completedDeliveries >= Mathf.Max(1, totalTargets))
        {
            levelCompleted = true;
            SetDeliveryStatus(levelCompleteMessage, Mathf.Max(completeMessageDuration, 3f));
            LevelCompleted?.Invoke();
            Debug.Log("Manual delivery level complete.");
        }
    }

    private IEnumerator HideBuildingAfterDelay(
        Transform buildingRoot,
        bool waitForPresentAnimation)
    {
        float delay = Mathf.Max(0f, hideDeliveredBuildingDelay);

        if (waitForPresentAnimation)
        {
            // DeliveryScoreManager leaves the building visible for the complete
            // drop and roof-stay animation. Match that appearance before hiding.
            delay += Mathf.Max(0.05f, presentDropDuration);
            delay += Mathf.Max(0f, presentStayDuration);
        }

        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        if (buildingRoot != null)
        {
            buildingRoot.gameObject.SetActive(false);
        }
    }

    private TextMeshProUGUI GetDeliveryStatusText()
    {
        return deliveryStatusText != null ? deliveryStatusText : statusText;
    }

    private void SetDeliveryStatus(string message, float durationSeconds = -1f)
    {
        latestStatus = string.IsNullOrEmpty(message) ? "" : message;

        TextMeshProUGUI text = GetDeliveryStatusText();
        if (text != null)
        {
            text.text = latestStatus;
        }

        statusUntilTime = durationSeconds > 0f ? Time.time + durationSeconds : -1f;
    }

    private void ClearDeliveryStatus()
    {
        latestStatus = "";
        statusUntilTime = -1f;

        TextMeshProUGUI text = GetDeliveryStatusText();
        if (text != null)
        {
            text.text = "";
        }
    }

    private void ClearProgressStatusOnly()
    {
        if (latestStatus.StartsWith(progressMessage) || latestStatus == "Hold position...")
        {
            ClearDeliveryStatus();
        }
    }

    private void AutoBindDrone()
    {
        if (drone != null)
        {
            return;
        }

        GameObject droneObject = GameObject.FindGameObjectWithTag("Drone");
        if (droneObject == null)
        {
            return;
        }

        ArcGISLocationComponent locationComponent =
            droneObject.GetComponentInChildren<ArcGISLocationComponent>(true);

        drone = locationComponent != null ? locationComponent.transform : droneObject.transform;
    }

    private string BuildTargetKey(ManualDeliveryTarget marker)
    {
        string readableId = string.IsNullOrWhiteSpace(marker.targetId)
            ? marker.gameObject.name
            : marker.targetId.Trim();

        return $"{readableId}_{marker.GetInstanceID()}";
    }

    private float GetTargetRadius(ManualBuildingData target)
    {
        return target.marker.overrideDeliveryRadius
            ? target.marker.deliveryRadius
            : deliveryRadius;
    }

    private void RefreshDeliveryRings()
    {
        RemoveUnusedDeliveryRings();
        UpdateAllDeliveryRings(null);
    }

    private void UpdateAllDeliveryRings(ManualBuildingData deliverableTarget)
    {
        if (!showSpawnRadius)
        {
            SetAllDeliveryRingsActive(false);
            return;
        }

        RemoveUnusedDeliveryRings();

        for (int i = 0; i < activeTargets.Count; i++)
        {
            ManualBuildingData target = activeTargets[i];
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

            Color ringColor = target == deliverableTarget
                ? deliveryRingReadyColor
                : deliveryRingSearchColor;

            ring.loop = true;
            ring.useWorldSpace = true;
            ring.widthMultiplier = deliveryRingWidth;
            ring.positionCount = Mathf.Max(12, deliveryRingSegments);
            ring.startColor = ringColor;
            ring.endColor = ringColor;

            Vector3 center = target.roofTarget + Vector3.up * deliveryRingVerticalOffset;
            float radius = GetTargetRadius(target);

            for (int segment = 0; segment < ring.positionCount; segment++)
            {
                float angle = Mathf.PI * 2f * segment / ring.positionCount;
                Vector3 point = center +
                    new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                ring.SetPosition(segment, point);
            }
        }
    }

    private LineRenderer EnsureDeliveryRing(ManualBuildingData target)
    {
        if (deliveryRings.TryGetValue(target.key, out LineRenderer existingRing) && existingRing != null)
        {
            return existingRing;
        }

        GameObject ringObject = new GameObject($"ManualDeliveryRing_{target.key}");
        ringObject.transform.SetParent(transform, false);

        LineRenderer ring = ringObject.AddComponent<LineRenderer>();
        ring.loop = true;
        ring.useWorldSpace = true;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.alignment = LineAlignment.View;
        ring.numCornerVertices = 4;
        ring.numCapVertices = 4;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader != null) ring.material = new Material(shader);

        deliveryRings[target.key] = ring;
        deliveryRingObjects[target.key] = ringObject;
        return ring;
    }

    private void RefreshTargetBeacons()
    {
        RemoveUnusedTargetBeacons();

        for (int i = 0; i < activeTargets.Count; i++)
        {
            ManualBuildingData target = activeTargets[i];
            if (target == null || target.root == null || targetBeacons.ContainsKey(target.key))
            {
                continue;
            }

            GameObject beacon;
            Vector3 position = target.roofTarget + targetBeaconOffset;

            if (targetBeaconPrefab != null)
            {
                beacon = Instantiate(targetBeaconPrefab, position, Quaternion.identity, transform);
            }
            else
            {
                beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                beacon.transform.SetParent(transform, true);
                beacon.transform.position = position;

                Collider beaconCollider = beacon.GetComponent<Collider>();
                if (beaconCollider != null) Destroy(beaconCollider);

                Renderer beaconRenderer = beacon.GetComponent<Renderer>();
                if (beaconRenderer != null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null) shader = Shader.Find("Unlit/Color");
                    if (shader == null) shader = Shader.Find("Sprites/Default");

                    if (shader != null)
                    {
                        Material material = new Material(shader);
                        material.color = targetBeaconColor;
                        beaconRenderer.material = material;
                    }
                }
            }

            beacon.name = $"ManualDeliveryBeacon_{target.key}";
            beacon.transform.localScale = targetBeaconScale;
            targetBeacons[target.key] = beacon;
        }
    }


    private void UpdateTargetBeaconPositions()
    {
        for (int i = 0; i < activeTargets.Count; i++)
        {
            ManualBuildingData target = activeTargets[i];
            if (target == null)
            {
                continue;
            }

            if (targetBeacons.TryGetValue(target.key, out GameObject beacon) && beacon != null)
            {
                beacon.transform.position = target.roofTarget + targetBeaconOffset;
            }
        }
    }

    private void RemoveUnusedDeliveryRings()
    {
        HashSet<string> activeKeys = GetActiveKeys();
        List<string> removeKeys = new List<string>();

        foreach (KeyValuePair<string, GameObject> pair in deliveryRingObjects)
        {
            if (!activeKeys.Contains(pair.Key))
            {
                if (pair.Value != null) Destroy(pair.Value);
                removeKeys.Add(pair.Key);
            }
        }

        for (int i = 0; i < removeKeys.Count; i++)
        {
            deliveryRingObjects.Remove(removeKeys[i]);
            deliveryRings.Remove(removeKeys[i]);
        }
    }

    private void RemoveUnusedTargetBeacons()
    {
        HashSet<string> activeKeys = GetActiveKeys();
        List<string> removeKeys = new List<string>();

        foreach (KeyValuePair<string, GameObject> pair in targetBeacons)
        {
            if (!activeKeys.Contains(pair.Key))
            {
                if (pair.Value != null) Destroy(pair.Value);
                removeKeys.Add(pair.Key);
            }
        }

        for (int i = 0; i < removeKeys.Count; i++)
        {
            targetBeacons.Remove(removeKeys[i]);
        }
    }

    private HashSet<string> GetActiveKeys()
    {
        HashSet<string> keys = new HashSet<string>();
        for (int i = 0; i < activeTargets.Count; i++)
        {
            if (activeTargets[i] != null)
            {
                keys.Add(activeTargets[i].key);
            }
        }
        return keys;
    }

    private void RemoveTargetVisuals(string key)
    {
        if (deliveryRingObjects.TryGetValue(key, out GameObject ringObject) && ringObject != null)
        {
            Destroy(ringObject);
        }
        deliveryRingObjects.Remove(key);
        deliveryRings.Remove(key);

        if (targetBeacons.TryGetValue(key, out GameObject beacon) && beacon != null)
        {
            Destroy(beacon);
        }
        targetBeacons.Remove(key);
    }

    private void SetAllDeliveryRingsActive(bool active)
    {
        foreach (GameObject ringObject in deliveryRingObjects.Values)
        {
            if (ringObject != null)
            {
                ringObject.SetActive(active);
            }
        }
    }

    private void CleanupLegacyDeliveryObjects()
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

            if (sceneObject.name.StartsWith("ManualDeliveryRing_") ||
                sceneObject.name.StartsWith("ManualDeliveryBeacon_"))
            {
                Destroy(sceneObject);
            }
        }
    }

    private bool HasValidPresentPrefab()
    {
        if (presentPrefabs == null)
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

    private IEnumerator PlayPresentDrop(Vector3 roofTarget)
    {
        if (drone == null || presentPrefabs == null || presentPrefabs.Length == 0)
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

        GameObject selectedPrefab = validPrefabs[UnityEngine.Random.Range(0, validPrefabs.Count)];
        Vector3 startPosition = drone.TransformPoint(presentSpawnOffset);
        Vector3 endPosition = roofTarget + Vector3.up * presentLandingHeight;

        GameObject present = Instantiate(
            selectedPrefab,
            startPosition,
            UnityEngine.Random.rotation
        );

        present.name = selectedPrefab.name + "_Dropped";
        present.transform.localScale *= presentScaleMultiplier;

        if (disablePresentPhysics)
        {
            Collider[] colliders = present.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            Rigidbody[] rigidbodies = present.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                rigidbodies[i].isKinematic = true;
                rigidbodies[i].useGravity = false;
                rigidbodies[i].linearVelocity = Vector3.zero;
                rigidbodies[i].angularVelocity = Vector3.zero;
            }
        }

        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, presentDropDuration);

        while (elapsed < duration && present != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            Vector3 position = Vector3.Lerp(startPosition, endPosition, smoothT);
            position.y += Mathf.Sin(t * Mathf.PI) * presentDropArcHeight;

            present.transform.position = position;
            present.transform.Rotate(
                presentSpinDegreesPerSecond * Time.deltaTime,
                Space.Self
            );

            yield return null;
        }

        if (present == null)
        {
            yield break;
        }

        present.transform.position = endPosition;

        if (presentStayDuration > 0f)
        {
            yield return new WaitForSeconds(presentStayDuration);

            if (present != null)
            {
                Destroy(present);
            }
        }
    }

    private void UpdateScoreUI()
    {
        if (scoreText != null)
        {
            scoreText.text = "Score: " + score;
        }
    }

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || !showSpawnRadius)
        {
            return;
        }

        for (int i = 0; i < activeTargets.Count; i++)
        {
            ManualBuildingData target = activeTargets[i];
            if (target == null) continue;

            Gizmos.color = target == currentDeliverableTarget ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(target.roofTarget, GetTargetRadius(target));
        }
    }
}
