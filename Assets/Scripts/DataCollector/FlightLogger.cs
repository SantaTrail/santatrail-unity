using UnityEngine;
using System.IO;
using System.Text;

public class FlightLogger : MonoBehaviour
{
    public Transform drone;
    public Terrain terrain;

    string path;

    // ✅ ADD: manual velocity tracking
    Vector3 lastPosition;
    Vector3 velocity;

    void Start()
    {
        string folder = Application.dataPath + "/FlightLogs/";

        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        path = folder + "flight_log_" + timestamp + ".csv";
    }

    void Update()
    {
        // ✅ auto-find terrain if not assigned
        if (terrain == null)
        {
            terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain == null) return;
        }

        if (drone == null) return;

        Vector3 pos = drone.position;

        // ✅ FIX: manual velocity (NO Rigidbody)
        Vector3 rawVel = (pos - lastPosition) / Time.deltaTime;
        velocity = Vector3.Lerp(velocity, rawVel, 0.5f);
        lastPosition = pos;

        float roll = drone.eulerAngles.z;
        float pitch = drone.eulerAngles.x;
        float yaw = drone.eulerAngles.y;

        float terrainHeight = terrain.SampleHeight(pos);

        float reward = CalculateReward(pos, velocity);

        string line = $"{Time.time},{pos.x},{pos.y},{pos.z},{velocity.magnitude},{roll},{pitch},{yaw},{terrainHeight},{reward}\n";

        File.AppendAllText(path, line);
    }

    float CalculateReward(Vector3 pos, Vector3 vel)
    {
        float reward = 0;

        // ✅ stable flight reward
        reward += Mathf.Clamp(1f - vel.magnitude * 0.1f, -1f, 1f);

        // ✅ penalty if too low (near terrain)
        if (terrain != null)
        {
            float ground = terrain.SampleHeight(pos);
            if (pos.y < ground + 3f)
                reward -= 5f;
        }

        return reward;
    }
}
