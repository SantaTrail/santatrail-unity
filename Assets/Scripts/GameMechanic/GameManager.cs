using System;
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
    public static bool hasActiveLevelSettings;
    public static string activeSceneName = "";
    public static string activeLevelLabel = "";
    public static MissionDifficulty activeMissionDifficulty = MissionDifficulty.Easy;
    public static bool hasActiveDeliveryCount;
    public static int activeDeliveryCount;
    public static bool activeShowTutorial = true;
    public static bool activeAutoLoadNextScene;
    public static string activeNextSceneName = "";
    public static float activeNextSceneDelaySeconds = 2f;
    public MissionDifficulty difficulty;
    public MissionProgressUI missionUI;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        homeLat = 0;
        homeLon = 0;
        homeAlt = 0;
        homeYaw = 0;
        hasHomeLocation = false;
        hasActiveLevelSettings = false;
        activeSceneName = "";
        activeLevelLabel = "";
        activeMissionDifficulty = MissionDifficulty.Easy;
        hasActiveDeliveryCount = false;
        activeDeliveryCount = 0;
        activeShowTutorial = true;
        activeAutoLoadNextScene = false;
        activeNextSceneName = "";
        activeNextSceneDelaySeconds = 2f;
    }

    public static void SetHomeLocation(double latitude, double longitude, double altitude, double yaw = 0)
    {
        homeLat = latitude;
        homeLon = longitude;
        homeAlt = altitude;
        homeYaw = yaw;
        hasHomeLocation = true;
    }

    public static void ApplyLevel(LevelConfig level)
    {
        if (level == null)
        {
            return;
        }

        hasActiveLevelSettings = true;
        activeSceneName = level.sceneName ?? "";
        activeLevelLabel = level.label ?? "";
        activeMissionDifficulty = level.missionDifficulty;
        hasActiveDeliveryCount = level.deliveryCount > 0;
        activeDeliveryCount = Mathf.Max(0, level.deliveryCount);
        activeShowTutorial = level.showTutorial;
        activeAutoLoadNextScene = level.autoLoadNextScene;
        activeNextSceneName = level.nextSceneName ?? "";
        activeNextSceneDelaySeconds = Mathf.Max(0f, level.nextSceneDelaySeconds);
        SetHomeLocation(
            level.homeLatitude,
            level.homeLongitude,
            level.homeAltitude,
            level.homeYaw
        );
    }

    public static bool TryApplyLevelForScene(string sceneName)
    {
        if (hasActiveLevelSettings &&
            string.Equals(activeSceneName, sceneName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (LevelConfigLoader.TryLoadLevels(out var levels))
        {
            LevelConfig match = levels.Find(level =>
                string.Equals(level.sceneName, sceneName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                ApplyLevel(match);
                return true;
            }
        }

        return false;
    }

    void Start()
    {
        Debug.Log(hasHomeLocation
            ? $"HOME LOCKED: {homeLat}, {homeLon}, {homeAlt}, {homeYaw}"
            : "HOME LOCKED: not set yet");
        
        int deliveries = GetDeliveryCount();
        if (missionUI != null)
        {
            missionUI.SetupMission(deliveries);
        }
    }

    int GetDeliveryCount()
    {
        if (hasActiveDeliveryCount)
        {
            return Mathf.Max(1, activeDeliveryCount);
        }

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
