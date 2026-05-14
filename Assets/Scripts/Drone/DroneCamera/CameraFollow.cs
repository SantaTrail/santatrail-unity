using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform drone;
    public MAVLinkReceiver receiver;
    [Tooltip("If true, camera is mounted to drone at local offset (no chase follow).")]
    public bool attachToDrone = true;
    public Vector3 offset = new Vector3(0, 2, -5);
    public float smoothSpeed = 5f;
    [Tooltip("If true, offset rotates with drone yaw only (ignores roll/pitch).")]
    public bool followYawOnly = true;
    [Tooltip("Lock camera to look at drone each frame.")]
    public bool alwaysLookAtDrone = true;
    [Tooltip("Face the direction the drone is moving (flight direction) instead of always looking at drone center.")]
    public bool faceFlightDirection = true;
    [Tooltip("How quickly the camera rotation aligns to the target direction.")]
    public float rotationSmoothSpeed = 6f;
    [Tooltip("Minimum speed needed before using movement direction for facing.")]
    public float minFlightSpeedForFacing = 0.2f;
    [Tooltip("Seconds to keep velocity-heading after it becomes weak, reducing twitch.")]
    public float velocityHeadingHoldSeconds = 0.4f;

    private Vector3 lastDronePos;
    private Rigidbody droneRb;
    private Vector3 smoothedLookDir = Vector3.forward;
    private float lastVelocityHeadingTime = -999f;

    void Awake()
    {
        if (drone == null)
        {
            GameObject droneObj = GameObject.FindGameObjectWithTag("Drone");
            if (droneObj != null)
            {
                drone = droneObj.transform;
            }
        }

        if (drone != null)
        {
            lastDronePos = drone.position;
            droneRb = drone.GetComponent<Rigidbody>();
            if (receiver == null)
            {
                receiver = drone.GetComponent<MAVLinkReceiver>();
            }
            Vector3 initialForward = new Vector3(drone.forward.x, 0f, drone.forward.z);
            if (initialForward.sqrMagnitude > 0.0001f)
            {
                smoothedLookDir = initialForward.normalized;
            }
        }
    }

    void LateUpdate()
    {
        if (drone == null) return;

        if (attachToDrone)
        {
            if (transform.parent != drone)
            {
                transform.SetParent(drone, true);
            }

            transform.localPosition = offset;
            transform.localRotation = Quaternion.identity;
            return;
        }

        Vector3 desiredOffset;
        if (followYawOnly)
        {
            Quaternion yawOnly = Quaternion.Euler(0f, drone.eulerAngles.y, 0f);
            desiredOffset = yawOnly * offset;
        }
        else
        {
            desiredOffset = drone.TransformDirection(offset);
        }

        Vector3 desiredPosition = drone.position + desiredOffset;

        Vector3 flightVelocity = (drone.position - lastDronePos) / Mathf.Max(Time.deltaTime, 0.0001f);
        if (droneRb != null && droneRb.linearVelocity.sqrMagnitude > 0.01f)
        {
            flightVelocity = droneRb.linearVelocity;
        }
        lastDronePos = drone.position;

        Quaternion targetRotation = transform.rotation;
        bool hasFlightDirection = flightVelocity.sqrMagnitude > (minFlightSpeedForFacing * minFlightSpeedForFacing);
        Vector3 desiredLookDir = smoothedLookDir;

        if (faceFlightDirection && hasFlightDirection)
        {
            Vector3 lookDir = flightVelocity.normalized;
            if (lookDir.sqrMagnitude > 0.0001f)
            {
                desiredLookDir = lookDir;
                lastVelocityHeadingTime = Time.time;
            }
        }
        else if (faceFlightDirection && (Time.time - lastVelocityHeadingTime) <= velocityHeadingHoldSeconds)
        {
            // Keep last velocity heading briefly to avoid rapid source-switch twitch.
            desiredLookDir = smoothedLookDir;
        }
        else if (faceFlightDirection && receiver != null)
        {
            float yawDeg = receiver.hasHeadingDeg ? receiver.headingDeg : receiver.yaw * Mathf.Rad2Deg;
            Vector3 yawDir = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            if (yawDir.sqrMagnitude > 0.0001f)
            {
                desiredLookDir = yawDir.normalized;
            }
        }
        else if (faceFlightDirection)
        {
            Vector3 droneForwardFlat = new Vector3(drone.forward.x, 0f, drone.forward.z);
            if (droneForwardFlat.sqrMagnitude > 0.0001f)
            {
                desiredLookDir = droneForwardFlat.normalized;
            }
        }
        else if (alwaysLookAtDrone)
        {
            Vector3 lookAtDir = (drone.position - transform.position).normalized;
            if (lookAtDir.sqrMagnitude > 0.0001f)
            {
                desiredLookDir = lookAtDir;
            }
        }

        float dirT = 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime);
        smoothedLookDir = Vector3.Slerp(smoothedLookDir, desiredLookDir.normalized, dirT);
        if (smoothedLookDir.sqrMagnitude > 0.0001f)
        {
            targetRotation = Quaternion.LookRotation(smoothedLookDir.normalized, Vector3.up);
        }

        float rotT = 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotT);
    }
}
