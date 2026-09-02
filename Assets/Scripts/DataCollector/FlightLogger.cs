using UnityEngine;
using System.IO;
using System.Text;

public class FlightLogger : MonoBehaviour
{
    public Transform drone;
    public Terrain terrain;
    public DeliveryScoreManager deliveryScoreManager;

    string path;

    // ✅ ADD: manual velocity tracking
    Vector3 lastPosition;
    Vector3 velocity;
    bool hasLastPosition;
    bool levelCompleted;
    float levelCompletedAt = -1f;

    void Start()
    {
        string folder = Path.Combine(
            Application.persistentDataPath,
            "SantaTrail",
            "FlightLogs"
        );

        Directory.CreateDirectory(folder);

        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        path = Path.Combine(folder, "flight_log_" + timestamp + ".csv");

        if (deliveryScoreManager == null)
        {
            deliveryScoreManager = Object.FindFirstObjectByType<DeliveryScoreManager>();
        }

        if (drone == null && deliveryScoreManager != null)
        {
            drone = deliveryScoreManager.drone;
        }

        if (drone == null)
        {
            Debug.LogWarning("FlightLogger could not find a drone. Assign Drone in the Inspector.");
        }

        if (deliveryScoreManager != null)
        {
            deliveryScoreManager.LevelCompleted += HandleLevelCompleted;
        }

        File.WriteAllText(
            path,
            "time,x,y,z,vel,roll,pitch,yaw,terrainHeight,altitudeAboveGround,reward,completedTargets,totalTargets,remainingTargets,levelCompleted,levelCompletedAt,missionState\n"
        );
    }

    void OnDestroy()
    {
        if (deliveryScoreManager != null)
        {
            deliveryScoreManager.LevelCompleted -= HandleLevelCompleted;
        }
    }

    void HandleLevelCompleted()
    {
        levelCompleted = true;
        levelCompletedAt = Time.time;
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

        if (!hasLastPosition)
        {
            lastPosition = pos;
            hasLastPosition = true;
            velocity = Vector3.zero;
        }

        // ✅ FIX: manual velocity (NO Rigidbody)
        Vector3 rawVel = (pos - lastPosition) / Time.deltaTime;
        velocity = Vector3.Lerp(velocity, rawVel, 0.5f);
        lastPosition = pos;

        float roll = drone.eulerAngles.z;
        float pitch = drone.eulerAngles.x;
        float yaw = drone.eulerAngles.y;

        float terrainHeight = terrain.SampleHeight(pos);
        float altitudeAboveGround = pos.y - terrainHeight;

        float reward = CalculateReward(pos, velocity);
        int completedTargets = deliveryScoreManager != null ? deliveryScoreManager.CompletedTargets : 0;
        int totalTargets = deliveryScoreManager != null ? deliveryScoreManager.TotalTargets : 0;
        int remainingTargets = deliveryScoreManager != null ? deliveryScoreManager.RemainingTargets : 0;
        string missionState = levelCompleted ? "completed" : "in_progress";

        string line =
            $"{Time.time},{pos.x},{pos.y},{pos.z},{velocity.magnitude},{roll},{pitch},{yaw}," +
            $"{terrainHeight},{altitudeAboveGround},{reward},{completedTargets},{totalTargets},{remainingTargets}," +
            $"{(levelCompleted ? 1 : 0)},{levelCompletedAt},{missionState}\n";

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
