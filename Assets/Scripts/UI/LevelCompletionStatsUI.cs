using System.Collections;
using TMPro;
using UnityEngine;

public class LevelCompletionStatsUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DeliveryScoreManager deliveryScoreManager;
    [SerializeField] private Level1Manager level1Manager;
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text totalScoreText;
    [SerializeField] private TMP_Text timeUsedText;
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private TMP_Text christmasSpiritText;
    [SerializeField] private TMP_Text presentDeliveredText;
    [SerializeField] private TMP_Text presentRemainText;
    [SerializeField] private TMP_Text levelStatusText;
    [SerializeField] private TMP_Text statsText;
    [SerializeField] private TMP_Text noteText;
    [SerializeField] private CanvasGroup panelCanvasGroup;

    [Header("Behaviour")]
    [SerializeField] private bool hideOnStart = true;
    [SerializeField] private string completionTitle = "Level Complete";
    [SerializeField] private string completedNote = "Nice work. Your flight log now includes the completion flag and delivery totals.";
    [SerializeField] private string inProgressStatus = "In progress";
    [SerializeField] private string completedStatus = "Completed";
    [SerializeField] private float popupDuration = 0.35f;
    [SerializeField] private float popupStartScale = 0.85f;
    [SerializeField] private float popupEndScale = 1f;

    [Header("Reward Formula")]
    [SerializeField] private int moneyPerPresent = 100;
    [SerializeField] private int moneyCompletionBonus = 150;
    [SerializeField] private int moneyFastClearBonusMax = 100;
    [SerializeField] private float moneyParTimeSeconds = 180f;

    [SerializeField] private int christmasSpiritPerPresent = 40;
    [SerializeField] private int christmasSpiritCompletionBonus = 100;
    [SerializeField] private int christmasSpiritFastClearBonusMax = 75;
    [SerializeField] private float christmasSpiritParTimeSeconds = 180f;

    private float levelStartTime;
    private Coroutine popupRoutine;

    private void Start()
    {
        if (deliveryScoreManager == null)
        {
            deliveryScoreManager = FindFirstObjectByType<DeliveryScoreManager>();
        }

        if (level1Manager == null)
        {
            level1Manager = FindFirstObjectByType<Level1Manager>();
        }

        if (panel == null)
        {
            panel = gameObject;
        }

        if (panel != null && panelCanvasGroup == null)
        {
            panelCanvasGroup = panel.GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
            {
                panelCanvasGroup = panel.AddComponent<CanvasGroup>();
            }
        }

        levelStartTime = Time.time;

        if (hideOnStart)
        {
            SetVisible(false);
        }

        if (deliveryScoreManager == null)
        {
            Debug.LogWarning("LevelCompletionStatsUI could not find DeliveryScoreManager.");
            return;
        }

        deliveryScoreManager.LevelCompleted += HandleLevelCompleted;

        if (level1Manager != null)
        {
            level1Manager.LevelStarted += HandleLevelStarted;
        }

        if (IsAlreadyComplete())
        {
            ShowCompletionStats();
        }
    }

    private void OnDestroy()
    {
        if (deliveryScoreManager != null)
        {
            deliveryScoreManager.LevelCompleted -= HandleLevelCompleted;
        }

        if (level1Manager != null)
        {
            level1Manager.LevelStarted -= HandleLevelStarted;
        }
    }

    private void HandleLevelStarted()
    {
        RefreshFields();
    }

    private void HandleLevelCompleted()
    {
        ShowCompletionStats();
    }

    public void ShowCompletionStats()
    {
        if (deliveryScoreManager == null)
        {
            return;
        }

        RefreshFields();
        SetTitle();
        SetStatus();
        SetCountsAndRewards();
        SetNote();
        SetLegacySummary();
        PlayPopup();
        Debug.Log(BuildDebugSummary());
    }

    private void RefreshFields()
    {
        if (deliveryScoreManager == null)
        {
            return;
        }

        int completedTargets = deliveryScoreManager.CompletedTargets;
        int totalTargets = deliveryScoreManager.TotalTargets;
        int remainingTargets = deliveryScoreManager.RemainingTargets;
        float elapsedSeconds = GetLevelElapsedSeconds();
        int money = CalculateMoney(completedTargets, elapsedSeconds);
        int christmasSpirit = CalculateChristmasSpirit(completedTargets, elapsedSeconds);
        int totalScore = deliveryScoreManager.score;

        if (titleText != null)
        {
            titleText.text = completionTitle;
        }

        if (totalScoreText != null)
        {
            totalScoreText.text = totalScore.ToString();
        }

        if (timeUsedText != null)
        {
            timeUsedText.text = FormatSeconds(elapsedSeconds);
        }

        if (moneyText != null)
        {
            moneyText.text = money.ToString();
        }

        if (christmasSpiritText != null)
        {
            christmasSpiritText.text = christmasSpirit.ToString();
        }

        if (presentDeliveredText != null)
        {
            presentDeliveredText.text = completedTargets.ToString();
        }

        if (presentRemainText != null)
        {
            presentRemainText.text = remainingTargets.ToString();
        }

        if (levelStatusText != null)
        {
            levelStatusText.text = HasLevelCompleted() ? completedStatus : inProgressStatus;
        }

        if (statsText != null)
        {
            statsText.text =
                $"Deliveries: {completedTargets}/{totalTargets}\n" +
                $"Remaining: {remainingTargets}\n" +
                $"Money: {money}\n" +
                $"Christmas Spirit: {christmasSpirit}\n" +
                $"Total Score: {totalScore}\n" +
                $"Time used: {FormatSeconds(elapsedSeconds)}";
        }

        if (noteText != null)
        {
            noteText.text = completedNote;
        }
    }

    private void SetTitle()
    {
        if (titleText != null)
        {
            titleText.text = completionTitle;
        }
    }

    private void SetStatus()
    {
        if (levelStatusText != null)
        {
            levelStatusText.text = completedStatus;
        }
    }

    private void SetCountsAndRewards()
    {
        if (deliveryScoreManager == null)
        {
            return;
        }

        int completedTargets = deliveryScoreManager.CompletedTargets;
        int remainingTargets = deliveryScoreManager.RemainingTargets;
        float elapsedSeconds = GetLevelElapsedSeconds();
        int money = CalculateMoney(completedTargets, elapsedSeconds);
        int christmasSpirit = CalculateChristmasSpirit(completedTargets, elapsedSeconds);
        int totalScore = deliveryScoreManager.score;

        if (totalScoreText != null)
        {
            totalScoreText.text = totalScore.ToString();
        }

        if (timeUsedText != null)
        {
            timeUsedText.text = FormatSeconds(elapsedSeconds);
        }

        if (moneyText != null)
        {
            moneyText.text = money.ToString();
        }

        if (christmasSpiritText != null)
        {
            christmasSpiritText.text = christmasSpirit.ToString();
        }

        if (presentDeliveredText != null)
        {
            presentDeliveredText.text = completedTargets.ToString();
        }

        if (presentRemainText != null)
        {
            presentRemainText.text = remainingTargets.ToString();
        }
    }

    private void SetNote()
    {
        if (noteText != null)
        {
            noteText.text = completedNote;
        }
    }

    private void SetLegacySummary()
    {
        if (statsText == null || deliveryScoreManager == null)
        {
            return;
        }

        int completedTargets = deliveryScoreManager.CompletedTargets;
        int totalTargets = deliveryScoreManager.TotalTargets;
        int remainingTargets = deliveryScoreManager.RemainingTargets;
        float elapsedSeconds = GetLevelElapsedSeconds();
        int money = CalculateMoney(completedTargets, elapsedSeconds);
        int christmasSpirit = CalculateChristmasSpirit(completedTargets, elapsedSeconds);

        statsText.text =
            $"Deliveries: {completedTargets}/{totalTargets}\n" +
            $"Remaining: {remainingTargets}\n" +
            $"Money: {money}\n" +
            $"Christmas Spirit: {christmasSpirit}\n" +
            $"Time used: {FormatSeconds(elapsedSeconds)}";
    }

    public void Hide()
    {
        SetVisible(false);
    }

    private bool IsAlreadyComplete()
    {
        return deliveryScoreManager != null &&
               deliveryScoreManager.TotalTargets > 0 &&
               deliveryScoreManager.CompletedTargets >= deliveryScoreManager.TotalTargets;
    }

    private bool HasLevelCompleted()
    {
        return level1Manager != null
            ? level1Manager.HasLevelCompleted
            : IsAlreadyComplete();
    }

    private float GetLevelElapsedSeconds()
    {
        if (level1Manager != null && level1Manager.HasLevelStarted)
        {
            float endTime = level1Manager.HasLevelCompleted
                ? level1Manager.LevelCompletedAt
                : Time.time;

            return Mathf.Max(0f, endTime - level1Manager.LevelStartedAt);
        }

        return Mathf.Max(0f, Time.time - levelStartTime);
    }

    private int CalculateMoney(int completedTargets, float elapsedSeconds)
    {
        int baseMoney = completedTargets * moneyPerPresent;
        int completionBonus = HasLevelCompleted() ? moneyCompletionBonus : 0;
        int fastClearBonus = CalculateFastClearBonus(elapsedSeconds, moneyParTimeSeconds, moneyFastClearBonusMax);
        return baseMoney + completionBonus + fastClearBonus;
    }

    private int CalculateChristmasSpirit(int completedTargets, float elapsedSeconds)
    {
        int baseSpirit = completedTargets * christmasSpiritPerPresent;
        int completionBonus = HasLevelCompleted() ? christmasSpiritCompletionBonus : 0;
        int fastClearBonus = CalculateFastClearBonus(
            elapsedSeconds,
            christmasSpiritParTimeSeconds,
            christmasSpiritFastClearBonusMax);
        return baseSpirit + completionBonus + fastClearBonus;
    }

    private int CalculateFastClearBonus(float elapsedSeconds, float parTimeSeconds, int bonusMax)
    {
        if (!HasLevelCompleted() || parTimeSeconds <= 0f || bonusMax <= 0)
        {
            return 0;
        }

        float ratio = Mathf.Clamp01(1f - (elapsedSeconds / parTimeSeconds));
        return Mathf.RoundToInt(ratio * bonusMax);
    }

    private string FormatSeconds(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int minutes = totalSeconds / 60;
        int remainingSeconds = totalSeconds % 60;
        return $"{minutes:0}:{remainingSeconds:00}";
    }

    private string BuildDebugSummary()
    {
        if (deliveryScoreManager == null)
        {
            return "LevelCompletionStatsUI: no delivery manager";
        }

        int completedTargets = deliveryScoreManager.CompletedTargets;
        int totalTargets = deliveryScoreManager.TotalTargets;
        int remainingTargets = deliveryScoreManager.RemainingTargets;
        float elapsedSeconds = GetLevelElapsedSeconds();
        int money = CalculateMoney(completedTargets, elapsedSeconds);
        int christmasSpirit = CalculateChristmasSpirit(completedTargets, elapsedSeconds);

        return
            $"LevelCompletionStatsUI: completed {completedTargets}/{totalTargets}, " +
            $"remaining {remainingTargets}, money {money}, spirit {christmasSpirit}, time {FormatSeconds(elapsedSeconds)}";
    }

    private void SetVisible(bool visible)
    {
        GameObject targetPanel = panel != null ? panel : gameObject;

        if (targetPanel != null)
        {
            if (!visible && targetPanel == gameObject)
            {
                if (panelCanvasGroup != null)
                {
                    panelCanvasGroup.alpha = 0f;
                    panelCanvasGroup.interactable = false;
                    panelCanvasGroup.blocksRaycasts = false;
                }

                return;
            }

            targetPanel.SetActive(visible);
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = visible ? 1f : 0f;
                panelCanvasGroup.interactable = visible;
                panelCanvasGroup.blocksRaycasts = visible;
            }
        }
        else if (!visible)
        {
            Debug.LogWarning(
                "LevelCompletionStatsUI has no panel assigned, so it cannot hide/show anything."
            );
        }
    }

    private void PlayPopup()
    {
        if (panel == null)
        {
            return;
        }

        if (popupRoutine != null)
        {
            StopCoroutine(popupRoutine);
        }

        popupRoutine = StartCoroutine(PopupRoutine());
    }

    private IEnumerator PopupRoutine()
    {
        panel.SetActive(true);

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        if (panelRect != null)
        {
            panelRect.localScale = Vector3.one * popupStartScale;
        }

        float elapsed = 0f;
        while (elapsed < popupDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = popupDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / popupDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = eased;
            }

            if (panelRect != null)
            {
                float scale = Mathf.Lerp(popupStartScale, popupEndScale, eased);
                panelRect.localScale = Vector3.one * scale;
            }

            yield return null;
        }

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 1f;
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
        }

        if (panelRect != null)
        {
            panelRect.localScale = Vector3.one * popupEndScale;
        }

        popupRoutine = null;
    }
}
