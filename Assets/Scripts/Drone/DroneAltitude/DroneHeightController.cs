using UnityEngine;
using Esri.ArcGISMapsSDK.Components;

public class DroneHeightController : MonoBehaviour
{
    public Terrain terrain;
    public float safeHeight = 5f; // distance above ground
    [Header("Smoothing")]
    public float heightSmoothTime = 0.18f;

    private float heightVelocity;
    private Transform targetTransform;

    void Awake()
    {
        ArcGISLocationComponent locationComponent = GetComponentInChildren<ArcGISLocationComponent>(true);
        targetTransform = locationComponent != null ? locationComponent.transform : transform;
    }

    void Update()
    {
        if (terrain == null) return;

        Vector3 pos = targetTransform.position;

        // 🔥 get terrain height under drone
        float groundHeight = terrain.SampleHeight(pos);
        float targetHeight = groundHeight + safeHeight;

        // ✅ THIS IS WHERE IT GOES
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
