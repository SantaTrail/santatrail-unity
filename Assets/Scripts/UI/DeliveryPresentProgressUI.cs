using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class DeliveryPresentProgressUI : MonoBehaviour
{
    [Header("Present Icon")]
    [Tooltip("Optional override. When empty, the bundled closed-present icon is used.")]
    public GameObject presentIconPrefab;
    public Color pendingPresentColor = new Color(0.72f, 0.72f, 0.72f, 0.72f);
    public Color deliveredPresentColor = Color.white;
    public Vector2 presentIconSize = new Vector2(40f, 40f);
    [Min(0f)] public float presentIconSpacing = 6f;

    private Image[] presentIcons = Array.Empty<Image>();
    private Material pendingPresentMaterial;
    private bool missingPresentIconWarningLogged;

    public void SetProgress(int completedTargets, int totalTargets)
    {
        totalTargets = Mathf.Max(0, totalTargets);
        EnsureIcons(totalTargets);

        for (int i = 0; i < presentIcons.Length; i++)
        {
            Image icon = presentIcons[i];
            if (icon == null)
            {
                continue;
            }

            bool isDelivered = i < completedTargets;
            icon.color = isDelivered ? deliveredPresentColor : pendingPresentColor;
            icon.material = isDelivered ? null : GetPendingPresentMaterial();
        }
    }

    private void EnsureIcons(int totalTargets)
    {
        if (presentIcons.Length == totalTargets && IconsAreValid())
        {
            return;
        }

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }

        if (totalTargets == 0)
        {
            presentIcons = Array.Empty<Image>();
            return;
        }

        GameObject iconTemplate = presentIconPrefab != null
            ? presentIconPrefab
            : Resources.Load<GameObject>("SantaTrailDeliveryPresentIcon");

        if (iconTemplate == null || iconTemplate.GetComponent<Image>() == null)
        {
            if (!missingPresentIconWarningLogged)
            {
                Debug.LogWarning("DeliveryPresentProgressUI: Present icon resource was not found.");
                missingPresentIconWarningLogged = true;
            }

            presentIcons = Array.Empty<Image>();
            return;
        }

        float iconWidth = Mathf.Max(1f, presentIconSize.x);
        float iconHeight = Mathf.Max(1f, presentIconSize.y);
        float rowWidth = (totalTargets * iconWidth) + ((totalTargets - 1) * presentIconSpacing);

        if (transform is RectTransform rowRect)
        {
            rowRect.sizeDelta = new Vector2(rowWidth, iconHeight);
        }

        presentIcons = new Image[totalTargets];
        float leftEdge = -rowWidth * 0.5f;

        for (int i = 0; i < totalTargets; i++)
        {
            GameObject iconObject = Instantiate(iconTemplate, transform, false);
            iconObject.name = $"Present {i + 1}";

            Image icon = iconObject.GetComponent<Image>();
            icon.enabled = true;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            RectTransform iconRect = icon.rectTransform;
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(iconWidth, iconHeight);
            iconRect.anchoredPosition = new Vector2(
                leftEdge + (iconWidth * 0.5f) + (i * (iconWidth + presentIconSpacing)),
                0f);

            presentIcons[i] = icon;
        }
    }

    private bool IconsAreValid()
    {
        for (int i = 0; i < presentIcons.Length; i++)
        {
            if (presentIcons[i] == null)
            {
                return false;
            }
        }

        return true;
    }

    private Material GetPendingPresentMaterial()
    {
        if (pendingPresentMaterial != null)
        {
            return pendingPresentMaterial;
        }

        Shader grayscaleShader = Resources.Load<Shader>("SantaTrailUIGrayscale");
        if (grayscaleShader == null)
        {
            return null;
        }

        pendingPresentMaterial = new Material(grayscaleShader)
        {
            name = "SantaTrail Pending Present (Runtime)"
        };

        return pendingPresentMaterial;
    }

    private void OnDestroy()
    {
        if (pendingPresentMaterial != null)
        {
            Destroy(pendingPresentMaterial);
        }
    }
}
