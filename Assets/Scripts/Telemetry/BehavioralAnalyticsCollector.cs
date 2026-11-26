using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Call GrantConsent only from an explicit consent action in the game UI.
// Display names, GPS coordinates, device identifiers and free-form text are not collected.
public sealed class BehavioralAnalyticsCollector : MonoBehaviour
{
    private const string ProfileIdKey = "SantaTrail.Analytics.ProfileId";
    private const string ConsentKey = "SantaTrail.Analytics.Consent";
    private const string ConsentVersionKey = "SantaTrail.Analytics.ConsentVersion";
    private const string ApiUrlKey = "SantaTrail.Analytics.ApiUrl";
    private const string LocalProfilesKey = "SantaTrail.Analytics.LocalProfiles";
    private const string LocalDisplayNameKey = "SantaTrail.Analytics.LocalDisplayName.";
    private const string Endpoint = "/v1/analytics/bundles";
    private const string ConsentEndpoint = "/v1/analytics/consent";
    private const string WithdrawEndpoint = "/v1/analytics/withdraw";
    private const float SampleInterval = 0.1f;

    private static BehavioralAnalyticsCollector instance;
    private string apiBaseUrl;
    private string profileId;
    private string sessionId;
    private string sessionStartedAt;
    private ActiveAttempt activeAttempt;
    private float nextSampleAt;
    private Vector3 previousPosition;
    private bool hasPreviousPosition;
    private bool serverConsentConfirmed;
    private float lastSampleAt;

    [Serializable] private sealed class ActiveAttempt
    {
        public AttemptRow row;
        public RecordingRow recording;
        public readonly List<EventRow> events = new List<EventRow>();
        public readonly List<SampleRow> samples = new List<SampleRow>();
        public readonly List<DecisionRow> decisions = new List<DecisionRow>();
        public Transform drone;
        public Terrain terrain;
        public float startedAt;
        public Vector3 previousVelocity;
        public bool collisionWasActive;
        public bool altitudeWarningWasActive;
    }

    [Serializable] private sealed class ProfileRow
    {
        public string profile_id;
        public bool telemetry_consent = true;
        public string consent_version;
        public string created_at;
        public string telemetry_disabled_at;
    }

    [Serializable] private sealed class SessionRow
    {
        public string session_id;
        public string profile_id;
        public string game_version;
        public string platform;
        public string started_at;
        public string ended_at;
        public string session_end_reason;
    }

    [Serializable] private sealed class AttemptRow
    {
        public string attempt_id, session_id, profile_id, level_id, mission_config_version;
        public int attempt_number;
        public string started_at, ended_at, end_reason;
        public double duration_seconds;
        public bool level_completed;
        public int score, total_targets, completed_targets;
        public int failed_delivery_count = -1, collision_count, altitude_violation_count;
        public int boundary_violation_count = -1, hint_count, pause_count;
        public double route_distance = -1, direct_route_distance = -1, route_efficiency = -1;
        public double average_speed = -1, max_speed = -1, average_altitude_agl = -1;
        public double minimum_altitude_agl = -1, maximum_altitude_agl = -1;
        public double altitude_standard_deviation = -1, average_delivery_accuracy = -1;
    }

    [Serializable] private sealed class RecordingRow
    {
        public string recording_id, attempt_id, started_at, ended_at;
        public int sample_rate_hz = 10, sample_count;
        public string upload_status = "complete";
    }

    [Serializable] private sealed class EventRow
    {
        public string event_id, session_id, attempt_id, event_name;
        public double elapsed_seconds;
        public string properties_json;
        public string received_at;
    }

    [Serializable] private sealed class SampleRow
    {
        public string recording_id, attempt_id;
        public int sample_index;
        public double elapsed_seconds;
        public double world_x, world_y, world_z, terrain_height, altitude_agl;
        public double velocity_x, velocity_y, velocity_z, speed, roll, pitch, yaw;
        public bool armed;
        public string flight_mode, warning_state;
    }

    [Serializable] private sealed class Bundle
    {
        public ProfileRow profile;
        public SessionRow session;
        public AttemptRow[] mission_attempts;
        public EventRow[] game_events;
        public RecordingRow[] flight_recordings;
        public SampleRow[] flight_samples;
        public DecisionRow[] bandit_decisions;
        public ExperimentRow[] experiments;
        public AssignmentRow[] experiment_assignments;
    }

