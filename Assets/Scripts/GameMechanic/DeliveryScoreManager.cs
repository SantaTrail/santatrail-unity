using System.Collections.Generic;
using System;
using TMPro;
using UnityEngine;

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

    [Header("UI")]
    public string progressMessage = "Sending present...";
    public string completeMessage = "Present send complete!";
    public string level1CompleteMessage = "Level 1 Complete!";
    public float completeMessageDuration = 2f;
    [Header("Level Progress")]
    public int level1RequiredDeliveries = 5;

    public int score = 0;

    private readonly HashSet<string> deliveredBuildingKeys = new HashSet<string>();
    private readonly Dictionary<string, float> hoverTimers = new Dictionary<string, float>();
    private readonly List<Renderer> cachedBuildings = new List<Renderer>();
    private float nextScanTime = 0f;
    private string latestStatus = "";
    private float statusUntilTime = -1f;
    private int completedDeliveries = 0;
    private bool level1Completed = false;
    private MAVLinkReceiver mavReceiver;

    void Start()
    {
        AutoBindDrone();
        ScanBuildings();
        UpdateScoreUI();
        ScoreChanged?.Invoke(score);
    }

    void Update()
    {
        AutoBindDrone();
        if (mavReceiver == null) mavReceiver = MAVLinkReceiver.Active;
        if (drone == null) return;

        bool farGuidedTargetActive =
            mavReceiver != null &&
            mavReceiver.IsGuidedArmed &&
            mavReceiver.HasNavTargetDistance &&
            mavReceiver.NavTargetDistanceMeters > 25f;

        // While actively traveling to a far guided target, don't pause on building-delivery hover logic.
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

            return;
        }

        if (Time.time >= nextScanTime)
        {
            ScanBuildings();
            nextScanTime = Time.time + scanInterval;
        }

        Renderer nearest = FindNearestEligibleBuilding();
        if (nearest != null)
        {
            ProcessBuildingHover(nearest);
        }
        else
        {
            // Drone left delivery range: clear all in-progress hover timers.
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
        if (drone != null) return;
        GameObject droneObj = GameObject.FindGameObjectWithTag("Drone");
        if (droneObj != null) drone = droneObj.transform;
    }

    void ScanBuildings()
    {
        cachedBuildings.Clear();
        Renderer[] all = Resources.FindObjectsOfTypeAll<Renderer>();

        foreach (Renderer r in all)
        {
            if (r == null) continue;
            if (!includeInactiveRenderers && !r.enabled) continue;
            if (r.gameObject.layer == 5) continue; // UI layer
            if (drone != null && (r.transform == drone || r.transform.IsChildOf(drone))) continue;

            string n = r.name.ToLowerInvariant();
            if (n.Contains("drone") || n.Contains("camera") || n.Contains("canvas"))
                continue;

            Bounds b = r.bounds;
            if (b.size.y < minBuildingHeight) continue;
            if (Mathf.Max(b.size.x, b.size.z) < minBuildingFootprint) continue;

            cachedBuildings.Add(r);
        }

        // If strict filters found nothing, relax rules so OSM-only ArcGIS scenes still work.
        if (cachedBuildings.Count == 0)
        {
            foreach (Renderer r in all)
            {
                if (r == null) continue;
                if (r.gameObject.layer == 5) continue;
                if (drone != null && (r.transform == drone || r.transform.IsChildOf(drone))) continue;

                string n = r.name.ToLowerInvariant();
                if (n.Contains("drone") || n.Contains("camera") || n.Contains("canvas"))
                    continue;

                Bounds b = r.bounds;
                if (Mathf.Max(b.size.x, b.size.z) < 1f) continue;

                cachedBuildings.Add(r);
            }
        }
    }
    Renderer FindNearestEligibleBuilding()
    {
        if (cachedBuildings.Count == 0) return null;

        Vector3 dronePos = drone.position;

        Renderer nearest = null;
        float nearestHeight = float.MaxValue;

        for (int i = 0; i < cachedBuildings.Count; i++)
        {
            Renderer mr = cachedBuildings[i];
            if (mr == null) continue;

            Bounds b = mr.bounds;

            // Expand bounds slightly for easier hovering
            Bounds expanded = b;
            expanded.Expand(new Vector3(deliveryRange, 0f, deliveryRange));

            // Check if drone XZ position is above building footprint
            bool insideX =
                dronePos.x >= expanded.min.x &&
                dronePos.x <= expanded.max.x;

            bool insideZ =
                dronePos.z >= expanded.min.z &&
                dronePos.z <= expanded.max.z;

            if (!insideX || !insideZ)
                continue;

            // Optional:
            // Require drone to be ABOVE building roof
            if (dronePos.y < b.max.y)
                continue;

            float heightAboveRoof = dronePos.y - b.max.y;

            // Choose closest roof vertically
            if (heightAboveRoof < nearestHeight)
            {
                nearestHeight = heightAboveRoof;
                nearest = mr;
            }
        }

        return nearest;
    }

    void ProcessBuildingHover(Renderer building)
    {
        string key = MakeBuildingKey(building.bounds.center);
        if (deliveredBuildingKeys.Contains(key)) return;

        // Only keep timer for the current nearest building; reset others to avoid carry-over.
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
            if (statusText != null) statusText.text = latestStatus;
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
        if (statusText != null) statusText.text = latestStatus;
        Debug.Log($"Delivered (OSM)! +{rewardPerDelivery} | Total: {score} | Key={key}");

        if (!level1Completed && completedDeliveries >= Mathf.Max(1, level1RequiredDeliveries))
        {
            level1Completed = true;
            latestStatus = level1CompleteMessage;
            statusUntilTime = Time.time + Mathf.Max(completeMessageDuration, 3f);
            if (statusText != null) statusText.text = latestStatus;
            Debug.Log("✅ Level 1 Complete");
        }
    }

    string MakeBuildingKey(Vector3 worldCenter)
    {
        // Quantize to make stable per-building keys across tiny runtime jitter.
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
