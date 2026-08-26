using UnityEngine;

/// <summary>
/// Very small house LOD helper:
/// - shows the detailed building when the viewer is close
/// - swaps to a cheap proxy cube when the viewer is far away
/// </summary>
public class HouseSimpleLOD : MonoBehaviour
{
    [Header("Viewer")]
    [Tooltip("Viewer transform used for distance checks. If empty, Camera.main is used.")]
    [SerializeField] Transform viewer;

    [Header("Distances")]
    [Min(0f)]
    [SerializeField] float detailedDistance = 500f;

    [Min(0f)]
    [SerializeField] float cullDistance = 800f;

    [Min(0.05f)]
    [SerializeField] float refreshInterval = 0.5f;

    [Header("Editor Preview")]
    [Tooltip("Keep detailed house meshes visible while testing in the Unity Editor. The far-distance proxy remains enabled in player builds.")]
    [SerializeField] bool useDistanceLodInEditorPlayMode = false;

    [Header("Proxy")]
    [SerializeField] bool createProxyCube = true;

    Renderer[] detailedRenderers = System.Array.Empty<Renderer>();
    Renderer proxyRenderer;
    bool showingDetailed = true;
    float nextRefreshTime;

    public void Configure(
        Transform viewerTransform,
        float nearDistance,
        float farDistance,
        float intervalSeconds)
    {
        viewer = viewerTransform;
        detailedDistance = Mathf.Max(0f, nearDistance);
        cullDistance = Mathf.Max(detailedDistance, farDistance);
        refreshInterval = Mathf.Max(0.05f, intervalSeconds);
        nextRefreshTime = 0f;
        ApplyVisibility(force: true);
    }

    void Awake()
    {
        CacheDetailedRenderers();
        CreateProxyIfNeeded();
        ApplyVisibility(force: true);
    }

    void OnEnable()
    {
        nextRefreshTime = 0f;
        ApplyVisibility(force: true);
    }

    void LateUpdate()
    {
        if (Time.time < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.time + refreshInterval;
        ApplyVisibility(force: false);
    }

    void CacheDetailedRenderers()
    {
        detailedRenderers = GetComponentsInChildren<Renderer>(true);
    }

    void CreateProxyIfNeeded()
    {
        if (!createProxyCube || proxyRenderer != null)
        {
            return;
        }

        Bounds localBounds;
        if (!TryMeasureLocalBounds(out localBounds))
        {
            return;
        }

        GameObject proxyObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        proxyObject.name = "FarLODProxy";
        proxyObject.transform.SetParent(transform, false);
        proxyObject.transform.localPosition = localBounds.center;
        proxyObject.transform.localRotation = Quaternion.identity;
        proxyObject.transform.localScale = new Vector3(
            Mathf.Max(0.25f, localBounds.size.x),
            Mathf.Max(0.25f, localBounds.size.y),
            Mathf.Max(0.25f, localBounds.size.z)
        );

        Collider collider = proxyObject.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        proxyRenderer = proxyObject.GetComponent<Renderer>();
        proxyRenderer.enabled = false;
    }

    bool TryMeasureLocalBounds(out Bounds localBounds)
    {
        localBounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        if (detailedRenderers == null || detailedRenderers.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < detailedRenderers.Length; i++)
        {
            Renderer renderer = detailedRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            Vector3[] corners =
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

            for (int j = 0; j < corners.Length; j++)
            {
                Vector3 localCorner = transform.InverseTransformPoint(corners[j]);
                if (!hasBounds)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }

        return hasBounds;
    }

    Transform ResolveViewer()
    {
        if (viewer != null)
        {
            return viewer;
        }

        if (Camera.main != null)
        {
            viewer = Camera.main.transform;
        }

        return viewer;
    }

    void ApplyVisibility(bool force)
    {
        if (!Application.isPlaying ||
            (Application.isEditor && !useDistanceLodInEditorPlayMode))
        {
            SetDetailedVisibility(true);
            return;
        }

        Transform focus = ResolveViewer();
        if (focus == null)
        {
            // If the viewer is not ready yet, keep the house visible rather
            // than hiding it and making the level look empty.
            SetDetailedVisibility(true);
            return;
        }

        float distance = Vector3.Distance(focus.position, transform.position);
        bool desiredDetailed = showingDetailed;

        if (distance <= detailedDistance)
        {
            desiredDetailed = true;
        }
        else if (distance >= cullDistance)
        {
            desiredDetailed = false;
        }

        if (!force && desiredDetailed == showingDetailed)
        {
            return;
        }

        showingDetailed = desiredDetailed;

        SetDetailedVisibility(showingDetailed);
    }

    void SetDetailedVisibility(bool visible)
    {
        if (detailedRenderers != null)
        {
            for (int i = 0; i < detailedRenderers.Length; i++)
            {
                Renderer renderer = detailedRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = visible;
            }
        }

        if (proxyRenderer != null)
        {
            proxyRenderer.enabled = !visible;
        }
    }
}
