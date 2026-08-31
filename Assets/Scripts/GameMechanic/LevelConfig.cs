using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class LevelConfig
{
    public string id = "";
    public string label = "Level 1";
    public string sceneName = "LV1";
    public double homeLatitude = 13.8455;
    public double homeLongitude = 100.5688;
    public double homeAltitude = 10;
    public double homeYaw = 0;

    [Header("Level Conditions")]
    public MissionDifficulty missionDifficulty = MissionDifficulty.Easy;
    [Min(0)]
    public int deliveryCount = 5;
    public bool showTutorial = true;

    [Header("Scene Flow")]
    public bool autoLoadNextScene = false;
    public string nextSceneName = "";
    [Min(0f)]
    public float nextSceneDelaySeconds = 2f;
}

[Serializable]
public class LevelConfigFile
{
    public List<LevelConfig> levels = new List<LevelConfig>();
}

public static class LevelConfigLoader
{
    private const string ResourceName = "LevelDatabase";
    private const string StreamingAssetsFileName = "SantaTrailLevels.json";

    public static bool TryLoadLevels(out List<LevelConfig> levels)
    {
        levels = new List<LevelConfig>();

        string streamingAssetsPath = Path.Combine(
            Application.streamingAssetsPath,
            StreamingAssetsFileName
        );
        if (File.Exists(streamingAssetsPath) &&
            TryParseLevels(File.ReadAllText(streamingAssetsPath), out levels))
        {
            return true;
        }

        TextAsset resource = Resources.Load<TextAsset>(ResourceName);
        if (resource == null || string.IsNullOrWhiteSpace(resource.text))
        {
            return false;
        }

        return TryParseLevels(resource.text, out levels);
    }

    private static bool TryParseLevels(string json, out List<LevelConfig> levels)
    {
        levels = new List<LevelConfig>();
        LevelConfigFile file = null;

        try
        {
            file = JsonUtility.FromJson<LevelConfigFile>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"LevelConfigLoader: failed to parse the level database. {exception.Message}"
            );
            return false;
        }

        if (file == null || file.levels == null || file.levels.Count == 0)
        {
            return false;
        }

        levels = file.levels;
        return true;
    }
}
