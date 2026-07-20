using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(12000)]
public class MinimapOverlayController : MonoBehaviour
{
    [Header("Camera")]
    [Tooltip("Optional. If empty, the script looks for the camera that renders to this RawImage texture.")]
    [SerializeField] private Camera minimapCamera;

    [Tooltip("Optional player transform. If empty, the script auto-finds the drone.")]
    [SerializeField] private Transform player;

    [SerializeField] private bool autoFindPlayer = true;

    [Header("Mission Markers")]
    [SerializeField] private bool autoFindManualDeliveryTargets = true;
    [SerializeField] private List<Transform> extraMissionTargets = new List<Transform>();
    [SerializeField] private bool missionMarkersUseRoofPoint = false;

    [Header("Marker Look")]
    [SerializeField] private Color playerColor = new Color(1f, 0.92f, 0.25f, 1f);
    [SerializeField] private Color missionColor = new Color(0.9f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color inactiveMissionColor = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] private Sprite playerMarkerSprite;
    [SerializeField] private Texture2D playerMarkerTexture;
    [SerializeField] private Sprite missionMarkerSprite;
    [SerializeField] private Texture2D missionMarkerTexture;
    [SerializeField] private bool showInactiveMissionTargets = false;
    [SerializeField] private Vector2 playerMarkerSize = new Vector2(20f, 20f);
    [SerializeField] private Vector2 missionMarkerSize = new Vector2(12f, 12f);
    [SerializeField] private bool clampMarkersToEdge = true;
    [SerializeField] private float edgePadding = 10f;

    [Header("Refresh")]
    [Min(0.1f)]
    [SerializeField] private float missionRefreshInterval = 1f;

