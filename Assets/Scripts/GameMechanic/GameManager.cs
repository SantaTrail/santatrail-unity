using UnityEngine;

public enum MissionDifficulty
{
    Easy,
    Medium,
    Hard
}

public class GameManager : MonoBehaviour
{
    public static double homeLat;
    public static double homeLon;
    public static double homeAlt;
    public static double homeYaw;
    public static bool hasHomeLocation;
    public MissionDifficulty difficulty;
    public MissionProgressUI missionUI;

    public static void SetHomeLocation(double latitude, double longitude, double altitude, double yaw = 0)
    {
        homeLat = latitude;
        homeLon = longitude;
        homeAlt = altitude;
        homeYaw = yaw;
        hasHomeLocation = true;
    }

    void Start()
    {
        Debug.Log(hasHomeLocation
            ? $"HOME LOCKED: {homeLat}, {homeLon}, {homeAlt}, {homeYaw}"
            : "HOME LOCKED: not set yet");
        
        int deliveries = GetDeliveryCount();
        missionUI.SetupMission(deliveries);
    }

    int GetDeliveryCount()
    {
        switch (difficulty)
        {
            case MissionDifficulty.Easy:
                return 3;

            case MissionDifficulty.Medium:
                return 5;

            case MissionDifficulty.Hard:
                return 8;

            default:
                return 5;
        }
    }
    
}
