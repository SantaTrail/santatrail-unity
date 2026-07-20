using UnityEngine;

[DefaultExecutionOrder(11000)]
public class CameraFollow : MonoBehaviour
{
    [Header("References")]
    public Transform drone;
    public MAVLinkReceiver receiver;

    [Header("Follow Mode")]
    [Tooltip("Keep OFF for a smooth chase camera.")]
    public bool attachToDrone = false;

    [Tooltip("Camera offset in heading space. Z should usually be negative.")]
    public Vector3 offset = new Vector3(0f, 2f, -5f);

    [Min(0.01f)]
    public float positionSmoothSpeed = 4f;

    [Min(0.01f)]
    public float rotationSmoothSpeed = 3f;

    [Header("Heading Source")]
    [Tooltip(
        "Use MAVLink heading so the camera direction matches the vehicle arrow " +
        "shown in QGroundControl."
    )]
    public bool useMavlinkHeading = true;

    [Tooltip(
        "Extra correction for the camera heading. Start at 0. " +
        "Try 180 only when the camera faces exactly backward."
    )]
    public float cameraHeadingOffset = 0f;

    [Tooltip(
        "Invert heading only when the camera turns in the opposite direction " +
        "from QGroundControl."
    )]
    public bool invertHeading = false;

    [Tooltip(
        "When enabled, the camera looks forward in the QGroundControl heading. " +
        "When disabled, it looks at the drone."
    )]
    public bool lookForwardAlongHeading = true;

    [Tooltip("How far in front of the drone the camera looks.")]
    [Min(0.1f)]
    public float forwardLookDistance = 10f;

    [Tooltip(
        "Use movement direction only while the drone is moving. Keep OFF when " +
        "you need the camera to match QGroundControl's heading arrow exactly."
    )]
    public bool useFlightDirection = false;

    [Min(0f)]
    public float minimumFlightSpeed = 0.4f;

    [Header("Vertical Look")]
    [Tooltip("Vertical offset of the point the camera looks toward.")]
    public float lookHeightOffset = 0.5f;

    private Vector3 lastDronePosition;
    private Vector3 smoothedFlightDirection = Vector3.forward;
    private bool initialized;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        if (drone == null)
        {
            return;
        }

        lastDronePosition = drone.position;

        Vector3 initialForward = GetHeadingDirection();
        if (initialForward.sqrMagnitude > 0.0001f)
        {
            smoothedFlightDirection = initialForward.normalized;
        }

        initialized = true;
    }

    private void ResolveReferences()
    {
        if (receiver == null)
        {
            receiver = FindFirstObjectByType<MAVLinkReceiver>();
        }

        if (drone == null && receiver != null)
        {
            drone = receiver.transform;
        }

        if (drone == null)
        {
            GameObject droneObject =
                GameObject.FindGameObjectWithTag("Drone");

            if (droneObject != null)
            {
                drone = droneObject.transform;

                if (receiver == null)
                {
                    receiver =
                        droneObject.GetComponent<MAVLinkReceiver>();
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (drone == null)
        {
            return;
        }

        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);

        if (!initialized)
        {
            lastDronePosition = drone.position;
            initialized = true;
        }

        Vector3 headingDirection = GetHeadingDirection();

        Vector3 positionDirection = headingDirection;

        if (useFlightDirection)
        {
            Vector3 velocity =
                (drone.position - lastDronePosition) / deltaTime;

            Vector3 flatVelocity =
                Vector3.ProjectOnPlane(velocity, Vector3.up);

            if (flatVelocity.magnitude >= minimumFlightSpeed)
            {
                float directionT =
                    1f -
                    Mathf.Exp(-rotationSmoothSpeed * deltaTime);

                smoothedFlightDirection = Vector3.Slerp(
                    smoothedFlightDirection,
                    flatVelocity.normalized,
                    directionT
                );

                positionDirection =
                    smoothedFlightDirection.normalized;
            }
        }

        lastDronePosition = drone.position;

        Quaternion headingRotation =
            Quaternion.LookRotation(
                positionDirection,
                Vector3.up
            );

        Vector3 desiredPosition =
            drone.position +
            headingRotation * offset;

        if (attachToDrone)
        {
            transform.position = desiredPosition;
        }
        else
        {
            float positionT =
                1f -
                Mathf.Exp(-positionSmoothSpeed * deltaTime);

            transform.position = Vector3.Lerp(
                transform.position,
                desiredPosition,
                positionT
            );
        }

        Vector3 lookTarget;

        if (lookForwardAlongHeading)
        {
            lookTarget =
                drone.position +
                headingDirection * forwardLookDistance +
                Vector3.up * lookHeightOffset;
        }
        else
        {
            lookTarget =
                drone.position +
                Vector3.up * lookHeightOffset;
        }

        Vector3 lookDirection =
            lookTarget - transform.position;

        if (lookDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion targetRotation =
            Quaternion.LookRotation(
                lookDirection.normalized,
                Vector3.up
            );

        float rotationT =
            1f -
            Mathf.Exp(-rotationSmoothSpeed * deltaTime);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationT
        );
    }

    private Vector3 GetHeadingDirection()
    {
        float headingDegrees;

        if (useMavlinkHeading &&
            receiver != null &&
            receiver.hasHeadingDeg)
        {
            // GLOBAL_POSITION_INT.hdg / VFR_HUD heading:
            // 0 = North, 90 = East, increasing clockwise.
            headingDegrees = receiver.headingDeg;
        }
        else if (receiver != null)
        {
            headingDegrees =
                receiver.yaw * Mathf.Rad2Deg;
        }
        else
        {
            Vector3 flatForward =
                Vector3.ProjectOnPlane(
                    drone.forward,
                    Vector3.up
                );

            return flatForward.sqrMagnitude > 0.0001f
                ? flatForward.normalized
                : Vector3.forward;
        }

        if (invertHeading)
        {
            headingDegrees = -headingDegrees;
        }

        headingDegrees += cameraHeadingOffset;

        Quaternion headingRotation =
            Quaternion.Euler(
                0f,
                headingDegrees,
                0f
            );

        return headingRotation * Vector3.forward;
    }
}
