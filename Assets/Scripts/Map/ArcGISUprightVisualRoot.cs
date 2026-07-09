using UnityEngine;


[DisallowMultipleComponent]
public class ArcGISUprightVisualRoot : MonoBehaviour
{
    [SerializeField] float worldYawDegrees;
    [SerializeField] bool updateContinuously = true;

    public void Configure(float yawDegrees)
    {
        worldYawDegrees = NormalizeDegrees(yawDegrees);
        ApplyUprightRotation();
    }

    void Start()
    {
        ApplyUprightRotation();
    }

    void LateUpdate()
    {
        if (updateContinuously)
        {
            ApplyUprightRotation();
        }
    }

    void ApplyUprightRotation()
    {
        Transform parentTransform = transform.parent;
        if (parentTransform == null)
        {
            return;
        }

        Quaternion parentWorldRotation = parentTransform.rotation;
        if (!IsFinite(parentWorldRotation))
        {
            return;
        }

        Quaternion desiredWorldRotation = Quaternion.Euler(
            0f,
            worldYawDegrees,
            0f
        );

        Quaternion requiredLocalRotation =
            Quaternion.Inverse(parentWorldRotation) *
            desiredWorldRotation;

        if (!IsFinite(requiredLocalRotation))
        {
            return;
        }

        transform.localRotation = Normalize(requiredLocalRotation);
    }

    static Quaternion Normalize(Quaternion value)
    {
        float magnitude = Mathf.Sqrt(
            value.x * value.x +
            value.y * value.y +
            value.z * value.z +
            value.w * value.w
        );

        if (magnitude <= 0.000001f ||
            float.IsNaN(magnitude) ||
            float.IsInfinity(magnitude))
        {
            return Quaternion.identity;
        }

        float inverse = 1f / magnitude;
        return new Quaternion(
            value.x * inverse,
            value.y * inverse,
            value.z * inverse,
            value.w * inverse
        );
    }

    static bool IsFinite(Quaternion value)
    {
        return
            IsFinite(value.x) &&
            IsFinite(value.y) &&
            IsFinite(value.z) &&
            IsFinite(value.w);
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    static float NormalizeDegrees(float degrees)
    {
        degrees %= 360f;
        if (degrees < 0f)
        {
            degrees += 360f;
        }

        return degrees;
    }
}
