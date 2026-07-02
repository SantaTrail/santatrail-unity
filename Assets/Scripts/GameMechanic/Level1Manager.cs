using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Level1Manager : MonoBehaviour
{
    [Header("Delivery System")]
    public DeliveryScoreManager deliveryScoreManager;

    [Tooltip("When enabled, this level uses the target count from DeliveryScoreManager.")]
    public bool useDeliveryManagerTargetCount = true;

    [Tooltip("Used only when Use Delivery Manager Target Count is disabled.")]
    [Min(1)]
    public int requiredDeliveries = 5;

    [Header("UI")]
    public TextMeshProUGUI objectiveText;
    public TextMeshProUGUI levelStatusText;

    public string objectivePrefix = "Presents Delivered";
    public string completedMessage = "Level 1 Complete!";

    [Header("Scene Flow")]
    public bool autoLoadNextScene = false;
    public string nextSceneName = "";
    [Min(0f)]
    public float nextSceneDelaySeconds = 2f;

    private bool completed;

    void Start()
    {
        if (deliveryScoreManager == null)
        {
            deliveryScoreManager =
                FindFirstObjectByType<DeliveryScoreManager>();
        }

        if (deliveryScoreManager == null)
        {
            SetObjectiveText(0, GetRequiredDeliveries());
            SetStatusText("Waiting for delivery system...");

            Debug.LogWarning(
                "Level1Manager: DeliveryScoreManager was not found."
            );

            return;
        }

        deliveryScoreManager.ScoreChanged += OnScoreChanged;

        RefreshProgress();
    }

    void Update()
    {
        // This also updates the UI after the random buildings finish spawning.
        if (!completed && deliveryScoreManager != null)
        {
            RefreshProgress();
        }
    }

    void OnDestroy()
    {
        if (deliveryScoreManager != null)
        {
            deliveryScoreManager.ScoreChanged -= OnScoreChanged;
        }

        CancelInvoke();
    }

    void OnScoreChanged(int newScore)
    {
        RefreshProgress();
    }

    void RefreshProgress()
    {
        if (deliveryScoreManager == null || completed)
        {
            return;
        }

        int delivered = deliveryScoreManager.CompletedTargets;
        int required = GetRequiredDeliveries();

        SetObjectiveText(delivered, required);

        if (delivered >= required && required > 0)
        {
            CompleteLevel();
        }
    }

    int GetRequiredDeliveries()
    {
        if (!useDeliveryManagerTargetCount)
        {
            return Mathf.Max(1, requiredDeliveries);
        }

        if (deliveryScoreManager == null)
        {
            return Mathf.Max(1, requiredDeliveries);
        }

        // TotalTargets becomes available after random targets are selected.
        if (deliveryScoreManager.TotalTargets > 0)
        {
            return deliveryScoreManager.TotalTargets;
        }

        // Display the Inspector target count while waiting for buildings.
        return Mathf.Max(
            1,
            deliveryScoreManager.targetBuildingCount
        );
    }

    void SetObjectiveText(int delivered, int required)
    {
        if (objectiveText == null)
        {
            return;
        }

        objectiveText.text =
            $"{objectivePrefix}: {delivered}/{required}";
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
        if (completed)
        {
            return;
        }

        completed = true;

        int required = GetRequiredDeliveries();
        SetObjectiveText(required, required);
        SetStatusText(completedMessage);

        Debug.Log("Level1Manager: Level 1 complete.");

        if (!autoLoadNextScene)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(nextSceneName))
        {
            Debug.LogWarning(
                "Level1Manager: Auto Load Next Scene is enabled, " +
                "but Next Scene Name is empty."
            );

            return;
        }

        Invoke(
            nameof(LoadNextScene),
            Mathf.Max(0f, nextSceneDelaySeconds)
        );
    }

    void LoadNextScene()
    {
        SceneManager.LoadScene(nextSceneName);
    }
}