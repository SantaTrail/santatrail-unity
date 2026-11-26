using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>LV1 guided-flight onboarding and delivery hints.</summary>
public sealed class Level1Manager : LevelManager
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

    [Header("LV1 Guided Flight Tutorial")]
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
    [Tooltip("Show delivery help after this many seconds with no completed delivery.")]
    [Min(1f)] public float struggleHintDelaySeconds = 30f;

    private bool tutorialFinished;
    private TutorialStep tutorialStep = TutorialStep.WaitForConnection;
    private Vector3 controlStartPosition;
    private bool hasControlReference;
    private float connectionDetectedTime = -1f;
    private float mapModeInstructionStartedAt = -1f;
    private float nextMapTypeCheckAt = -1f;
    private bool qgcMapSettingsWarningLogged;
    private float deliveryUnlockedTime = -1f;
    private bool struggleHintShown;

    protected override bool LocksDeliveryUntilTutorialCompletes => showTutorial && !tutorialFinished;
    protected override bool StartsTimerWhenTargetsLoad => !showTutorial || tutorialFinished;

    protected override void ApplyActiveLevelOverrides()
    {
        base.ApplyActiveLevelOverrides();
        if (GameManager.hasActiveLevelSettings)
        {
            showTutorial = GameManager.activeShowTutorial;
        }
        if (GameManager.hasActiveDeliveryCount)
        {
            string presentWord = GameManager.activeDeliveryCount == 1 ? "present" : "presents";
            transitionMessage =
                $"Good. The delivery targets are now unlocked. You still need to deliver " +
                $"{GameManager.activeDeliveryCount} {presentWord}.";
        }
    }

    protected override void OnLevelInitialized()
    {
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
            SetStatusText(welcomeMessage);
        }
        else
        {
            tutorialFinished = true;
            SetStatusText("");
        }
        RefreshDeliveryObjective();
    }

    protected override void OnLevelUpdate()
    {
        if (!showTutorial || tutorialFinished)
        {
            ShowStruggleHintIfNeeded();
            return;
        }

        AdvanceTutorialIfReady();
        ShowStruggleHintIfNeeded();
    }

    private void AdvanceTutorialIfReady()
    {
        if (MavlinkReceiver == null)
        {
            return;
        }

        switch (tutorialStep)
        {
            case TutorialStep.WaitForConnection:
                if (MavlinkReceiver.HasRecentTelemetry)
                {
                    if (connectionDetectedTime < 0f)
                    {
                        connectionDetectedTime = Time.time;
                    }
                    if (Time.time - connectionDetectedTime >= connectionHoldSeconds)
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
                else if (Time.time - mapModeInstructionStartedAt >= mapModeDisplaySeconds)
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
                if (MavlinkReceiver.IsGuidedMode)
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
                    if (deliveryScoreManager != null)
                    {
                        deliveryScoreManager.SetTutorialLockout(false);
                    }
                    SetTutorialStep(TutorialStep.DeliveryUnlocked);
                }
                break;
        }
    }

    private bool IsQgcStreetMapSelected()
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
                Debug.LogWarning("Level1Manager: could not read QGroundControl's FlightMap/mapType setting.");
            }
            return false;
        }
        qgcMapSettingsWarningLogged = false;
        return string.Equals(mapType, "Street Map", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadQgcMapType(out string mapType)
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

        foreach (string candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                bool inFlightMapSection = false;
                foreach (string rawLine in File.ReadAllLines(candidate))
                {
                    string line = rawLine.Trim();
                    if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                    {
                        inFlightMapSection = string.Equals(line.Substring(1, line.Length - 2), "FlightMap", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inFlightMapSection || line.Length == 0 || line.StartsWith(";")) continue;
                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0) continue;
                    if (string.Equals(line.Substring(0, separatorIndex).Trim(), "mapType", StringComparison.OrdinalIgnoreCase))
                    {
                        mapType = line.Substring(separatorIndex + 1).Trim();
                        return true;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return false;
    }

    private bool HasTakenOff()
    {
        return MavlinkReceiver != null &&
            (MavlinkReceiver.LandedState == MAVLink.MAV_LANDED_STATE.IN_AIR ||
             MavlinkReceiver.RelativeAltitudeMeters >= takeoffAltitudeMeters);
    }

    private bool HasMovedEnough()
    {
        Vector3 currentPosition = MavlinkReceiver != null
            ? MavlinkReceiver.transform.position
            : (deliveryScoreManager != null && deliveryScoreManager.drone != null
                ? deliveryScoreManager.drone.position
                : Vector3.zero);
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

    private void ShowStruggleHintIfNeeded()
    {
        if (struggleHintShown || !tutorialFinished ||
            tutorialStep != TutorialStep.DeliveryUnlocked || deliveryScoreManager == null ||
            deliveryScoreManager.CompletedTargets > 0 || deliveryUnlockedTime < 0f ||
            Time.time - deliveryUnlockedTime < struggleHintDelaySeconds)
        {
            return;
        }
        struggleHintShown = true;
        SetStatusText(struggleHintMessage);
    }

    private void SetTutorialStep(TutorialStep step)
    {
        tutorialStep = step;
        switch (step)
        {
            case TutorialStep.WaitForConnection: SetStatusText(welcomeMessage); break;
            case TutorialStep.ShowMapMode:
            case TutorialStep.WaitForMapType: SetStatusText(mapModeMessage); break;
            case TutorialStep.WaitForGuidedMode: SetStatusText(findTargetMessage); break;
            case TutorialStep.WaitForTakeoff: SetStatusText(approachMessage); break;
            case TutorialStep.WaitForControl: SetStatusText(holdPositionMessage); break;
            case TutorialStep.DeliveryUnlocked:
                deliveryUnlockedTime = Time.time;
                struggleHintShown = false;
                SetStatusText(transitionMessage);
                BeginLevelTimer();
                RefreshDeliveryObjective();
                break;
        }
    }
}
