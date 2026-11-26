using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared level lifecycle, delivery events, HUD updates, timing, and scene flow.
/// Each playable level derives from this class and owns its own tutorial.
/// </summary>
public abstract class LevelManager : MonoBehaviour
{
    [Header("Delivery System")]
    public DeliveryScoreManager deliveryScoreManager;

    [Header("UI")]
    public TextMeshProUGUI objectiveText;
    public TextMeshProUGUI levelStatusText;
    public string objectivePrefix = "Present Delivered: ";
    public string completedMessage = "Nice work! You completed the level.";
    public DeliveryPresentProgressUI presentProgressUI;

    [Header("Scene Flow")]
    public bool autoLoadNextScene;
    public string nextSceneName = "";
    [Min(0f)] public float nextSceneDelaySeconds = 2f;

    public event Action LevelStarted;

    public float LevelStartedAt { get; private set; } = -1f;
    public float LevelCompletedAt { get; private set; } = -1f;
    public bool HasLevelStarted => LevelStartedAt >= 0f;
    public bool HasLevelCompleted => LevelCompletedAt >= 0f;
    protected bool LevelIsComplete { get; private set; }
    protected MAVLinkReceiver MavlinkReceiver { get; private set; }

    protected virtual bool LocksDeliveryUntilTutorialCompletes => false;
    protected virtual bool StartsTimerWhenTargetsLoad => true;

    protected virtual void Start()
    {
        ApplyActiveLevelOverrides();
        if (deliveryScoreManager == null)
        {
            deliveryScoreManager = FindFirstObjectByType<DeliveryScoreManager>();
        }

        ResolveMavlinkReceiver();
        ResolvePresentProgressUI();

        if (deliveryScoreManager != null)
        {
            deliveryScoreManager.TargetsRemainingChanged += UpdateObjective;
            deliveryScoreManager.DeliveryTargetDiscovered += OnDeliveryTargetDiscovered;
            deliveryScoreManager.LevelCompleted += CompleteLevel;
            deliveryScoreManager.SetTutorialLockout(LocksDeliveryUntilTutorialCompletes);
            deliveryScoreManager.SetDeliveryStatusSuppressed(LocksDeliveryUntilTutorialCompletes);
        }
        else
        {
            SetObjectiveText("Deliveries: 0/0");
            Debug.LogWarning($"{GetType().Name}: DeliveryScoreManager was not found.");
        }

        OnLevelInitialized();
    }

    protected virtual void Update()
    {
        if (PauseMenuController.IsPaused || LevelIsComplete)
        {
            return;
        }

        ResolveMavlinkReceiver();
        if (StartsTimerWhenTargetsLoad)
        {
            TryAutoStartLevelTimer();
        }
        OnLevelUpdate();
    }

    protected virtual void OnDestroy()
    {
        if (deliveryScoreManager != null)
        {
            deliveryScoreManager.TargetsRemainingChanged -= UpdateObjective;
            deliveryScoreManager.DeliveryTargetDiscovered -= OnDeliveryTargetDiscovered;
            deliveryScoreManager.LevelCompleted -= CompleteLevel;
            deliveryScoreManager.SetTutorialLockout(false);
            deliveryScoreManager.SetDeliveryStatusSuppressed(false);
        }

        CancelInvoke();
    }

    protected virtual void OnLevelInitialized() { }
    protected virtual void OnLevelUpdate() { }
    protected virtual void OnDeliveryTargetDiscovered(int targetNumber, float roofAltitudeAMSL, float deliveryAltitudeAMSL) { }

    protected virtual string GetObjectiveText()
    {
        int completed = deliveryScoreManager != null ? deliveryScoreManager.CompletedTargets : 0;
        int total = deliveryScoreManager != null
            ? Mathf.Max(1, deliveryScoreManager.TotalTargets > 0
                ? deliveryScoreManager.TotalTargets
                : deliveryScoreManager.targetBuildingCount)
            : 0;
        return $"{GetObjectiveLabel()}: {completed}/{total}";
    }

    protected virtual string GetObjectiveLabel()
    {
        return string.IsNullOrWhiteSpace(objectivePrefix)
            ? "Deliver presents"
            : objectivePrefix.Trim();
    }

