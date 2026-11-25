using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Scene-owned Santa's Mail inbox. It keeps every unlocked level letter visible,
/// marks unopened letters as new, and opens the selected level's packing flow.
/// </summary>
public sealed class Level2LetterNotification : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const string LoadingSceneName = "LoadingScene";
    private const string ReadKeyPrefix = "SantaTrail.LetterInbox.Read.";

    [Header("Scene Objects")]
    [SerializeField] private GameObject badgeRoot;
    [SerializeField] private Button badgeButton;
    [SerializeField] private TMP_Text badgeLabelText;
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private Button popupButton;
    [SerializeField] private TMP_Text headingText;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;

    [Header("Inbox Layout")]
    [SerializeField] private Vector2 listOffset = new Vector2(16f, -120f);
    [SerializeField] private Vector2 listSize = new Vector2(404f, 160f);
    [SerializeField] private float rowHeight = 52f;
    [SerializeField] private float rowSpacing = 6f;
    [SerializeField, Min(1)] private int maxVisibleRows = 5;

    [Header("Inbox Copy")]
    [SerializeField] private string inboxHeading = "SANTA'S MAIL";
    [SerializeField] private string inboxTitle = "Letters";
    [SerializeField] private string emptyTitle = "No letters yet";
    [SerializeField] private string emptyBody = "New letters will arrive as you unlock levels.";
    [SerializeField] private string unreadStatus = "NEW";
    [SerializeField] private string readStatus = "READ";
    [SerializeField] private string completedStatus = "DELIVERED";
    [SerializeField] private string unavailableTitle = "Mail unavailable";
    [SerializeField] private Color unreadColor = new Color(0.48f, 0.22f, 0.12f, 1f);
    [SerializeField] private Color readColor = new Color(0.35f, 0.29f, 0.22f, 1f);
    [SerializeField] private Color deliveredColor = new Color(0.15f, 0.38f, 0.22f, 1f);

    private readonly List<GameObject> generatedRows = new List<GameObject>();
    private Coroutine hideCoroutine;
    private bool pointerIsInside;
    private bool previewPinned;
    private RectTransform listContent;
    private ScrollRect scrollRect;
    private List<LevelConfig> visibleLevels = new List<LevelConfig>();
    private bool levelListLoaded;

    private void Awake()
    {
        if (popupRoot == null)
        {
            // Ignore an unconfigured duplicate if the scene already has its
            // controller assigned to the authored popup.
            Level2LetterNotification[] controllers = FindObjectsByType<Level2LetterNotification>(FindObjectsSortMode.None);
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i] != this && controllers[i].popupRoot != null)
                {
                    enabled = false;
                    return;
                }
            }

            // Support adding this component directly to the popup in the scene.
            popupRoot = gameObject;
        }

        EnsureInboxList();
        RefreshInbox();
        popupRoot.SetActive(false);
    }

    public static void ShowForMainPage()
    {
        Level2LetterNotification inbox = FindInboxController();
        if (inbox != null)
        {
            inbox.TogglePinnedPreview();
        }
    }

    public static Level2LetterNotification FindInboxController()
    {
        Level2LetterNotification[] controllers = FindObjectsByType<Level2LetterNotification>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] != null && controllers[i].isActiveAndEnabled && controllers[i].popupRoot != null)
                return controllers[i];
        }
        return null;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerIsInside = true;
        ShowPreview();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerIsInside = false;
        ScheduleHide();
    }

    private void ShowPreview()
    {
        if (popupRoot == null) return;
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }

        RefreshInbox();
        popupRoot.SetActive(true);
        popupRoot.transform.SetAsLastSibling();
    }

    private void TogglePinnedPreview()
    {
        previewPinned = !previewPinned;
        if (previewPinned) ShowPreview();
        else if (popupRoot != null)
        {
            if (hideCoroutine != null)
            {
                StopCoroutine(hideCoroutine);
                hideCoroutine = null;
            }
            popupRoot.SetActive(false);
        }
    }

    private void ScheduleHide()
    {
        if (hideCoroutine != null) StopCoroutine(hideCoroutine);
        hideCoroutine = StartCoroutine(HidePreviewAfterPointerLeaves());
    }

    private IEnumerator HidePreviewAfterPointerLeaves()
    {
        yield return new WaitForSecondsRealtime(0.12f);
        if (!pointerIsInside && !previewPinned && popupRoot != null) popupRoot.SetActive(false);
        hideCoroutine = null;
    }

    private void RefreshInbox()
    {
        visibleLevels.Clear();
        levelListLoaded = LevelConfigLoader.TryLoadLevels(out List<LevelConfig> levels);
        if (levelListLoaded && levels != null)
        {
            int firstIncomplete = -1;
            for (int i = 0; i < levels.Count; i++)
            {
                LevelConfig level = levels[i];
                if (level == null || string.IsNullOrWhiteSpace(level.sceneName)) continue;

                bool completed = ProgressSaveSystem.TryGetLevelResult(
                    level.sceneName,
                    out ProgressSaveSystem.LevelProgressRecord result
                ) && result.levelCompleted;

                visibleLevels.Add(level);
                if (!completed && firstIncomplete < 0) firstIncomplete = visibleLevels.Count - 1;
            }

            // Expose the next playable level while keeping all previous letters in the inbox.
            int unlockedCount = firstIncomplete < 0 ? visibleLevels.Count : firstIncomplete + 1;
            if (unlockedCount < visibleLevels.Count)
                visibleLevels.RemoveRange(unlockedCount, visibleLevels.Count - unlockedCount);
        }

        int unreadCount = 0;
        for (int i = 0; i < visibleLevels.Count; i++)
            if (!WasRead(visibleLevels[i])) unreadCount++;

        if (badgeLabelText != null) badgeLabelText.text = unreadCount > 0 ? unreadCount.ToString() : "MAIL";
        if (badgeRoot != null) badgeRoot.SetActive(!levelListLoaded || visibleLevels.Count > 0);
        if (headingText != null)
        {
            headingText.gameObject.SetActive(true);
            headingText.text = inboxHeading;
        }

        if (titleText != null)
        {
            titleText.gameObject.SetActive(true);
            titleText.text = !levelListLoaded
                ? unavailableTitle
                : (visibleLevels.Count == 0 ? emptyTitle : $"{inboxTitle}  •  {visibleLevels.Count}");
        }

        if (bodyText != null)
        {
            bodyText.gameObject.SetActive(visibleLevels.Count == 0);
            bodyText.text = visibleLevels.Count == 0 ? emptyBody : "Select a letter to pack a present.";
        }

        RebuildRows();
        ResizePopupToContent();
    }

    private void EnsureInboxList()
    {
        if (listContent != null) return;

        GameObject viewportObject = new GameObject("LetterInboxViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObject.transform.SetParent(popupRoot.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        viewport.anchorMin = new Vector2(0f, 1f);
        viewport.anchorMax = new Vector2(0f, 1f);
        viewport.pivot = new Vector2(0f, 1f);
        viewport.anchoredPosition = listOffset;
        viewport.sizeDelta = listSize;
        Image viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.015f);
        viewportImage.raycastTarget = true;
        viewportObject.GetComponent<Mask>().showMaskGraphic = false;
        scrollRect = viewportObject.GetComponent<ScrollRect>();

        GameObject contentObject = new GameObject("LetterInboxContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewport, false);
        listContent = contentObject.GetComponent<RectTransform>();
        listContent.anchorMin = new Vector2(0f, 1f);
        listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.anchoredPosition = Vector2.zero;
        listContent.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = rowSpacing;
        layout.padding = new RectOffset(0, 4, 2, 4);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Keep the ScrollRect on the viewport so pointer drags on the masked list scroll it.
        scrollRect.viewport = viewport;
        scrollRect.content = listContent;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 28f;
    }

    private void RebuildRows()
    {
        EnsureInboxList();
        for (int i = 0; i < generatedRows.Count; i++)
            if (generatedRows[i] != null) Destroy(generatedRows[i]);
        generatedRows.Clear();

        // Inbox convention: the most recently unlocked level appears first.
        for (int i = visibleLevels.Count - 1; i >= 0; i--)
        {
            LevelConfig level = visibleLevels[i];
            bool completed = ProgressSaveSystem.TryGetLevelResult(
                level.sceneName,
                out ProgressSaveSystem.LevelProgressRecord result
            ) && result.levelCompleted;
            bool read = WasRead(level);
            CreateRow(level, completed, read);
        }

        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
    }

    private void ResizePopupToContent()
    {
        if (popupRoot == null) return;

        RectTransform popupRect = popupRoot.transform as RectTransform;
        if (popupRect == null) return;

        int visibleRowCount = Mathf.Min(visibleLevels.Count, Mathf.Max(1, maxVisibleRows));
        float listHeight = visibleLevels.Count == 0
            ? 0f
            : visibleRowCount * rowHeight +
              Mathf.Max(0, visibleRowCount - 1) * rowSpacing + 6f;

        RectTransform viewportRect = scrollRect != null ? scrollRect.transform as RectTransform : null;
        if (viewportRect != null)
        {
            viewportRect.sizeDelta = new Vector2(listSize.x, listHeight);
            viewportRect.gameObject.SetActive(visibleLevels.Count > 0);
        }

        // Keep the title and list together, and leave a little breathing room
        // below the final row. Empty mail uses the authored helper message.
        float contentBottom = visibleLevels.Count == 0
            ? 132f
            : Mathf.Abs(listOffset.y) + listHeight;
        const float bottomPadding = 24f;
        float previousHeight = popupRect.rect.height;
        float nextHeight = contentBottom + bottomPadding;
        popupRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            nextHeight
        );
        Vector2 position = popupRect.anchoredPosition;
        position.y += (previousHeight - nextHeight) * 0.5f;
        popupRect.anchoredPosition = position;
    }

    private void CreateRow(LevelConfig level, bool completed, bool read)
    {
        GameObject rowObject = new GameObject($"Letter_{level.sceneName}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        rowObject.transform.SetParent(listContent, false);
        generatedRows.Add(rowObject);

        RectTransform rowRect = rowObject.GetComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(0f, rowHeight);
        LayoutElement element = rowObject.GetComponent<LayoutElement>();
        element.minHeight = rowHeight;
        element.preferredHeight = rowHeight;
        element.flexibleHeight = 0f;

        bool isNew = !read && !completed;
        Color statusColor = completed ? deliveredColor : (isNew ? unreadColor : readColor);
        Image background = rowObject.GetComponent<Image>();
        background.color = isNew ? new Color(1f, 0.91f, 0.63f, 1f) : new Color(0.96f, 0.89f, 0.76f, 1f);
        Button button = rowObject.GetComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;
        button.onClick.AddListener(() => OpenLetter(level));

        float rowWidth = Mathf.Max(0f, listSize.x - 8f);
        float senderWidth = Mathf.Max(100f, rowWidth - 112f);
        float subjectWidth = Mathf.Max(100f, rowWidth - 24f);

        string location = string.IsNullOrWhiteSpace(level.previewLocation) ? "our village" : level.previewLocation;
        CreateRowText(rowObject.transform, "Sender", $"Letter from {location}", 17f, FontStyles.Bold,
            new Vector2(12f, -7f), new Vector2(senderWidth, 22f), TextAlignmentOptions.Left, new Color(0.24f, 0.17f, 0.12f, 1f));
        CreateRowText(rowObject.transform, "Status", completed ? completedStatus : (read ? readStatus : unreadStatus), 11f,
            FontStyles.Bold, new Vector2(-12f, -7f), new Vector2(82f, 20f), TextAlignmentOptions.Right, statusColor, true);
        CreateRowText(rowObject.transform, "Subject", $"A child's wish  ·  {level.label}", 12f, FontStyles.Normal,
            new Vector2(12f, -29f), new Vector2(subjectWidth, 17f), TextAlignmentOptions.Left, new Color(0.38f, 0.29f, 0.19f, 1f));
    }

    private void CreateRowText(Transform parent, string objectName, string value, float fontSize, FontStyles style,
        Vector2 position, Vector2 size, TextAlignmentOptions alignment, Color color, bool rightAnchored = false)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rightAnchored ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        rect.anchorMax = rightAnchored ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        rect.pivot = rightAnchored ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TMP_Text text = textObject.GetComponent<TMP_Text>();
        if (titleText != null) text.font = titleText.font;
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(9f, fontSize - 3f);
        text.fontSizeMax = fontSize;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static bool WasRead(LevelConfig level)
    {
        return PlayerPrefs.GetInt(ReadKey(level), 0) == 1;
    }

    private static string ReadKey(LevelConfig level)
    {
        return ReadKeyPrefix + level.sceneName;
    }

    private void OpenLetter(LevelConfig level)
    {
        if (level == null || string.IsNullOrWhiteSpace(level.sceneName)) return;
        PlayerPrefs.SetInt(ReadKey(level), 1);
        PlayerPrefs.Save();
        GameManager.ApplyLevel(level);
        SantaTrailSceneLoadRequest.Request("letter", true);
        SceneManager.LoadSceneAsync(LoadingSceneName, LoadSceneMode.Single);
    }
}

public sealed class Level2LetterNotificationPointer : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    public void OnPointerEnter(PointerEventData eventData)
    {
        Level2LetterNotification.FindInboxController()?.OnPointerEnter(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Level2LetterNotification.FindInboxController()?.OnPointerExit(eventData);
    }
}
