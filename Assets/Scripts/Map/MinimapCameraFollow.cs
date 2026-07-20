using UnityEngine;

[RequireComponent(typeof(Camera))]
public class MinimapCameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform drone;

    [Header("Camera")]
    [SerializeField] private float height = 150f;
    [SerializeField] private float followSpeed = 10f;

    [Tooltip("Off = north stays at the top. On = map rotates with the drone.")]
    [SerializeField] private bool rotateWithDrone = false;

    [Tooltip("Use this when the top of your ArcGIS map is not Unity's forward direction.")]
    [SerializeField] private float northOffset = 0f;

    private void Awake()
    {
        TryResolveDrone();
    }

    private void Start()
    {
        TryResolveDrone();
    }

    private void LateUpdate()
    {
        if (!TryResolveDrone())
        {
            return;
        }

        Vector3 targetPosition = new Vector3(
            drone.position.x,
            drone.position.y + height,
            drone.position.z
        );

        // Smooth movement that behaves consistently at different frame rates.
        float smoothAmount = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            smoothAmount
        );

        float heading = northOffset;

        if (rotateWithDrone)
        {
            heading += drone.eulerAngles.y;
        }

        transform.rotation = Quaternion.Euler(90f, heading, 0f);
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

    public void SetDrone(Transform newDrone)
    {
        drone = newDrone;
    }
}
