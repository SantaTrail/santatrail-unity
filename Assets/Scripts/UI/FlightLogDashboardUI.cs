using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FlightLogDashboardUI : MonoBehaviour
{
    private const int DeliveriesPerLevel = 5;
    private const int InitialSpiritLevelCapacity = 5;
    private const float GeneratedRowWidth = 1500f;
    private const float GeneratedRowHeight = 88f;

    private enum FilterMode
    {
        All,
        Passed,
        InProgress
    }

    private sealed class DashboardFlightRecord
    {
        public ProgressSaveSystem.FlightHistoryRecord record;
    }

    private sealed class FlightLogRowWidgets
    {
        public readonly GameObject root;
        public readonly TMP_Text dateText;
        public readonly TMP_Text missionNameText;
        public TMP_Text missionDetailText;
        public readonly TMP_Text scoreText;
        public readonly TMP_Text flightTimeText;
        public readonly TMP_Text outcomeText;

        public FlightLogRowWidgets(Transform row)
        {
            root = row.gameObject;
            dateText = FindText(row, "DateText");
            missionNameText = FindText(row, "MissionNameText");
            missionDetailText = FindText(row, "MissionDetailText");
            scoreText = FindText(row, "ScoreText");
            flightTimeText = FindText(row, "FlightTimeText");
            outcomeText = FindText(row, "OutcomeText");
        }

        public void EnsureMissionDetailText(TMP_Text template)
        {
            if (missionDetailText != null || template == null || missionNameText == null)
            {
                return;
            }

            Transform missionColumn = missionNameText.transform.parent;
            if (missionColumn == null)
            {
                return;
            }

            GameObject clone = Instantiate(template.gameObject, missionColumn, false);
            clone.name = "MissionDetailText";
            missionDetailText = clone.GetComponent<TMP_Text>();
        }

        public void Show(DashboardFlightRecord displayRecord)
        {
            ProgressSaveSystem.FlightHistoryRecord record = displayRecord.record;

            root.SetActive(true);

            SetText(dateText, FormatDate(record.savedAtUtc));
            SetText(missionNameText, GetLevelLabel(record));
            SetText(missionDetailText, GetMissionDetail(record));
            SetText(scoreText, record.score.ToString("N0", CultureInfo.InvariantCulture));
            SetText(
                flightTimeText,
                record.elapsedSeconds >= 0f
                    ? FormatSeconds(record.elapsedSeconds)
                    : "--:--"
            );
            SetText(outcomeText, record.levelCompleted ? "Passed" : "In progress");

            if (outcomeText != null)
            {
                outcomeText.color = record.levelCompleted
                    ? new Color(0.32f, 0.95f, 0.45f, 1f)
                    : new Color(1f, 0.78f, 0.25f, 1f);
            }
        }

        public void ShowEmpty()
        {
            root.SetActive(true);
            SetText(dateText, "--");
            SetText(missionNameText, "No saved flights yet");
            SetText(missionDetailText, "--");
            SetText(scoreText, "--");
            SetText(flightTimeText, "--:--");
            SetText(outcomeText, "--");
        }

        public void Hide()
        {
            root.SetActive(false);
        }

        private static TMP_Text FindText(Transform row, string textName)
        {
            foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (string.Equals(text.name, textName, StringComparison.Ordinal))
                {
                    return text;
                }
            }

            return null;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value;
            }
        }
    }

    private readonly List<FlightLogRowWidgets> rows = new List<FlightLogRowWidgets>();
    private FilterMode filterMode = FilterMode.All;
    private Transform content;
    private Transform rowTemplate;
    private float rowSpacing = 88f;
    private Button allButton;
    private Button passedButton;
    private Button failedButton;
    private TMP_Text magicAmountText;
    private TMP_Text deliveredSummaryText;
    private Slider spiritSlider;
    private TMP_Text spiritPercentageText;
    private TMP_Text altitudeDisciplineText;
    private TMP_Text minimumAltitudeText;
    private TMP_Text maximumAltitudeText;
    private Image altitudeDisciplineMeter;
    private bool configured;
    private bool generatedRowTemplate;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneLoadHandler()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, "MainPageScene", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AddToMainPage();
    }

    private static void AddToMainPage()
    {
        if (!string.Equals(
                SceneManager.GetActiveScene().name,
                "MainPageScene",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return;
        }

        Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            return;
        }

        Transform flightLog = FindChildByName(canvas.transform, "FlightLog");
        if (flightLog == null)
        {
            return;
        }

        FlightLogDashboardUI dashboard =
            flightLog.GetComponent<FlightLogDashboardUI>();
        if (dashboard == null)
        {
            flightLog.gameObject.AddComponent<FlightLogDashboardUI>();
        }
        else
        {
            dashboard.Refresh();
        }
    }

    private void Start()
    {
        Configure();
        Refresh();
    }

    private void OnEnable()
    {
        if (configured)
        {
            Refresh();
        }
    }

    private void OnDestroy()
    {
        if (allButton != null)
        {
            allButton.onClick.RemoveListener(ShowAll);
        }

        if (passedButton != null)
        {
            passedButton.onClick.RemoveListener(ShowPassed);
        }

        if (failedButton != null)
        {
            failedButton.onClick.RemoveListener(ShowInProgress);
        }
    }

    private void Configure()
    {
        content = transform.Find("FlightLogScrollView/Viewport/Content");
        if (content == null)
        {
            Debug.LogWarning(
                "FlightLogDashboardUI could not find FlightLogScrollView/Viewport/Content."
            );
            configured = true;
            return;
        }

        rows.Clear();
        foreach (Transform child in content)
        {
            if (child.name.StartsWith("FlightLogRow", StringComparison.Ordinal))
            {
                rows.Add(new FlightLogRowWidgets(child));
            }
        }

        if (rows.Count == 0)
        {
            CreateGeneratedRowTemplate();
        }

        if (rows.Count > 0)
        {
            rowTemplate = rows[0].root.transform;

            TMP_Text detailTemplate = rows[0].missionDetailText;
            if (detailTemplate != null)
            {
                for (int index = 1; index < rows.Count; index++)
                {
                    rows[index].EnsureMissionDetailText(detailTemplate);
                }
            }
        }

        if (rows.Count >= 2)
        {
            RectTransform firstRow = rows[0].root.GetComponent<RectTransform>();
            RectTransform secondRow = rows[1].root.GetComponent<RectTransform>();
            float measuredSpacing = Mathf.Abs(
                firstRow.anchoredPosition.y - secondRow.anchoredPosition.y
            );
            if (measuredSpacing > 1f)
            {
                rowSpacing = measuredSpacing;
            }
        }

        allButton = FindButton("AllButton");
        passedButton = FindButton("PassedButton");
        failedButton = FindButton("FailedButton");

        if (allButton != null)
        {
            allButton.onClick.AddListener(ShowAll);
        }

        if (passedButton != null)
        {
            passedButton.onClick.AddListener(ShowPassed);
        }

        if (failedButton != null)
        {
            failedButton.onClick.AddListener(ShowInProgress);
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            magicAmountText = FindTextInHierarchy(canvas.transform, "MagicAmount");
            deliveredSummaryText = FindTextInHierarchy(canvas.transform, "PresentDeliverValue");
            altitudeDisciplineText = FindTextInHierarchy(canvas.transform, "Altitude");
            minimumAltitudeText = FindTextInHierarchy(canvas.transform, "MinAltValue");
            maximumAltitudeText = FindTextInHierarchy(canvas.transform, "MaxAltValue");
            altitudeDisciplineMeter = FindImageInHierarchy(canvas.transform, "Meter");

            Transform spirit = FindChildByName(canvas.transform, "ChristmasSpirit");
            if (spirit != null)
            {
                foreach (Slider slider in spirit.GetComponentsInChildren<Slider>(true))
                {
                    if (string.Equals(slider.name, "Slider", StringComparison.Ordinal))
                    {
                        spiritSlider = slider;
                        break;
                    }
                }

                if (spiritSlider == null)
                {
                    spiritSlider = spirit.GetComponentInChildren<Slider>(true);
                }

                spiritPercentageText = FindTextInHierarchy(spirit, "Percentage");
            }
        }

        configured = true;
    }

    private void Refresh()
    {
        if (!configured)
        {
            Configure();
        }

        List<DashboardFlightRecord> records = BuildDisplayRecords();
        records.RemoveAll(displayRecord => !MatchesFilter(displayRecord.record));

        records.Sort((left, right) =>
            string.Compare(
                right.record.savedAtUtc,
                left.record.savedAtUtc,
                StringComparison.Ordinal
            ));

        EnsureRows(Mathf.Max(records.Count, InitialSpiritLevelCapacity));
        UpdateChristmasSpiritProgress();

        if (deliveredSummaryText != null)
        {
            deliveredSummaryText.text = ProgressSaveSystem.TotalDeliveredTargets.ToString(
                CultureInfo.InvariantCulture
            );
        }

        if (magicAmountText != null)
        {
            magicAmountText.text = ProgressSaveSystem.BestScoreTotal.ToString(
                "N0",
                CultureInfo.InvariantCulture
            );
        }

        int altitudeDiscipline = ProgressSaveSystem.BestAltitudeDiscipline;
        if (altitudeDisciplineText != null)
        {
            altitudeDisciplineText.text = ProgressSaveSystem.AverageAltitude.ToString(
                "0.0",
                CultureInfo.InvariantCulture
            );
        }

        if (minimumAltitudeText != null)
        {
            minimumAltitudeText.text = FormatAltitude(ProgressSaveSystem.MinimumAltitude);
        }

        if (maximumAltitudeText != null)
        {
            maximumAltitudeText.text = FormatAltitude(ProgressSaveSystem.MaximumAltitude);
        }

        if (altitudeDisciplineMeter != null)
        {
            altitudeDisciplineMeter.fillAmount = altitudeDiscipline / 100f;
        }

        for (int index = 0; index < rows.Count; index++)
        {
            if (index < records.Count)
            {
                rows[index].Show(records[index]);
            }
            else if (records.Count == 0 && index == 0)
            {
                rows[index].ShowEmpty();
            }
            else
            {
                rows[index].Hide();
            }
        }
    }

    private static List<DashboardFlightRecord> BuildDisplayRecords()
    {
        List<DashboardFlightRecord> displayRecords =
            new List<DashboardFlightRecord>();
        Dictionary<string, List<ProgressSaveSystem.FlightHistoryRecord>> recordsByLevel =
            new Dictionary<string, List<ProgressSaveSystem.FlightHistoryRecord>>(
                StringComparer.OrdinalIgnoreCase
            );

        List<ProgressSaveSystem.FlightHistoryRecord> history =
            ProgressSaveSystem.Current.flightHistory;
        if (history != null)
        {
            foreach (ProgressSaveSystem.FlightHistoryRecord record in history)
            {
                if (record == null)
                {
                    continue;
                }

                string key = GetRecordKey(record.sceneName, record.levelLabel);
                if (!recordsByLevel.TryGetValue(key, out List<ProgressSaveSystem.FlightHistoryRecord> levelHistory))
                {
                    levelHistory = new List<ProgressSaveSystem.FlightHistoryRecord>();
                    recordsByLevel.Add(key, levelHistory);
                }

                levelHistory.Add(record);
            }
        }

        HashSet<string> representedLevels =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, List<ProgressSaveSystem.FlightHistoryRecord>> entry in
                 recordsByLevel)
        {
            ProgressSaveSystem.FlightHistoryRecord latest = entry.Value[0];
            for (int index = 1; index < entry.Value.Count; index++)
            {
                ProgressSaveSystem.FlightHistoryRecord candidate = entry.Value[index];
                if (string.Compare(
                        candidate.savedAtUtc,
                        latest.savedAtUtc,
                        StringComparison.Ordinal
                    ) > 0)
                {
                    latest = candidate;
                }
            }

            displayRecords.Add(new DashboardFlightRecord
            {
                record = latest
            });
            representedLevels.Add(entry.Key);
        }

        // Keep older saves visible when they predate flightHistory.
        if (ProgressSaveSystem.Current.levelRecords != null)
        {
            foreach (ProgressSaveSystem.LevelProgressRecord record in
                     ProgressSaveSystem.Current.levelRecords)
            {
                if (record == null)
                {
                    continue;
                }

                string key = GetRecordKey(record.sceneName, record.levelLabel);
                if (!representedLevels.Add(key))
                {
                    continue;
                }

                displayRecords.Add(new DashboardFlightRecord
                {
                    record = new ProgressSaveSystem.FlightHistoryRecord
                    {
                        sceneName = record.sceneName,
                        levelLabel = record.levelLabel,
                        completedTargets = record.completedTargets > 0
                            ? record.completedTargets
                            : record.bestCompletedTargets,
                        totalTargets = record.totalTargets,
                        score = record.score > 0 ? record.score : record.bestScore,
                        levelCompleted = record.levelCompleted,
                        elapsedSeconds = record.bestTimeSeconds,
                        savedAtUtc = record.savedAtUtc
                    }
                });
            }
        }

        return displayRecords;
    }

    private static string GetRecordKey(string sceneName, string levelLabel)
    {
        if (!string.IsNullOrWhiteSpace(sceneName))
        {
            return sceneName;
        }

        if (!string.IsNullOrWhiteSpace(levelLabel))
        {
            return levelLabel;
        }

        return "UnknownLevel";
    }

    private bool MatchesFilter(ProgressSaveSystem.FlightHistoryRecord record)
    {
        switch (filterMode)
        {
            case FilterMode.Passed:
                return record.levelCompleted;
            case FilterMode.InProgress:
                return !record.levelCompleted;
            default:
                return true;
        }
    }

    private void ShowAll()
    {
        filterMode = FilterMode.All;
        Refresh();
    }

    private void CreateGeneratedRowTemplate()
    {
        if (content == null)
        {
            return;
        }

        RectTransform contentRect = content.GetComponent<RectTransform>();
        if (contentRect != null)
        {
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, GeneratedRowHeight);
        }

        GameObject rowObject = new GameObject(
            "FlightLogRow (Generated)",
            typeof(RectTransform)
        );
        rowObject.layer = content.gameObject.layer;
        rowObject.transform.SetParent(content, false);

        RectTransform rowRect = rowObject.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 1f);
        rowRect.anchorMax = new Vector2(0.5f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = Vector2.zero;
        rowRect.sizeDelta = new Vector2(GeneratedRowWidth, GeneratedRowHeight);

        CreateGeneratedText(
            rowObject.transform,
            "DateText",
            new Vector2(-615f, 0f),
            new Vector2(220f, 60f),
            TextAlignmentOptions.MidlineLeft,
            20f
        );
        CreateGeneratedText(
            rowObject.transform,
            "MissionNameText",
            new Vector2(-315f, 16f),
            new Vector2(520f, 34f),
            TextAlignmentOptions.BottomLeft,
            20f
        );
        CreateGeneratedText(
            rowObject.transform,
            "MissionDetailText",
            new Vector2(-315f, -18f),
            new Vector2(520f, 28f),
            TextAlignmentOptions.TopLeft,
            16f
        );
        CreateGeneratedText(
            rowObject.transform,
            "ScoreText",
            new Vector2(175f, 0f),
            new Vector2(150f, 60f),
            TextAlignmentOptions.MidlineLeft,
            20f
        );
        CreateGeneratedText(
            rowObject.transform,
            "FlightTimeText",
            new Vector2(370f, 0f),
            new Vector2(200f, 60f),
            TextAlignmentOptions.MidlineLeft,
            20f
        );
        CreateGeneratedText(
            rowObject.transform,
            "OutcomeText",
            new Vector2(625f, 0f),
            new Vector2(300f, 60f),
            TextAlignmentOptions.MidlineLeft,
            20f
        );

        AlignGeneratedTextToHeader(rowObject.transform, "DateText", "DateHeader");
        AlignGeneratedTextToHeader(rowObject.transform, "MissionNameText", "MissionHeader");
        AlignGeneratedTextToHeader(rowObject.transform, "ScoreText", "ScoreHeader");
        AlignGeneratedTextToHeader(rowObject.transform, "FlightTimeText", "FlightTimeHeader");
        AlignGeneratedTextToHeader(rowObject.transform, "OutcomeText", "OutcomeHeader");

        rows.Add(new FlightLogRowWidgets(rowObject.transform));
        rowTemplate = rowObject.transform;
        generatedRowTemplate = true;
        Debug.Log("FlightLogDashboardUI: no row template found; created a runtime flight-log row.");
    }

    private static TMP_Text CreateGeneratedText(
        Transform parent,
        string objectName,
        Vector2 position,
        Vector2 size,
        TextAlignmentOptions alignment,
        float fontSize
    )
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform));
        textObject.layer = parent.gameObject.layer;
        textObject.transform.SetParent(parent, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = position;
        textRect.sizeDelta = size;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = new Color(0.93f, 0.78f, 0.36f, 1f);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private void AlignGeneratedTextToHeader(
        Transform row,
        string generatedTextName,
        string headerName
    )
    {
        Transform header = FindChildByName(transform, headerName);
        if (header == null)
        {
            return;
        }

        TMP_Text headerText = header.GetComponentInChildren<TMP_Text>(true);
        TMP_Text generatedText = FindText(row, generatedTextName);
        if (headerText == null || generatedText == null)
        {
            return;
        }

        RectTransform headerRect = headerText.rectTransform;
        RectTransform generatedRect = generatedText.rectTransform;
        Vector3[] headerCorners = new Vector3[4];
        Vector3[] generatedCorners = new Vector3[4];
        headerRect.GetWorldCorners(headerCorners);
        generatedRect.GetWorldCorners(generatedCorners);

        Vector3 horizontalOffset = headerCorners[0] - generatedCorners[0];
        horizontalOffset.y = 0f;
        generatedRect.position += horizontalOffset;
    }

    private static TMP_Text FindText(Transform root, string textName)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (string.Equals(text.name, textName, StringComparison.Ordinal))
            {
                return text;
            }
        }

        return null;
    }

    private void ShowPassed()
    {
        filterMode = FilterMode.Passed;
        Refresh();
    }

    private void ShowInProgress()
    {
        filterMode = FilterMode.InProgress;
        Refresh();
    }

    private Button FindButton(string buttonName)
    {
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            if (string.Equals(button.name, buttonName, StringComparison.Ordinal))
            {
                return button;
            }
        }

        return null;
    }

    private void EnsureRows(int requiredCount)
    {
        if (rowTemplate == null || content == null)
        {
            return;
        }

        while (rows.Count < requiredCount)
        {
            GameObject clone = Instantiate(rowTemplate.gameObject, content, false);
            clone.name = $"FlightLogRow (Saved {rows.Count})";

            RectTransform cloneRect = clone.GetComponent<RectTransform>();
            RectTransform previousRect = rows[rows.Count - 1].root.GetComponent<RectTransform>();
            cloneRect.anchoredPosition = previousRect.anchoredPosition +
                new Vector2(0f, -rowSpacing);
            rows.Add(new FlightLogRowWidgets(clone.transform));
        }

        if (generatedRowTemplate)
        {
            RectTransform contentRect = content.GetComponent<RectTransform>();
            if (contentRect != null)
            {
                contentRect.sizeDelta = new Vector2(
                    contentRect.sizeDelta.x,
                    Mathf.Max(GeneratedRowHeight, rows.Count * rowSpacing)
                );
            }
        }
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, childName, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    private static TMP_Text FindTextInHierarchy(Transform root, string textName)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (string.Equals(text.name, textName, StringComparison.Ordinal))
            {
                return text;
            }
        }

        return null;
    }

    private static Image FindImageInHierarchy(Transform root, string objectName)
    {
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (string.Equals(image.name, objectName, StringComparison.Ordinal))
            {
                return image;
            }
        }

        return null;
    }

    private static string FormatAltitude(float altitude)
    {
        return $"{altitude.ToString("0.0", CultureInfo.InvariantCulture)} M";
    }

    private static string GetLevelLabel(ProgressSaveSystem.FlightHistoryRecord record)
    {
        if (LevelConfigLoader.TryLoadLevels(out List<LevelConfig> levels))
        {
            LevelConfig match = levels.Find(level =>
                level != null &&
                string.Equals(level.sceneName, record.sceneName, StringComparison.OrdinalIgnoreCase));
            if (match != null && !string.IsNullOrWhiteSpace(match.label))
            {
                return FormatLevelLabel(match.label);
            }
        }

        if (!string.IsNullOrWhiteSpace(record.levelLabel))
        {
            return FormatLevelLabel(record.levelLabel);
        }

        return FormatLevelLabel(string.IsNullOrWhiteSpace(record.sceneName)
            ? "Unknown level"
            : record.sceneName);
    }

    private static string FormatLevelLabel(string label)
    {
        const string levelOnePrefix = "Level 1";
        if (label.StartsWith(levelOnePrefix, StringComparison.OrdinalIgnoreCase) &&
            (label.Length == levelOnePrefix.Length ||
             char.IsWhiteSpace(label[levelOnePrefix.Length])))
        {
            return "1st Delivery" + label.Substring(levelOnePrefix.Length);
        }

        return label;
    }

    private static string GetMissionDetail(ProgressSaveSystem.FlightHistoryRecord record)
    {
        int deliveryGoal = DeliveriesPerLevel;
        if (LevelConfigLoader.TryLoadLevels(out List<LevelConfig> levels))
        {
            LevelConfig match = levels.Find(level =>
                level != null &&
                string.Equals(level.sceneName, record.sceneName, StringComparison.OrdinalIgnoreCase));
            if (match != null && match.deliveryCount > 0)
            {
                deliveryGoal = match.deliveryCount;
            }
        }

        return $"Deliver {deliveryGoal} presents";
    }

    private void UpdateChristmasSpiritProgress()
    {
        int delivered = 0;
        if (ProgressSaveSystem.Current.levelRecords != null)
        {
            foreach (ProgressSaveSystem.LevelProgressRecord record in
                     ProgressSaveSystem.Current.levelRecords)
            {
                if (record != null)
                {
                    delivered += Mathf.Clamp(
                        record.bestCompletedTargets,
                        0,
                        DeliveriesPerLevel
                    );
                }
            }
        }

        int configuredLevelCount = 0;
        if (LevelConfigLoader.TryLoadLevels(out List<LevelConfig> levels))
        {
            foreach (LevelConfig level in levels)
            {
                if (level != null && !string.IsNullOrWhiteSpace(level.sceneName))
                {
                    configuredLevelCount++;
                }
            }
        }

        int levelCapacity = Mathf.Max(InitialSpiritLevelCapacity, configuredLevelCount);
        int totalDeliveries = Mathf.Max(1, levelCapacity * DeliveriesPerLevel);
        float progress = Mathf.Clamp01(delivered / (float)totalDeliveries);

        if (spiritSlider != null)
        {
            spiritSlider.minValue = 0f;
            spiritSlider.maxValue = 1f;
            spiritSlider.SetValueWithoutNotify(progress);
        }

        if (spiritPercentageText != null)
        {
            spiritPercentageText.text = $"{Mathf.RoundToInt(progress * 100f)} %";
        }
    }

    private static string FormatDate(string savedAtUtc)
    {
        if (!DateTime.TryParse(
                savedAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed
            ))
        {
            return "--";
        }

        DateTime local = parsed.ToLocalTime();
        DateTime today = DateTime.Now.Date;
        if (local.Date == today)
        {
            return $"Today {local:HH:mm}";
        }

        if (local.Date == today.AddDays(-1))
        {
            return $"Yesterday {local:HH:mm}";
        }

        return local.ToString("MMM d HH:mm", CultureInfo.InvariantCulture);
    }

    private static string FormatSeconds(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return $"{totalSeconds / 60:0}:{totalSeconds % 60:00}";
    }
}
