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
        public string levelLabel = "";
        public int completedTargets;
        public int totalTargets;
        public int score;
        public bool levelCompleted;
        public int attempts;
        public int bestScore;
        public int bestCompletedTargets;
        public float bestTimeSeconds = -1f;
        public string savedAtUtc = "";
    }

    [Serializable]
    public class FlightHistoryRecord
    {
        public string flightId = "";
        public string sceneName = "";
        public string levelLabel = "";
        public int completedTargets;
        public int totalTargets;
        public int score;
        public bool levelCompleted;
        public float elapsedSeconds = -1f;
        public string savedAtUtc = "";
    }

    [Serializable]
    public class ProgressSaveData
    {
        public List<LevelProgressRecord> levelRecords = new List<LevelProgressRecord>();
        public List<FlightHistoryRecord> flightHistory = new List<FlightHistoryRecord>();
        public string lastSavedSceneName = "";
        public string lastSavedAtUtc = "";
    }

    private const string SaveFileName = "santatrail_progress.json";
    private static ProgressSaveData currentData;
    private static bool hasLoaded;

    public static string SavePath =>
        Path.Combine(Application.persistentDataPath, "SantaTrail", SaveFileName);

    private static string LegacySavePath =>
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
        string pathToLoad = File.Exists(SavePath)
            ? SavePath
            : LegacySavePath;
        bool loadedLegacyFile = pathToLoad != SavePath && File.Exists(pathToLoad);

        if (File.Exists(pathToLoad))
        {
            try
            {
                string json = File.ReadAllText(pathToLoad);
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

        if (currentData.flightHistory == null)
        {
            currentData.flightHistory = new List<FlightHistoryRecord>();
        }

        hasLoaded = true;

        if (loadedLegacyFile)
        {
            Save();
            Debug.Log(
                "ProgressSaveSystem: migrated the old save file to the writable user data folder."
            );
        }
    }

    public static void Save()
    {
        EnsureLoaded();

        try
        {
            string saveDirectory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
            }

            string json = JsonUtility.ToJson(currentData, true);
            string temporaryPath = SavePath + ".tmp";
            File.WriteAllText(temporaryPath, json);

            if (File.Exists(SavePath))
            {
                try
                {
                    File.Replace(temporaryPath, SavePath, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Delete(SavePath);
                    File.Move(temporaryPath, SavePath);
                }
            }
            else
            {
                File.Move(temporaryPath, SavePath);
            }
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
        bool levelCompleted,
        float elapsedSeconds = -1f,
        string flightId = "")
    {
        RecordLevelProgress(
            sceneName,
            completedTargets,
            totalTargets,
            score,
            levelCompleted,
            elapsedSeconds,
            true,
            flightId
        );
    }

    public static void RecordLevelProgress(
        string sceneName,
        int completedTargets,
        int totalTargets,
        int score,
        bool levelCompleted,
        float elapsedSeconds = -1f,
        bool countAttempt = false,
        string flightId = "")
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
        if (string.IsNullOrWhiteSpace(record.levelLabel) &&
            string.Equals(sceneName, GameManager.activeSceneName, StringComparison.OrdinalIgnoreCase))
        {
            record.levelLabel = GameManager.activeLevelLabel;
        }
        record.completedTargets = Mathf.Max(0, completedTargets);
        record.totalTargets = Mathf.Max(record.totalTargets, totalTargets);
        record.score = score;
        record.levelCompleted = record.levelCompleted || levelCompleted;

        if (countAttempt)
        {
            record.attempts++;
        }

        record.bestScore = Mathf.Max(record.bestScore, score);
        record.bestCompletedTargets = Mathf.Max(
            record.bestCompletedTargets,
            completedTargets
        );

        if (levelCompleted && elapsedSeconds >= 0f &&
            (record.bestTimeSeconds < 0f || elapsedSeconds < record.bestTimeSeconds))
        {
            record.bestTimeSeconds = elapsedSeconds;
        }

        record.savedAtUtc = DateTime.UtcNow.ToString("o");

        currentData.lastSavedSceneName = sceneName;
        currentData.lastSavedAtUtc = record.savedAtUtc;

        UpdateFlightHistory(
            flightId,
            sceneName,
            record.levelLabel,
            completedTargets,
            totalTargets,
            score,
            levelCompleted,
            elapsedSeconds,
            record.savedAtUtc
        );

        Save();
    }

    public static int CompletedLevelCount
    {
        get
        {
            EnsureLoaded();
            return currentData.levelRecords.FindAll(record => record.levelCompleted).Count;
        }
    }

    public static int TotalDeliveredTargets
    {
        get
        {
            EnsureLoaded();
            int total = 0;
            foreach (LevelProgressRecord record in currentData.levelRecords)
            {
                total += Mathf.Max(0, record.bestCompletedTargets);
            }
            return total;
        }
    }

    public static int BestScoreTotal
    {
        get
        {
            EnsureLoaded();
            int total = 0;
            foreach (LevelProgressRecord record in currentData.levelRecords)
            {
                total += record.bestScore;
            }
            return total;
        }
    }

    public static string BuildSummary()
    {
        EnsureLoaded();

        if (currentData.levelRecords.Count == 0)
        {
            return "No saved flights yet.\nComplete a delivery mission to build your flight record.";
        }

        System.Text.StringBuilder summary = new System.Text.StringBuilder();
        summary.AppendLine($"Levels complete: {CompletedLevelCount}");
        summary.AppendLine($"Deliveries: {TotalDeliveredTargets}");
        summary.AppendLine($"Best score total: {BestScoreTotal}");
        summary.AppendLine();
        summary.AppendLine("Level records:");

        foreach (LevelProgressRecord record in currentData.levelRecords)
        {
            string label = string.IsNullOrWhiteSpace(record.levelLabel)
                ? record.sceneName
                : record.levelLabel;
            string status = record.levelCompleted ? "Complete" : "In progress";
            string bestTime = record.bestTimeSeconds >= 0f
                ? FormatSeconds(record.bestTimeSeconds)
                : "--:--";

            summary.AppendLine(
                $"{label}: {status} | {record.bestCompletedTargets}/" +
                $"{record.totalTargets} deliveries | best {record.bestScore} | " +
                $"{bestTime} | attempts {record.attempts}"
            );
        }

        return summary.ToString().TrimEnd();
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

    private static void UpdateFlightHistory(
        string flightId,
        string sceneName,
        string levelLabel,
        int completedTargets,
        int totalTargets,
        int score,
        bool levelCompleted,
        float elapsedSeconds,
        string savedAtUtc)
    {
        if (currentData.flightHistory == null)
        {
            currentData.flightHistory = new List<FlightHistoryRecord>();
        }

        FlightHistoryRecord history = null;
        if (!string.IsNullOrWhiteSpace(flightId))
        {
            history = currentData.flightHistory.Find(entry =>
                string.Equals(entry.flightId, flightId, StringComparison.Ordinal));
        }

        if (history == null)
        {
            history = new FlightHistoryRecord
            {
                flightId = string.IsNullOrWhiteSpace(flightId)
                    ? Guid.NewGuid().ToString("N")
                    : flightId
            };
            currentData.flightHistory.Add(history);
        }

        history.sceneName = sceneName;
        history.levelLabel = levelLabel;
        history.completedTargets = Mathf.Max(0, completedTargets);
        history.totalTargets = Mathf.Max(0, totalTargets);
        history.score = score;
        history.levelCompleted = history.levelCompleted || levelCompleted;
        history.elapsedSeconds = elapsedSeconds;
        history.savedAtUtc = savedAtUtc;
    }

    private static string FormatSeconds(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return $"{totalSeconds / 60:0}:{totalSeconds % 60:00}";
    }
}
