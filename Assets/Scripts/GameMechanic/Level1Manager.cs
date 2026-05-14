using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Level1Manager : MonoBehaviour
{
    [Header("Level 1 Goal")]
    public DeliveryScoreManager deliveryScoreManager;
    public int targetScore = 500;

    [Header("UI")]
    public TextMeshProUGUI objectiveText;
    public TextMeshProUGUI levelStatusText;
    public string objectivePrefix = "Level 1 Goal";
    public string completedMessage = "Level 1 Complete!";

    [Header("Scene Flow")]
    public bool autoLoadNextScene = false;
    public string nextSceneName = "";
    public float nextSceneDelaySeconds = 2f;

    private bool completed;

    void Start()
    {
        if (deliveryScoreManager == null)
        {
            deliveryScoreManager = FindFirstObjectByType<DeliveryScoreManager>();
        }

        if (deliveryScoreManager != null)
        {
            deliveryScoreManager.ScoreChanged += OnScoreChanged;
            OnScoreChanged(deliveryScoreManager.score);
        }
        else
        {
            SetObjectiveText(0);
            SetStatusText("Level 1 waiting for DeliveryScoreManager...");
            Debug.LogWarning("Level1Manager: DeliveryScoreManager not found in scene.");
        }
    }

    void OnDestroy()
    {
        if (deliveryScoreManager != null)
        {
            deliveryScoreManager.ScoreChanged -= OnScoreChanged;
        }
    }

    void OnScoreChanged(int score)
    {
        if (completed) return;

        SetObjectiveText(score);

        if (score >= targetScore)
        {
            CompleteLevel();
        }
    }

    void SetObjectiveText(int score)
    {
        if (objectiveText == null) return;
        objectiveText.text = $"{objectivePrefix}: {score}/{targetScore}";
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
        completed = true;
        SetStatusText(completedMessage);
        Debug.Log("Level1Manager: Level 1 complete.");

        if (!autoLoadNextScene) return;
        if (string.IsNullOrWhiteSpace(nextSceneName))
        {
            Debug.LogWarning("Level1Manager: autoLoadNextScene enabled but nextSceneName is empty.");
            return;
        }

        Invoke(nameof(LoadNextScene), Mathf.Max(0f, nextSceneDelaySeconds));
    }

    void LoadNextScene()
    {
        SceneManager.LoadScene(nextSceneName);
    }
}
