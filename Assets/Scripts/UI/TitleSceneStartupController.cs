using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Holds the player on the title screen until the first-run checks finish.
/// It uses the scene's Canvas and only creates missing status elements.
/// </summary>
public sealed class TitleSceneStartupController : MonoBehaviour
{
    [SerializeField] private string mainPageSceneName = "MainPageScene";
    [SerializeField] private string loadingSceneName = "LoadingScene";
    [SerializeField] private bool runSetupInThisScene;
    [SerializeField] private bool loadMainPageAfterSetup = true;
    [SerializeField] private Canvas existingCanvas;
    [SerializeField] private TextMeshProUGUI existingPromptText;
    [SerializeField] private TextMeshProUGUI existingStatusText;
    [SerializeField] private TextMeshProUGUI existingPercentageText;
    [SerializeField] private Image existingProgressFill;
    [SerializeField] private Image existingBackgroundImage;

    private Canvas overlayCanvas;
    private TextMeshProUGUI promptText;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI percentageText;
    private Image progressFill;
    private bool startupStarted;

    private void Start()
    {
        CreateOverlay();
        if (runSetupInThisScene)
        {
            BeginStartup();
        }
        else
        {
            promptText.text = "Click anywhere to continue";
        }
    }

    private void Update()
    {
        if (startupStarted || !WasContinueInputPressed())
        {
            return;
        }

        BeginStartup();
    }

    /// <summary>
    /// Can be called by the title Play button as well as the click-anywhere flow.
    /// </summary>
    public void BeginStartup()
    {
        if (startupStarted)
        {
            return;
        }

        startupStarted = true;
        if (promptText != null)
        {
            promptText.text = string.Empty;
        }

        if (!runSetupInThisScene)
        {
            SceneManager.LoadSceneAsync(loadingSceneName, LoadSceneMode.Single);
            return;
        }

        StartCoroutine(PrepareAndOpenMainPage());
    }

    private IEnumerator PrepareAndOpenMainPage()
    {
        SetProgress(0.03f, "Checking SantaTrail files...");
        yield return null;

        Task setupTask = null;
        bool setupNeeded = false;
        try
        {
            setupNeeded = SantaTrailWindowsFirstRunSetup.IsNeeded();
            if (setupNeeded)
            {
                SetProgress(0.08f, "Downloading required support packages...");
                setupTask = SantaTrailWindowsFirstRunSetup.EnsureAsync();
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("SantaTrail startup check failed: " + exception.Message);
        }

        if (setupTask != null)
        {
            float fakeProgress = 0.10f;
            while (!setupTask.IsCompleted)
            {
                // The setup class downloads several files but does not expose
                // byte progress, so show honest activity instead of a fake ETA.
                fakeProgress = Mathf.Min(0.82f, fakeProgress + 0.015f);
                SetProgress(fakeProgress, "Preparing support packages...");
                yield return new WaitForSecondsRealtime(0.25f);
            }

            if (setupTask.IsFaulted)
            {
                Debug.LogException(setupTask.Exception);
            }

            if (SantaTrailWindowsFirstRunSetup.IsNeeded())
            {
                SetProgress(
                    0.86f,
                    "Some optional flight tools are unavailable. Continuing with guided mode..."
                );
                yield return new WaitForSecondsRealtime(1.2f);
            }
            else
            {
                SetProgress(0.86f, "Support packages are ready.");
            }
        }
        else
        {
            SetProgress(0.72f, "Support packages are already ready.");
        }

        string backendPath = Path.Combine(
            Application.streamingAssetsPath,
            "Backend",
            "main.py"
        );
        if (!File.Exists(backendPath))
        {
            SetProgress(
                0.86f,
                "The bundled terrain backend is missing. Rebuild the Windows package."
            );
            Debug.LogError("SantaTrail startup: Backend/main.py was not found.");
            yield break;
        }

        SetProgress(0.92f, "Opening the SantaTrail main page...");
        if (!loadMainPageAfterSetup)
        {
            SetProgress(1f, "Ready.");
            yield break;
        }

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(
            mainPageSceneName,
            LoadSceneMode.Single
        );
        if (loadOperation == null)
        {
            SetProgress(0.92f, "Could not load the main page scene.");
            Debug.LogError(
                $"SantaTrail startup: scene '{mainPageSceneName}' could not be loaded."
            );
            yield break;
        }

        loadOperation.allowSceneActivation = false;
        while (loadOperation.progress < 0.9f)
        {
            float sceneProgress = Mathf.Lerp(0.92f, 0.99f, loadOperation.progress / 0.9f);
            SetProgress(sceneProgress, "Opening the SantaTrail main page...");
            yield return null;
        }

        SetProgress(1f, "Ready.");
        loadOperation.allowSceneActivation = true;
    }

    private bool WasContinueInputPressed()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            return true;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null &&
            (keyboard.spaceKey.wasPressedThisFrame ||
             keyboard.enterKey.wasPressedThisFrame ||
             keyboard.numpadEnterKey.wasPressedThisFrame))
        {
            return true;
        }

