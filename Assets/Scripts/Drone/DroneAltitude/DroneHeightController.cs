using UnityEngine;

public class DroneHeightController : MonoBehaviour
{
    public Terrain terrain;
    public float safeHeight = 5f; // distance above ground

    void Update()
    {
        if (terrain == null) return;

        Vector3 pos = transform.position;

        // 🔥 get terrain height under drone
        float groundHeight = terrain.SampleHeight(pos);

        // ✅ THIS IS WHERE IT GOES
        pos.y = Mathf.Max(pos.y, groundHeight + safeHeight);

        transform.position = pos;
    }
}