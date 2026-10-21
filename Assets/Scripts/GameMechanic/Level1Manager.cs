using TMPro;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;

public class Level1Manager : MonoBehaviour
{
    private enum TutorialStep
    {
        WaitForConnection,
        ShowMapMode,
        WaitForMapType,
        WaitForGuidedMode,
        WaitForTakeoff,
        WaitForControl,
        DeliveryUnlocked,
        Finished
    }

    private enum LV2PlanningTutorialStep
    {
        Inactive,
        WaitingForTelemetry,
        PlanningMission,
        MissionDetected,
        Finished
    }

    [Header("Delivery System")]
    public DeliveryScoreManager deliveryScoreManager;

    [Header("UI")]
    public TextMeshProUGUI objectiveText;
    public TextMeshProUGUI levelStatusText;
    public string objectivePrefix = "Present Delivered: ";
    public string completedMessage = "Nice work! You finished the guided-flight tutorial.";
    public DeliveryPresentProgressUI presentProgressUI;

    [Header("Beginner Tutorial")]
    public bool showTutorial = true;
    [TextArea(2, 4)] public string welcomeMessage =
        "Welcome! Connect to the drone in QGroundControl. I’ll guide you through the first flight.";
    [TextArea(2, 4)] public string mapModeMessage =
        "In QGroundControl, open Application Settings > Maps and set Type to Street Map. If it already shows Street Map, the tutorial will continue automatically.";
    [TextArea(2, 4)] public string findTargetMessage =
        "Open the flight-mode menu in QGC Fly View. If Guided isn’t listed, enable Edit Displayed Flight Modes and add it with Configure. Then select Guided.";
    [TextArea(2, 4)] public string approachMessage =
        "Choose Guided mode and Takeoff in QGroundControl and enter 12 m as the target altitude.";
    [TextArea(2, 4)] public string holdPositionMessage =
        "Move the drone a short distance, then release the controls and let it hover.";
    [TextArea(2, 4)] public string transitionMessage =
        "Tutorial complete! Delivery targets are unlocked. Follow the highlighted house to make your first delivery.";
    [TextArea(2, 4)] public string struggleHintMessage =
        "Need a hand? Stay around 12 m while traveling. Above the highlighted house, descend to no more than 3 m above the roof and hover to deliver.";
    [Min(0.1f)] public float connectionHoldSeconds = 8f;
    [Min(0.1f)] public float mapModeDisplaySeconds = 4f;
    [Min(0.1f)] public float takeoffAltitudeMeters = 1.5f;
    [Min(0.1f)] public float controlMovementMeters = 2f;
    [Tooltip("Show the delivery help when this many seconds pass after the tutorial with no completed delivery.")]
    [Min(1f)] public float struggleHintDelaySeconds = 30f;

    [Header("Scene Flow")]
    public bool autoLoadNextScene = false;
    public string nextSceneName = "";
    [Min(0f)] public float nextSceneDelaySeconds = 2f;

    public event Action LevelStarted;

    private bool completed;
    private bool tutorialFinished;
    private TutorialStep tutorialStep = TutorialStep.WaitForConnection;
    private MAVLinkReceiver mavlinkReceiver;
    private Vector3 controlStartPosition;
    private bool hasControlReference;
    private float connectionDetectedTime = -1f;
    private float mapModeInstructionStartedAt = -1f;
    private float nextMapTypeCheckAt = -1f;
    private bool qgcMapSettingsWarningLogged;
    private float deliveryUnlockedTime = -1f;
    private bool struggleHintShown;
    private LV2PlanningTutorialStep lv2PlanningTutorialStep =
        LV2PlanningTutorialStep.Inactive;
    private bool isLV2PlanningTutorial;
    private bool hadMissionCurrentWhenLV2Started;
    private ushort missionTotalWhenLV2Started;
    private ushort missionSequenceWhenLV2Started;
    private bool autoModeWhenLV2Started;
    private int missionUpdateCountWhenLV2Started;

