using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public sealed class FlightLogSyncSettings
{
    public bool enabled = false;
    public bool telemetryOptIn = false;
    public string apiBaseUrl = "";
    public bool uploadCompletedFlightsOnly = true;
}

[Serializable]
public sealed class FlightLogSyncRequest
{
    public string anonymousPlayerId;
    public string sessionId;
    public ProgressSaveSystem.LevelProgressRecord level;
}

public sealed class FlightLogSyncService : MonoBehaviour
{
    private const string SettingsResourceName = "SantaTrailFlightLogSyncSettings";
    private const string AnonymousPlayerIdKey = "SantaTrail.AnonymousPlayerId";
    private const string EndpointPath = "/v1/flight-sessions";

    private static FlightLogSyncService instance;
    private FlightLogSyncSettings settings;
    private string anonymousPlayerId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }

        TextAsset settingsAsset = Resources.Load<TextAsset>(SettingsResourceName);
        FlightLogSyncSettings loadedSettings = settingsAsset != null
            ? JsonUtility.FromJson<FlightLogSyncSettings>(settingsAsset.text)
            : new FlightLogSyncSettings();

        if (loadedSettings == null || !loadedSettings.enabled ||
            !loadedSettings.telemetryOptIn ||
            string.IsNullOrWhiteSpace(loadedSettings.apiBaseUrl))
        {
            return;
        }

        GameObject serviceObject = new GameObject("FlightLogSyncService");
        instance = serviceObject.AddComponent<FlightLogSyncService>();
        instance.settings = loadedSettings;
        instance.anonymousPlayerId = GetOrCreateAnonymousPlayerId();
        DontDestroyOnLoad(serviceObject);
        ProgressSaveSystem.LevelProgressSaved += instance.OnLevelProgressSaved;
    }

    private static string GetOrCreateAnonymousPlayerId()
    {
        string playerId = PlayerPrefs.GetString(AnonymousPlayerIdKey, "");
        if (!string.IsNullOrWhiteSpace(playerId))
        {
            return playerId;
        }

        playerId = Guid.NewGuid().ToString("N");
        PlayerPrefs.SetString(AnonymousPlayerIdKey, playerId);
        PlayerPrefs.Save();
        return playerId;
    }

    private void OnLevelProgressSaved(ProgressSaveSystem.LevelProgressRecord record)
    {
        if (record == null || settings == null)
        {
            return;
        }

        if (settings.uploadCompletedFlightsOnly && !record.levelCompleted)
        {
            return;
        }

        StartCoroutine(UploadFlight(record));
    }

    private IEnumerator UploadFlight(ProgressSaveSystem.LevelProgressRecord record)
    {
        string baseUrl = settings.apiBaseUrl.TrimEnd('/');
        string requestUrl = baseUrl + EndpointPath;
        FlightLogSyncRequest payload = new FlightLogSyncRequest
        {
            anonymousPlayerId = anonymousPlayerId,
            sessionId = string.IsNullOrWhiteSpace(record.lastFlightId)
                ? Guid.NewGuid().ToString("N")
                : record.lastFlightId,
            level = record
        };
        string json = JsonUtility.ToJson(payload);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(
                   requestUrl,
                   UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "Flight log upload failed; local save is still safe. " +
                    request.error
                );
            }
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            ProgressSaveSystem.LevelProgressSaved -= OnLevelProgressSaved;
            instance = null;
        }
    }
}
