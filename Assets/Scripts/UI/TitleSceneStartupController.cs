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
    private string destinationSceneName;
    private bool prepareLetterScene;

    private void Start()
    {
        Debug.Log(
            $"SantaTrail startup: entered {gameObject.scene.name}. " +
            $"SetupInThisScene={runSetupInThisScene}."
        );
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
        Debug.Log(runSetupInThisScene
            ? "SantaTrail startup: LoadingScene setup started."
            : $"SantaTrail startup: title activated loading scene '{loadingSceneName}'.");
        if (promptText != null)
        {
            promptText.text = string.Empty;
        }

        if (!runSetupInThisScene)
        {
            try
            {
                AsyncOperation loadingOperation = SceneManager.LoadSceneAsync(
                    loadingSceneName,
                    LoadSceneMode.Single
                );
                if (loadingOperation == null)
                {
                    Debug.LogError(
                        $"SantaTrail startup: loading scene '{loadingSceneName}' was not found."
                    );
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "SantaTrail startup: could not open LoadingScene. " + exception.Message
                );
            }
            return;
        }

        StartCoroutine(PrepareAndOpenDestination());
    }

    private IEnumerator PrepareAndOpenDestination()
    {
        destinationSceneName = mainPageSceneName;
        prepareLetterScene = false;
        bool hasRequestedDestination = SantaTrailSceneLoadRequest.TryConsume(
                out string requestedSceneName,
                out bool requestedLetterPreparation
            );
        if (hasRequestedDestination)
        {
            if (!string.IsNullOrWhiteSpace(requestedSceneName))
            {
                destinationSceneName = requestedSceneName;
            }

            prepareLetterScene = requestedLetterPreparation;
            Debug.Log(
                $"SantaTrail startup: LoadingScene destination is '{destinationSceneName}'. " +
                $"PrepareLetter={prepareLetterScene}."
            );
        }

        yield return null;

        // The title-to-main-page transition owns flight-tool setup. A
        // MainPage-to-letter transition only needs to prepare the letter
        // scene, so it must not run the flight setup again.
        if (prepareLetterScene)
        {
            yield return StartCoroutine(PrepareLetterSceneAndOpen());
            yield break;
        }

        // Other scene transitions can reuse this loading screen without
        // repeating first-run flight setup. The title entry is the only path
        // that performs the dependency check below.
        if (hasRequestedDestination)
        {
            yield return StartCoroutine(OpenRequestedScene());
            yield break;
        }

        SetProgress(0.03f, "Santa is checking the flight desk and delivery tools...");

        Task setupTask = null;
        bool setupNeeded = false;
        try
        {
            setupNeeded = SantaTrailWindowsFirstRunSetup.IsNeeded();
            Debug.Log(
                "SantaTrail startup: LoadingScene dependency check. " +
                SantaTrailWindowsFirstRunSetup.GetReadinessSummary()
            );
            SetProgress(
                0.08f,
                setupNeeded
                    ? "The elves are downloading tools for tonight's deliveries..."
                    : "Santa is checking the present delivery tools..."
            );
            // Run the check on every LoadingScene entry. EnsureAsync skips
            // downloads and extraction when the files are already present.
            setupTask = SantaTrailWindowsFirstRunSetup.EnsureAsync();
            Debug.Log("SantaTrail startup: dependency setup task started.");
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
                SetProgress(fakeProgress, "The elves are preparing Santa's delivery tools...");
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
                    "Some flight tools are missing; Santa will use guided mode..."
                );
                yield return new WaitForSecondsRealtime(1.2f);
            }
            else
            {
                SetProgress(0.86f, "Santa's flight desk is ready for deliveries.");
            }
            Debug.Log(
                "SantaTrail startup: dependency setup task finished. " +
                SantaTrailWindowsFirstRunSetup.GetReadinessSummary()
            );
        }
        else
        {
            SetProgress(0.72f, "Santa's delivery tools are ready.");
        }

        string backendDirectory = Path.Combine(
            Application.streamingAssetsPath,
            "Backend"
        );
        string[] requiredBackendFiles =
        {
            "main.py",
            "parser.py",
            "osm.py",
            "elevation.py",
            "features.py",
            "classifier.py"
        };
        string missingBackendFiles = "";

        for (int i = 0; i < requiredBackendFiles.Length; i++)
        {
            if (!File.Exists(Path.Combine(
                    backendDirectory,
                    requiredBackendFiles[i]
                )))
            {
                if (missingBackendFiles.Length > 0)
                {
                    missingBackendFiles += ", ";
                }

                missingBackendFiles += requiredBackendFiles[i];
            }
        }

        if (missingBackendFiles.Length > 0)
        {
            SetProgress(
                0.86f,
                "Oh snow! The terrain delivery package is missing. Rebuild the Windows package."
            );
            Debug.LogError(
                "SantaTrail startup: incomplete Backend folder. Missing: " +
                missingBackendFiles
            );
            yield break;
        }

        SetProgress(0.92f, "Santa is opening the present delivery desk...");
        if (!loadMainPageAfterSetup)
        {
            SetProgress(1f, "Santa's delivery desk is ready!");
            yield break;
        }

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(
            destinationSceneName,
            LoadSceneMode.Single
        );
        if (loadOperation == null)
        {
            SetProgress(0.92f, "Oh snow! Santa's delivery desk could not open.");
            Debug.LogError(
                $"SantaTrail startup: scene '{destinationSceneName}' could not be loaded."
            );
            yield break;
        }

        loadOperation.allowSceneActivation = false;
        while (loadOperation.progress < 0.9f)
        {
            float sceneProgress = Mathf.Lerp(0.92f, 0.99f, loadOperation.progress / 0.9f);
            SetProgress(sceneProgress, "Santa is opening the present delivery desk...");
            yield return null;
        }

        SetProgress(1f, "Santa's delivery desk is ready!");
        loadOperation.allowSceneActivation = true;
    }

    private IEnumerator OpenRequestedScene()
    {
        if (string.Equals(destinationSceneName, "LV1", StringComparison.OrdinalIgnoreCase))
        {
            yield return StartCoroutine(OpenLevelSceneAndWaitForReady());
            yield break;
        }

        SetProgress(0.08f, $"Opening {destinationSceneName}...");

        AsyncOperation loadOperation;
        try
        {
            loadOperation = SceneManager.LoadSceneAsync(
                destinationSceneName,
                LoadSceneMode.Single
            );
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetProgress(0.08f, $"Could not load {destinationSceneName}.");
            yield break;
        }

        if (loadOperation == null)
        {
            Debug.LogError(
                $"SantaTrail startup: requested scene '{destinationSceneName}' could not be loaded."
            );
            SetProgress(0.08f, $"Could not load {destinationSceneName}.");
            yield break;
        }

        loadOperation.allowSceneActivation = false;
        while (loadOperation.progress < 0.9f)
        {
            float sceneProgress = Mathf.Lerp(0.08f, 0.99f, loadOperation.progress / 0.9f);
            SetProgress(sceneProgress, $"Opening {destinationSceneName}...");
            yield return null;
        }

        SetProgress(1f, "Ready.");
        loadOperation.allowSceneActivation = true;
    }

    private IEnumerator OpenLevelSceneAndWaitForReady()
    {
        SetProgress(0.08f, "Santa is loading the LV1 present route...");

        AsyncOperation loadOperation;
        try
        {
            loadOperation = SceneManager.LoadSceneAsync(
                destinationSceneName,
                LoadSceneMode.Additive
            );
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SetProgress(0.08f, "Could not load LV1.");
            yield break;
        }

        if (loadOperation == null)
        {
            Debug.LogError("SantaTrail startup: LV1 could not be loaded additively.");
            SetProgress(0.08f, "Could not load LV1.");
            yield break;
        }

        while (!loadOperation.isDone)
        {
            float loadProgress = Mathf.Lerp(0.08f, 0.35f, loadOperation.progress);
            SetProgress(loadProgress, "Santa is opening the delivery village...");
            yield return null;
        }

        Scene levelScene = SceneManager.GetSceneByName(destinationSceneName);
        if (!levelScene.IsValid() || !levelScene.isLoaded)
        {
            SetProgress(0.08f, "Oh snow! Santa's delivery village could not be opened.");
            Debug.LogError("SantaTrail startup: loaded LV1 scene is invalid.");
            yield break;
        }

        SceneManager.SetActiveScene(levelScene);
        yield return null;

        LoadingScreenUI levelLoadingScreen = null;
        float startupWait = 0f;
        const float startupWaitLimit = 10f;
        while (levelLoadingScreen == null && startupWait < startupWaitLimit)
        {
            levelLoadingScreen = FindFirstObjectByType<LoadingScreenUI>(
                FindObjectsInactive.Include
            );
            if (levelLoadingScreen != null)
            {
                break;
            }

            startupWait += 0.1f;
            yield return new WaitForSecondsRealtime(0.1f);
        }

        if (levelLoadingScreen == null)
        {
            Debug.LogWarning(
                "SantaTrail startup: LV1 has no LoadingScreenUI; revealing the scene after loading."
            );
            SetProgress(1f, "Santa's present route is ready!");
            yield return new WaitForSecondsRealtime(0.25f);
            SceneManager.SetActiveScene(levelScene);
            SceneManager.UnloadSceneAsync(gameObject.scene);
            yield break;
        }

        float levelWait = 0f;
        const float levelWaitLimit = 300f;
        while (!levelLoadingScreen.IsReady &&
               !levelLoadingScreen.HasError &&
               levelWait < levelWaitLimit)
        {
            float levelProgress = Mathf.Clamp01(levelLoadingScreen.CurrentProgress);
            float sharedProgress = Mathf.Lerp(0.35f, 0.98f, levelProgress);
            SetProgress(sharedProgress, "Santa is preparing rooftops for present delivery...");
            levelWait += 0.2f;
            yield return new WaitForSecondsRealtime(0.2f);
        }

        if (levelLoadingScreen.HasError)
        {
            SetProgress(
                0.98f,
                "Oh snow! Santa could not finish preparing the present route."
            );
            Debug.LogError("SantaTrail startup: LV1 reported a loading error.");
            yield break;
        }

        if (!levelLoadingScreen.IsReady)
        {
            SetProgress(0.98f, "Santa is still preparing the present route...");
            Debug.LogError("SantaTrail startup: timed out waiting for LV1 readiness.");
            yield break;
        }

        SetProgress(1f, "Santa's present route is ready! Opening...");
        yield return new WaitForSecondsRealtime(0.25f);
        SceneManager.SetActiveScene(levelScene);
        SceneManager.UnloadSceneAsync(gameObject.scene);
    }

    private IEnumerator PrepareLetterSceneAndOpen()
    {
        if (!string.Equals(destinationSceneName, "letter", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError(
                $"SantaTrail startup: letter preparation was requested for '{destinationSceneName}'."
            );
            SetProgress(0.92f, "The letter destination is not configured correctly.");
            yield break;
        }

        SetProgress(0.08f, "Santa is opening envelopes from the children...");
        SantaLetterPreloadSession.IsPreparing = true;

        AsyncOperation letterLoadOperation;
        try
        {
            letterLoadOperation = SceneManager.LoadSceneAsync(
                destinationSceneName,
                LoadSceneMode.Additive
            );
        }
        catch (Exception exception)
        {
            SantaLetterPreloadSession.IsPreparing = false;
            Debug.LogException(exception);
            SetProgress(0.08f, "Oh snow! Santa could not open the children's envelopes.");
            yield break;
        }

        if (letterLoadOperation == null)
        {
            SantaLetterPreloadSession.IsPreparing = false;
            Debug.LogError(
                $"SantaTrail startup: scene '{destinationSceneName}' could not be loaded additively."
            );
            SetProgress(0.08f, "Oh snow! Santa could not open the children's envelopes.");
            yield break;
        }

        while (!letterLoadOperation.isDone)
        {
            float loadProgress = Mathf.Lerp(0.08f, 0.35f, letterLoadOperation.progress);
            SetProgress(loadProgress, "Opening the children's envelopes...");
            yield return null;
        }

        Scene letterScene = SceneManager.GetSceneByName(destinationSceneName);
        if (!letterScene.IsValid() || !letterScene.isLoaded)
        {
            SantaLetterPreloadSession.IsPreparing = false;
            SetProgress(0.08f, "Oh snow! Santa could not open the children's envelopes.");
            Debug.LogError("SantaTrail startup: loaded letter scene is invalid.");
            yield break;
        }

        SceneManager.SetActiveScene(letterScene);
        yield return null;

        SantaLetterGameManager letterManager =
            FindFirstObjectByType<SantaLetterGameManager>();
        if (letterManager == null)
        {
            Debug.LogError("SantaTrail startup: SantaLetterGameManager was not found in letter scene.");
            yield return StartCoroutine(ReturnToMainPageAfterLetterFailure(
                letterScene,
                "The letter manager is missing from the letter scene."
            ));
            yield break;
        }

        SetProgress(0.4f, "Reading the children's wishes and preparing gift choices...");
        Task<bool> preparationTask = null;
        try
        {
            preparationTask = letterManager.PrepareNewDeliveryAsync();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }

        if (preparationTask == null)
        {
            yield return StartCoroutine(ReturnToMainPageAfterLetterFailure(
                letterScene,
                "The children's letter could not be prepared."
            ));
            yield break;
        }

        float preparationProgress = 0.4f;
        while (!preparationTask.IsCompleted)
        {
            preparationProgress = Mathf.Min(0.985f, preparationProgress + 0.012f);
            SetProgress(preparationProgress, "Reading each child's wishes and matching gift choices...");
            yield return new WaitForSecondsRealtime(0.2f);
        }

        bool prepared = false;
        if (preparationTask.IsFaulted)
        {
            Debug.LogException(preparationTask.Exception);
        }
        else if (!preparationTask.IsCanceled)
        {
            prepared = preparationTask.Result;
        }

        SantaLetterPreloadSession.IsPreparing = false;
        if (!prepared)
        {
            Debug.LogError("SantaTrail startup: letter preparation failed.");
            yield return StartCoroutine(ReturnToMainPageAfterLetterFailure(
                letterScene,
                "The children's letters could not be prepared. Returning to the main page..."
            ));
            yield break;
        }

        Debug.Log("SantaTrail startup: letter scene is prepared; revealing generated letter and choices.");
        SetProgress(1f, "The children's letters and gift choices are ready! Opening...");
        yield return new WaitForSecondsRealtime(0.25f);

        // Keep the prepared letter scene alive and remove only the loading
        // scene, leaving its generated UI and choices untouched.
        SceneManager.SetActiveScene(letterScene);
        SceneManager.UnloadSceneAsync(gameObject.scene);
    }

    private IEnumerator ReturnToMainPageAfterLetterFailure(
        Scene letterScene,
        string message
    )
    {
        SantaLetterPreloadSession.IsPreparing = false;
        SetProgress(0.94f, message);

        Scene loadingScene = gameObject.scene;
        if (loadingScene.IsValid() && loadingScene.isLoaded)
        {
            SceneManager.SetActiveScene(loadingScene);
        }

        if (letterScene.IsValid() && letterScene.isLoaded)
        {
            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(letterScene);
            if (unloadOperation != null)
            {
                while (!unloadOperation.isDone)
                {
                    yield return null;
                }
            }
        }

        yield return new WaitForSecondsRealtime(1f);

        AsyncOperation mainPageOperation = SceneManager.LoadSceneAsync(
            mainPageSceneName,
            LoadSceneMode.Single
        );
        if (mainPageOperation == null)
        {
            Debug.LogError(
                $"SantaTrail startup: could not return to '{mainPageSceneName}' after letter failure."
            );
        }
    }

    private bool WasContinueInputPressed()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null &&
            (mouse.leftButton.wasPressedThisFrame ||
             mouse.rightButton.wasPressedThisFrame ||
             mouse.middleButton.wasPressedThisFrame))
        {
            return true;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
        {
            return true;
        }

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
        {
            return true;
        }

        return false;
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
        // LV1 has a legacy loading canvas at sorting order 1000. Keep this
        // shared loading screen above it until LV1 has finished initializing.
        overlayCanvas.sortingOrder = 2000;

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