    private const string LV2SceneName = "LV2";
    private const string LV2WaitingForTelemetryMessage =
        "LV2 • BASIC SURVEY\n" +
        "Connect the vehicle in QGroundControl. Open Plan View to begin.";
    private const string LV2PlanningMessage =
        "LV2 • BASIC SURVEY  |  PLAN IN QGC\n" +
        "1  Choose Survey, then Basic for a simple four-corner area.\n" +
        "2  Cover the village and hillside homes with the survey area. Set altitude to 50 m and choose Calculated Above Terrain (CalcT).\n" +
        "3  Keep the grid and terrain tolerance at their defaults; camera setup is optional. Check the profile stays above the terrain.\n" +
        "4  Add Return to Launch, Upload and slide to confirm. Start it in Fly View when ready; LV2 will watch for mission telemetry.";
    private const string LV2MissionReceivedMessage =
        "SURVEY MISSION REPORTED\n" +
        "The vehicle is reporting a mission. In QGC, review it in Fly View and start it when ready.\n" +
        "If you change the battery during a flight, land safely, then use Resume Mission from Waypoint.";
    public float LevelStartedAt { get; private set; } = -1f;
    public float LevelCompletedAt { get; private set; } = -1f;
    public bool HasLevelStarted => LevelStartedAt >= 0f;
    public bool HasLevelCompleted => LevelCompletedAt >= 0f;

    void Start()
    {
        ApplyActiveLevelOverrides();
        bool isLV2Scene = string.Equals(
            SceneManager.GetActiveScene().name,
            LV2SceneName,
            StringComparison.OrdinalIgnoreCase
        );
        if (isLV2Scene)
        {
            showTutorial = false;
        }

        if (deliveryScoreManager == null)
        {
            deliveryScoreManager = FindFirstObjectByType<DeliveryScoreManager>();
        }

        ResolveMavlinkReceiver();
        ResolvePresentProgressUI();

        if (deliveryScoreManager == null)
        {
            SetObjectiveText($"{GetObjectiveLabel()}: 0/0");
            if (isLV2Scene)
            {
                StartLV2PlanningTutorial();
            }
            else
            {
                SetStatusText("Waiting for delivery system...");
            }
            Debug.LogWarning("Level1Manager: DeliveryScoreManager was not found.");
            return;
        }

        deliveryScoreManager.TargetsRemainingChanged += UpdateObjective;
        deliveryScoreManager.DeliveryTargetDiscovered += OnLV2DeliveryTargetDiscovered;
        deliveryScoreManager.LevelCompleted += CompleteLevel;
        deliveryScoreManager.SetTutorialLockout(showTutorial);
        deliveryScoreManager.SetDeliveryStatusSuppressed(showTutorial);

        if (showTutorial)
        {
            tutorialFinished = false;
            tutorialStep = TutorialStep.WaitForConnection;
            hasControlReference = false;
            connectionDetectedTime = -1f;
            mapModeInstructionStartedAt = -1f;
            nextMapTypeCheckAt = -1f;
            qgcMapSettingsWarningLogged = false;
            deliveryUnlockedTime = -1f;
            struggleHintShown = false;
            LevelStartedAt = -1f;
            LevelCompletedAt = -1f;
            RefreshDeliveryObjective();
            SetStatusText(welcomeMessage);
        }
        else
        {
            tutorialFinished = true;
            UpdateObjective(deliveryScoreManager.TotalTargets > 0
                ? deliveryScoreManager.RemainingTargets
                : deliveryScoreManager.targetBuildingCount);
            SetStatusText("");
        }

        StartLV2PlanningTutorial();
    }

    void ApplyActiveLevelOverrides()
    {
        if (!GameManager.hasActiveLevelSettings)
        {
            return;
        }

        showTutorial = GameManager.activeShowTutorial;
        autoLoadNextScene = GameManager.activeAutoLoadNextScene;

        if (!string.IsNullOrWhiteSpace(GameManager.activeNextSceneName))
        {
            nextSceneName = GameManager.activeNextSceneName;
        }

        if (GameManager.activeNextSceneDelaySeconds >= 0f)
        {
            nextSceneDelaySeconds = GameManager.activeNextSceneDelaySeconds;
        }

        if (GameManager.hasActiveDeliveryCount)
        {
            string presentWord = GameManager.activeDeliveryCount == 1 ? "present" : "presents";
            transitionMessage =
                $"Good. The delivery targets are now unlocked. " +
                $"You still need to deliver {GameManager.activeDeliveryCount} {presentWord}.";
        }
    }

    void Update()
    {
        if (PauseMenuController.IsPaused) return;
        if (completed || (deliveryScoreManager == null && !isLV2PlanningTutorial))
        {
            return;
        }

        ResolveMavlinkReceiver();
        TryAutoStartLevelTimer();

        if (isLV2PlanningTutorial)
        {
            AdvanceLV2PlanningTutorial();
            return;
        }

        if (!showTutorial)
        {
            return;
        }

        AdvanceTutorialIfReady();
        ShowStruggleHintIfNeeded();
    }

