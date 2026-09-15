using TMPro;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("SantaTrail/UI/Drone Boundary Warning")]
[DisallowMultipleComponent]
[DefaultExecutionOrder(9100)]
public sealed class DroneBoundaryWarning : MonoBehaviour
{
    [Header("Warning")]
    [Min(1f)]
    [SerializeField] float warningDistanceMeters = 40f;
    [Min(0f)]
    [SerializeField] float criticalDistanceMeters = 10f;
    [SerializeField] string approachingMessage =
        "MAP BOUNDARY AHEAD\nTurn back — {0:0} m remaining";
    [SerializeField] string criticalMessage =
        "STOP — MAP BOUNDARY\nOnly {0:0} m remaining";
    [SerializeField] string blockedMessage =
        "CANNOT GO FARTHER\nTurn back toward the houses";

    [Header("Visible Boundary")]
    [SerializeField] bool showBoundaryRing = true;
    [Range(48, 256)]
    [SerializeField] int ringSegments = 160;
    [Min(0.1f)]
    [SerializeField] float ringWidthMeters = 1.4f;
    [SerializeField] float ringGroundOffsetMeters = 0.35f;
    [SerializeField] Color normalRingColor =
        new Color(1f, 0.58f, 0.05f, 0.9f);
    [SerializeField] Color blockedRingColor =
        new Color(1f, 0.08f, 0.05f, 1f);

    MAVLinkReceiver receiver;
    CanvasGroup warningGroup;
    Image warningPanel;
    TextMeshProUGUI warningText;
    LineRenderer boundaryRing;
    Material boundaryMaterial;
    float displayedAlpha;
    double drawnRadius = -1.0;
    Vector3 drawnCenter;
    bool hasDrawnRing;

    void Awake()
    {
        receiver = GetComponent<MAVLinkReceiver>();
        CreateWarningUI();
        CreateBoundaryRing();
    }

    void LateUpdate()
    {
        if (receiver == null)
        {
            receiver = MAVLinkReceiver.Active;
        }

        if (receiver == null ||
            !receiver.HasPosition ||
            !receiver.TryGetPlayableBoundaryRadius(out double radiusMeters))
        {
            SetWarningVisible(false, false, false, 0f);
            SetRingVisible(false);
            return;
        }

        double remainingMeters =
            radiusMeters - receiver.MapGeofenceDistanceMeters;
        bool blocked = remainingMeters <= 0.5;
        bool critical = remainingMeters <= criticalDistanceMeters;
        bool approaching =
            receiver.HasRecentTelemetry &&
            remainingMeters <= Mathf.Max(1f, warningDistanceMeters);

        UpdateBoundaryRing(radiusMeters, blocked || critical);

        SetWarningVisible(
            approaching || blocked,
            blocked,
            critical,
            Mathf.Max(0f, (float)remainingMeters)
        );
    }