    [Serializable] private sealed class ExperimentRow
    {
        public string experiment_id, experiment_key;
    }

    [Serializable] private sealed class AssignmentRow
    {
        public string experiment_id, profile_id, variant, assigned_at, assignment_version;
    }

    [Serializable] private sealed class DecisionRow
    {
        public string decision_id, attempt_id, profile_id, policy_version, experiment_variant;
        public int context_attempts_on_level, context_previous_altitude_violations;
        public bool context_previous_completed;
        public double context_previous_duration_seconds, context_previous_delivery_accuracy;
        public string candidate_actions = "[]", chosen_action, decided_at, reward_calculated_at;
        public double exploration_probability, reward;
        public string reward_components;
    }

    private readonly List<ExperimentRow> experiments = new List<ExperimentRow>();
    private readonly List<AssignmentRow> assignments = new List<AssignmentRow>();

    private static string ScopedKey(string key, string learnerId = null)
    {
        string id = string.IsNullOrWhiteSpace(learnerId)
            ? PlayerPrefs.GetString(ProfileIdKey, "")
            : learnerId;
        return key + "." + id;
    }

    public static bool HasConsent =>
        !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(ProfileIdKey, "")) &&
        PlayerPrefs.GetInt(ScopedKey(ConsentKey), 0) == 1;

    // Display names are stored only in local PlayerPrefs. The cloud payload uses
    // the returned random UUID and never includes the display name.
    public static string CreateLocalLearnerProfile(string displayName)
    {
        if (instance != null && instance.activeAttempt != null)
            throw new InvalidOperationException("Finish the current mission before switching learner profiles.");
        if (instance != null)
        {
            Destroy(instance.gameObject);
            instance = null;
        }
        string id = Guid.NewGuid().ToString();
        string names = PlayerPrefs.GetString(LocalProfilesKey, "");
        PlayerPrefs.SetString(LocalProfilesKey,
            string.IsNullOrEmpty(names) ? id : names + ";" + id);
        PlayerPrefs.SetString(LocalDisplayNameKey + id, displayName ?? "");
        PlayerPrefs.SetString(ProfileIdKey, id);
        PlayerPrefs.Save();
        return id;
    }

    public static string[] GetLocalLearnerProfileIds()
    {
        string value = PlayerPrefs.GetString(LocalProfilesKey, "");
        return string.IsNullOrWhiteSpace(value) ? new string[0] : value.Split(';');
    }

    public static string GetLocalDisplayName(string learnerProfileId)
    {
        return PlayerPrefs.GetString(LocalDisplayNameKey + learnerProfileId, "");
    }

    public static bool SelectLocalLearnerProfile(string learnerProfileId)
    {
        Guid parsed;
        if (!Guid.TryParse(learnerProfileId, out parsed) || instance != null && instance.activeAttempt != null)
            return false;
        bool knownProfile = Array.IndexOf(GetLocalLearnerProfileIds(), parsed.ToString()) >= 0;
        if (!knownProfile) return false;
        if (instance != null)
        {
            Destroy(instance.gameObject);
            instance = null;
        }
        PlayerPrefs.SetString(ProfileIdKey, parsed.ToString());
        PlayerPrefs.Save();
        if (HasConsent)
        {
            string savedApiUrl = PlayerPrefs.GetString(ScopedKey(ApiUrlKey), "");
            if (!string.IsNullOrWhiteSpace(savedApiUrl))
            {
                EnsureInstance(savedApiUrl);
                instance.StartNewSession();
                instance.StartCoroutine(instance.RegisterConsent());
            }
        }
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RestorePreviouslyGrantedConsent()
    {
        if (!HasConsent) return;
        string savedApiUrl = PlayerPrefs.GetString(ScopedKey(ApiUrlKey), "");
        if (string.IsNullOrWhiteSpace(savedApiUrl)) return;
        EnsureInstance(savedApiUrl);
        instance.StartNewSession();
        instance.StartCoroutine(instance.RegisterConsent());
    }

    // Invoke after the learner accepts the consent statement. This creates or reuses
    // an anonymous local profile; the visual-novel display name is never requested.
    public static void GrantConsent(string apiUrl, string consentVersion)
    {
        if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(consentVersion))
            throw new ArgumentException("API URL and consent version are required.");

        if (string.IsNullOrWhiteSpace(PlayerPrefs.GetString(ProfileIdKey, "")))
            CreateLocalLearnerProfile("");
        PlayerPrefs.SetInt(ScopedKey(ConsentKey), 1);
        PlayerPrefs.SetString(ScopedKey(ConsentVersionKey), consentVersion);
        PlayerPrefs.SetString(ScopedKey(ApiUrlKey), apiUrl.Trim().TrimEnd('/'));
        PlayerPrefs.Save();

        EnsureInstance(apiUrl);
        instance.StartNewSession();
        instance.StartCoroutine(instance.RegisterConsent());
    }

    public static void WithdrawConsent()
    {
        string savedApiUrl = PlayerPrefs.GetString(ScopedKey(ApiUrlKey), "");
        string savedProfileId = PlayerPrefs.GetString(ProfileIdKey, "");
        PlayerPrefs.SetInt(ScopedKey(ConsentKey), 0);
        PlayerPrefs.Save();
        if (instance != null)
        {
            instance.activeAttempt = null;
            instance.sessionId = null;
            instance.RemovePendingBackups();
            if (!string.IsNullOrWhiteSpace(savedApiUrl) && !string.IsNullOrWhiteSpace(savedProfileId))
            {
                instance.StartCoroutine(instance.WithdrawOnServer(savedProfileId));
                return;
            }
            Destroy(instance.gameObject);
            instance = null;
        }
        else if (!string.IsNullOrWhiteSpace(savedApiUrl) && !string.IsNullOrWhiteSpace(savedProfileId))
        {
            EnsureInstance(savedApiUrl);
            instance.StartCoroutine(instance.WithdrawOnServer(savedProfileId));
        }
    }

    private static void EnsureInstance(string apiUrl)
    {
        if (instance != null) { instance.apiBaseUrl = apiUrl.Trim().TrimEnd('/'); return; }
        GameObject host = new GameObject("BehavioralAnalyticsCollector");
        instance = host.AddComponent<BehavioralAnalyticsCollector>();
        instance.apiBaseUrl = apiUrl.Trim().TrimEnd('/');
        instance.profileId = PlayerPrefs.GetString(ProfileIdKey, "");
        DontDestroyOnLoad(host);
        instance.StartCoroutine(instance.RetryPendingUploads());
    }

    private void StartNewSession()
    {
        sessionId = Guid.NewGuid().ToString();
        sessionStartedAt = DateTime.UtcNow.ToString("o");
    }

    // Call when the learner starts a mission. Use a stable mission/config version.
    public static string BeginAttempt(string levelId, string configVersion, int attemptNumber,
        Transform drone, Terrain terrain, int totalTargets)
    {
        if (!HasConsent || instance == null || drone == null) return null;
        if (string.IsNullOrEmpty(instance.sessionId)) instance.StartNewSession();

        string attemptsKey = "SantaTrail.Analytics.Attempts." + instance.profileId + "." + levelId;
        int nextAttemptNumber = PlayerPrefs.GetInt(attemptsKey, 0) + 1;
        PlayerPrefs.SetInt(attemptsKey, nextAttemptNumber);
        PlayerPrefs.Save();
        attemptNumber = Mathf.Max(attemptNumber, nextAttemptNumber);

        string attemptId = Guid.NewGuid().ToString();
        float now = Time.time;
        instance.activeAttempt = new ActiveAttempt
        {
            row = new AttemptRow
            {
                attempt_id = attemptId, session_id = instance.sessionId,
                profile_id = instance.profileId, level_id = levelId,
                mission_config_version = configVersion, attempt_number = Mathf.Max(1, attemptNumber),
                started_at = DateTime.UtcNow.ToString("o"), end_reason = "abandoned",
                total_targets = Mathf.Max(0, totalTargets)
            },
            recording = new RecordingRow
            {
                recording_id = Guid.NewGuid().ToString(), attempt_id = attemptId,
                started_at = DateTime.UtcNow.ToString("o")
            },
            drone = drone, terrain = terrain, startedAt = now
        };
        instance.nextSampleAt = Time.unscaledTime;
        instance.lastSampleAt = Time.unscaledTime;
        instance.hasPreviousPosition = false;
        AddEvent("mission_started", "{}");
        return attemptId;
    }

    // Event properties must be JSON containing safe, non-identifying values only.
    public static void AddEvent(string eventName, string propertiesJson = "{}")
    {
        if (!HasConsent || instance == null || instance.activeAttempt == null ||
            string.IsNullOrWhiteSpace(eventName)) return;
        ActiveAttempt a = instance.activeAttempt;
        a.events.Add(new EventRow
        {
            event_id = Guid.NewGuid().ToString(), session_id = instance.sessionId,
            attempt_id = a.row.attempt_id, event_name = eventName,
            elapsed_seconds = Math.Max(0, Time.time - a.startedAt),
            properties_json = string.IsNullOrWhiteSpace(propertiesJson) ? "{}" : propertiesJson,
            received_at = DateTime.UtcNow.ToString("o")
        });
        if (eventName == "hint_used" || eventName == "hint_shown") a.row.hint_count++;
    }

    public static void NotifyApplicationPause(bool paused)
    {
        if (!HasConsent || instance == null || instance.activeAttempt == null) return;
        if (paused)
        {
            instance.activeAttempt.row.pause_count++;
            AddEvent("mission_paused", "{}");
        }
        else
        {
            AddEvent("mission_resumed", "{}");
        }
    }

    // Call once per experiment. The returned variant is stable across launches
    // for this local learner profile; use it in the policy and decision records.
    public static string AssignExperiment(string experimentId, string experimentKey,
        string variant, string assignmentVersion = "")
    {
        if (!HasConsent || instance == null ||
            string.IsNullOrWhiteSpace(experimentId) || string.IsNullOrWhiteSpace(experimentKey) ||
            string.IsNullOrWhiteSpace(variant)) return "";
        string variantKey = "SantaTrail.Analytics.Variant." + instance.profileId + "." + experimentKey;
        string savedVariant = PlayerPrefs.GetString(variantKey, "");
        string stableVariant = string.IsNullOrWhiteSpace(savedVariant) ? variant : savedVariant;
        if (string.IsNullOrWhiteSpace(savedVariant))
        {
            PlayerPrefs.SetString(variantKey, stableVariant);
            PlayerPrefs.Save();
        }
        if (instance.activeAttempt == null) return stableVariant;
        if (instance.experiments.Exists(row => row.experiment_id == experimentId)) return stableVariant;
        instance.experiments.Add(new ExperimentRow
        {
            experiment_id = experimentId, experiment_key = experimentKey
        });
        instance.assignments.Add(new AssignmentRow
        {
            experiment_id = experimentId, profile_id = instance.profileId,
            variant = stableVariant, assigned_at = DateTime.UtcNow.ToString("o"),
            assignment_version = assignmentVersion
        });
        return stableVariant;
    }

    public static void RecordBanditDecision(string policyVersion, string variant,
        int previousAttempts, bool previousCompleted, double previousDurationSeconds,
        int previousAltitudeViolations, double previousDeliveryAccuracy,
        string candidateActionsJson, string chosenAction, double explorationProbability = 0)
    {
        if (!HasConsent || instance == null || instance.activeAttempt == null) return;
        instance.activeAttempt.decisions.Add(new DecisionRow
        {
            decision_id = Guid.NewGuid().ToString(),
            attempt_id = instance.activeAttempt.row.attempt_id,
            profile_id = instance.profileId,
            policy_version = policyVersion,
            experiment_variant = variant,
            context_attempts_on_level = Math.Max(0, previousAttempts),
            context_previous_completed = previousCompleted,
            context_previous_duration_seconds = Math.Max(0, previousDurationSeconds),
            context_previous_altitude_violations = Math.Max(0, previousAltitudeViolations),
            context_previous_delivery_accuracy = previousDeliveryAccuracy,
            candidate_actions = string.IsNullOrWhiteSpace(candidateActionsJson) ? "[]" : candidateActionsJson,
            chosen_action = chosenAction,
            exploration_probability = explorationProbability,
            decided_at = DateTime.UtcNow.ToString("o")
        });
    }

    // Call once when an attempt finishes or the learner quits. Counts/metrics that
    // are unavailable can remain zero here and be supplied by the gameplay system.
    public static void EndAttempt(string endReason, bool completed, int score,
        int completedTargets, int collisionCount = -1, int altitudeViolationCount = -1,
        int hintCount = -1, int pauseCount = -1, double averageAltitudeAgl = -1,
        double minimumAltitudeAgl = -1, double maximumAltitudeAgl = -1)
    {
        if (!HasConsent || instance == null || instance.activeAttempt == null) return;
        ActiveAttempt a = instance.activeAttempt;
        a.row.ended_at = DateTime.UtcNow.ToString("o");
        a.row.duration_seconds = Math.Max(0, Time.time - a.startedAt);
        a.row.end_reason = string.IsNullOrWhiteSpace(endReason) ? "unknown" : endReason;
        a.row.level_completed = completed;
        a.row.score = score;
        a.row.completed_targets = Math.Max(0, completedTargets);
        if (collisionCount >= 0) a.row.collision_count = collisionCount;
        if (altitudeViolationCount >= 0) a.row.altitude_violation_count = altitudeViolationCount;
        if (hintCount >= 0) a.row.hint_count = hintCount;
        if (pauseCount >= 0) a.row.pause_count = pauseCount;
        if (averageAltitudeAgl >= 0) a.row.average_altitude_agl = averageAltitudeAgl;
        if (minimumAltitudeAgl >= 0) a.row.minimum_altitude_agl = minimumAltitudeAgl;
        if (maximumAltitudeAgl >= 0) a.row.maximum_altitude_agl = maximumAltitudeAgl;
        a.recording.ended_at = a.row.ended_at;
        a.recording.sample_count = a.samples.Count;
        AddEvent("mission_" + a.row.end_reason, "{}");
        instance.activeAttempt = null;
        instance.StartCoroutine(instance.Upload(a));
    }

    private void Update()
    {
        if (!HasConsent || PauseMenuController.IsPaused || activeAttempt == null || activeAttempt.drone == null ||
            Time.unscaledTime < nextSampleAt) return;
        nextSampleAt = Time.unscaledTime + SampleInterval;
        float sampleDelta = Mathf.Max(0.01f, Time.unscaledTime - lastSampleAt);
        lastSampleAt = Time.unscaledTime;
        CaptureSample(activeAttempt, sampleDelta);
    }

    private void CaptureSample(ActiveAttempt a, float sampleDelta)
    {
        Vector3 p = a.drone.position;
        Vector3 velocity = hasPreviousPosition ? (p - previousPosition) / sampleDelta : Vector3.zero;
        previousPosition = p;
        hasPreviousPosition = true;
        double terrainHeight = a.terrain != null ? a.terrain.SampleHeight(p) : 0;
        MAVLinkReceiver receiver = MAVLinkReceiver.Active;
        double altitudeAgl = a.terrain != null
            ? p.y - terrainHeight
            : receiver != null && receiver.HasRecentTelemetry
                ? receiver.RelativeAltitudeMeters
                : -1;
        a.samples.Add(new SampleRow
        {
            recording_id = a.recording.recording_id, attempt_id = a.row.attempt_id,
            sample_index = a.samples.Count,
            elapsed_seconds = Math.Max(0, Time.time - a.startedAt),
            world_x = p.x, world_y = p.y, world_z = p.z,
            terrain_height = a.terrain != null ? terrainHeight : -1,
            altitude_agl = altitudeAgl,
            velocity_x = velocity.x, velocity_y = velocity.y, velocity_z = velocity.z,
            speed = velocity.magnitude,
            roll = receiver != null ? receiver.roll : a.drone.eulerAngles.z,
            pitch = receiver != null ? receiver.pitch : a.drone.eulerAngles.x,
            yaw = receiver != null ? receiver.yaw : a.drone.eulerAngles.y,
            armed = receiver != null && receiver.IsArmed,
            flight_mode = "",
            warning_state = receiver != null && receiver.BuildingCollisionTriggered
                ? "collision"
                : receiver != null && receiver.AltitudeLimitTriggered ? "altitude" : ""
        });
        bool collision = receiver != null && receiver.BuildingCollisionTriggered;
        if (collision && !a.collisionWasActive)
        {
            a.row.collision_count++;
            AddEvent("collision", "{}");
        }
        a.collisionWasActive = collision;
        bool altitudeWarning = receiver != null && receiver.AltitudeLimitTriggered;
        if (altitudeWarning && !a.altitudeWarningWasActive)
        {
            a.row.altitude_violation_count++;
            AddEvent("altitude_warning", "{}");
        }
        a.altitudeWarningWasActive = altitudeWarning;
    }

    private IEnumerator Upload(ActiveAttempt a)
    {
        Bundle bundle = new Bundle
        {
            profile = new ProfileRow
            {
                profile_id = profileId, telemetry_consent = true,
                consent_version = PlayerPrefs.GetString(ScopedKey(ConsentVersionKey), ""),
                created_at = DateTime.UtcNow.ToString("o")
            },
            session = new SessionRow
            {
                session_id = sessionId, profile_id = profileId,
                game_version = Application.version, platform = Application.platform.ToString(),
                started_at = sessionStartedAt
            },
            mission_attempts = new[] { a.row }, game_events = a.events.ToArray(),
            flight_recordings = new[] { a.recording }, flight_samples = a.samples.ToArray(),
            bandit_decisions = a.decisions.ToArray(),
            experiments = experiments.ToArray(), experiment_assignments = assignments.ToArray()
        };
        string json = JsonUtility.ToJson(bundle);
        string folder = Path.Combine(Application.persistentDataPath, "SantaTrail", "AnalyticsPending");
        Directory.CreateDirectory(folder);
        string backupPath = Path.Combine(folder, a.row.attempt_id + ".json");
        File.WriteAllText(backupPath, json, Encoding.UTF8);

        float consentDeadline = Time.realtimeSinceStartup + 15f;
        while (HasConsent && !serverConsentConfirmed && Time.realtimeSinceStartup < consentDeadline)
            yield return null;
        if (!serverConsentConfirmed) yield break;

        using (UnityWebRequest request = new UnityWebRequest(apiBaseUrl + Endpoint, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 20;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
                File.Delete(backupPath);
            else
                Debug.LogWarning("Behavioral analytics upload failed; a local JSON backup was retained. " + request.error);
        }
    }

    private IEnumerator RetryPendingUploads()
    {
        float consentDeadline = Time.realtimeSinceStartup + 15f;
        while (HasConsent && !serverConsentConfirmed && Time.realtimeSinceStartup < consentDeadline)
            yield return null;
        if (!serverConsentConfirmed) yield break;
        string folder = Path.Combine(Application.persistentDataPath, "SantaTrail", "AnalyticsPending");
        if (!Directory.Exists(folder)) yield break;
        foreach (string file in Directory.GetFiles(folder, "*.json"))
        {
            if (!HasConsent) yield break;
            string json;
            try { json = File.ReadAllText(file, Encoding.UTF8); }
            catch (Exception) { continue; }
            using (UnityWebRequest request = new UnityWebRequest(apiBaseUrl + Endpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 20;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try { File.Delete(file); } catch (Exception) { }
                }
            }
        }
    }

    private IEnumerator RegisterConsent()
    {
        ProfileRow consent = new ProfileRow
        {
            profile_id = profileId, telemetry_consent = true,
            consent_version = PlayerPrefs.GetString(ScopedKey(ConsentVersionKey), ""),
            created_at = DateTime.UtcNow.ToString("o")
        };
        string json = JsonUtility.ToJson(consent);
        using (UnityWebRequest request = new UnityWebRequest(apiBaseUrl + ConsentEndpoint, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10;
            yield return request.SendWebRequest();
            serverConsentConfirmed = request.result == UnityWebRequest.Result.Success;
            if (!serverConsentConfirmed)
                Debug.LogWarning("Telemetry consent could not be confirmed by the server. " + request.error);
        }
    }

    private void RemovePendingBackups()
    {
        string folder = Path.Combine(Application.persistentDataPath, "SantaTrail", "AnalyticsPending");
        if (!Directory.Exists(folder)) return;
        foreach (string file in Directory.GetFiles(folder, "*.json"))
        {
            try { File.Delete(file); } catch (Exception) { }
        }
    }

    private IEnumerator WithdrawOnServer(string learnerProfileId)
    {
        string json = "{\"profile_id\":\"" + learnerProfileId + "\"}";
        using (UnityWebRequest request = new UnityWebRequest(apiBaseUrl + WithdrawEndpoint, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                Debug.LogWarning("Consent withdrawal could not be confirmed by the server. " + request.error);
        }
        if (instance == this) instance = null;
        Destroy(gameObject);
    }
}
