using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Level1Manager : MonoBehaviour
{
    [Header("Delivery System")]
    public DeliveryScoreManager deliveryScoreManager;

    [Header("UI")]
    public TextMeshProUGUI objectiveText;
    public TextMeshProUGUI levelStatusText;
    public string objectivePrefix = "Remaining Presents: ";
    public string completedMessage = "Level 1 Complete!";

    [Header("Scene Flow")]
    public bool autoLoadNextScene = false;
    public string nextSceneName = "";
    [Min(0f)] public float nextSceneDelaySeconds = 2f;

    private bool completed;

    void Start()
    {
        if (deliveryScoreManager == null)
        {
            deliveryScoreManager = FindFirstObjectByType<DeliveryScoreManager>();
        }

        if (deliveryScoreManager == null)
        {
            SetObjectiveText(0);
            SetStatusText("Waiting for delivery system...");
            Debug.LogWarning("Level1Manager: DeliveryScoreManager was not found.");
            return;
        }

        deliveryScoreManager.TargetsRemainingChanged += UpdateObjective;
        deliveryScoreManager.LevelCompleted += CompleteLevel;

        int startingRemaining = deliveryScoreManager.TotalTargets > 0
            ? deliveryScoreManager.RemainingTargets
            : deliveryScoreManager.targetBuildingCount;

        UpdateObjective(startingRemaining);
        SetStatusText("");
    }

    void OnDestroy()
    {
        if (deliveryScoreManager != null)
        {
            deliveryScoreManager.TargetsRemainingChanged -= UpdateObjective;
            deliveryScoreManager.LevelCompleted -= CompleteLevel;
        }

        CancelInvoke();
    }

    void UpdateObjective(int remaining)
    {
        if (completed) return;
        SetObjectiveText(Mathf.Max(0, remaining));
    }

    void SetObjectiveText(int remaining)
    {
        if (objectiveText != null)
        {
            objectiveText.text = $"{objectivePrefix}: {remaining}";
        }
    }

    void SetStatusText(string message)
    {
        if (levelStatusText != null)
        {
            levelStatusText.text = message;
        }
    }

    void CompleteLevel()
    {
        if (completed) return;

        completed = true;
        SetObjectiveText(0);
        SetStatusText(completedMessage);
        Debug.Log("Level1Manager: Level 1 complete.");

        if (!autoLoadNextScene) return;

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
}
