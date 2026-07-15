using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(10000)]
public class DroneController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("MAVLinkReceiver on this object or a parent. It is found automatically when left empty.")]
    public MAVLinkReceiver receiver;

    [Tooltip(
        "Optional child object containing only the visible drone model. " +
        "Leave empty when the visible mesh is on this root object. " +
        "Do not assign this root drone object here."
    )]
    public Transform visualModel;

    [Tooltip("Uniform scale applied only when Visual Model is a separate child.")]
    [Min(0.0001f)]
    public float visualModelScale = 1f;

    [Header("Root Transform Ownership")]
    [Tooltip(
        "Keep OFF when MAVLinkReceiver controls the ArcGIS drone root. " +
        "Turning this on lets this script modify the root position too."
    )]
    public bool allowRootPositionControl = false;

    [Tooltip(
        "Keep OFF when MAVLinkReceiver controls the ArcGIS drone root rotation. " +
        "This prevents two scripts from fighting over the same Transform."
    )]
    public bool allowRootRotationControl = false;

    [Header("Movement Smoothing")]
    [Tooltip(
        "Optional extra position smoothing. Keep this off when MAVLinkReceiver " +
        "already performs position smoothing."
    )]
    public bool smoothPosition = false;

    [Tooltip(
        "When Visual Model is a separate child, smooth only that child and leave " +
        "the ArcGIS/MAVLink root untouched."
    )]
    public bool smoothChildVisualOnly = true;

    [Min(0.01f)]
    public float positionSmoothTime = 0.14f;

    [Min(0.1f)]
    public float maximumPositionSpeed = 100f;

    [Min(0f)]
    [Tooltip("Position jumps above this distance are applied immediately.")]
    public float teleportDistance = 20f;

    [Min(0.000001f)]
    public float telemetryPositionEpsilon = 0.0001f;

    [Header("Rotation")]
    [Min(0.01f)]
    public float rotationSmoothSpeed = 10f;

    [Min(0f)]
    public float rotationDeadZoneDegrees = 0.12f;

    [Min(1f)]
    public float maximumRotationSpeed = 360f;

    [Tooltip("Yaw correction for the imported drone model.")]
    public float modelYawOffset = 180f;

    [Tooltip("Invert heading direction.")]
    public bool invertYaw = false;

    [Tooltip("Use GPS heading when available instead of ATTITUDE yaw.")]
    public bool useGpsHeadingForYaw = false;

    [Min(0f)]
    [Tooltip("Fall back to GPS heading when ATTITUDE data is older than this.")]
    public float attitudeTimeoutSeconds = 0.3f;

    [Header("Manual Propeller Animation")]
    [Tooltip(
        "Keep this off until the drone is stable. This script does not use the " +
        "imported FBX animation or PlayableGraph."
    )]
    public bool useManualPropellerSpin = false;

    [Tooltip(
        "Assign only the individual propeller transforms. Do not assign Fans, " +
        "the whole drone, or another shared parent."
    )]
    public Transform[] propellers;

    [Tooltip(
        "Searches for child names containing Propeller, Rotor, Blade, or Fan. " +
        "Keep this off if your model has one shared Fans object."
    )]
    public bool autoFindPropellers = false;

    [Tooltip("Local axis around which each propeller rotates.")]
    public Vector3 propellerLocalAxis = Vector3.up;

    [Min(0f)]
    public float propellerSpinDegreesPerSecond = 1800f;

    public bool alternatePropellerDirection = true;

    private Vector3 positionVelocity;
    private Vector3 smoothedPosition;
    private Vector3 telemetryTargetPosition;
    private Vector3 lastAppliedRootPosition;

    private Vector3 visualModelInitialLocalPosition;
    private Transform visualModelInitialParent;

    private bool positionInitialized;

    private Quaternion[] propellerBaseRotations;
    private float propellerAngle;

    private void Awake()
    {
        ResolveReferences();
        ValidateVisualModel();
    }

    private void Start()
    {
        InitializeVisualModel();
        InitializePositionSmoothing();
        InitializePropellers();
    }

    private void ResolveReferences()
    {
        if (receiver == null)
        {
            receiver = GetComponent<MAVLinkReceiver>();

            if (receiver == null)
            {
                receiver = GetComponentInParent<MAVLinkReceiver>();
            }
        }

        if (receiver == null)
        {
            Debug.LogError(
                "DroneController: MAVLinkReceiver was not found on this drone " +
                "or its parent.",
                this
            );
        }

        // Only auto-assign a deliberately created child called DroneVisual.
        // Never fall back to assigning this root Transform.
        if (visualModel == null)
        {
            Transform foundVisual = transform.Find("DroneVisual");

            if (foundVisual != null)
            {
                visualModel = foundVisual;
            }
        }
    }

    private void ValidateVisualModel()
    {
        // The root object is controlled by ArcGIS/MAVLink. Treating it as a
        // separate visual model previously caused scaling and hierarchy issues.
        if (visualModel == transform)
        {
            Debug.LogWarning(
                "DroneController: Visual Model was assigned to the root drone. " +
                "The assignment has been cleared. Create a child named " +
                "'DroneVisual' if you need separate visual scaling.",
                this
            );

            visualModel = null;
        }
    }

    private void InitializeVisualModel()
    {
        if (visualModel == null)
        {
            return;
        }

        visualModelInitialParent = visualModel.parent;
        visualModelInitialLocalPosition = visualModel.localPosition;

        // Scale only a separate child model, never the ArcGIS/MAVLink root.
        visualModel.localScale =
            Vector3.one * Mathf.Max(0.0001f, visualModelScale);
    }

    private void InitializePositionSmoothing()
    {
        Transform output = GetPositionOutputTransform();

        if (output == null)
        {
            return;
        }

        smoothedPosition = output.position;
        telemetryTargetPosition = transform.position;
        lastAppliedRootPosition = transform.position;
        positionVelocity = Vector3.zero;
        positionInitialized = true;
    }

    private Transform GetPositionOutputTransform()
    {
        if (smoothChildVisualOnly && visualModel != null)
        {
            return visualModel;
        }

        // MAVLinkReceiver owns the ArcGIS root by default.
        return allowRootPositionControl ? transform : null;
    }

    private Transform GetRotationOutputTransform()
    {
        if (visualModel != null)
        {
            return visualModel;
        }

        // Do not rotate the ArcGIS root unless explicitly requested.
        // MAVLinkReceiver should be the sole root rotation controller.
        return allowRootRotationControl ? transform : null;
    }

    private void LateUpdate()
    {
        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);

        UpdateSmoothedPosition(deltaTime);
        UpdateSmoothedRotation(deltaTime);
        UpdatePropellers(deltaTime);
    }

    private void UpdateSmoothedPosition(float deltaTime)
    {
        if (!smoothPosition || !positionInitialized)
        {
            return;
        }

        Transform output = GetPositionOutputTransform();

        if (output == null)
        {
            return;
        }

        Vector3 targetPosition;

        if (output == visualModel &&
            visualModelInitialParent != null)
        {
            targetPosition = visualModelInitialParent.TransformPoint(
                visualModelInitialLocalPosition
            );
        }
        else
        {
            Vector3 currentRootPosition = transform.position;
            float epsilonSquared =
                telemetryPositionEpsilon * telemetryPositionEpsilon;

            if ((currentRootPosition - lastAppliedRootPosition).sqrMagnitude >
                epsilonSquared)
            {
                telemetryTargetPosition = currentRootPosition;
            }

            targetPosition = telemetryTargetPosition;
        }

        if (teleportDistance > 0f &&
            Vector3.Distance(smoothedPosition, targetPosition) >
            teleportDistance)
        {
            smoothedPosition = targetPosition;
            positionVelocity = Vector3.zero;
        }
        else
        {
            smoothedPosition = Vector3.SmoothDamp(
                smoothedPosition,
                targetPosition,
                ref positionVelocity,
                Mathf.Max(0.01f, positionSmoothTime),
                Mathf.Max(0.1f, maximumPositionSpeed),
                deltaTime
            );
        }

        output.position = smoothedPosition;

        if (output == transform)
        {
            lastAppliedRootPosition = smoothedPosition;
        }
    }

    private void UpdateSmoothedRotation(float deltaTime)
    {
        if (receiver == null)
        {
            return;
        }

        float yawDegrees = receiver.yaw * Mathf.Rad2Deg;

        bool attitudeFresh =
            (Time.time - receiver.lastAttitudeTime) <=
            attitudeTimeoutSeconds;

        if ((useGpsHeadingForYaw || !attitudeFresh) &&
            receiver.hasHeadingDeg)
        {
            yawDegrees = receiver.headingDeg;
        }

        if (invertYaw)
        {
            yawDegrees = -yawDegrees;
        }

        float pitchDegrees = receiver.pitch * Mathf.Rad2Deg;
        float rollDegrees = receiver.roll * Mathf.Rad2Deg;

        Quaternion targetRotation =
            Quaternion.Euler(
                -pitchDegrees,
                -yawDegrees,
                rollDegrees
            ) *
            Quaternion.Euler(0f, modelYawOffset, 0f);

        Transform rotationTarget = GetRotationOutputTransform();

        if (rotationTarget == null)
        {
            return;
        }

        float angleDifference = Quaternion.Angle(
            rotationTarget.rotation,
            targetRotation
        );

        if (angleDifference <= rotationDeadZoneDegrees)
        {
            return;
        }

        float interpolation =
            1f - Mathf.Exp(-rotationSmoothSpeed * deltaTime);

        Quaternion smoothedRotation = Quaternion.Slerp(
            rotationTarget.rotation,
            targetRotation,
            interpolation
        );

        rotationTarget.rotation = Quaternion.RotateTowards(
            rotationTarget.rotation,
            smoothedRotation,
            maximumRotationSpeed * deltaTime
        );
    }

    private void InitializePropellers()
    {
        if (autoFindPropellers &&
            (propellers == null || propellers.Length == 0))
        {
            AutoFindPropellerTransforms();
        }

        CachePropellerBaseRotations();
    }

    private void AutoFindPropellerTransforms()
    {
        Transform searchRoot =
            visualModel != null ? visualModel : transform;

        Transform[] children =
            searchRoot.GetComponentsInChildren<Transform>(true);

        List<Transform> found = new List<Transform>();

        foreach (Transform candidate in children)
        {
            if (candidate == null || candidate == searchRoot)
            {
                continue;
            }

            string lowerName = candidate.name.ToLowerInvariant();

            bool nameMatches =
                lowerName.Contains("propeller") ||
                lowerName.Contains("rotor") ||
                lowerName.Contains("blade");

            // Deliberately do not match a shared object named "Fans".
            if (!nameMatches)
            {
                continue;
            }

            bool hasMatchingChild = false;

            for (int i = 0; i < candidate.childCount; i++)
            {
                string childName =
                    candidate.GetChild(i).name.ToLowerInvariant();

                if (childName.Contains("propeller") ||
                    childName.Contains("rotor") ||
                    childName.Contains("blade"))
                {
                    hasMatchingChild = true;
                    break;
                }
            }

            if (!hasMatchingChild)
            {
                found.Add(candidate);
            }
        }

        propellers = found.ToArray();

        Debug.Log(
            $"DroneController: found {propellers.Length} individual " +
            "propeller transforms.",
            this
        );
    }

    private void CachePropellerBaseRotations()
    {
        if (propellers == null)
        {
            propellerBaseRotations = System.Array.Empty<Quaternion>();
            return;
        }

        propellerBaseRotations =
            new Quaternion[propellers.Length];

        for (int i = 0; i < propellers.Length; i++)
        {
            propellerBaseRotations[i] =
                propellers[i] != null
                    ? propellers[i].localRotation
                    : Quaternion.identity;
        }
    }

    private void UpdatePropellers(float deltaTime)
    {
        if (!useManualPropellerSpin ||
            propellers == null ||
            propellerBaseRotations == null ||
            propellerLocalAxis.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        propellerAngle = Mathf.Repeat(
            propellerAngle +
            propellerSpinDegreesPerSecond * deltaTime,
            360f
        );

        Vector3 spinAxis = propellerLocalAxis.normalized;

        for (int i = 0; i < propellers.Length; i++)
        {
            Transform propeller = propellers[i];

            if (propeller == null ||
                i >= propellerBaseRotations.Length)
            {
                continue;
            }

            float direction =
                alternatePropellerDirection && i % 2 == 1
                    ? -1f
                    : 1f;

            propeller.localRotation =
                propellerBaseRotations[i] *
                Quaternion.AngleAxis(
                    propellerAngle * direction,
                    spinAxis
                );
        }
    }

    private void OnValidate()
    {
        visualModelScale = Mathf.Max(0.0001f, visualModelScale);
        positionSmoothTime = Mathf.Max(0.01f, positionSmoothTime);
        maximumPositionSpeed = Mathf.Max(0.1f, maximumPositionSpeed);
        rotationSmoothSpeed = Mathf.Max(0.01f, rotationSmoothSpeed);
        maximumRotationSpeed = Mathf.Max(1f, maximumRotationSpeed);
        attitudeTimeoutSeconds = Mathf.Max(0f, attitudeTimeoutSeconds);
        propellerSpinDegreesPerSecond =
            Mathf.Max(0f, propellerSpinDegreesPerSecond);

        if (visualModel == transform)
        {
            visualModel = null;
        }
    }
}
