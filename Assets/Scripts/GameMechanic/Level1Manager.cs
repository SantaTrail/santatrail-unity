using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;

public class Level1Manager : MonoBehaviour
{
    private enum TutorialStep
    {
        WaitForConnection,
        WaitForGuidedMode,
        WaitForTakeoff,
        WaitForControl,
        DeliveryUnlocked,
        Finished
    }

    [Header("Delivery System")]
    public DeliveryScoreManager deliveryScoreManager;

    [Header("UI")]
    public TextMeshProUGUI objectiveText;
    public TextMeshProUGUI levelStatusText;
    public string objectivePrefix = "Present Delivered: ";
    public string completedMessage = "Nice work! You finished the guided-flight tutorial.";

    [Header("Beginner Tutorial")]
    public bool showTutorial = true;
    [TextArea(2, 4)] public string welcomeMessage =
        "Welcome! First, open QGroundControl and make sure the drone is connected.";
    [TextArea(2, 4)] public string findTargetMessage =
        "Change the flight mode to Guided so the drone accepts the tutorial controls.";
    [TextArea(2, 4)] public string approachMessage =
        "Use Guided takeoff and climb to a safe height.";
    [TextArea(2, 4)] public string holdPositionMessage =
        "Now try to gently control the drone and move it a little.";
    [TextArea(2, 4)] public string transitionMessage =
        "Good. The delivery targets are now unlocked. You still need to deliver 5 presents.";
    [Min(0.1f)] public float connectionHoldSeconds = 8f;
    [Min(0.1f)] public float takeoffAltitudeMeters = 1.5f;
    [Min(0.1f)] public float controlMovementMeters = 2f;

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

    public float LevelStartedAt { get; private set; } = -1f;
    public float LevelCompletedAt { get; private set; } = -1f;
    public bool HasLevelStarted => LevelStartedAt >= 0f;
    public bool HasLevelCompleted => LevelCompletedAt >= 0f;

    void Start()
    {
        if (deliveryScoreManager == null)
        {
            deliveryScoreManager = FindFirstObjectByType<DeliveryScoreManager>();
        }

        ResolveMavlinkReceiver();

        if (deliveryScoreManager == null)
        {
            SetObjectiveText(0);
            SetStatusText("Waiting for delivery system...");
            Debug.LogWarning("Level1Manager: DeliveryScoreManager was not found.");
            return;
        }

        deliveryScoreManager.TargetsRemainingChanged += UpdateObjective;
        deliveryScoreManager.LevelCompleted += CompleteLevel;
        deliveryScoreManager.SetTutorialLockout(showTutorial);

        if (showTutorial)
        {
            tutorialFinished = false;
            tutorialStep = TutorialStep.WaitForConnection;
            hasControlReference = false;
            connectionDetectedTime = -1f;
            LevelStartedAt = -1f;
            LevelCompletedAt = -1f;
            SetObjectiveText("Tutorial step 1/4");
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
    }

    void Update()
    {
        if (completed || deliveryScoreManager == null)
        {
            return;
        }

        ResolveMavlinkReceiver();
        TryAutoStartLevelTimer();

        if (!showTutorial)
        {
            return;
        }

        AdvanceTutorialIfReady();
    }

    void OnDestroy()
    {
        if (deliveryScoreManager != null)
        {
            deliveryScoreManager.TargetsRemainingChanged -= UpdateObjective;
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
                        SetTutorialStep(TutorialStep.WaitForGuidedMode);
                    }
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

        if (showTutorial && !tutorialFinished)
        {
            return;
        }

        RefreshDeliveryObjective();

        if (tutorialFinished &&
            deliveryScoreManager != null &&
            deliveryScoreManager.CompletedTargets > 0)
        {
            SetStatusText("");
        }
    }

    void SetObjectiveText(int remaining)
    {
        if (objectiveText != null)
        {
            objectiveText.text = $"{objectivePrefix}{remaining}";
        }
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
                SetObjectiveText("Tutorial step 1/4");
                SetStatusText(welcomeMessage);
                break;
            case TutorialStep.WaitForGuidedMode:
                SetObjectiveText("Tutorial step 2/4");
                SetStatusText(findTargetMessage);
                break;
            case TutorialStep.WaitForTakeoff:
                SetObjectiveText("Tutorial step 3/4");
                SetStatusText(approachMessage);
                break;
            case TutorialStep.WaitForControl:
                SetObjectiveText("Tutorial step 4/4");
                SetStatusText(holdPositionMessage);
                break;
            case TutorialStep.DeliveryUnlocked:
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

        objectiveText.text = $"{objectivePrefix}{completedTargets}/{Mathf.Max(1, totalTargets)}";
    }
}
