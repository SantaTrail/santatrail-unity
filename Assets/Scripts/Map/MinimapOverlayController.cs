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

    [Header("Current Target Guidance")]
    [Tooltip("Draw a high-contrast ray from the drone to the closest remaining delivery house.")]
    [SerializeField] private bool showTargetGuidanceRay = true;
    [Tooltip("Draw a ring around the closest delivery area, or a directional arrow when it is offscreen.")]
    [SerializeField] private bool showTargetGuidanceRing = true;
    [SerializeField] private Color targetGuidanceColor = new Color(1f, 0.78f, 0.08f, 1f);
    [SerializeField] private Color targetGuidanceOutlineColor = new Color(0.05f, 0.04f, 0.02f, 0.9f);
    [Min(1f)] [SerializeField] private float targetGuidanceRayWidth = 3f;
    [Min(8f)] [SerializeField] private float minimumTargetRingSize = 34f;
    [Min(8f)] [SerializeField] private float maximumTargetRingSize = 72f;
    [Min(12f)] [SerializeField] private float offscreenTargetArrowSize = 30f;
    [Range(0f, 0.25f)] [SerializeField] private float targetRingPulseAmount = 0.08f;
    [Min(0f)] [SerializeField] private float targetRingPulseSpeed = 2.5f;

    [Header("Refresh")]
    [Min(0.1f)]
    [SerializeField] private float missionRefreshInterval = 1f;

    private RectTransform rectTransform;
    private RectTransform markerLayer;
    private RectTransform playerMarker;
    private RectTransform guidanceRayOutline;
    private RectTransform guidanceRay;
    private RectTransform guidanceRingOutline;
    private RectTransform guidanceRing;
    private readonly Dictionary<Transform, RectTransform> missionMarkers =
        new Dictionary<Transform, RectTransform>();
    private readonly Dictionary<Transform, ManualDeliveryTarget> missionTargetSources =
        new Dictionary<Transform, ManualDeliveryTarget>();
    private readonly HashSet<Transform> seenMissionTargets =
        new HashSet<Transform>();
    private Sprite defaultSprite;
    private Sprite playerTextureSprite;
    private Sprite missionTextureSprite;
    private Sprite guidanceRingSprite;
    private Sprite guidanceArrowSprite;
    private Texture2D guidanceRingTexture;
    private Texture2D guidanceArrowTexture;
    private float nextMissionRefreshTime;
    private DeliveryScoreManager deliveryScoreManager;
    private ManualDeliveryScoreManager manualDeliveryScoreManager;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        defaultSprite = CreateFallbackSprite();
        playerTextureSprite = CreateSpriteFromTexture(playerMarkerTexture);
        missionTextureSprite = CreateSpriteFromTexture(missionMarkerTexture);
        guidanceRingSprite = CreateGuidanceRingSprite();
        guidanceArrowSprite = CreateGuidanceArrowSprite();
        EnsureMarkerLayer();
        EnsureGuidanceVisuals();
    }

    private static Sprite CreateFallbackSprite()
    {
        Texture2D texture = Texture2D.whiteTexture;
        Rect rect = new Rect(0f, 0f, texture.width, texture.height);
        return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 1f);
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
        UpdateTargetGuidance();
    }

    private void OnDestroy()
    {
        if (guidanceRingSprite != null)
        {
            Destroy(guidanceRingSprite);
        }

        if (guidanceRingTexture != null)
        {
            Destroy(guidanceRingTexture);
        }

        if (guidanceArrowSprite != null)
        {
            Destroy(guidanceArrowSprite);
        }

        if (guidanceArrowTexture != null)
        {
            Destroy(guidanceArrowTexture);
        }
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

    private void EnsureGuidanceVisuals()
    {
        if (guidanceRayOutline != null &&
            guidanceRay != null &&
            guidanceRingOutline != null &&
            guidanceRing != null)
        {
            return;
        }

        if (guidanceRayOutline == null)
        {
            guidanceRayOutline = CreateGuidanceImage(
                "TargetGuidanceRayOutline",
                targetGuidanceOutlineColor,
                defaultSprite);
            guidanceRayOutline.SetAsFirstSibling();
        }

        if (guidanceRay == null)
        {
            guidanceRay = CreateGuidanceImage(
                "TargetGuidanceRay",
                targetGuidanceColor,
                defaultSprite);
            guidanceRay.SetSiblingIndex(1);
        }

        if (guidanceRingOutline == null)
        {
            guidanceRingOutline = CreateGuidanceImage(
                "TargetGuidanceRingOutline",
                targetGuidanceOutlineColor,
                guidanceRingSprite);
        }

        if (guidanceRing == null)
        {
            guidanceRing = CreateGuidanceImage(
                "TargetGuidanceRing",
                targetGuidanceColor,
                guidanceRingSprite);
        }

        SetGuidanceVisualsActive(false);
    }

    private RectTransform CreateGuidanceImage(string objectName, Color color, Sprite sprite)
    {
        GameObject imageObject = CreateMarkerObject(
            objectName,
            color,
            Vector2.zero,
            sprite);
        imageObject.transform.SetParent(markerLayer, false);

        Image image = imageObject.GetComponent<Image>();
        image.preserveAspect = sprite == guidanceRingSprite;

        RectTransform imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = new Vector2(0.5f, 0.5f);
        imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        imageRect.pivot = new Vector2(0.5f, 0.5f);
        imageRect.anchoredPosition = Vector2.zero;
        return imageRect;
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

    private void UpdateTargetGuidance()
    {
        EnsureGuidanceVisuals();

        if ((!showTargetGuidanceRay && !showTargetGuidanceRing) ||
            minimapCamera == null ||
            !TryGetCurrentGuidanceTarget(out Vector3 targetWorldPosition, out float targetRadius))
        {
            SetGuidanceVisualsActive(false);
            return;
        }

        Vector3 viewport = minimapCamera.WorldToViewportPoint(targetWorldPosition);
        if (viewport.z < 0f)
        {
            SetGuidanceVisualsActive(false);
            return;
        }

        Rect rect = rectTransform.rect;
        Vector2 halfSize = rect.size * 0.5f;
        float ringSize = CalculateTargetRingSize(targetRadius, rect.height);
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * targetRingPulseSpeed) * targetRingPulseAmount;
        float pulsedRingSize = ringSize * pulse;

        Vector2 unclampedTargetPosition = new Vector2(
            (viewport.x - 0.5f) * rect.width,
            (viewport.y - 0.5f) * rect.height);
        Vector2 targetPosition = unclampedTargetPosition;
        bool targetIsOffscreen = viewport.x < 0f ||
                                 viewport.x > 1f ||
                                 viewport.y < 0f ||
                                 viewport.y > 1f;
        float targetIndicatorSize = targetIsOffscreen
            ? Mathf.Max(12f, offscreenTargetArrowSize)
            : pulsedRingSize;

        if (clampMarkersToEdge)
        {
            float indicatorMargin = targetIsOffscreen ? 8f : 2f;
            float padding = Mathf.Max(
                edgePadding,
                targetIndicatorSize * 0.5f + indicatorMargin);
            float horizontalLimit = Mathf.Max(0f, halfSize.x - padding);
            float verticalLimit = Mathf.Max(0f, halfSize.y - padding);

            targetPosition = targetIsOffscreen
                ? PlaceIndicatorOnMinimapEdge(
                    unclampedTargetPosition,
                    horizontalLimit,
                    verticalLimit)
                : new Vector2(
                    Mathf.Clamp(targetPosition.x, -horizontalLimit, horizontalLimit),
                    Mathf.Clamp(targetPosition.y, -verticalLimit, verticalLimit));
        }

        guidanceRingOutline.gameObject.SetActive(showTargetGuidanceRing);
        guidanceRing.gameObject.SetActive(showTargetGuidanceRing);
        if (showTargetGuidanceRing)
        {
            UpdateGuidanceTargetIndicator(
                targetPosition,
                unclampedTargetPosition,
                targetIndicatorSize,
                targetIsOffscreen);
        }

        guidanceRayOutline.gameObject.SetActive(showTargetGuidanceRay);
        guidanceRay.gameObject.SetActive(showTargetGuidanceRay);
        if (showTargetGuidanceRay)
        {
            Vector2 start = playerMarker != null
                ? playerMarker.anchoredPosition
                : Vector2.zero;
            Vector2 direction = targetPosition - start;
            float distance = direction.magnitude;

            if (distance > 0.001f)
            {
                direction /= distance;
                float startInset = playerMarker != null
                    ? Mathf.Max(playerMarker.rect.width, playerMarker.rect.height) * 0.35f
                    : 0f;
                float endInset = showTargetGuidanceRing ? targetIndicatorSize * 0.42f : 0f;
                float totalInset = Mathf.Min(distance * 0.8f, startInset + endInset);
                float insetScale = startInset + endInset > 0f
                    ? totalInset / (startInset + endInset)
                    : 0f;

                Vector2 rayStart = start + direction * startInset * insetScale;
                Vector2 rayEnd = targetPosition - direction * endInset * insetScale;
                SetUiLine(
                    guidanceRayOutline,
                    rayStart,
                    rayEnd,
                    targetGuidanceRayWidth + 4f);
                SetUiLine(
                    guidanceRay,
                    rayStart,
                    rayEnd,
                    targetGuidanceRayWidth);
            }
            else
            {
                guidanceRayOutline.gameObject.SetActive(false);
                guidanceRay.gameObject.SetActive(false);
            }
        }
    }

    private void UpdateGuidanceTargetIndicator(
        Vector2 targetPosition,
        Vector2 unclampedTargetPosition,
        float indicatorSize,
        bool targetIsOffscreen)
    {
        Image outlineImage = guidanceRingOutline.GetComponent<Image>();
        Image indicatorImage = guidanceRing.GetComponent<Image>();
        Sprite indicatorSprite = targetIsOffscreen
            ? guidanceArrowSprite
            : guidanceRingSprite;

        outlineImage.sprite = indicatorSprite;
        indicatorImage.sprite = indicatorSprite;
        outlineImage.preserveAspect = true;
        indicatorImage.preserveAspect = true;

        guidanceRingOutline.anchoredPosition = targetPosition;
        guidanceRing.anchoredPosition = targetPosition;
        guidanceRingOutline.sizeDelta = Vector2.one * (indicatorSize + 3f);
        guidanceRing.sizeDelta = Vector2.one * indicatorSize;

        float rotation = 0f;
        if (targetIsOffscreen)
        {
            Vector2 direction = unclampedTargetPosition -
                                (playerMarker != null
                                    ? playerMarker.anchoredPosition
                                    : Vector2.zero);
            if (direction.sqrMagnitude > 0.0001f)
            {
                rotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            }
        }

        Quaternion indicatorRotation = Quaternion.Euler(0f, 0f, rotation);
        guidanceRingOutline.localRotation = indicatorRotation;
        guidanceRing.localRotation = indicatorRotation;
    }

    private Vector2 PlaceIndicatorOnMinimapEdge(
        Vector2 targetPosition,
        float horizontalLimit,
        float verticalLimit)
    {
        Vector2 origin = playerMarker != null
            ? playerMarker.anchoredPosition
            : Vector2.zero;
        Vector2 direction = targetPosition - origin;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return origin;
        }

        float horizontalScale = float.PositiveInfinity;
        if (Mathf.Abs(direction.x) > 0.0001f)
        {
            float horizontalEdge = direction.x > 0f
                ? horizontalLimit
                : -horizontalLimit;
            horizontalScale = (horizontalEdge - origin.x) / direction.x;
        }

        float verticalScale = float.PositiveInfinity;
        if (Mathf.Abs(direction.y) > 0.0001f)
        {
            float verticalEdge = direction.y > 0f
                ? verticalLimit
                : -verticalLimit;
            verticalScale = (verticalEdge - origin.y) / direction.y;
        }

        float edgeScale = Mathf.Max(0f, Mathf.Min(horizontalScale, verticalScale));
        return origin + direction * edgeScale;
    }

    private bool TryGetCurrentGuidanceTarget(
        out Vector3 worldPosition,
        out float targetRadius)
    {
        worldPosition = Vector3.zero;
        targetRadius = 0f;

        if (deliveryScoreManager == null)
        {
            deliveryScoreManager = Object.FindFirstObjectByType<DeliveryScoreManager>();
        }

        if (deliveryScoreManager != null &&
            deliveryScoreManager.isActiveAndEnabled &&
            deliveryScoreManager.TryGetGuidanceTarget(out worldPosition, out targetRadius))
        {
            return true;
        }

        if (manualDeliveryScoreManager == null)
        {
            manualDeliveryScoreManager = Object.FindFirstObjectByType<ManualDeliveryScoreManager>();
        }

        return manualDeliveryScoreManager != null &&
               manualDeliveryScoreManager.isActiveAndEnabled &&
               manualDeliveryScoreManager.TryGetGuidanceTarget(
                   out worldPosition,
                   out targetRadius);
    }

    private float CalculateTargetRingSize(float targetRadius, float minimapHeight)
    {
        float ringSize = minimumTargetRingSize;

        if (minimapCamera != null && minimapCamera.orthographic && minimapCamera.orthographicSize > 0f)
        {
            float pixelsPerWorldUnit = minimapHeight / (minimapCamera.orthographicSize * 2f);
            ringSize = targetRadius * 2f * pixelsPerWorldUnit;
        }

        float minimum = Mathf.Max(8f, minimumTargetRingSize);
        float maximum = Mathf.Max(minimum, maximumTargetRingSize);
        return Mathf.Clamp(ringSize, minimum, maximum);
    }

    private static void SetUiLine(
        RectTransform line,
        Vector2 start,
        Vector2 end,
        float width)
    {
        Vector2 delta = end - start;
        float length = delta.magnitude;
        line.anchoredPosition = (start + end) * 0.5f;
        line.sizeDelta = new Vector2(length, Mathf.Max(1f, width));
        line.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private void SetGuidanceVisualsActive(bool active)
    {
        if (guidanceRayOutline != null)
        {
            guidanceRayOutline.gameObject.SetActive(active && showTargetGuidanceRay);
        }

        if (guidanceRay != null)
        {
            guidanceRay.gameObject.SetActive(active && showTargetGuidanceRay);
        }

        if (guidanceRingOutline != null)
        {
            guidanceRingOutline.gameObject.SetActive(active && showTargetGuidanceRing);
        }

        if (guidanceRing != null)
        {
            guidanceRing.gameObject.SetActive(active && showTargetGuidanceRing);
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

    private Sprite CreateGuidanceRingSprite()
    {
        const int textureSize = 64;
        const float outerRadius = 30f;
        // Keep the center completely transparent so streets, roofs and the
        // target marker remain readable beneath the radius highlight.
        const float innerRadius = 27.5f;
        Vector2 center = Vector2.one * ((textureSize - 1) * 0.5f);

        guidanceRingTexture = new Texture2D(
            textureSize,
            textureSize,
            TextureFormat.RGBA32,
            false)
        {
            name = "RuntimeMinimapTargetRing",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[textureSize * textureSize];
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float outerAlpha = Mathf.Clamp01(outerRadius + 0.75f - distance);
                float innerAlpha = Mathf.Clamp01(distance - innerRadius + 0.75f);
                float alpha = Mathf.Min(outerAlpha, innerAlpha);
                pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        guidanceRingTexture.SetPixels(pixels);
        guidanceRingTexture.Apply(false, true);

        Sprite sprite = Sprite.Create(
            guidanceRingTexture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f);
        sprite.name = "RuntimeMinimapTargetRing";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private Sprite CreateGuidanceArrowSprite()
    {
        const int textureSize = 64;
        const float tipX = 60f;
        const float backX = 6f;
        const float halfHeight = 25f;
        float centerY = (textureSize - 1) * 0.5f;

        guidanceArrowTexture = new Texture2D(
            textureSize,
            textureSize,
            TextureFormat.RGBA32,
            false)
        {
            name = "RuntimeMinimapTargetArrow",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[textureSize * textureSize];
        float arrowLength = tipX - backX;
        for (int y = 0; y < textureSize; y++)
        {
            float verticalDistance = Mathf.Abs(y - centerY);
            float rightEdge = backX + arrowLength *
                              (1f - verticalDistance / halfHeight);

            for (int x = 0; x < textureSize; x++)
            {
                float edgeDistance = Mathf.Min(
                    x - backX,
                    Mathf.Min(rightEdge - x, halfHeight - verticalDistance));
                float alpha = Mathf.Clamp01(edgeDistance + 1f);
                pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        guidanceArrowTexture.SetPixels(pixels);
        guidanceArrowTexture.Apply(false, true);

        Sprite sprite = Sprite.Create(
            guidanceArrowTexture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f);
        sprite.name = "RuntimeMinimapTargetArrow";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
