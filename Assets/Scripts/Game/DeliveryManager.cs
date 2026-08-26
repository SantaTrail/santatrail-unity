using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DeliveryManager : MonoBehaviour
{
    [Header("Settings")]
    public int totalDeliveries = 2;

    [Header("UI")]
    public TMP_Text progressText;

    [Header("References")]
    public SantaLetterGameManager santaLetterGameManager;

    [Header("Scene Flow")]
    [SerializeField] private string nextSceneName = "PreviewLV";

    private int completedDeliveries = 0;

    private void Awake()
    {
        if (GameManager.hasActiveDeliveryCount)
        {
            totalDeliveries = Mathf.Max(1, GameManager.activeDeliveryCount);
        }
    }

    public void CompleteDelivery()
    {
        completedDeliveries++;

        UpdateProgress();

        if (santaLetterGameManager != null && santaLetterGameManager.missionUI != null)
        {
            santaLetterGameManager.missionUI.CompleteDelivery();
        }

        if (completedDeliveries >= totalDeliveries)
        {
            FinishMiniGame();
        }
        else
        {
            santaLetterGameManager.StartNewDelivery();
        }
    }

    void UpdateProgress()
    {
        if (progressText != null)
        {
            progressText.text =
                $"Delivery {completedDeliveries}/{totalDeliveries}";
        }
    }

    void FinishMiniGame()
    {
        Debug.Log("Santa Delivery Minigame Complete!");

        if (string.IsNullOrWhiteSpace(nextSceneName))
        {
            Debug.LogError("DeliveryManager: nextSceneName is empty.");
            return;
        }

        SceneManager.LoadScene(nextSceneName);
    }
}
