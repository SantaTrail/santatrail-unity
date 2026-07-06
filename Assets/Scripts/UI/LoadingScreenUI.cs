using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LoadingScreenUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI percentageText;
    [SerializeField] private RectTransform spinner;

    [Header("Behaviour")]
    [SerializeField] private bool showOnAwake = true;
    [SerializeField] private float fadeDuration = 0.35f;
    [SerializeField] private float spinnerSpeed = 150f;

    private Coroutine fadeCoroutine;
    private bool spinnerEnabled;

    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (showOnAwake)
        {
            Show("Preparing mission...");
        }
        else
        {
            SetVisibleImmediately(false);
        }
    }

    private void Update()
    {
        if (spinnerEnabled && spinner != null)
        {
            spinner.Rotate(
                0f,
                0f,
                -spinnerSpeed * Time.unscaledDeltaTime
            );
        }
    }

    public void Show(string message = "Loading...")
    {
        gameObject.SetActive(true);
        StopFade();

        spinnerEnabled = true;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        SetProgress(0f, message);
    }

    public void SetProgress(float progress, string message = null)
    {
        progress = Mathf.Clamp01(progress);

        if (progressSlider != null)
        {
            progressSlider.minValue = 0f;
            progressSlider.maxValue = 1f;
            progressSlider.value = progress;
        }

        if (percentageText != null)
        {
            percentageText.text = Mathf.RoundToInt(progress * 100f) + "%";
        }

        if (statusText != null && !string.IsNullOrWhiteSpace(message))
        {
            statusText.text = message;
        }
    }

    public void ShowError(string message)
    {
        StopFade();
        gameObject.SetActive(true);
        spinnerEnabled = false;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        SetProgress(1f, message);

        if (percentageText != null)
        {
            percentageText.text = "Error";
        }
    }

    public void Hide()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        StopFade();
        fadeCoroutine = StartCoroutine(FadeOut());
    }

    public void SetVisibleImmediately(bool visible)
    {
        StopFade();

        spinnerEnabled = visible;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        gameObject.SetActive(visible);
    }

    private IEnumerator FadeOut()
    {
        spinnerEnabled = false;

        if (canvasGroup == null || fadeDuration <= 0f)
        {
            SetVisibleImmediately(false);
            yield break;
        }

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        float startingAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(
                startingAlpha,
                0f,
                elapsed / fadeDuration
            );

            yield return null;
        }

        canvasGroup.alpha = 0f;
        fadeCoroutine = null;
        gameObject.SetActive(false);
    }

    private void StopFade()
    {
        if (fadeCoroutine == null)
        {
            return;
        }

        StopCoroutine(fadeCoroutine);
        fadeCoroutine = null;
    }
}