    void OnDestroy()
    {
        if (deliveryScoreManager != null)
        {
            deliveryScoreManager.TargetsRemainingChanged -= UpdateObjective;
            deliveryScoreManager.DeliveryTargetDiscovered -= OnLV2DeliveryTargetDiscovered;
            deliveryScoreManager.LevelCompleted -= CompleteLevel;
            deliveryScoreManager.SetTutorialLockout(false);
        }

        CancelInvoke();
    }

    void ResolveMavlinkReceiver()
    {
        if (mavlinkReceiver == null)
        {
            mavlinkReceiver = MAVLinkReceiver.Active;
        }

        if (mavlinkReceiver == null)
        {
            mavlinkReceiver = FindFirstObjectByType<MAVLinkReceiver>();
        }
    }

    void StartLV2PlanningTutorial()
    {
        isLV2PlanningTutorial = string.Equals(
            SceneManager.GetActiveScene().name,
            LV2SceneName,
            StringComparison.OrdinalIgnoreCase
        );
        if (!isLV2PlanningTutorial)
        {
            return;
        }

        ConfigureLV2TutorialText();
        ResolveMavlinkReceiver();
        hadMissionCurrentWhenLV2Started =
            mavlinkReceiver != null && mavlinkReceiver.HasMissionCurrent;
        missionTotalWhenLV2Started = hadMissionCurrentWhenLV2Started
            ? mavlinkReceiver.MissionCurrentTotal
            : (ushort)0;
        missionSequenceWhenLV2Started = hadMissionCurrentWhenLV2Started
            ? mavlinkReceiver.MissionCurrentSequence
            : (ushort)0;
        autoModeWhenLV2Started = mavlinkReceiver != null && mavlinkReceiver.IsAutoMode;
        missionUpdateCountWhenLV2Started = mavlinkReceiver != null
            ? mavlinkReceiver.MissionCurrentUpdateCount
            : 0;

        if (mavlinkReceiver != null && mavlinkReceiver.HasRecentTelemetry)
        {
            lv2PlanningTutorialStep = LV2PlanningTutorialStep.PlanningMission;
            SetStatusText(LV2PlanningMessage);
        }
        else
        {
            lv2PlanningTutorialStep = LV2PlanningTutorialStep.WaitingForTelemetry;
            SetStatusText(LV2WaitingForTelemetryMessage);
        }
    }

    void ConfigureLV2TutorialText()
    {
        if (levelStatusText == null)
        {
            return;
        }

        RectTransform textRect = levelStatusText.rectTransform;
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(1580f, 430f);

        levelStatusText.alignment = TMPro.TextAlignmentOptions.Center;
        levelStatusText.fontSize = 34f;
        levelStatusText.fontSizeMin = 26f;
        levelStatusText.fontSizeMax = 34f;
        levelStatusText.enableAutoSizing = true;
        levelStatusText.characterSpacing = 0f;
        levelStatusText.wordSpacing = 0f;
        levelStatusText.margin = Vector4.zero;
        levelStatusText.raycastTarget = false;
    }

