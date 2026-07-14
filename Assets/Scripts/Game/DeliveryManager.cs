using TMPro;
using UnityEngine;

public class DeliveryManager : MonoBehaviour
{
    [Header("Settings")]
    public int totalDeliveries = 2;

    [Header("UI")]
    public TMP_Text progressText;

    [Header("References")]
    public SantaLetterGameManager santaLetterGameManager;

    private int completedDeliveries = 0;

    public void CompleteDelivery()
    {
        completedDeliveries++;

        UpdateProgress();

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

        // We'll replace this later with whatever
        // your main game needs.
    }
}