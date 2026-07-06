using UnityEngine;

/// <summary>
/// Add this component to every manually placed building that can receive a present.
/// The ManualDeliveryScoreManager uses it instead of searching for OSM/ArcGIS spawned buildings.
/// </summary>
[DisallowMultipleComponent]
public class ManualDeliveryTarget : MonoBehaviour
{
    [Header("Target References")]
    [Tooltip("The complete building object. Leave empty to use the GameObject containing this component.")]
    public Transform buildingRoot;

    [Tooltip("Optional exact delivery point on the roof. Create an empty child and place it above the roof. When empty, renderer bounds are used.")]
    public Transform roofPoint;

    [Header("Mission")]
    [Tooltip("Uncheck this to keep the building in the scene without making it a delivery target.")]
    public bool includeInMission = true;

    [Tooltip("Optional readable ID used in debug messages.")]
    public string targetId = "";

    [Header("Optional Per-Building Overrides")]
    public bool overrideDeliveryRadius = false;
    [Min(0.1f)] public float deliveryRadius = 5f;

    public bool overrideMaxHeightAboveRoof = false;
    [Min(0.1f)] public float maxHeightAboveRoof = 3f;

    [Tooltip("When enabled, this building uses its own hide-after-delivery setting instead of the manager setting.")]
    public bool overrideHideAfterDelivery = false;
    public bool hideAfterDelivery = false;

    public Transform Root => buildingRoot != null ? buildingRoot : transform;
}