    void AdvanceLV2PlanningTutorial()
    {
        if (lv2PlanningTutorialStep == LV2PlanningTutorialStep.Finished)
        {
            return;
        }

        if (mavlinkReceiver != null && mavlinkReceiver.IsAutoMode && mavlinkReceiver.IsArmed)
        {
            lv2PlanningTutorialStep = LV2PlanningTutorialStep.Finished;
            SetStatusText("");
            return;
        }

        if (lv2PlanningTutorialStep == LV2PlanningTutorialStep.MissionDetected)
        {
            SetStatusText(LV2MissionReceivedMessage);
            return;
        }

        if (mavlinkReceiver == null || !mavlinkReceiver.HasRecentTelemetry)
        {
            lv2PlanningTutorialStep = LV2PlanningTutorialStep.WaitingForTelemetry;
            SetStatusText(LV2WaitingForTelemetryMessage);
            return;
        }

        if (lv2PlanningTutorialStep == LV2PlanningTutorialStep.WaitingForTelemetry)
        {
            lv2PlanningTutorialStep = LV2PlanningTutorialStep.PlanningMission;
            SetStatusText(LV2PlanningMessage);
        }

        if (!mavlinkReceiver.HasMissionCurrent)
        {
            return;
        }

        ushort missionTotal = mavlinkReceiver.MissionCurrentTotal;
        bool vehicleHasMission = missionTotal > 0 && missionTotal < ushort.MaxValue;
        bool missionChangedSinceLV2Started =
            !hadMissionCurrentWhenLV2Started ||
            missionTotal != missionTotalWhenLV2Started ||
            mavlinkReceiver.MissionCurrentSequence != missionSequenceWhenLV2Started ||
            (!autoModeWhenLV2Started && mavlinkReceiver.IsAutoMode);
        bool missionTelemetryUpdated =
            mavlinkReceiver.MissionCurrentUpdateCount > missionUpdateCountWhenLV2Started;

        if (vehicleHasMission && missionChangedSinceLV2Started && missionTelemetryUpdated)
        {
            lv2PlanningTutorialStep = LV2PlanningTutorialStep.MissionDetected;
            SetStatusText(LV2MissionReceivedMessage);
        }
    }

    void AdvanceTutorialIfReady()
    {
        if (mavlinkReceiver == null)
        {
            return;
        }

        switch (tutorialStep)
        {
            case TutorialStep.WaitForConnection:
                if (mavlinkReceiver.HasRecentTelemetry)
                {
                    if (connectionDetectedTime < 0f)
                    {
                        connectionDetectedTime = Time.time;
                    }

                    if ((Time.time - connectionDetectedTime) >= connectionHoldSeconds)
                    {
                        SetTutorialStep(TutorialStep.ShowMapMode);
                    }
                }
                break;

            case TutorialStep.ShowMapMode:
                if (mapModeInstructionStartedAt < 0f)
                {
                    mapModeInstructionStartedAt = Time.time;
                }
                else if ((Time.time - mapModeInstructionStartedAt) >= mapModeDisplaySeconds)
                {
                    SetTutorialStep(IsQgcStreetMapSelected()
                        ? TutorialStep.WaitForGuidedMode
                        : TutorialStep.WaitForMapType);
                }
                break;

            case TutorialStep.WaitForMapType:
                if (IsQgcStreetMapSelected())
                {
                    SetTutorialStep(TutorialStep.WaitForGuidedMode);
                }
                break;

            case TutorialStep.WaitForGuidedMode:
                if (mavlinkReceiver.IsGuidedMode)
                {
                    SetTutorialStep(TutorialStep.WaitForTakeoff);
                }
                break;

            case TutorialStep.WaitForTakeoff:
                if (HasTakenOff())
                {
                    hasControlReference = false;
                    SetTutorialStep(TutorialStep.WaitForControl);
                }
                break;

            case TutorialStep.WaitForControl:
                if (HasMovedEnough())
                {
                    tutorialFinished = true;
                    deliveryScoreManager.SetTutorialLockout(false);
                    SetTutorialStep(TutorialStep.DeliveryUnlocked);
                }
                break;
        }
    }

    bool IsQgcStreetMapSelected()
    {
        if (Time.time < nextMapTypeCheckAt)
        {
            return false;
        }

        nextMapTypeCheckAt = Time.time + 0.5f;
        if (!TryReadQgcMapType(out string mapType))
        {
            if (!qgcMapSettingsWarningLogged)
            {
                qgcMapSettingsWarningLogged = true;
                Debug.LogWarning(
                    "Level1Manager: could not read QGroundControl's FlightMap/mapType " +
                    "setting. The map selection tutorial will keep waiting."
                );
            }

            return false;
        }

        qgcMapSettingsWarningLogged = false;
        return string.Equals(mapType, "Street Map", StringComparison.OrdinalIgnoreCase);
    }

    static bool TryReadQgcMapType(out string mapType)
    {
        mapType = null;
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] candidates =
        {
            Path.Combine(userProfile, ".config", "qgroundcontrol.com", "QGroundControl.ini"),
            Path.Combine(userProfile, ".config", "org.qgroundcontrol", "QGroundControl.ini"),
            Path.Combine(userProfile, ".config", "QGroundControl.org", "QGroundControl.ini"),
            Path.Combine(roamingAppData, "QGroundControl", "QGroundControl.ini"),
            Path.Combine(roamingAppData, "QGroundControl.org", "QGroundControl.ini"),
            Path.Combine(roamingAppData, "QGroundControl.ini")
        };

        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            if (!File.Exists(candidates[candidateIndex]))
            {
                continue;
            }

