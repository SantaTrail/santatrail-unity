using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ProgressSaveSystem
{
    [Serializable]
    public class LevelProgressRecord
    {
        public string sceneName = "";
        public int completedTargets;
        public int totalTargets;
        public int score;
        public bool levelCompleted;
        public string savedAtUtc = "";
    }

    [Serializable]
    public class ProgressSaveData
    {
        public List<LevelProgressRecord> levelRecords = new List<LevelProgressRecord>();
        public string lastSavedSceneName = "";
        public string lastSavedAtUtc = "";
    }

    private const string SaveFileName = "santatrail_progress.json";
    private static ProgressSaveData currentData;
    private static bool hasLoaded;

    private static string SavePath =>
        Path.Combine(Application.dataPath, SaveFileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        Load();
    }

    public static ProgressSaveData Current
    {
        get
        {
            EnsureLoaded();
            return currentData;
        }
    }

    public static void Load()
    {
        if (File.Exists(SavePath))
        {
            try
            {
                string json = File.ReadAllText(SavePath);
                currentData = JsonUtility.FromJson<ProgressSaveData>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"ProgressSaveSystem: failed to load save file. {exception.Message}");
                currentData = new ProgressSaveData();
            }
        }
        else
        {
            currentData = new ProgressSaveData();
        }

        if (currentData == null)
        {
            currentData = new ProgressSaveData();
        }

        if (currentData.levelRecords == null)
        {
            currentData.levelRecords = new List<LevelProgressRecord>();
        }

        hasLoaded = true;
    }

    public static void Save()
    {
        EnsureLoaded();

        try
        {
            string json = JsonUtility.ToJson(currentData, true);
            File.WriteAllText(SavePath, json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"ProgressSaveSystem: failed to save progress. {exception.Message}");
        }
    }

    public static void RecordLevelResult(
        string sceneName,
        int completedTargets,
        int totalTargets,
        int score,
        bool levelCompleted)
    {
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            sceneName = "UnknownScene";
        }

        LevelProgressRecord record = currentData.levelRecords.Find(entry =>
            string.Equals(entry.sceneName, sceneName, StringComparison.OrdinalIgnoreCase));

        if (record == null)
        {
            record = new LevelProgressRecord();
            currentData.levelRecords.Add(record);
        }

        record.sceneName = sceneName;
        record.completedTargets = Mathf.Max(0, completedTargets);
        record.totalTargets = Mathf.Max(0, totalTargets);
        record.score = score;
        record.levelCompleted = levelCompleted;
        record.savedAtUtc = DateTime.UtcNow.ToString("o");

        currentData.lastSavedSceneName = sceneName;
        currentData.lastSavedAtUtc = record.savedAtUtc;

        Save();
    }

    public static bool TryGetLevelResult(string sceneName, out LevelProgressRecord record)
    {
        EnsureLoaded();

        record = null;

        if (string.IsNullOrWhiteSpace(sceneName) || currentData.levelRecords == null)
        {
            return false;
        }

        record = currentData.levelRecords.Find(entry =>
            string.Equals(entry.sceneName, sceneName, StringComparison.OrdinalIgnoreCase));
        return record != null;
    }

    public static void Clear()
    {
        currentData = new ProgressSaveData();
        hasLoaded = true;
        Save();
    }

    private static void EnsureLoaded()
    {
        if (!hasLoaded)
        {
            Load();
        }
    }
}
