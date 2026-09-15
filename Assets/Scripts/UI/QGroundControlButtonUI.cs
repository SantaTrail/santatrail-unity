using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("SantaTrail/UI/QGroundControl Button")]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Image), typeof(Button))]
public sealed class QGroundControlButtonUI : MonoBehaviour
{
    [Header("Appearance")]
    public Color closedColor = new Color(0.72f, 0.08f, 0.10f, 0.96f);
    public Color openColor = new Color(0.08f, 0.58f, 0.24f, 0.96f);
    public Color labelColor = Color.white;
    [Min(8)] public int fontSize = 20;

    private Button button;
    private Image background;
    private Text label;

    private void Awake()
    {
        EnsureVisuals();
        Bind(true);
    }

    private void OnEnable()
    {
        Bind(true);
        StartCoroutine(ConnectToController());
    }

    private void OnDisable()
    {
        Bind(false);
    }

    public void SetAvailable(bool available)
    {
        if (gameObject.activeSelf != available)
        {
            gameObject.SetActive(available);
        }
    }

    public void SetOpen(bool isOpen)
    {
        EnsureVisuals();
        Color stateColor = isOpen ? openColor : closedColor;
        background.color = stateColor;

        ColorBlock colors = button.colors;
        colors.normalColor = stateColor;
        colors.highlightedColor = Color.Lerp(stateColor, Color.white, 0.18f);
        colors.pressedColor = Color.Lerp(stateColor, Color.black, 0.22f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
    }

    private IEnumerator ConnectToController()
    {
        yield return null;
        QGroundControlOverlayController.Active?.RefreshButtonComponents();
    }

    private void ToggleQGroundControl()
    {
        QGroundControlOverlayController.Active?.ToggleOverlay();
    }

    private void Bind(bool bind)
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(ToggleQGroundControl);
        if (bind)
        {
            button.onClick.AddListener(ToggleQGroundControl);
        }
    }

    private void EnsureVisuals()
    {
        background = GetComponent<Image>();
        button = GetComponent<Button>();
        background.raycastTarget = true;
        button.targetGraphic = background;

        Transform existingLabel = transform.Find("Label");
        if (existingLabel == null)
        {
            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(transform, false);
            label = labelObject.GetComponent<Text>();
        }
        else
        {
            label = existingLabel.GetComponent<Text>();
        }

        if (label == null)
        {
            return;
        }

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = fontSize;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = labelColor;
        label.raycastTarget = false;
        label.text = "QGC";
    }
}