            try
            {
                string[] lines = File.ReadAllLines(candidates[candidateIndex]);
                bool inFlightMapSection = false;
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex].Trim();
                    if (line.StartsWith("[", StringComparison.Ordinal) &&
                        line.EndsWith("]", StringComparison.Ordinal))
                    {
                        inFlightMapSection = string.Equals(
                            line.Substring(1, line.Length - 2),
                            "FlightMap",
                            StringComparison.OrdinalIgnoreCase
                        );
                        continue;
                    }

                    if (!inFlightMapSection || line.Length == 0 || line.StartsWith(";"))
                    {
                        continue;
                    }

                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separatorIndex).Trim();
                    if (string.Equals(key, "mapType", StringComparison.OrdinalIgnoreCase))
                    {
                        mapType = line.Substring(separatorIndex + 1).Trim();
                        return true;
                    }
                }
            }
            catch (IOException)
            {
                // QGC can be writing the settings file at the same time. Retry
                // during the next tutorial poll.
            }
            catch (UnauthorizedAccessException)
            {
                // A later candidate may still be readable on this platform.
            }
        }

        return false;
    }

    bool HasTakenOff()
    {
        if (mavlinkReceiver == null)
        {
            return false;
        }

        if (mavlinkReceiver.LandedState == MAVLink.MAV_LANDED_STATE.IN_AIR)
        {
            return true;
        }

        return mavlinkReceiver.RelativeAltitudeMeters >= takeoffAltitudeMeters;
    }

    bool HasMovedEnough()
    {
        Vector3 currentPosition = GetDroneWorldPosition();

        if (!hasControlReference)
        {
            controlStartPosition = currentPosition;
            hasControlReference = true;
            return false;
        }

        Vector3 delta = currentPosition - controlStartPosition;
        delta.y = 0f;
        return delta.magnitude >= controlMovementMeters;
    }

    Vector3 GetDroneWorldPosition()
    {
        if (mavlinkReceiver != null)
        {
            return mavlinkReceiver.transform.position;
        }

        if (deliveryScoreManager != null && deliveryScoreManager.drone != null)
        {
            return deliveryScoreManager.drone.position;
        }

        return Vector3.zero;
    }

    void UpdateObjective(int remaining)
    {
        if (completed)
        {
            return;
        }

        RefreshDeliveryObjective();

        if (tutorialFinished &&
            deliveryScoreManager != null &&
            deliveryScoreManager.CompletedTargets > 0)
        {
            SetStatusText("");
            deliveryScoreManager.SetDeliveryStatusSuppressed(false);
        }
    }

    private void OnLV2DeliveryTargetDiscovered(int targetNumber, float roofAltitudeAMSL, float deliveryAltitudeAMSL)
    {
        if (!isLV2PlanningTutorial || deliveryScoreManager == null)
        {
            return;
        }

        int found = deliveryScoreManager.DiscoveredDeliveryTargetCount;
        int total = Mathf.Max(1, deliveryScoreManager.targetBuildingCount);
        SetStatusText($"HOUSE FOUND  |  {found}/{total} delivery houses discovered");
        RefreshDeliveryObjective();
    }

    void ShowStruggleHintIfNeeded()
    {
        if (struggleHintShown ||
            !tutorialFinished ||
            tutorialStep != TutorialStep.DeliveryUnlocked ||
            deliveryScoreManager.CompletedTargets > 0 ||
            deliveryUnlockedTime < 0f ||
            (Time.time - deliveryUnlockedTime) < struggleHintDelaySeconds)
        {
            return;
        }

        struggleHintShown = true;
        SetStatusText(struggleHintMessage);
    }

    void SetObjectiveText(string text)
    {
        if (objectiveText != null)
        {
            objectiveText.text = text;
        }
    }

    void SetStatusText(string message)
    {
        if (levelStatusText != null)
        {
            levelStatusText.text = message;
        }
    }

    void SetTutorialStep(TutorialStep newStep)
    {
        tutorialStep = newStep;

        switch (tutorialStep)
        {
            case TutorialStep.WaitForConnection:
                SetStatusText(welcomeMessage);
                break;
            case TutorialStep.ShowMapMode:
                SetStatusText(mapModeMessage);
                break;
            case TutorialStep.WaitForMapType:
                SetStatusText(mapModeMessage);
                break;
            case TutorialStep.WaitForGuidedMode:
                SetStatusText(findTargetMessage);
                break;
            case TutorialStep.WaitForTakeoff:
                SetStatusText(approachMessage);
                break;
            case TutorialStep.WaitForControl:
                SetStatusText(holdPositionMessage);
                break;
            case TutorialStep.DeliveryUnlocked:
                deliveryUnlockedTime = Time.time;
                struggleHintShown = false;
                SetStatusText(transitionMessage);
                BeginLevelTimer();
                RefreshDeliveryObjective();
                break;
            case TutorialStep.Finished:
                break;
        }
    }

    void CompleteLevel()
    {
        if (completed)
        {
            return;
        }

        completed = true;
        tutorialStep = TutorialStep.Finished;
        LevelCompletedAt = Time.time;
        RefreshDeliveryObjective();
        SetStatusText("");
        deliveryScoreManager.SetDeliveryStatusSuppressed(true);
        Debug.Log("Level1Manager: Level 1 complete.");

        if (!autoLoadNextScene)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(nextSceneName))
        {
            Debug.LogWarning("Level1Manager: Next Scene Name is empty.");
            return;
        }

        Invoke(nameof(LoadNextScene), Mathf.Max(0f, nextSceneDelaySeconds));
    }

    void LoadNextScene()
    {
        SceneManager.LoadScene(nextSceneName);
    }

    void BeginLevelTimer()
    {
        if (LevelStartedAt >= 0f)
        {
            return;
        }

        LevelStartedAt = Time.time;
        LevelStarted?.Invoke();
    }

    void TryAutoStartLevelTimer()
    {
        if (HasLevelStarted || deliveryScoreManager == null)
        {
            return;
        }

        if (!showTutorial || tutorialFinished)
        {
            if (deliveryScoreManager.TotalTargets > 0)
            {
                BeginLevelTimer();
            }
        }
    }

    void RefreshDeliveryObjective()
    {
        if (objectiveText == null || deliveryScoreManager == null)
        {
            return;
        }

        int completedTargets = deliveryScoreManager.CompletedTargets;
        int totalTargets = deliveryScoreManager.TotalTargets > 0
            ? deliveryScoreManager.TotalTargets
            : deliveryScoreManager.targetBuildingCount;

        bool isLV2Scene = string.Equals(
            SceneManager.GetActiveScene().name,
            LV2SceneName,
            StringComparison.OrdinalIgnoreCase);
        int displayTotal = Mathf.Max(1, deliveryScoreManager.targetBuildingCount);
        objectiveText.text = isLV2Scene
            ? $"Houses to deliver found: {deliveryScoreManager.DiscoveredDeliveryTargetCount}/{displayTotal}"
            : $"{GetObjectiveLabel()}: {completedTargets}/{Mathf.Max(1, totalTargets)}";

        ResolvePresentProgressUI();
        if (presentProgressUI != null)
        {
            presentProgressUI.SetProgress(completedTargets, Mathf.Max(1, totalTargets));
        }
    }

    private void ResolvePresentProgressUI()
    {
        if (presentProgressUI != null)
        {
            return;
        }

        presentProgressUI = FindFirstObjectByType<DeliveryPresentProgressUI>(
            FindObjectsInactive.Include);
        if (presentProgressUI != null || objectiveText == null)
        {
            return;
        }

        GameObject progressObject = new GameObject(
            "DeliveryPresentProgress",
            typeof(RectTransform),
            typeof(DeliveryPresentProgressUI));
        RectTransform progressRect = progressObject.GetComponent<RectTransform>();
        progressRect.SetParent(objectiveText.rectTransform.parent, false);
        progressRect.anchorMin = new Vector2(1f, 1f);
        progressRect.anchorMax = new Vector2(1f, 1f);
        progressRect.pivot = new Vector2(0.5f, 0.5f);
        progressRect.anchoredPosition = new Vector2(-174f, -170f);
        progressRect.sizeDelta = new Vector2(224f, 40f);
        presentProgressUI = progressObject.GetComponent<DeliveryPresentProgressUI>();
    }

    private string GetObjectiveLabel()
    {
        return string.IsNullOrWhiteSpace(objectivePrefix)
            ? "Deliver presents"
            : objectivePrefix.Trim();
    }
}
