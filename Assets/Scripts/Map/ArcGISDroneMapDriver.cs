using Esri.ArcGISMapsSDK.Components;
using Esri.HPFramework;
using UnityEngine;

/// <summary>
/// Drives ArcGIS streaming from the visible drone camera while remaining a
/// child of ArcGISMap, which is required by ArcGISCameraComponent.
/// </summary>
public class ArcGISDroneMapDriver : MonoBehaviour
{
    [Header("Streaming Performance")]
    [Range(0.1f, 1f)]
    [SerializeField] float qualityScalingFactor = 0.65f;
    [Min(0.05f)]
    [SerializeField] float minimumPositionChangeMeters = 1f;
    [Min(0.05f)]
    [SerializeField] float minimumRotationChangeDegrees = 1f;

    Camera sourceCamera;
    Camera driverCamera;
    HPTransform sourceHighPrecisionTransform;
    HPTransform driverHighPrecisionTransform;
    ArcGISCameraComponent arcGisCamera;
    Vector3 lastSyncedSourcePosition;
    Quaternion lastSyncedSourceRotation;
    float lastSyncedFieldOfView;
    float lastSyncedAspect;
    int lastSyncedPixelWidth;
    int lastSyncedPixelHeight;
    bool hasSyncedSourceState;
    bool holdCurrentViewForReadiness;

    public ArcGISCameraComponent ArcGisCamera => arcGisCamera;

    public void Configure(Camera visibleCamera)
    {
        sourceCamera = visibleCamera;
        sourceHighPrecisionTransform = sourceCamera != null
            ? sourceCamera.GetComponent<HPTransform>()
            : null;

        driverCamera = GetComponent<Camera>();
        driverHighPrecisionTransform = GetComponent<HPTransform>();
        arcGisCamera = GetComponent<ArcGISCameraComponent>();

        if (driverCamera != null)
        {
            // This camera supplies ArcGIS with a viewpoint only. The drone
            // camera remains the sole camera that renders to the Game view.
            driverCamera.enabled = false;
        }

        if (arcGisCamera != null)
        {
            arcGisCamera.UpdateClippingPlanes = false;
            arcGisCamera.UseCameraViewportProperties = false;
            arcGisCamera.qualityScalingFactor = Mathf.Clamp(
                qualityScalingFactor,
                0.1f,
                1f
            );
        }

        SyncFromSource(force: true);
    }

    void LateUpdate()
    {
        SyncFromSource(force: false);
    }

    public void HoldCurrentViewForReadiness()
    {
        // Capture the final loading camera once, then stop tiny telemetry or
        // smoothing changes from restarting ArcGIS tile drawing every frame.
        holdCurrentViewForReadiness = false;
        SyncFromSource(force: true);
        holdCurrentViewForReadiness = true;
    }

    public void ReleaseReadinessHold()
    {
        holdCurrentViewForReadiness = false;
    }

    void SyncFromSource(bool force)
    {
        if (sourceCamera == null ||
            driverCamera == null ||
            driverHighPrecisionTransform == null ||
            arcGisCamera == null)
        {
            return;
        }

        if (holdCurrentViewForReadiness && !force)
        {
            return;
        }

        Vector3 sourcePosition = sourceCamera.transform.position;
        Quaternion sourceRotation = sourceCamera.transform.rotation;
        bool poseChanged =
            force ||
            !hasSyncedSourceState ||
            (sourcePosition - lastSyncedSourcePosition).sqrMagnitude >=
                minimumPositionChangeMeters * minimumPositionChangeMeters ||
            Quaternion.Angle(sourceRotation, lastSyncedSourceRotation) >=
                minimumRotationChangeDegrees;

        bool viewportChanged =
            force ||
            !hasSyncedSourceState ||
            !Mathf.Approximately(
                sourceCamera.fieldOfView,
                lastSyncedFieldOfView
            ) ||
            !Mathf.Approximately(sourceCamera.aspect, lastSyncedAspect) ||
            sourceCamera.pixelWidth != lastSyncedPixelWidth ||
            sourceCamera.pixelHeight != lastSyncedPixelHeight;

        sourceHighPrecisionTransform ??= sourceCamera.GetComponent<HPTransform>();

        if (poseChanged && sourceHighPrecisionTransform != null)
        {
            driverHighPrecisionTransform.UniversePosition =
                sourceHighPrecisionTransform.UniversePosition;
            driverHighPrecisionTransform.UniverseRotation =
                sourceHighPrecisionTransform.UniverseRotation;
        }
        else if (poseChanged)
        {
            driverCamera.transform.SetPositionAndRotation(
                sourceCamera.transform.position,
                sourceCamera.transform.rotation
            );
        }

        if (viewportChanged)
        {
            driverCamera.fieldOfView = sourceCamera.fieldOfView;
            driverCamera.aspect = sourceCamera.aspect;
            arcGisCamera.verticalFov = sourceCamera.fieldOfView;
            arcGisCamera.horizontalFov = CalculateHorizontalFov(
                sourceCamera.fieldOfView,
                sourceCamera.aspect
            );
            arcGisCamera.viewportSizeX =
                (uint)Mathf.Max(1, sourceCamera.pixelWidth);
            arcGisCamera.viewportSizeY =
                (uint)Mathf.Max(1, sourceCamera.pixelHeight);
        }

        if (poseChanged || viewportChanged)
        {
            lastSyncedSourcePosition = sourcePosition;
            lastSyncedSourceRotation = sourceRotation;
            lastSyncedFieldOfView = sourceCamera.fieldOfView;
            lastSyncedAspect = sourceCamera.aspect;
            lastSyncedPixelWidth = sourceCamera.pixelWidth;
            lastSyncedPixelHeight = sourceCamera.pixelHeight;
            hasSyncedSourceState = true;
        }
    }

    static float CalculateHorizontalFov(float verticalFov, float aspect)
    {
        float verticalRadians = verticalFov * Mathf.Deg2Rad;
        float horizontalRadians = 2f * Mathf.Atan(
            Mathf.Tan(verticalRadians * 0.5f) * Mathf.Max(0.01f, aspect)
        );
        return horizontalRadians * Mathf.Rad2Deg;
    }
}
