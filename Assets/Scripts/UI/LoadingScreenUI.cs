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
            return "Opening tonight's mission letter...";
        }

        if (progress < 0.18f)
        {
            return "Waking up the delivery drone...";
        }

        if (progress < 0.30f)
        {
            return "Connecting to Santa's navigation system...";
        }

        if (progress < 0.42f)
        {
            return "Unfolding the village map...";
        }

        if (progress < 0.55f)
        {
            return "Building the snowy village...";
        }

        if (progress < 0.68f)
        {
            return "Searching for delivery rooftops...";
        }

        if (progress < 0.80f)
        {
            return "Packing presents into the cargo bay...";
        }

        if (progress < 0.90f)
        {
            return "Planning a safe delivery route...";
        }

        if (progress < 0.97f)
        {
            return "Checking the Christmas spirit signal...";
        }

        if (progress < 1f)
        {
            return "Finishing the elf flight checklist...";
        }

        return "Mission ready! Prepare for takeoff!";
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
