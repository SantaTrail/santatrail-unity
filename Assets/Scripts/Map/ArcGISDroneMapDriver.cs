using Esri.ArcGISMapsSDK.Components;
using Esri.HPFramework;
using UnityEngine;

/// <summary>
/// Drives ArcGIS streaming from the visible drone camera while remaining a
/// child of ArcGISMap, which is required by ArcGISCameraComponent.
/// </summary>
public class ArcGISDroneMapDriver : MonoBehaviour
{
    Camera sourceCamera;
    Camera driverCamera;
    HPTransform sourceHighPrecisionTransform;
    HPTransform driverHighPrecisionTransform;
    ArcGISCameraComponent arcGisCamera;

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
        }

        SyncFromSource();
    }

    void LateUpdate()
    {
        SyncFromSource();
    }

    void SyncFromSource()
    {
        if (sourceCamera == null ||
            driverCamera == null ||
            driverHighPrecisionTransform == null ||
            arcGisCamera == null)
        {
            return;
        }

        sourceHighPrecisionTransform ??= sourceCamera.GetComponent<HPTransform>();

        if (sourceHighPrecisionTransform != null)
        {
            driverHighPrecisionTransform.UniversePosition =
                sourceHighPrecisionTransform.UniversePosition;
            driverHighPrecisionTransform.UniverseRotation =
                sourceHighPrecisionTransform.UniverseRotation;
        }
        else
        {
            driverCamera.transform.SetPositionAndRotation(
                sourceCamera.transform.position,
                sourceCamera.transform.rotation
            );
        }

        driverCamera.fieldOfView = sourceCamera.fieldOfView;
        driverCamera.aspect = sourceCamera.aspect;
        arcGisCamera.verticalFov = sourceCamera.fieldOfView;
        arcGisCamera.horizontalFov = CalculateHorizontalFov(
            sourceCamera.fieldOfView,
            sourceCamera.aspect
        );
        arcGisCamera.viewportSizeX = (uint)Mathf.Max(1, sourceCamera.pixelWidth);
        arcGisCamera.viewportSizeY = (uint)Mathf.Max(1, sourceCamera.pixelHeight);
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
