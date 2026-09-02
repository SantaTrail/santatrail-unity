using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// Fills the PreviewLV screen from the selected level database entry.
/// The layout is authored once; level-specific copy stays in SantaTrailLevels.json.
/// </summary>
public sealed class PreviewLevelController : MonoBehaviour
{
    [Header("Level Selection")]
    [SerializeField] private string defaultLevelSceneName = "LV1";
    [SerializeField] private bool preferActiveLevel = true;

    [Header("Preview Text")]
    [SerializeField] private TMP_Text missionTypeText;
    [SerializeField] private TMP_Text missionTitleText;
    [SerializeField] private TMP_Text locationText;
    [SerializeField] private TMP_Text difficultyText;
    [SerializeField] private TMP_Text rewardText;
    [SerializeField] private TMP_Text timeWindowText;
    [SerializeField] private TMP_Text checklistItem1Text;
    [SerializeField] private TMP_Text checklistItem2Text;
    [SerializeField] private TMP_Text checklistItem3Text;

    public LevelConfig DisplayedLevel { get; private set; }

    private void Awake()
    {
        RefreshPreview();
    }

    public void RefreshPreview()
    {
        if (!TryResolveLevel(out LevelConfig level))
        {
            Debug.LogWarning(
                $"{nameof(PreviewLevelController)}: no level was found for " +
                $"'{defaultLevelSceneName}'."
            );
            return;
        }

        DisplayedLevel = level;
        GameManager.ApplyLevel(level);

        SetText(missionTypeText, FirstNonEmpty(
            level.previewMissionType,
            level.showTutorial ? "TUTORIAL MISSION" : "DELIVERY MISSION"
        ));
        SetText(missionTitleText, FirstNonEmpty(level.previewMissionTitle, level.label));
        SetText(locationText, FirstNonEmpty(level.previewLocation, FormatLocation(level)));
        SetText(
            difficultyText,
            FirstNonEmpty(level.previewDifficulty, level.missionDifficulty.ToString())
        );
        SetText(rewardText, FirstNonEmpty(level.previewReward, $"{GetDeliveryCount(level) * 100} XP"));
        SetText(timeWindowText, FirstNonEmpty(level.previewTimeWindow, "Open skies"));

        SetText(checklistItem1Text, GetChecklistItem(level, 0));
        SetText(checklistItem2Text, GetChecklistItem(level, 1));
        SetText(checklistItem3Text, GetChecklistItem(level, 2));

        Debug.Log(
            $"{nameof(PreviewLevelController)}: showing '{level.label}' " +
            $"({level.sceneName}) with {GetDeliveryCount(level)} deliveries."
        );
    }

    private bool TryResolveLevel(out LevelConfig level)
    {
        level = null;

        if (preferActiveLevel &&
            GameManager.hasActiveLevelSettings &&
            !string.IsNullOrWhiteSpace(GameManager.activeSceneName) &&
            !string.Equals(GameManager.activeSceneName, "PreviewLV", StringComparison.OrdinalIgnoreCase) &&
            LevelConfigLoader.TryFindLevel(GameManager.activeSceneName, out level))
        {
            return true;
        }

        if (LevelConfigLoader.TryFindLevel(defaultLevelSceneName, out level))
        {
            return true;
        }

        if (LevelConfigLoader.TryLoadLevels(out List<LevelConfig> levels))
        {
            level = levels.Find(candidate => candidate != null);
        }

        return level != null;
    }

    private static string GetChecklistItem(LevelConfig level, int index)
    {
        string configuredGoal = level.mainGoal;
        if (string.IsNullOrWhiteSpace(configuredGoal) &&
            level.previewChecklist != null &&
            level.previewChecklist.Count > 0)
        {
            configuredGoal = level.previewChecklist[0];
        }

        string goal = FirstNonEmpty(configuredGoal, $"Deliver {GetDeliveryCount(level)} presents");

        if (index == 0)
        {
            return goal;
        }

        if (level.previewChecklist != null && index < level.previewChecklist.Count)
        {
            string configuredItem = level.previewChecklist[index];
            if (!string.IsNullOrWhiteSpace(configuredItem))
            {
                return configuredItem;
            }
        }

        return index == 1
            ? "Land the drone safely"
            : level.showTutorial
                ? "Complete the guided first delivery"
                : "Complete the delivery route";
    }

    private static int GetDeliveryCount(LevelConfig level)
    {
        return Mathf.Max(1, level != null ? level.deliveryCount : 1);
    }

    private static string FormatLocation(LevelConfig level)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:F6}, {1:F6}",
            level.homeLatitude,
            level.homeLongitude
        );
    }

    private static string FirstNonEmpty(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
        {
            target.text = value ?? string.Empty;
        }
    }
}
