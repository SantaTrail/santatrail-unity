using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PauseMenuAudioSettingsUI : MonoBehaviour
{
    private static readonly Color Gold = new Color(0.96f, 0.79f, 0.43f, 1f);
    private static readonly Color Track = new Color(0.08f, 0.06f, 0.04f, 0.9f);
    private static readonly Color Handle = new Color(1f, 0.92f, 0.72f, 1f);

    private RectTransform mainPanel;
    private GameObject settingsPanel;
    private Button settingsButton;
    private Button backButton;
    private Slider masterSlider;
    private Slider musicSlider;
    private Slider soundEffectsSlider;
    private TMP_Text masterValue;
    private TMP_Text musicValue;
    private TMP_Text soundEffectsValue;
    private bool initialized;

    public bool IsOpen => initialized && settingsPanel != null && settingsPanel.activeSelf;

    public void Initialize(RectTransform menuPanel, Button buttonTemplate)
    {
        if (initialized || menuPanel == null || buttonTemplate == null) return;

        initialized = true;
        mainPanel = menuPanel;
        ExpandPanel(mainPanel);
        settingsButton = CreateButton(buttonTemplate, mainPanel, "Settings Button", "Audio Settings");
        settingsButton.transform.SetSiblingIndex(buttonTemplate.transform.GetSiblingIndex() + 1);
        settingsButton.onClick.AddListener(Open);

        BuildSettingsPanel(buttonTemplate);
        ShowMainMenu(false);
    }

    public void Open()
    {
        if (!initialized) return;

        SyncControls();
        mainPanel.gameObject.SetActive(false);
        settingsPanel.SetActive(true);
        masterSlider.Select();
    }

    public void ShowMainMenu(bool selectSettingsButton = true)
    {
        if (!initialized) return;

        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (mainPanel != null) mainPanel.gameObject.SetActive(true);
        SantaTrailAudioSettings.Save();

        if (selectSettingsButton && settingsButton != null && EventSystem.current != null)
        {
            settingsButton.Select();
        }
    }

    private void BuildSettingsPanel(Button buttonTemplate)
    {
        RectTransform panelRect = CreateRectObject("Audio Settings Panel", mainPanel.parent);
        panelRect.anchorMin = mainPanel.anchorMin;
        panelRect.anchorMax = mainPanel.anchorMax;
        panelRect.anchoredPosition = mainPanel.anchoredPosition;
        panelRect.sizeDelta = mainPanel.sizeDelta;
        panelRect.pivot = mainPanel.pivot;
        settingsPanel = panelRect.gameObject;

        Image sourceImage = mainPanel.GetComponent<Image>();
        Image panelImage = settingsPanel.AddComponent<Image>();
        panelImage.color = sourceImage != null
            ? sourceImage.color
            : new Color(0.19f, 0.11f, 0.05f, 1f);

        VerticalLayoutGroup sourceLayout = mainPanel.GetComponent<VerticalLayoutGroup>();
        VerticalLayoutGroup panelLayout = settingsPanel.AddComponent<VerticalLayoutGroup>();
        if (sourceLayout != null)
        {
            panelLayout.padding = new RectOffset(
                sourceLayout.padding.left,
                sourceLayout.padding.right,
                sourceLayout.padding.top,
                sourceLayout.padding.bottom);
            panelLayout.spacing = sourceLayout.spacing;
        }
        else
        {
            panelLayout.padding = new RectOffset(42, 42, 34, 28);
            panelLayout.spacing = 12f;
        }
        panelLayout.childAlignment = TextAnchor.UpperCenter;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        TMP_Text textTemplate = mainPanel.GetComponentInChildren<TMP_Text>(true);
        CreateText("Brand", panelRect, textTemplate, "S A N T A T R A I L", 17f, Gold, 26f);
        CreateText("Title", panelRect, textTemplate, "Audio Settings", 36f, Color.white, 54f);

        masterSlider = CreateSliderRow(
            panelRect,
            textTemplate,
            "Master Volume",
            SantaTrailAudioSettings.MasterVolume,
            value => SantaTrailAudioSettings.MasterVolume = value,
            out masterValue);
        musicSlider = CreateSliderRow(
            panelRect,
            textTemplate,
            "Music",
            SantaTrailAudioSettings.MusicVolume,
            value => SantaTrailAudioSettings.MusicVolume = value,
            out musicValue);
        soundEffectsSlider = CreateSliderRow(
            panelRect,
            textTemplate,
            "Sound Effects",
            SantaTrailAudioSettings.SoundEffectsVolume,
            value => SantaTrailAudioSettings.SoundEffectsVolume = value,
            out soundEffectsValue);

        GameObject spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.layer = gameObject.layer;
        spacer.transform.SetParent(panelRect, false);
        spacer.GetComponent<LayoutElement>().flexibleHeight = 1f;

        backButton = CreateButton(buttonTemplate, panelRect, "Back Button", "Back");
        backButton.onClick.AddListener(() => ShowMainMenu());
        settingsPanel.SetActive(false);
    }

    private Slider CreateSliderRow(
        RectTransform parent,
        TMP_Text textTemplate,
        string label,
        float initialValue,
        UnityAction<float> onChanged,
        out TMP_Text valueText)
    {
        RectTransform row = CreateRectObject(label + " Row", parent);
        LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
        rowLayout.minHeight = 88f;
        rowLayout.preferredHeight = 88f;

        VerticalLayoutGroup rowGroup = row.gameObject.AddComponent<VerticalLayoutGroup>();
        rowGroup.spacing = 8f;
        rowGroup.childControlWidth = true;
        rowGroup.childControlHeight = true;
        rowGroup.childForceExpandWidth = true;
        rowGroup.childForceExpandHeight = false;

        RectTransform header = CreateRectObject("Header", row);
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
        HorizontalLayoutGroup headerGroup = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        headerGroup.childControlWidth = true;
        headerGroup.childControlHeight = true;
        headerGroup.childForceExpandWidth = false;
        headerGroup.childForceExpandHeight = true;

        TMP_Text labelText = CreateText(
            "Label",
            header,
            textTemplate,
            label,
            20f,
            Color.white,
            28f);
        labelText.alignment = TextAlignmentOptions.Left;
        labelText.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

        valueText = CreateText(
            "Value",
            header,
            textTemplate,
            FormatPercent(initialValue),
            19f,
            Gold,
            28f);
        valueText.alignment = TextAlignmentOptions.Right;
        LayoutElement valueLayout = valueText.gameObject.GetComponent<LayoutElement>();
        valueLayout.minWidth = 68f;
        valueLayout.preferredWidth = 68f;

        RectTransform sliderRect = CreateRectObject("Slider", row);
        sliderRect.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
        Slider slider = sliderRect.gameObject.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.direction = Slider.Direction.LeftToRight;

        RectTransform background = CreateRectObject("Background", sliderRect);
        background.anchorMin = new Vector2(0f, 0.5f);
        background.anchorMax = new Vector2(1f, 0.5f);
        background.sizeDelta = new Vector2(-4f, 10f);
        background.gameObject.AddComponent<Image>().color = Track;

        RectTransform fillArea = CreateRectObject("Fill Area", sliderRect);
        Stretch(fillArea, 10f, 10f, 15f, 15f);
        RectTransform fill = CreateRectObject("Fill", fillArea);
        Stretch(fill, 0f, 0f, 0f, 0f);
        fill.gameObject.AddComponent<Image>().color = Gold;

        RectTransform handleArea = CreateRectObject("Handle Slide Area", sliderRect);
        Stretch(handleArea, 11f, 11f, 0f, 0f);
        RectTransform handle = CreateRectObject("Handle", handleArea);
        handle.sizeDelta = new Vector2(22f, 30f);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = Handle;

        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        slider.SetValueWithoutNotify(initialValue);

        TMP_Text capturedValueText = valueText;
        slider.onValueChanged.AddListener(value =>
        {
            capturedValueText.text = FormatPercent(value);
            onChanged(value);
        });
        return slider;
    }

    private static Button CreateButton(
        Button template,
        Transform parent,
        string objectName,
        string label)
    {
        Button button = Instantiate(template, parent);
        button.name = objectName;
        button.onClick.RemoveAllListeners();
        TMP_Text buttonLabel = button.GetComponentInChildren<TMP_Text>(true);
        if (buttonLabel != null) buttonLabel.text = label;
        return button;
    }

    private static TMP_Text CreateText(
        string objectName,
        Transform parent,
        TMP_Text template,
        string value,
        float fontSize,
        Color color,
        float height)
    {
        RectTransform rect = CreateRectObject(objectName, parent);
        TMP_Text text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (template != null) text.font = template.font;
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        return text;
    }

    private static RectTransform CreateRectObject(string objectName, Transform parent)
    {
        GameObject created = new GameObject(objectName, typeof(RectTransform));
        created.layer = parent.gameObject.layer;
        RectTransform rect = created.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void Stretch(
        RectTransform rect,
        float left,
        float right,
        float bottom,
        float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void ExpandPanel(RectTransform panel)
    {
        Vector2 size = panel.sizeDelta;
        size.y = Mathf.Max(size.y, 670f);
        panel.sizeDelta = size;
    }

    private void SyncControls()
    {
        SyncControl(masterSlider, masterValue, SantaTrailAudioSettings.MasterVolume);
        SyncControl(musicSlider, musicValue, SantaTrailAudioSettings.MusicVolume);
        SyncControl(
            soundEffectsSlider,
            soundEffectsValue,
            SantaTrailAudioSettings.SoundEffectsVolume);
    }

    private static void SyncControl(Slider slider, TMP_Text valueText, float value)
    {
        if (slider != null) slider.SetValueWithoutNotify(value);
        if (valueText != null) valueText.text = FormatPercent(value);
    }

    private static string FormatPercent(float value)
    {
        return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
    }

    private void OnDestroy()
    {
        SantaTrailAudioSettings.Save();
    }
}