    void CreateWarningUI()
    {
        GameObject canvasObject = new GameObject(
            "DroneBoundaryWarningUI",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(CanvasGroup)
        );
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 24000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        warningGroup = canvasObject.GetComponent<CanvasGroup>();
        warningGroup.alpha = 0f;
        warningGroup.interactable = false;
        warningGroup.blocksRaycasts = false;

        GameObject panelObject = new GameObject(
            "BoundaryWarningPanel",
            typeof(RectTransform),
            typeof(Image),
            typeof(Outline)
        );
        panelObject.transform.SetParent(canvasObject.transform, false);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.82f);
        panelRect.anchorMax = new Vector2(0.5f, 0.82f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(680f, 112f);

        warningPanel = panelObject.GetComponent<Image>();
        warningPanel.color = new Color(0.82f, 0.34f, 0.02f, 0.94f);
        warningPanel.raycastTarget = false;

        Outline outline = panelObject.GetComponent<Outline>();
        outline.effectColor = new Color(1f, 0.82f, 0.25f, 0.95f);
        outline.effectDistance = new Vector2(3f, -3f);

        GameObject textObject = new GameObject(
            "BoundaryWarningText",
            typeof(RectTransform),
            typeof(TextMeshProUGUI)
        );
        textObject.transform.SetParent(panelObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(22f, 10f);
        textRect.offsetMax = new Vector2(-22f, -10f);

        warningText = textObject.GetComponent<TextMeshProUGUI>();
        warningText.alignment = TextAlignmentOptions.Center;
        warningText.color = Color.white;
        warningText.fontSize = 30f;
        warningText.fontStyle = FontStyles.Bold;
        warningText.textWrappingMode = TextWrappingModes.Normal;
        warningText.raycastTarget = false;
    }

    void CreateBoundaryRing()
    {
        GameObject ringObject = new GameObject("DroneFlightBoundaryRing");
        ringObject.transform.SetParent(transform, false);
        boundaryRing = ringObject.AddComponent<LineRenderer>();
        boundaryRing.useWorldSpace = true;
        boundaryRing.loop = true;
        boundaryRing.alignment = LineAlignment.View;
        boundaryRing.textureMode = LineTextureMode.Tile;
        boundaryRing.numCornerVertices = 2;
        boundaryRing.numCapVertices = 2;
        boundaryRing.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        boundaryRing.receiveShadows = false;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader != null)
        {
            boundaryMaterial = new Material(shader)
            {
                name = "Drone Boundary Ring Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            boundaryRing.material = boundaryMaterial;
        }

        SetRingVisible(false);
    }

    void UpdateBoundaryRing(double radiusMeters, bool danger)
    {
        if (!showBoundaryRing || receiver.locationComponent == null)
        {
            SetRingVisible(false);
            return;
        }

        if (!GameManager.hasHomeLocation)
        {
            SetRingVisible(false);
            return;
        }

        SetRingVisible(true);
        int segmentCount = Mathf.Clamp(ringSegments, 48, 256);
        if (boundaryRing.positionCount != segmentCount)
        {
            boundaryRing.positionCount = segmentCount;
        }

        Transform droneTransform = receiver.locationComponent.transform;
        const double metersPerDegreeLatitude = 111320.0;
        double metersPerDegreeLongitude =
            metersPerDegreeLatitude *
            System.Math.Max(
                0.0001,
                System.Math.Cos(GameManager.homeLat * System.Math.PI / 180.0)
            );
        Vector3 center = droneTransform.position - new Vector3(
            (float)((receiver.CurrentLongitude - GameManager.homeLon) *
                    metersPerDegreeLongitude),
            receiver.RelativeAltitudeMeters,
            (float)((receiver.CurrentLatitude - GameManager.homeLat) *
                    metersPerDegreeLatitude)
        );
        center.y += ringGroundOffsetMeters;

        float radius = Mathf.Max(1f, (float)radiusMeters);
        if (!hasDrawnRing ||
            System.Math.Abs(drawnRadius - radiusMeters) > 0.01 ||
            Vector3.Distance(drawnCenter, center) > 0.05f)
        {
            for (int i = 0; i < segmentCount; i++)
            {
                float angle = (i / (float)segmentCount) * Mathf.PI * 2f;
                boundaryRing.SetPosition(
                    i,
                    center + new Vector3(
                        Mathf.Cos(angle) * radius,
                        0f,
                        Mathf.Sin(angle) * radius
                    )
                );
            }

            drawnRadius = radiusMeters;
            drawnCenter = center;
            hasDrawnRing = true;
        }

        Color ringColor = danger ? blockedRingColor : normalRingColor;
        boundaryRing.startColor = ringColor;
        boundaryRing.endColor = ringColor;
        if (boundaryMaterial != null && boundaryMaterial.HasProperty("_Color"))
        {
            boundaryMaterial.color = ringColor;
        }
        boundaryRing.startWidth = Mathf.Max(0.1f, ringWidthMeters);
        boundaryRing.endWidth = Mathf.Max(0.1f, ringWidthMeters);
    }

    void SetWarningVisible(
        bool visible,
        bool blocked,
        bool critical,
        float remainingMeters)
    {
        float targetAlpha =
            visible && !PauseMenuController.IsPaused ? 1f : 0f;
        displayedAlpha = Mathf.MoveTowards(
            displayedAlpha,
            targetAlpha,
            Time.unscaledDeltaTime * 5f
        );

        if (warningGroup != null)
        {
            warningGroup.alpha = displayedAlpha;
        }

        if (!visible || warningText == null || warningPanel == null)
        {
            return;
        }

        warningText.text = blocked
            ? blockedMessage
            : string.Format(
                critical ? criticalMessage : approachingMessage,
                remainingMeters
            );
        warningPanel.color = blocked || critical
            ? new Color(0.78f, 0.04f, 0.03f, 0.96f)
            : new Color(0.82f, 0.34f, 0.02f, 0.94f);
    }

    void SetRingVisible(bool visible)
    {
        if (boundaryRing != null)
        {
            boundaryRing.enabled = visible;
        }
    }

    void OnDestroy()
    {
        if (boundaryMaterial != null)
        {
            Destroy(boundaryMaterial);
        }
    }

    void OnValidate()
    {
        warningDistanceMeters = Mathf.Max(1f, warningDistanceMeters);
        criticalDistanceMeters = Mathf.Max(0f, criticalDistanceMeters);
        ringSegments = Mathf.Clamp(ringSegments, 48, 256);
        ringWidthMeters = Mathf.Max(0.1f, ringWidthMeters);
    }
}
