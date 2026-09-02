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

    [Header("Christmas Loading Theme")]
    [Tooltip("Display Santa/drone-themed loading messages based on progress.")]
    [SerializeField] private bool useThemedMessages = true;

    [Tooltip(
        "When enabled, a message passed by another script to Show() or " +
        "SetProgress() is shown instead of the themed message."
    )]
    [SerializeField] private bool allowExternalStatusMessages = false;

    [Tooltip("Show the technical error message to the player.")]
    [SerializeField] private bool showDetailedErrorToPlayer = false;

    [TextArea]
    [SerializeField] private string friendlyErrorMessage =
        "Oh snow! The elves could not prepare the mission.";

    private Coroutine fadeCoroutine;
    private bool spinnerEnabled;

    public float CurrentProgress { get; private set; }
    public bool IsReady { get; private set; }
    public bool HasError { get; private set; }

    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (showOnAwake)
        {
            Show();
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

    public void Show(string message = null)
    {
        gameObject.SetActive(true);
        StopFade();

        spinnerEnabled = true;
        IsReady = false;
        HasError = false;

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
        CurrentProgress = progress;
        IsReady = progress >= 1f;

        if (progressSlider != null)
        {
            progressSlider.minValue = 0f;
            progressSlider.maxValue = 1f;
            progressSlider.value = progress;
        }

        if (percentageText != null)
        {
            percentageText.text =
                progress >= 1f
                    ? "READY!"
                    : Mathf.RoundToInt(progress * 100f) + "%";
        }

        if (statusText != null)
        {
            statusText.text = ResolveStatusMessage(progress, message);
        }
    }

    public void ShowError(string message)
    {
        StopFade();
        gameObject.SetActive(true);
        spinnerEnabled = false;
        IsReady = false;
        HasError = true;

        if (!string.IsNullOrWhiteSpace(message))
        {
            Debug.LogError("Loading screen error: " + message);
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        float currentProgress =
            progressSlider != null ? progressSlider.value : 0f;

        string playerMessage = friendlyErrorMessage;

        if (showDetailedErrorToPlayer &&
            !string.IsNullOrWhiteSpace(message))
        {
            playerMessage += "\n" + message;
        }

        SetProgress(currentProgress, playerMessage);

        if (percentageText != null)
        {
            percentageText.text = "OH SNOW!";
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

    private string ResolveStatusMessage(
        float progress,
        string externalMessage
    )
    {
        bool hasExternalMessage =
            !string.IsNullOrWhiteSpace(externalMessage);

        if (hasExternalMessage &&
            (!useThemedMessages || allowExternalStatusMessages))
        {
            return externalMessage;
        }

        if (useThemedMessages)
        {
            return GetThemedStatusMessage(progress);
        }

        return hasExternalMessage ? externalMessage : "Loading...";
    }

    private string GetThemedStatusMessage(float progress)
    {
        if (progress < 0.08f)
        {
            return "Santa is checking tonight's present list...";
        }

        if (progress < 0.18f)
        {
            return "Santa is waking the delivery drone...";
        }

        if (progress < 0.30f)
        {
            return "Connecting the sleigh-drone to Santa's flight desk...";
        }

        if (progress < 0.42f)
        {
            return "Unfolding the Christmas delivery map...";
        }

        if (progress < 0.55f)
        {
            return "Building the snowy village for present deliveries...";
        }

        if (progress < 0.68f)
        {
            return "Finding rooftops ready for presents...";
        }

        if (progress < 0.80f)
        {
            return "Checking the present list for each child...";
        }

        if (progress < 0.90f)
        {
            return "Plotting Santa's safest delivery route...";
        }

        if (progress < 0.97f)
        {
            return "Checking Christmas spirit at every home...";
        }

        if (progress < 1f)
        {
            return "Finishing Santa's final flight checklist...";
        }

        return "Present route ready! Let's deliver Christmas!";
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
