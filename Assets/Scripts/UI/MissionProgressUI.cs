using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MissionProgressUI : MonoBehaviour
{
    [Header("Delivery")]
    public TMP_Text deliveryText;

    [Header("Top Presents")]
    public Image[] presentIcons;

    public Sprite emptyPresent;
    public Sprite filledPresent;

    private int totalDeliveries;
    private int currentDelivery;

    public void SetupMission(int total)
    {
        totalDeliveries = total;
        currentDelivery = 1;

        RefreshUI();
    }

    public bool CompleteDelivery()
    {
        currentDelivery++;

        RefreshUI();

        return currentDelivery > totalDeliveries;
    }

    void RefreshUI()
{
    if (deliveryText != null)
    {
        deliveryText.text = $"DELIVERY {currentDelivery}/{totalDeliveries}";
    }

    if (presentIcons == null || presentIcons.Length == 0)
        return;

    for (int i = 0; i < presentIcons.Length; i++)
    {
        if (i < totalDeliveries)
        {
            presentIcons[i].gameObject.SetActive(true);

            if (filledPresent != null && emptyPresent != null)
            {
                presentIcons[i].sprite =
                    (i < currentDelivery) ? filledPresent : emptyPresent;
            }
        }
        else
        {
            presentIcons[i].gameObject.SetActive(false);
        }
    }
}
}
