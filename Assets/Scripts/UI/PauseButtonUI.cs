using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("SantaTrail/UI/Pause Button")]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Image), typeof(Button))]
public sealed class PauseButtonUI : MonoBehaviour
{
    [Header("Appearance")]
    public Color backgroundColor = new Color(0.04f, 0.16f, 0.19f, 0.94f);
    public Color iconColor = Color.white;
    public Vector2 iconBarSize = new Vector2(7f, 24f);
    [Min(0f)] public float iconBarOffset = 6f;

    private Button button;
    private Image background;

    private void Awake()
    {
        EnsureVisuals();
        Bind(true);
    }

    private void OnEnable()
    {
        Bind(true);
    }

    private void OnDisable()
    {
        Bind(false);
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
        {
            gameObject.SetActive(visible);
        }
    }

    private void TogglePause()
    {
        PauseMenuController.TryTogglePause();
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

        button.onClick.RemoveListener(TogglePause);
        if (bind)
        {
            button.onClick.AddListener(TogglePause);
        }
    }

    private void EnsureVisuals()
    {
        background = GetComponent<Image>();
        button = GetComponent<Button>();
        background.color = backgroundColor;
        background.raycastTarget = true;
        button.targetGraphic = background;

        CreateOrUpdateBar("Pause Bar Left", -iconBarOffset);
        CreateOrUpdateBar("Pause Bar Right", iconBarOffset);
    }

    private void CreateOrUpdateBar(string objectName, float xPosition)
    {
        Transform existing = transform.Find(objectName);
        GameObject barObject = existing != null
            ? existing.gameObject
            : new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

        if (existing == null)
        {
            barObject.transform.SetParent(transform, false);
        }

        Image barImage = barObject.GetComponent<Image>();
        barImage.color = iconColor;
        barImage.raycastTarget = false;

        RectTransform barRect = barObject.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0.5f, 0.5f);
        barRect.anchorMax = new Vector2(0.5f, 0.5f);
        barRect.pivot = new Vector2(0.5f, 0.5f);
        barRect.anchoredPosition = new Vector2(xPosition, 0f);
        barRect.sizeDelta = iconBarSize;
    }
}
