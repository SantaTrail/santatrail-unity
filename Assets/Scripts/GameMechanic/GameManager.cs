using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static double homeLat;
    public static double homeLon;
    public static double homeAlt;
    public static double homeYaw;
    public static bool hasHomeLocation;

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
    }
    
}
