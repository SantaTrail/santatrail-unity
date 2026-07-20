using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class MinimapDroneArrow : MonoBehaviour
{
    [Header("Drone")]
    [SerializeField] private Transform drone;

    [Header("Rotation")]
    [SerializeField] private float rotationOffset = 0f;
    [SerializeField] private bool reverseRotation = true;

    private RectTransform arrowRect;

    private void Awake()
    {
        arrowRect = GetComponent<RectTransform>();
        TryResolveDrone();
    }

    private void Start()
    {
        TryResolveDrone();
    }

    private void LateUpdate()
    {
        // Only rotate actual marker graphics. If this component is left on a UI
        // container, it should not rotate the whole minimap.
        if (GetComponent<Image>() == null)
        {
            return;
        }

        if (!TryResolveDrone())
        {
            return;
        }

        float heading = drone.eulerAngles.y;

        float arrowRotation = reverseRotation
            ? -heading
            : heading;

        arrowRect.localEulerAngles = new Vector3(
            0f,
            0f,
            arrowRotation + rotationOffset
        );
    }

    private bool TryResolveDrone()
    {
        if (drone != null)
        {
            return true;
        }

        MAVLinkReceiver receiver = Object.FindFirstObjectByType<MAVLinkReceiver>();
        if (receiver != null)
        {
            drone = receiver.transform;
            return true;
        }

        GameObject droneObject =
            GameObject.FindGameObjectWithTag("Drone");

        if (droneObject != null)
        {
            drone = droneObject.transform;
            return true;
        }

        droneObject = GameObject.Find("Drone");
        if (droneObject != null)
        {
            drone = droneObject.transform;
            return true;
        }

        return false;
    }
}