    private RectTransform rectTransform;
    private RectTransform markerLayer;
    private RectTransform playerMarker;
    private readonly Dictionary<Transform, RectTransform> missionMarkers =
        new Dictionary<Transform, RectTransform>();
    private readonly Dictionary<Transform, ManualDeliveryTarget> missionTargetSources =
        new Dictionary<Transform, ManualDeliveryTarget>();
    private readonly HashSet<Transform> seenMissionTargets =
        new HashSet<Transform>();
    private Sprite defaultSprite;
    private Sprite playerTextureSprite;
    private Sprite missionTextureSprite;
    private float nextMissionRefreshTime;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        defaultSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        playerTextureSprite = CreateSpriteFromTexture(playerMarkerTexture);
        missionTextureSprite = CreateSpriteFromTexture(missionMarkerTexture);
        EnsureMarkerLayer();
    }

    private void Start()
    {
        TryResolveCamera();
        TryResolvePlayer();
        RefreshMissionTargets(force: true);
        EnsurePlayerMarker();
    }

    private void Update()
    {
        TryResolveCamera();
        TryResolvePlayer();

        if (Time.unscaledTime >= nextMissionRefreshTime)
        {
            RefreshMissionTargets(force: false);
            nextMissionRefreshTime =
                Time.unscaledTime + missionRefreshInterval;
        }

        UpdatePlayerMarker();
        UpdateMissionMarkers();
    }

    private void EnsureMarkerLayer()
    {
        if (markerLayer != null)
        {
            return;
        }

        GameObject layerObject = new GameObject("MinimapMarkerLayer", typeof(RectTransform));
        layerObject.transform.SetParent(transform, false);

        markerLayer = layerObject.GetComponent<RectTransform>();
        markerLayer.anchorMin = Vector2.zero;
        markerLayer.anchorMax = Vector2.one;
        markerLayer.offsetMin = Vector2.zero;
        markerLayer.offsetMax = Vector2.zero;
        markerLayer.pivot = new Vector2(0.5f, 0.5f);
    }

    private void EnsurePlayerMarker()
    {
        if (playerMarker != null)
        {
            return;
        }

        GameObject markerObject = CreateMarkerObject(
            "PlayerArrow",
            playerColor,
            playerMarkerSize,
            GetPlayerSprite()
        );

        markerObject.transform.SetParent(markerLayer, false);

        RectTransform markerRect = markerObject.GetComponent<RectTransform>();
        markerRect.anchorMin = new Vector2(0.5f, 0.5f);
        markerRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerRect.pivot = new Vector2(0.5f, 0.5f);
        markerRect.anchoredPosition = Vector2.zero;

        markerObject.AddComponent<MinimapDroneArrow>();
        playerMarker = markerRect;
    }

    private void RefreshMissionTargets(bool force)
    {
        if (!force && !autoFindManualDeliveryTargets)
        {
            return;
        }

        seenMissionTargets.Clear();

        if (autoFindManualDeliveryTargets)
        {
            ManualDeliveryTarget[] foundTargets =
                Object.FindObjectsByType<ManualDeliveryTarget>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            for (int i = 0; i < foundTargets.Length; i++)
            {
                ManualDeliveryTarget target = foundTargets[i];
                if (target == null)
                {
                    continue;
                }

                if (!target.includeInMission && !showInactiveMissionTargets)
                {
                    continue;
                }

                AddMissionTarget(target, target.includeInMission ? missionColor : inactiveMissionColor);
            }
        }

        for (int i = 0; i < extraMissionTargets.Count; i++)
        {
            AddMissionTarget(extraMissionTargets[i], missionColor);
        }

        List<Transform> deadTargets = new List<Transform>();
        foreach (KeyValuePair<Transform, RectTransform> pair in missionMarkers)
        {
            if (pair.Key == null || !seenMissionTargets.Contains(pair.Key))
            {
                deadTargets.Add(pair.Key);
            }
        }

        for (int i = 0; i < deadTargets.Count; i++)
        {
            Transform target = deadTargets[i];
            if (target != null && missionMarkers.TryGetValue(target, out RectTransform marker))
            {
                if (marker != null)
                {
                    Destroy(marker.gameObject);
                }
            }

            if (target != null)
            {
                missionMarkers.Remove(target);
                missionTargetSources.Remove(target);
            }
        }
    }

    private void AddMissionTarget(Transform target, Color markerColor)
    {
        AddMissionTarget(target, null, markerColor);
    }

    private void AddMissionTarget(ManualDeliveryTarget sourceTarget, Color markerColor)
    {
        if (sourceTarget == null)
        {
            return;
        }

        AddMissionTarget(ResolveMissionTargetTransform(sourceTarget), sourceTarget, markerColor);
    }

    private void AddMissionTarget(Transform target, ManualDeliveryTarget sourceTarget, Color markerColor)
    {
        if (target == null || seenMissionTargets.Contains(target))
        {
            return;
        }

        seenMissionTargets.Add(target);

        if (sourceTarget != null)
        {
            missionTargetSources[target] = sourceTarget;
        }

        if (missionMarkers.ContainsKey(target))
        {
            return;
        }

        GameObject markerObject = CreateMarkerObject(
            $"MissionMarker_{target.name}",
            markerColor,
            missionMarkerSize,
            GetMissionSprite()
        );

        markerObject.transform.SetParent(markerLayer, false);

        RectTransform markerRect = markerObject.GetComponent<RectTransform>();
        markerRect.anchorMin = new Vector2(0.5f, 0.5f);
        markerRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerRect.pivot = new Vector2(0.5f, 0.5f);
        markerRect.anchoredPosition = Vector2.zero;

        missionMarkers[target] = markerRect;
    }

    private Transform ResolveMissionTargetTransform(ManualDeliveryTarget target)
    {
        if (target == null)
        {
            return null;
        }

        if (missionMarkersUseRoofPoint && target.roofPoint != null)
        {
            return target.roofPoint;
        }

        return target.Root;
    }

    private Vector3 GetMissionTargetWorldPosition(Transform targetKey)
    {
        if (targetKey != null &&
            missionTargetSources.TryGetValue(targetKey, out ManualDeliveryTarget source) &&
            source != null)
        {
            return GetMarkerWorldPosition(source);
        }

        return targetKey != null ? targetKey.position : Vector3.zero;
    }

    private Vector3 GetMarkerWorldPosition(ManualDeliveryTarget target)
    {
        if (target == null)
        {
            return Vector3.zero;
        }

        if (missionMarkersUseRoofPoint && target.roofPoint != null)
        {
            return target.roofPoint.position;
        }

        Transform root = target.Root;
        if (root == null)
        {
            return target.transform.position;
        }

        if (TryGetCombinedBounds(root, out Bounds bounds))
        {
            return bounds.center;
        }

        return root.position;
    }

    private bool TryGetCombinedBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();

        if (root == null)
        {
            return false;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private void UpdatePlayerMarker()
    {
        if (playerMarker == null)
        {
            EnsurePlayerMarker();
        }

        if (playerMarker == null || player == null)
        {
            return;
        }

        playerMarker.anchoredPosition = Vector2.zero;
    }

    private void UpdateMissionMarkers()
    {
        if (minimapCamera == null)
        {
            return;
        }

        Rect rect = rectTransform.rect;
        Vector2 halfSize = rect.size * 0.5f;

        foreach (KeyValuePair<Transform, RectTransform> pair in missionMarkers)
        {
            Transform target = pair.Key;
            RectTransform marker = pair.Value;

            if (target == null || marker == null)
            {
                continue;
            }

            Vector3 worldPosition = GetMissionTargetWorldPosition(target);
            Vector3 viewport = minimapCamera.WorldToViewportPoint(worldPosition);
            if (viewport.z < 0f)
            {
                marker.gameObject.SetActive(false);
                continue;
            }

            marker.gameObject.SetActive(true);

            Vector2 anchoredPosition = new Vector2(
                (viewport.x - 0.5f) * rect.width,
                (viewport.y - 0.5f) * rect.height
            );

            if (clampMarkersToEdge)
            {
                anchoredPosition.x = Mathf.Clamp(
                    anchoredPosition.x,
                    -halfSize.x + edgePadding,
                    halfSize.x - edgePadding
                );
                anchoredPosition.y = Mathf.Clamp(
                    anchoredPosition.y,
                    -halfSize.y + edgePadding,
                    halfSize.y - edgePadding
                );
            }

            marker.anchoredPosition = anchoredPosition;
        }
    }

    private bool TryResolveCamera()
    {
        if (minimapCamera != null)
        {
            return true;
        }

        RawImage rawImage = GetComponent<RawImage>();
        Texture targetTexture = rawImage != null ? rawImage.texture : null;

        if (targetTexture != null)
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].targetTexture == targetTexture)
                {
                    minimapCamera = cameras[i];
                    return true;
                }
            }
        }

        GameObject namedCamera = GameObject.Find("DroneCamera");
        if (namedCamera != null && namedCamera.TryGetComponent(out Camera camera))
        {
            minimapCamera = camera;
            return true;
        }

        Camera fallbackCamera = Camera.main;
        if (fallbackCamera != null)
        {
            minimapCamera = fallbackCamera;
            return true;
        }

        return false;
    }

    private bool TryResolvePlayer()
    {
        if (player != null || !autoFindPlayer)
        {
            return player != null;
        }

        if (MAVLinkReceiver.Active != null)
        {
            player = MAVLinkReceiver.Active.transform;
            return true;
        }

        MAVLinkReceiver receiver = Object.FindFirstObjectByType<MAVLinkReceiver>();
        if (receiver != null)
        {
            player = receiver.transform;
            return true;
        }

        GameObject droneObject = GameObject.FindGameObjectWithTag("Drone");
        if (droneObject != null)
        {
            player = droneObject.transform;
            return true;
        }

        droneObject = GameObject.Find("Drone");
        if (droneObject != null)
        {
            player = droneObject.transform;
            return true;
        }

        return false;
    }

    private GameObject CreateMarkerObject(
        string name,
        Color color,
        Vector2 size,
        Sprite spriteOverride)
    {
        GameObject markerObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Image image = markerObject.GetComponent<Image>();
        image.sprite = spriteOverride != null ? spriteOverride : defaultSprite;
        image.color = color;
        image.raycastTarget = false;
        image.preserveAspect = true;

        RectTransform markerRect = markerObject.GetComponent<RectTransform>();
        markerRect.sizeDelta = size;

        return markerObject;
    }

    private Sprite GetMissionSprite()
    {
        if (missionMarkerSprite != null)
        {
            return missionMarkerSprite;
        }

        return missionTextureSprite != null ? missionTextureSprite : defaultSprite;
    }

    private Sprite GetPlayerSprite()
    {
        if (playerMarkerSprite != null)
        {
            return playerMarkerSprite;
        }

        return playerTextureSprite != null ? playerTextureSprite : defaultSprite;
    }

    private Sprite CreateSpriteFromTexture(Texture2D texture)
    {
        if (texture == null)
        {
            return null;
        }

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f
        );
    }
}
