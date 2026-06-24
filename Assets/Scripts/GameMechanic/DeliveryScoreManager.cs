using System;
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

    [Header("OSM Delivery")]
    public float deliveryRange = 2f;
    public float hoverSecondsRequired = 1.5f;
    public int rewardPerDelivery = 100;
    public float scanInterval = 1f;
    public float minBuildingHeight = 0.1f;
    public float minBuildingFootprint = 3f;
    public bool includeInactiveRenderers = false;

    [Header("Delivery Precision")]
    public float maxHorizontalOffset = 5f;
    public float maxHoverSpeed = 100f;
    public float maxHeightAboveRoof = 3f;
    public bool showSpawnRadius = true;
    public bool logCandidateBuildings = true;
    public bool logAcceptedBuildingRoots = true;

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
    public int level1RequiredDeliveries = 5;

    public int score = 0;

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
    private float nextScanTime = 0f;
    private string latestStatus = "";
    private float statusUntilTime = -1f;
    private int completedDeliveries = 0;
    private bool level1Completed = false;
    private MAVLinkReceiver mavReceiver;
    private SpawnedBuildingTarget currentNearestBuilding;
    private SpawnedBuildingTarget currentDeliverableTarget;
    private LineRenderer deliveryRing;
    private GameObject deliveryRingObject;
    private Vector3 lastDronePos;
    private float currentSpeed;

    void Start()
    {
        AutoBindDrone();
        ScanBuildings();
        UpdateScoreUI();
        ScoreChanged?.Invoke(score);

        if (drone != null)
        {
            lastDronePos = drone.position;
        }
    }

    void Update()
    {
        AutoBindDrone();
        if (mavReceiver == null)
        {
            mavReceiver = MAVLinkReceiver.Active;
        }

        if (drone == null)
        {
            UpdateDeliveryRing(null, false);
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

            UpdateDeliveryRing(null, false);
            return;
        }

        if (Time.time >= nextScanTime)
        {
            ScanBuildings();
            nextScanTime = Time.time + scanInterval;
        }

        EvaluateTargets(out SpawnedBuildingTarget nearestTarget, out SpawnedBuildingTarget deliverableTarget);
        currentNearestBuilding = nearestTarget;
        currentDeliverableTarget = deliverableTarget;

        UpdateDeliveryRing(currentDeliverableTarget ?? currentNearestBuilding, currentDeliverableTarget != null);

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

        Renderer[] all = Resources.FindObjectsOfTypeAll<Renderer>();
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

            if (logAcceptedBuildingRoots)
            {
                Debug.Log(
                    $"Accepted building root: {root.name} | Center: {target.bounds.center} | Size: {target.bounds.size}"
                );
            }
        }
    }

    Transform GetSpawnedBuildingRoot(Renderer renderer)
    {
        if (renderer == null)
        {
            return null;
        }

        Transform root = renderer.transform.root;
        if (root == null)
        {
            return null;
        }

        string rootName = root.name.ToLowerInvariant();
        if (!rootName.EndsWith("_arcgisbuilding"))
        {
            return null;
        }

        if (root.GetComponent<ArcGISLocationComponent>() == null)
        {
            return null;
        }

        return root;
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

        for (int i = 0; i < cachedBuildings.Count; i++)
        {
            SpawnedBuildingTarget target = cachedBuildings[i];
            if (target == null || target.root == null)
            {
                continue;
            }

            float horizontalDistance = Vector2.Distance(
                new Vector2(dronePos.x, dronePos.z),
                new Vector2(target.roofTarget.x, target.roofTarget.z)
            );

            float heightAboveRoof = dronePos.y - target.roofTarget.y;

            if (logCandidateBuildings)
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

            if (horizontalDistance > maxHorizontalOffset)
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

        if (deliverableTarget != null)
        {
            Debug.Log(
                $"✅ Delivery candidate selected: {deliverableTarget.root.name} | Roof: {deliverableTarget.roofTarget}"
            );
        }
    }

    void UpdateDeliveryRing(SpawnedBuildingTarget target, bool deliverable)
    {
        if (!showSpawnRadius)
        {
            SetDeliveryRingActive(false);
            return;
        }

        if (target == null)
        {
            SetDeliveryRingActive(false);
            return;
        }

        EnsureDeliveryRing();
        if (deliveryRing == null)
        {
            return;
        }

        deliveryRingObject.SetActive(true);
        deliveryRing.loop = true;
        deliveryRing.useWorldSpace = true;
        deliveryRing.widthMultiplier = deliveryRingWidth;
        deliveryRing.positionCount = Mathf.Max(12, deliveryRingSegments);
        deliveryRing.startColor = deliverable ? deliveryRingReadyColor : deliveryRingSearchColor;
        deliveryRing.endColor = deliverable ? deliveryRingReadyColor : deliveryRingSearchColor;

        Vector3 center = target.roofTarget + Vector3.up * deliveryRingVerticalOffset;
        int segmentCount = deliveryRing.positionCount;

        for (int i = 0; i < segmentCount; i++)
        {
            float angle = (Mathf.PI * 2f * i) / segmentCount;
            Vector3 point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * maxHorizontalOffset;
            deliveryRing.SetPosition(i, point);
        }
    }

    void EnsureDeliveryRing()
    {
        if (deliveryRing != null)
        {
            return;
        }

        deliveryRingObject = new GameObject("DeliveryRing");
        deliveryRingObject.hideFlags = HideFlags.DontSave;

        deliveryRing = deliveryRingObject.AddComponent<LineRenderer>();
        deliveryRing.loop = true;
        deliveryRing.useWorldSpace = true;
        deliveryRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        deliveryRing.receiveShadows = false;
        deliveryRing.alignment = LineAlignment.View;
        deliveryRing.numCornerVertices = 4;
        deliveryRing.numCapVertices = 4;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader != null)
        {
            deliveryRing.material = new Material(shader);
        }
    }

    void SetDeliveryRingActive(bool isActive)
    {
        if (deliveryRingObject != null)
        {
            deliveryRingObject.SetActive(isActive);
        }
    }

    void OnDrawGizmos()
    {
        if (!showSpawnRadius)
        {
            return;
        }

        if (currentDeliverableTarget != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(currentDeliverableTarget.roofTarget, maxHorizontalOffset);
            Gizmos.DrawLine(currentDeliverableTarget.roofTarget, currentDeliverableTarget.roofTarget + Vector3.up * 8f);
        }
        else if (currentNearestBuilding != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(currentNearestBuilding.roofTarget, maxHorizontalOffset);
            Gizmos.DrawLine(currentNearestBuilding.roofTarget, currentNearestBuilding.roofTarget + Vector3.up * 8f);
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
        hoverTimers[key] = 0f;
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

        if (!level1Completed && completedDeliveries >= Mathf.Max(1, level1RequiredDeliveries))
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
