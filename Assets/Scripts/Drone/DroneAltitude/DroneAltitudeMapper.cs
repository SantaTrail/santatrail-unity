using UnityEngine;
using Esri.ArcGISMapsSDK.Components;

public class DroneAltitudeMapper : MonoBehaviour
{
    public Terrain terrain;

    [Header("Altitude from autopilot")]
    public float flightAltitude = 30f; // 🔥 DEFAULT SAFE ALTITUDE

    [Header("Smoothing")]
    public float heightSmoothTime = 0.18f;

    private float heightVelocity;
    private Transform targetTransform;

    void Awake()
    {
        ArcGISLocationComponent locationComponent = GetComponentInChildren<ArcGISLocationComponent>(true);
        targetTransform = locationComponent != null ? locationComponent.transform : transform;
    }

    void LateUpdate()
    {
        if (terrain == null) return;

        Vector3 pos = targetTransform.position;

        float ground = terrain.SampleHeight(pos);

        // ✅ prevent going underground
        float targetHeight = ground + flightAltitude;

        // 🔥 ONLY APPLY IF HIGHER (important fix)
        if (pos.y < targetHeight)
        {
            pos.y = Mathf.SmoothDamp(
                pos.y,
                targetHeight,
                ref heightVelocity,
                Mathf.Max(0.01f, heightSmoothTime)
            );

            if (pos.y > targetHeight)
            {
                pos.y = targetHeight;
            }

            targetTransform.position = pos;
        }
        else
        {
            heightVelocity = 0f;
        }
    }
}
