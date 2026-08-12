using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Optional level manager for scenes that use ManualDeliveryScoreManager.
/// Use this instead of a Level1Manager field that requires DeliveryScoreManager.
/// </summary>
public class ManualDeliveryLevelManager : MonoBehaviour
{
    [Header("Manual Delivery Goal")]
    public ManualDeliveryScoreManager deliveryScoreManager;

    [Header("UI")]
    public TextMeshProUGUI objectiveText;
    public TextMeshProUGUI levelStatusText;
    public string objectivePrefix = "Deliver presents";
    public string completedMessage = "Level Complete!";

    [Header("Scene Flow")]
    public bool autoLoadNextScene = false;
    public string nextSceneName = "";
    [Min(0f)] public float nextSceneDelaySeconds = 2f;

    private bool completed;

    private void Start()
    {
        ApplyActiveLevelOverrides();

        if (deliveryScoreManager == null)
        {
            deliveryScoreManager = FindFirstObjectByType<ManualDeliveryScoreManager>();
        }

        if (deliveryScoreManager == null)
        {
            SetObjectiveText(0, 0);
            SetStatusText("Waiting for ManualDeliveryScoreManager...");
            Debug.LogWarning("ManualDeliveryLevelManager could not find ManualDeliveryScoreManager.");
            return;
        }

        deliveryScoreManager.ScoreChanged += OnScoreChanged;
        deliveryScoreManager.TargetsRemainingChanged += OnTargetsRemainingChanged;
        deliveryScoreManager.LevelCompleted += OnLevelCompleted;

        RefreshObjective();
        SetStatusText("");
    }

    private void OnDestroy()
    {
        if (deliveryScoreManager == null)
        {
            return;
        }

        deliveryScoreManager.ScoreChanged -= OnScoreChanged;
        deliveryScoreManager.TargetsRemainingChanged -= OnTargetsRemainingChanged;
        deliveryScoreManager.LevelCompleted -= OnLevelCompleted;
    }

    private void OnScoreChanged(int newScore)
    {
        RefreshObjective();
    }

    private void OnTargetsRemainingChanged(int remaining)
    {
        RefreshObjective();
    }

    private void RefreshObjective()
    {
        if (deliveryScoreManager == null)
        {
            return;
        }

        SetObjectiveText(
            deliveryScoreManager.CompletedTargets,
            deliveryScoreManager.TotalTargets);
    }

    private void SetObjectiveText(int completedTargets, int totalTargets)
    {
        if (objectiveText != null)
        {
            objectiveText.text = $"{objectivePrefix}: {completedTargets}/{totalTargets}";
        }
    }

    private void ApplyActiveLevelOverrides()
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

        if (GameManager.activeNextSceneDelaySeconds >= 0f)
        {
            nextSceneDelaySeconds = GameManager.activeNextSceneDelaySeconds;
        }
    }

    private void OnLevelCompleted()
    {
        if (completed)
        {
            return;
        }

        completed = true;
        RefreshObjective();
        SetStatusText(completedMessage);

        if (autoLoadNextScene && !string.IsNullOrWhiteSpace(nextSceneName))
        {
            StartCoroutine(LoadNextSceneAfterDelay());
        }
    }

    private IEnumerator LoadNextSceneAfterDelay()
    {
        if (nextSceneDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(nextSceneDelaySeconds);
        }

        SceneManager.LoadScene(nextSceneName);
    }

    private void SetStatusText(string message)
    {
        if (levelStatusText != null)
        {
            levelStatusText.text = message;
        }
    }
}
