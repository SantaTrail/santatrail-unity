using UnityEngine;

[DefaultExecutionOrder(10000)]
public class DroneController : MonoBehaviour
{
    [Header("References")]
    public MAVLinkReceiver receiver;
    public Transform visualModel;

    [Header("Rotation")]
    public float rotationSmoothSpeed = 12f;

    [Tooltip("Use 180 if drone faces backward")]
    public float modelYawOffset = 180f;

    [Tooltip("Invert heading direction")]
    public bool invertYaw = false;

    [Tooltip("Use GPS heading when available; disable to follow ATTITUDE yaw like QGroundControl")]
    public bool useGpsHeadingForYaw = false;
    
    [Tooltip("If ATTITUDE yaw is stale for this many seconds, fall back to GPS heading")]
    public float attitudeTimeoutSeconds = 0.3f;

    private Quaternion targetRotation;

    void Start()
    {
        if (receiver == null)
        {
            receiver = GetComponent<MAVLinkReceiver>();
        }

        if (visualModel == null)
        {
            Transform fans = transform.Find("Fans");
            visualModel = fans != null ? fans : transform;
        }
    }

    void LateUpdate()
    {
        if (receiver == null)
            return;

        // ==========================================
        // GET YAW
        // ==========================================

        // QGroundControl attitude view follows ATTITUDE yaw.
        // GPS heading can be stale when the vehicle rotates in place.
        float yawDeg = receiver.yaw * Mathf.Rad2Deg;
        bool attitudeFresh = (Time.time - receiver.lastAttitudeTime) <= attitudeTimeoutSeconds;

        if ((useGpsHeadingForYaw || !attitudeFresh) && receiver.hasHeadingDeg)
        {
            yawDeg = receiver.headingDeg;
        }

        if (invertYaw)
        {
            yawDeg = -yawDeg;
        }

        // ==========================================
        // BUILD ROTATION
        // ==========================================
float pitchDeg = receiver.pitch * Mathf.Rad2Deg;
float rollDeg = receiver.roll * Mathf.Rad2Deg;

targetRotation =
    Quaternion.Euler(
        -pitchDeg,
        -yawDeg,
        rollDeg
    )
    * Quaternion.Euler(0f, modelYawOffset, 0f);
        // ==========================================
        // APPLY SMOOTH ROTATION
        // ==========================================

        float t =
            1f - Mathf.Exp(
                -rotationSmoothSpeed * Time.deltaTime
            );

        Transform target = visualModel != null ? visualModel : transform;
        target.rotation = Quaternion.Slerp(
            target.rotation,
            targetRotation,
            t
        );
    }
}
