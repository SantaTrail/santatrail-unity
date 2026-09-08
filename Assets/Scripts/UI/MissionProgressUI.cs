using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class MissionProgressUI : MonoBehaviour
{
    [Header("Delivery")]
    public TMP_Text deliveryText;

    [Header("Present Icons")]
    [FormerlySerializedAs("emptyPresent")]
    public Sprite openPresentSprite;
    [FormerlySerializedAs("filledPresent")]
    public Sprite closedPresentSprite;

    [Tooltip("Status Image children on Gift1, Gift2, Gift3, and Gift4 in this order. They are created automatically.")]
    public Image[] presentIcons;

    [Tooltip("Optional per-box sizes. Leave empty to keep the Gift box sizes from the scene.")]
    public Vector2[] presentIconSizes;

    [Tooltip("Optional per-box anchored positions. Leave empty to keep the Gift box positions from the scene.")]
    public Vector2[] presentIconPositions;

    [Tooltip("Default status icon size when a per-box size is not provided.")]
    public Vector2 defaultPresentIconSize = new Vector2(70f, 70f);

    [Tooltip("Size used while the delivery is still open.")]
    public Vector2 openPresentIconSize = new Vector2(70f, 70f);

    [Tooltip("Size used after the delivery is packed correctly.")]
    public Vector2 closedPresentIconSize = new Vector2(70f, 70f);

    private int totalDeliveries;
    private int currentDelivery;

    private void Awake()
    {
        ResolveGiftBoxIcons();
    }

    private void ResolveGiftBoxIcons()
    {
        // Keep the UI visible if an old scene has lost the open-sprite reference.
        if (openPresentSprite == null)
        {
            openPresentSprite = closedPresentSprite;
        }

        if (closedPresentSprite == null)
        {
            closedPresentSprite = openPresentSprite;
        }

        string[] giftNames = { "Gift1", "Gift2", "Gift3", "Gift4", "Gift5" };
        presentIcons = new Image[giftNames.Length];

        Image[] sceneImages = FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < giftNames.Length; i++)
        {
            GameObject giftBox = GameObject.Find(giftNames[i]);
            if (giftBox != null)
            {
                Image giftBackground = giftBox.GetComponent<Image>();
                if (giftBackground != null)
                {
                    giftBackground.enabled = false;
                }

                presentIcons[i] = GetOrCreateStatusIcon(giftBox.transform);
                continue;
            }

            for (int imageIndex = 0; imageIndex < sceneImages.Length; imageIndex++)
            {
                Image sceneImage = sceneImages[imageIndex];
                if (sceneImage != null && sceneImage.gameObject.name == giftNames[i])
                {
                    // Keep the slot's RectTransform, but remove its white placeholder renderer.
                    sceneImage.enabled = false;
                    presentIcons[i] = GetOrCreateStatusIcon(sceneImage.transform);
                    break;
                }
            }
        }
    }

    private Image GetOrCreateStatusIcon(Transform giftBox)
    {
        Transform existingIcon = giftBox.Find("PresentStatusIcon");
        if (existingIcon != null)
        {
            return existingIcon.GetComponent<Image>();
        }

        GameObject iconObject = new GameObject(
            "PresentStatusIcon",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        iconObject.transform.SetParent(giftBox, false);

        Image icon = iconObject.GetComponent<Image>();
        icon.enabled = true;
        icon.color = Color.white;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.transform.SetAsLastSibling();

        RectTransform iconRect = icon.rectTransform;
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = openPresentIconSize;
        iconRect.anchoredPosition = Vector2.zero;

        return icon;
    }

    public void SetupMission(int total)
    {
        totalDeliveries = Mathf.Max(0, total);
        currentDelivery = 0;

        RefreshUI();
    }

    public bool CompleteDelivery()
    {
        currentDelivery = Mathf.Min(currentDelivery + 1, totalDeliveries);

        RefreshUI();

        return currentDelivery >= totalDeliveries;
    }

    private void RefreshUI()
    {
        if (deliveryText != null)
        {
            deliveryText.text = $"DELIVERY {currentDelivery}/{totalDeliveries}";
        }

        ResolveGiftBoxIcons();

        if (presentIcons == null || presentIcons.Length == 0)
        {
            return;
        }

        int activeDeliveryCount = Mathf.Clamp(currentDelivery, 0, totalDeliveries);

        for (int i = 0; i < presentIcons.Length; i++)
        {
            Image icon = presentIcons[i];
            if (icon == null)
            {
                continue;
            }

            bool shouldShow = i < totalDeliveries;
            icon.gameObject.SetActive(shouldShow);

            if (!shouldShow)
            {
                continue;
            }

            bool isClosed = i < activeDeliveryCount;
            RectTransform iconRect = icon.rectTransform;
            if (iconRect != null)
            {
                Vector2 iconSize = isClosed
                    ? closedPresentIconSize
                    : openPresentIconSize;

                if (iconSize.x <= 0f || iconSize.y <= 0f)
                {
                    iconSize = defaultPresentIconSize;
                }

                if (presentIconSizes != null && i < presentIconSizes.Length)
                {
                    Vector2 configuredSize = presentIconSizes[i];
                    if (configuredSize.x > 0f && configuredSize.y > 0f)
                    {
                        iconSize = configuredSize;
                    }
                }

                iconRect.sizeDelta = iconSize;
            }

            if (iconRect != null && presentIconPositions != null && i < presentIconPositions.Length)
            {
                iconRect.anchoredPosition = presentIconPositions[i];
            }

            Sprite nextSprite = isClosed
                ? closedPresentSprite
                : openPresentSprite;

            if (nextSprite != null)
            {
                icon.sprite = nextSprite;
                icon.enabled = true;
            }

            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.transform.SetAsLastSibling();
        }
    }

}