        Touchscreen touchscreen = Touchscreen.current;
        return touchscreen != null &&
            touchscreen.primaryTouch.press.wasPressedThisFrame;
    }

    private void CreateOverlay()
    {
        overlayCanvas = existingCanvas;
        if (overlayCanvas == null)
        {
            overlayCanvas = FindFirstObjectByType<Canvas>();
        }

        if (overlayCanvas == null)
        {
            GameObject canvasObject = new GameObject("SantaTrailStartupCanvas");
            canvasObject.transform.SetParent(transform, false);
            overlayCanvas = canvasObject.AddComponent<Canvas>();
        }

        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = 500;

        CanvasScaler scaler = overlayCanvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = overlayCanvas.gameObject.AddComponent<CanvasScaler>();
        }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRect = overlayCanvas.GetComponent<RectTransform>();
        if (canvasRect != null && canvasRect.localScale.sqrMagnitude < 0.001f)
        {
            canvasRect.localScale = Vector3.one;
        }

        if (runSetupInThisScene)
        {
            existingBackgroundImage = existingBackgroundImage != null
                ? existingBackgroundImage
                : FindImage("StartupBackground");
            if (existingBackgroundImage == null)
            {
                existingBackgroundImage = CreateBackground();
            }
            existingBackgroundImage.raycastTarget = false;
        }

        promptText = existingPromptText != null
            ? existingPromptText
            : FindText("StartupPrompt");
        if (promptText == null)
        {
            promptText = CreateText(
                "StartupPrompt",
                "",
                32,
                new Vector2(0.5f, 0.16f),
                new Vector2(0.5f, 0.16f),
                new Vector2(900f, 70f),
                new Color(1f, 0.82f, 0.25f, 1f)
            );
        }

        if (!runSetupInThisScene)
        {
            return;
        }

        statusText = existingStatusText != null
            ? existingStatusText
            : FindText("StartupStatus");
        if (statusText == null)
        {
            statusText = CreateText(
                "StartupStatus",
                "",
                24,
                new Vector2(0.5f, 0.09f),
                new Vector2(0.5f, 0.09f),
                new Vector2(1200f, 58f),
                Color.white
            );
        }

        percentageText = existingPercentageText != null
            ? existingPercentageText
            : FindText("StartupPercentage");
        if (percentageText == null)
        {
            percentageText = CreateText(
                "StartupPercentage",
                "0%",
                20,
                new Vector2(0.5f, 0.04f),
                new Vector2(0.5f, 0.04f),
                new Vector2(160f, 42f),
                new Color(1f, 0.88f, 0.55f, 1f)
            );
        }

        progressFill = existingProgressFill != null
            ? existingProgressFill
            : FindImage("StartupProgressFill");
        if (progressFill == null)
        {
            progressFill = CreateProgressBar();
        }
    }

    private Image CreateBackground()
    {
        GameObject backgroundObject = new GameObject("StartupBackground");
        backgroundObject.transform.SetParent(overlayCanvas.transform, false);
        RectTransform backgroundRect = backgroundObject.AddComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        Image background = backgroundObject.AddComponent<Image>();
        background.color = new Color(0.035f, 0.02f, 0.01f, 1f);
        return background;
    }

    private Image CreateProgressBar()
    {
        GameObject barObject = new GameObject("StartupProgressBar");
        barObject.transform.SetParent(overlayCanvas.transform, false);
        RectTransform barRect = barObject.AddComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0.5f, 0.115f);
        barRect.anchorMax = new Vector2(0.5f, 0.115f);
        barRect.pivot = new Vector2(0.5f, 0.5f);
        barRect.sizeDelta = new Vector2(820f, 16f);
        Image barBackground = barObject.AddComponent<Image>();
        barBackground.color = new Color(0.06f, 0.04f, 0.02f, 0.85f);
        barBackground.raycastTarget = false;

        GameObject fillObject = new GameObject("StartupProgressFill");
        fillObject.transform.SetParent(barObject.transform, false);
        RectTransform fillRect = fillObject.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fill = fillObject.AddComponent<Image>();
        fill.color = new Color(0.96f, 0.65f, 0.08f, 1f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.fillAmount = 0f;
        fill.raycastTarget = false;
        return fill;
    }

    private TextMeshProUGUI FindText(string objectName)
    {
        foreach (TextMeshProUGUI text in overlayCanvas.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text.gameObject.name == objectName)
            {
                return text;
            }
        }

        return null;
    }

    private Image FindImage(string objectName)
    {
        foreach (Image image in overlayCanvas.GetComponentsInChildren<Image>(true))
        {
            if (image.gameObject.name == objectName)
            {
                return image;
            }
        }

        return null;
    }

    private TextMeshProUGUI CreateText(
        string objectName,
        string initialText,
        float fontSize,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 size,
        Color color
    )
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(overlayCanvas.transform, false);
        RectTransform rectTransform = textObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = initialText;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private void SetProgress(float progress, string message)
    {
        if (progressFill != null)
        {
            progressFill.fillAmount = Mathf.Clamp01(progress);
        }

        if (percentageText != null)
        {
            percentageText.text = $"{Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f)}%";
        }

        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}
