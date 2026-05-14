using UnityEngine;

public class DroneAltitudeMapper : MonoBehaviour
{
    public Terrain terrain;

    [Header("Altitude from autopilot")]
    public float flightAltitude = 30f; // 🔥 DEFAULT SAFE ALTITUDE

    void LateUpdate()
    {
        if (terrain == null) return;

        Vector3 pos = transform.position;

        float ground = terrain.SampleHeight(pos);

        // ✅ prevent going underground
        float targetHeight = ground + flightAltitude;

        // 🔥 ONLY APPLY IF HIGHER (important fix)
        if (pos.y < targetHeight)
        {
            pos.y = targetHeight;
            transform.position = pos;
        }
    }
}