    protected virtual void ApplyActiveLevelOverrides()
    {
        if (!GameManager.hasActiveLevelSettings)
        {
            return;
        }

        autoLoadNextScene = GameManager.activeAutoLoadNextScene;
        if (!string.IsNullOrWhiteSpace(GameManager.activeNextSceneName))
        {
            nextSceneName = GameManager.activeNextSceneName;
        }

        nextSceneDelaySeconds = Mathf.Max(0f, GameManager.activeNextSceneDelaySeconds);
    }

    protected void ResolveMavlinkReceiver()
    {
        if (MavlinkReceiver == null)
        {
            MavlinkReceiver = MAVLinkReceiver.Active;
        }
        if (MavlinkReceiver == null)
        {
            MavlinkReceiver = FindFirstObjectByType<MAVLinkReceiver>();
        }
    }

    protected void SetStatusText(string message)
    {
        if (levelStatusText != null)
        {
            levelStatusText.text = message ?? string.Empty;
        }
    }

    protected void BeginLevelTimer()
    {
        if (HasLevelStarted)
        {
            return;
        }

        LevelStartedAt = Time.time;
        LevelStarted?.Invoke();
    }

    protected void TryAutoStartLevelTimer()
    {
        if (!HasLevelStarted && deliveryScoreManager != null && deliveryScoreManager.TotalTargets > 0)
        {
            BeginLevelTimer();
        }
    }

    protected void RefreshDeliveryObjective()
    {
        if (objectiveText != null)
        {
            objectiveText.text = GetObjectiveText();
        }

        ResolvePresentProgressUI();
        if (presentProgressUI != null && deliveryScoreManager != null)
        {
            int total = Mathf.Max(1, deliveryScoreManager.TotalTargets > 0
                ? deliveryScoreManager.TotalTargets
                : deliveryScoreManager.targetBuildingCount);
            presentProgressUI.SetProgress(deliveryScoreManager.CompletedTargets, total);
        }
    }

    protected void ResolvePresentProgressUI()
    {
        if (presentProgressUI != null)
        {
            return;
        }

        presentProgressUI = FindFirstObjectByType<DeliveryPresentProgressUI>(FindObjectsInactive.Include);
        if (presentProgressUI != null || objectiveText == null)
        {
            return;
        }

        GameObject progressObject = new GameObject(
            "DeliveryPresentProgress", typeof(RectTransform), typeof(DeliveryPresentProgressUI));
        RectTransform progressRect = progressObject.GetComponent<RectTransform>();
        progressRect.SetParent(objectiveText.rectTransform.parent, false);
        progressRect.anchorMin = Vector2.one;
        progressRect.anchorMax = Vector2.one;
        progressRect.pivot = new Vector2(0.5f, 0.5f);
        progressRect.anchoredPosition = new Vector2(-174f, -170f);
        progressRect.sizeDelta = new Vector2(224f, 40f);
        presentProgressUI = progressObject.GetComponent<DeliveryPresentProgressUI>();
    }

    private void UpdateObjective(int remaining)
    {
        if (LevelIsComplete)
        {
            return;
        }
        RefreshDeliveryObjective();
        if (deliveryScoreManager != null && deliveryScoreManager.CompletedTargets > 0)
        {
            deliveryScoreManager.SetDeliveryStatusSuppressed(false);
        }
    }

    private void CompleteLevel()
    {
        if (LevelIsComplete)
        {
            return;
        }

        LevelIsComplete = true;
        LevelCompletedAt = Time.time;
        RefreshDeliveryObjective();
        SetStatusText(completedMessage);
        deliveryScoreManager.SetDeliveryStatusSuppressed(true);
        Debug.Log($"{GetType().Name}: level complete.");

        if (autoLoadNextScene && !string.IsNullOrWhiteSpace(nextSceneName))
        {
            Invoke(nameof(LoadNextScene), Mathf.Max(0f, nextSceneDelaySeconds));
        }
    }

    private void LoadNextScene()
    {
        SceneManager.LoadScene(nextSceneName);
    }

    private void SetObjectiveText(string text)
    {
        if (objectiveText != null)
        {
            objectiveText.text = text;
        }
    }
}
