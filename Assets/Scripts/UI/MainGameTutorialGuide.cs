using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// First-run, replayable walkthrough for the dashboard and present-packing
/// scenes. It is created at runtime so scene UI can keep evolving without
/// fragile inspector references.
/// </summary>
public sealed class MainGameTutorialGuide : MonoBehaviour
{
    private struct SelectableState
    {
        public bool interactable;
        public Navigation navigation;

        public SelectableState(Selectable selectable)
        {
            interactable = selectable.interactable;
            navigation = selectable.navigation;
        }
    }

    private enum GuidedAction
    {
        None,
        OpenLetter,
        SelectCorrectGift,
        PackPresent
    }

    [System.Serializable]
    public sealed class TutorialStep
    {
        [Tooltip("Short heading shown in the elf tip.")]
        public string title;
        [TextArea(3, 8)]
        [Tooltip("Explanation shown to the player.")]
        public string body;
        [Tooltip("Drag the UI object this step should point at here.")]
        public RectTransform target;
        [Tooltip("Fallback when no target is assigned. Enter the exact Hierarchy name.")]
        public string targetName;
        [Tooltip("Fine-tune this tip's position after automatic placement.")]
        public Vector2 bubbleOffset;
        [Tooltip("Extra width and height for this step's yellow highlight frame. Use negative values to shrink it.")]
        public Vector2 yellowBoxSizeOffset;

        public TutorialStep(string title, string body, string targetName = null)
        {
            this.title = title;
            this.body = body;
            this.targetName = targetName;
        }
    }

    private const string MainPageScene = "MainPageScene";
    private const string LetterScene = "letter";
    private const string SeenPrefix = "SantaTrail.Tutorial.Seen.";

    [Header("Display")]
    [Tooltip("Show this tutorial automatically the first time this scene is opened.")]
    [SerializeField] private bool showOnFirstVisit = true;
    [Tooltip("Fallback position when a tutorial page has no highlighted UI element.")]
    [SerializeField] private Vector2 cardPosition = Vector2.zero;
    [SerializeField] private Vector2 cardSize = new Vector2(460f, 250f);
    [Tooltip("Gap between the highlighted control and its elf tip bubble.")]
    [SerializeField] private float tipGap = 28f;
    [Tooltip("Extra padding around the yellow spotlight for each described UI element.")]
    [SerializeField] private float spotlightPadding = 12f;

    [Header("Colours")]
    [SerializeField] private Color dimmerColor =
        new Color(0.015f, 0.05f, 0.035f, 0.78f);
    [SerializeField] private Color cardColor =
        new Color(0.045f, 0.19f, 0.12f, 0.98f);
    [SerializeField] private Color titleColor =
        new Color(1f, 0.82f, 0.32f, 1f);
    [SerializeField] private Color bodyColor =
        new Color(1f, 0.96f, 0.84f, 1f);
    [SerializeField] private Color stepColor =
        new Color(0.70f, 0.90f, 0.72f, 1f);
    [SerializeField] private Color buttonColor =
        new Color(0.72f, 0.12f, 0.12f, 1f);

    [Header("Tutorial Steps")]
    [Tooltip("Reorder, add, or remove steps here. Drag a UI object into Target to point at it.")]
    [SerializeField] private List<TutorialStep> pages = new List<TutorialStep>();
    private GameObject overlay;
    private GameObject card;
    private RectTransform cardRect;
    private GameObject spotlight;
    private TMP_Text titleText;
    private TMP_Text bodyText;
    private TMP_Text stepText;
    private Button previousButton;
    private Button nextButton;
    private Button skipButton;
    private TMP_Text nextButtonText;
    private readonly Dictionary<Selectable, SelectableState>
        sceneSelectableStates =
            new Dictionary<Selectable, SelectableState>();
    private readonly TutorialStep guidedLayoutStep =
        new TutorialStep("", "");
    private Image[] inputBlockers;
    private Button requiredButton;
    private RectTransform guidedTarget;
    private GuidedAction guidedAction;
    private bool isGuidedActionMode;
    private bool isMainPageTourMode;
    private string sceneName;
    private int pageIndex;
    private bool hasQueuedScene;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= EnsureTutorialForLoadedScene;
        SceneManager.sceneLoaded += EnsureTutorialForLoadedScene;
        EnsureTutorialForLoadedScene(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void EnsureTutorialForLoadedScene(Scene activeScene, LoadSceneMode mode)
    {
        if (!IsTutorialScene(activeScene.name)) return;

        MainGameTutorialGuide existing = FindGuideInScene(activeScene);
        if (existing != null)
        {
            existing.QueueForActiveScene();
            return;
        }

        GameObject guideObject = new GameObject("Main Game Tutorial Guide");
        SceneManager.MoveGameObjectToScene(guideObject, activeScene);
        guideObject.AddComponent<MainGameTutorialGuide>();
    }

    private static MainGameTutorialGuide FindGuideInScene(Scene scene)
    {
        MainGameTutorialGuide[] guides =
            FindObjectsByType<MainGameTutorialGuide>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
        for (int index = 0; index < guides.Length; index++)
        {
            if (guides[index] != null && guides[index].gameObject.scene == scene)
            {
                return guides[index];
            }
        }

        return null;
    }

    private void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        QueueForActiveScene();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying && pages.Count == 0 &&
            IsTutorialScene(gameObject.scene.name))
        {
            BuildPages(gameObject.scene.name);
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        StopRestrictedTutorialMode();
    }

    private void OnSceneLoaded(Scene loadedScene, LoadSceneMode mode)
    {
        if (loadedScene == gameObject.scene ||
            SceneManager.GetActiveScene() == gameObject.scene)
        {
            QueueForActiveScene();
        }
    }

    private void OnActiveSceneChanged(Scene previous, Scene current)
    {
        if (current == gameObject.scene)
        {
            QueueForActiveScene();
        }
        else
        {
            DestroyOverlay();
        }
    }

    private void QueueForActiveScene()
    {
        if (hasQueuedScene)
        {
            return;
        }

        hasQueuedScene = true;
        StartCoroutine(PrepareForScene());
    }

    private IEnumerator PrepareForScene()
    {
        yield return null;
        yield return null;

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene != gameObject.scene ||
            !IsTutorialScene(activeScene.name))
        {
            hasQueuedScene = false;
            DestroyOverlay();
            yield break;
        }

        if (string.Equals(activeScene.name, LetterScene,
                System.StringComparison.OrdinalIgnoreCase))
        {
            yield return WaitForLetterSceneReady(activeScene);

            if (SceneManager.GetActiveScene() != gameObject.scene)
            {
                hasQueuedScene = false;
                DestroyOverlay();
                yield break;
            }
        }

        yield return new WaitForEndOfFrame();

        // The opening story owns the screen before the player reaches the
        // mission desk.  Do not let the dashboard tutorial appear above it.
        while (SantaTrailOpeningStory.IsShowing)
        {
            yield return null;
        }

        hasQueuedScene = false;

        sceneName = activeScene.name;
        BuildPages(sceneName);
        CreateOverlay();

        bool shouldShow = showOnFirstVisit &&
            PlayerPrefs.GetInt(SeenPrefix + sceneName, 0) == 0;

        if (shouldShow)
        {
            StartFirstRunGuide();
        }
        else
        {
            SetTutorialVisible(false);
        }
    }

    private IEnumerator WaitForLetterSceneReady(Scene letterScene)
    {
        // The loading scene makes the letter scene active while it is still
        // generating the first child and gift choices. Keep the tutorial out
        // of the way until that work is complete and the loading canvas has
        // actually left the screen.
        while (SceneManager.GetActiveScene() == letterScene &&
               (SantaLetterPreloadSession.IsPreparing ||
                IsSceneLoaded("LoadingScene")))
        {
            yield return null;
        }

        SantaLetterGameManager letterManager = FindLetterManager(letterScene);
        while (SceneManager.GetActiveScene() == letterScene &&
               letterManager != null &&
               !letterManager.HasActiveDelivery)
        {
            yield return null;
        }
    }

    private static bool IsSceneLoaded(string candidate)
    {
        Scene scene = SceneManager.GetSceneByName(candidate);
        return scene.IsValid() && scene.isLoaded;
    }

    private static SantaLetterGameManager FindLetterManager(Scene scene)
    {
        SantaLetterGameManager[] managers =
            FindObjectsByType<SantaLetterGameManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        for (int index = 0; index < managers.Length; index++)
        {
            if (managers[index] != null && managers[index].gameObject.scene == scene)
            {
                return managers[index];
            }
        }

        return null;
    }

    private static bool IsTutorialScene(string candidate)
    {
        return string.Equals(candidate, MainPageScene,
                   System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidate, LetterScene,
                   System.StringComparison.OrdinalIgnoreCase);
    }

    private void BuildPages(string currentScene)
    {
        if (pages.Count > 0)
        {
            return;
        }

        pages.Clear();

        if (string.Equals(currentScene, MainPageScene,
                System.StringComparison.OrdinalIgnoreCase))
        {
            pages.Add(new TutorialStep(
                "Santa's Elf Handbook",
                "Welcome, helper! You are Santa's trusted elf: read each child's " +
                "wish, pack the right present, and guide the drone safely to their home. " +
                "This is your workshop dashboard. Use NEXT to take the guided tour."
            ));
            pages.Add(new TutorialStep(
                "Elf workshop stats",
                "Magic is the score your good work has earned. Presents Delivered " +
                "counts successful drops. Christmas Spirit fills as you help more " +
                "children. Altitude, Min Alt, Max Alt, and the meter show how " +
                "carefully you flew Santa's delivery drone. The yellow frame points " +
                "to your Magic total.",
                "MagicAmount"
            ));
            pages.Add(new TutorialStep(
                "Check Santa's flight ledger",
                "Each row records a delivery date, mission, score, flight time, " +
                "and result. ALL opens every record; PASSED celebrates completed " +
                "missions; IN PROGRESS / FAILED helps you find work still waiting.",
                "FlightLogScrollView"
            ));
            pages.Add(new TutorialStep(
                "Your next elf task",
                "Press the letter envelope to open a child's wish. Read the clues, " +
                "choose the best gift from the workshop, pack it, then launch the " +
                "drone for a careful Santa delivery. The yellow frame shows the " +
                "first button to click when you are ready.",
                "Letter"
            ));
        }
        else
        {
            pages.Add(new TutorialStep(
                "Read the child's wish",
                "Every letter is a small puzzle from Santa's list. Read it slowly, " +
                "look for favourite colours, activities, or needs, and use the hint " +
                "when you need an extra elf-sized nudge. The frame marks the letter."
                , "letter"
            ));
            pages.Add(new TutorialStep(
                "Choose from the toy shelf",
                "Gift buttons 1–4 are the presents available in your workshop. " +
                "Pick the one that best matches the letter. The right choice helps " +
                "Santa make this delivery extra special.",
                "GiftContainer"
            ));
            pages.Add(new TutorialStep(
                "Keep the sleigh list moving",
                "Mission / Delivery tells you how many children are in this run. " +
                "Progress shows where you are in the sequence. Complete each wish " +
                "to prepare the next drone delivery for Santa.",
                "MissionPanel"
            ));
            pages.Add(new TutorialStep(
                "Pack it and fly it",
                "PACK PRESENT confirms your choice and prepares the drone mission. " +
                "If you picked the wrong gift, RETRY lets you study the clues again. " +
                "Once packed, follow the prompt and help Santa deliver it safely. " +
                "This is the final button to click after choosing a gift.",
                "PackPresentButton"
            ));
        }
    }

    private void CreateOverlay()
    {
        DestroyOverlay();

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem),
                typeof(StandaloneInputModule));
        }

        overlay = new GameObject("Tutorial Overlay", typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        CanvasScaler scaler = overlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        Image dimmer = CreateImage("Dimmer", overlay.transform, dimmerColor);
        Stretch(dimmer.rectTransform);
        dimmer.raycastTarget = false;

        inputBlockers = new Image[4];
        for (int index = 0; index < inputBlockers.Length; index++)
        {
            Image blocker = CreateImage(
                "Input Blocker " + (index + 1),
                overlay.transform,
                new Color(0f, 0f, 0f, 0.001f));
            blocker.raycastTarget = true;
            blocker.gameObject.SetActive(false);
            inputBlockers[index] = blocker;
        }

        spotlight = CreateSpotlight(overlay.transform);

        card = new GameObject("Tutorial Card", typeof(RectTransform),
            typeof(Image));
        card.transform.SetParent(overlay.transform, false);
        Image cardImage = card.GetComponent<Image>();
        cardImage.color = cardColor;
        cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = cardPosition;
        cardRect.sizeDelta = cardSize;

        titleText = CreateText("Title", card.transform, 28, TextAnchor.UpperLeft,
            titleColor);
        SetRect(titleText.rectTransform, new Vector2(26f, -62f),
            new Vector2(-26f, -22f), new Vector2(0f, 1f), new Vector2(1f, 1f));

        bodyText = CreateText("Body", card.transform, 19, TextAnchor.UpperLeft,
            bodyColor);
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.overflowMode = TextOverflowModes.Overflow;
        SetRect(bodyText.rectTransform, new Vector2(26f, 62f),
            new Vector2(-26f, -76f), Vector2.zero, Vector2.one);

        stepText = CreateText("Step", card.transform, 16, TextAnchor.MiddleLeft,
            stepColor);
        SetRect(stepText.rectTransform, new Vector2(26f, 18f),
            new Vector2(115f, 50f), Vector2.zero, Vector2.zero);

        previousButton = CreateButton("Back", card.transform, "BACK", new Vector2(-320f, 18f), buttonColor);
        nextButton = CreateButton("Next", card.transform, "NEXT", new Vector2(-165f, 18f), buttonColor);
        nextButtonText = nextButton.GetComponentInChildren<TMP_Text>();
        skipButton = CreateButton("Skip", card.transform, "SKIP", new Vector2(-10f, 18f), buttonColor);
        ConfigureCardButton(previousButton, new Vector2(-320f, 18f));
        ConfigureCardButton(nextButton, new Vector2(-165f, 18f));
        ConfigureCardButton(skipButton, new Vector2(-10f, 18f));
        previousButton.onClick.AddListener(PreviousPage);
        nextButton.onClick.AddListener(NextPage);
        skipButton.onClick.AddListener(CloseTutorial);

        pageIndex = 0;
    }

    public void OpenTutorial()
    {
        if (overlay == null)
        {
            QueueForActiveScene();
            return;
        }

        sceneName = gameObject.scene.name;
        StartFirstRunGuide();
    }

    private void StartFirstRunGuide()
    {
        StopRestrictedTutorialMode();

        if (string.Equals(
                sceneName,
                MainPageScene,
                System.StringComparison.OrdinalIgnoreCase) &&
            pages.Count > 1)
        {
            StartMainPageTour();
            return;
        }

        isGuidedActionMode = true;

        bool started = string.Equals(
            sceneName,
            MainPageScene,
            System.StringComparison.OrdinalIgnoreCase)
            ? TryShowGuidedAction(GuidedAction.OpenLetter)
            : TryShowGuidedAction(GuidedAction.SelectCorrectGift);

        if (started)
        {
            SetTutorialVisible(true);
            return;
        }

        // Do not trap the player if a required scene reference was removed.
        // The existing page tutorial remains a usable fallback and the warning
        // identifies which auto-wired target needs repairing.
        Debug.LogWarning(
            "MainGameTutorialGuide: could not find the required first-run " +
            $"control in scene '{sceneName}'. Showing the page tutorial instead.");
        ShowPageTutorialFallback();
    }

    private void StartMainPageTour()
    {
        isMainPageTourMode = true;
        pageIndex = 0;
        LockSceneInputTo(null);
        SetTutorialVisible(true);
    }

    private void StartOpenLetterAction()
    {
        isMainPageTourMode = false;
        isGuidedActionMode = true;

        if (TryShowGuidedAction(GuidedAction.OpenLetter))
        {
            SetTutorialVisible(true);
            return;
        }

        Debug.LogWarning(
            "MainGameTutorialGuide: the Letter button was not found after " +
            "the Main Page tour. Showing the page tutorial instead.");
        ShowPageTutorialFallback();
    }

    private void ShowPageTutorialFallback()
    {
        StopRestrictedTutorialMode();
        pageIndex = 0;
        SetTutorialVisible(true);
    }

    private bool TryShowGuidedAction(GuidedAction action)
    {
        Button targetButton = null;
        string title = "";
        string body = "";
        string progress = "";

        if (action == GuidedAction.OpenLetter)
        {
            targetButton = FindSceneButton("Letter");
            title = "Start your first Santa task";
            body = "Click the highlighted letter icon to open a child's wish. " +
                "Other controls stay locked until you follow this step.";
            progress = pages.Count > 1
                ? $"{pages.Count} / {pages.Count}"
                : "1 / 1";
        }
        else
        {
            SantaLetterGameManager manager =
                FindLetterManager(gameObject.scene);

            if (action == GuidedAction.SelectCorrectGift)
            {
                GiftButton correctGift;
                if (manager != null &&
                    manager.TryGetCorrectGiftButton(out correctGift) &&
                    correctGift != null)
                {
                    targetButton = correctGift.GetComponent<Button>();
                }

                title = "Choose the correct present";
                body = "Read the child's clues, then click the highlighted toy. " +
                    "This guided delivery only allows the correct choice.";
                progress = "1 / 2";
            }
            else if (action == GuidedAction.PackPresent)
            {
                targetButton = manager != null
                    ? manager.packPresentButton
                    : null;
                title = "Pack the present";
                body = "Great match! Now click PACK PRESENT to prepare it for " +
                    "this child and finish the guided lesson.";
                progress = "2 / 2";
            }
        }

        if (targetButton == null || !targetButton.gameObject.activeInHierarchy)
        {
            return false;
        }

        UnsubscribeRequiredButton();
        guidedAction = action;
        requiredButton = targetButton;
        guidedTarget = targetButton.transform as RectTransform;
        guidedLayoutStep.target = guidedTarget;
        titleText.text = title;
        bodyText.text = body;
        stepText.text = progress;

        SetNavigationButtonsVisible(false);
        requiredButton.onClick.AddListener(OnRequiredActionCompleted);
        LockSceneInputTo(requiredButton);
        ResizeTipBubble();
        Canvas.ForceUpdateCanvases();
        RefreshGuidedLayout();
        return true;
    }

    private Button FindSceneButton(string objectName)
    {
        Button[] buttons = FindObjectsByType<Button>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int index = 0; index < buttons.Length; index++)
        {
            Button candidate = buttons[index];
            if (candidate != null &&
                candidate.gameObject.scene == gameObject.scene &&
                candidate.gameObject.name == objectName)
            {
                return candidate;
            }
        }

        return null;
    }

    private void OnRequiredActionCompleted()
    {
        GuidedAction completedAction = guidedAction;
        UnsubscribeRequiredButton();

        if (completedAction == GuidedAction.SelectCorrectGift)
        {
            if (!TryShowGuidedAction(GuidedAction.PackPresent))
            {
                Debug.LogWarning(
                    "MainGameTutorialGuide: PackPresentButton was not found; " +
                    "ending the guide without locking the player.");
                CompleteFirstRunGuide();
            }

            return;
        }

        CompleteFirstRunGuide();
    }

    private void CompleteFirstRunGuide()
    {
        PlayerPrefs.SetInt(SeenPrefix + sceneName, 1);
        PlayerPrefs.Save();
        StopRestrictedTutorialMode();
        SetTutorialVisible(false);
    }

    private void StopRestrictedTutorialMode()
    {
        UnsubscribeRequiredButton();
        RestoreSceneInput();
        isGuidedActionMode = false;
        isMainPageTourMode = false;
        guidedAction = GuidedAction.None;
        guidedTarget = null;
        guidedLayoutStep.target = null;
        SetInputBlockersActive(false);
        SetNavigationButtonsVisible(true);
    }

    private void UnsubscribeRequiredButton()
    {
        if (requiredButton != null)
        {
            requiredButton.onClick.RemoveListener(OnRequiredActionCompleted);
            requiredButton = null;
        }
    }

    private void LockSceneInputTo(Button allowedButton)
    {
        if (sceneSelectableStates.Count == 0)
        {
            Selectable[] selectables = FindObjectsByType<Selectable>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int index = 0; index < selectables.Length; index++)
            {
                Selectable selectable = selectables[index];
                if (selectable != null &&
                    selectable.gameObject.scene == gameObject.scene &&
                    (overlay == null ||
                     !selectable.transform.IsChildOf(overlay.transform)))
                {
                    sceneSelectableStates[selectable] =
                        new SelectableState(selectable);
                }
            }
        }

        foreach (KeyValuePair<Selectable, SelectableState> pair in
                 sceneSelectableStates)
        {
            if (pair.Key != null)
            {
                // Keep the normal visual state. Pointer input is blocked by
                // the overlay, while Navigation.None prevents focus from
                // escaping to dashboard controls with a keyboard or gamepad.
                Navigation lockedNavigation = pair.Key.navigation;
                lockedNavigation.mode = Navigation.Mode.None;
                pair.Key.navigation = lockedNavigation;
            }
        }

        // Pack Present becomes interactable in the same click that advances
        // the guide, so the allowed action always wins over its initial state.
        if (allowedButton == null)
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            return;
        }

        allowedButton.interactable = true;

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(allowedButton.gameObject);
        }
    }

    private void RestoreSceneInput()
    {
        foreach (KeyValuePair<Selectable, SelectableState> pair in
                 sceneSelectableStates)
        {
            if (pair.Key != null)
            {
                pair.Key.interactable = pair.Value.interactable;
                pair.Key.navigation = pair.Value.navigation;
            }
        }

        sceneSelectableStates.Clear();
    }

    private void SetNavigationButtonsVisible(bool visible)
    {
        if (previousButton != null)
        {
            previousButton.gameObject.SetActive(visible && pageIndex > 0);
        }

        if (nextButton != null)
        {
            nextButton.gameObject.SetActive(visible);
        }

        if (skipButton != null)
        {
            skipButton.gameObject.SetActive(visible);
        }
    }

    private void SetTutorialVisible(bool visible)
    {
        if (card == null || overlay == null)
        {
            return;
        }

        card.SetActive(visible);
        Image dimmer = overlay.transform.Find("Dimmer")?.GetComponent<Image>();
        if (dimmer != null)
        {
            dimmer.enabled = visible;
            // The dark layer is visual only: highlighted scene controls remain
            // clickable while the elf learns what each one does.
            dimmer.raycastTarget = false;
        }

        if (visible)
        {
            if (isGuidedActionMode)
            {
                SetNavigationButtonsVisible(false);
                SetInputBlockersActive(true);
                RefreshGuidedLayout();
            }
            else if (isMainPageTourMode)
            {
                SetNavigationButtonsVisible(true);
                skipButton.gameObject.SetActive(false);
                ShowPage();
                ShowFullScreenInputBlocker();
            }
            else
            {
                SetNavigationButtonsVisible(true);
                ShowPage();
            }
        }
        else if (spotlight != null)
        {
            spotlight.SetActive(false);
            SetInputBlockersActive(false);
        }
    }

    private void ShowPage()
    {
        if (pages.Count == 0 || titleText == null)
        {
            return;
        }

        pageIndex = Mathf.Clamp(pageIndex, 0, pages.Count - 1);
        TutorialStep page = pages[pageIndex];
        titleText.text = page.title;
        bodyText.text = page.body;
        stepText.text = $"{pageIndex + 1} / {pages.Count}";
        previousButton.gameObject.SetActive(pageIndex > 0);
        nextButtonText.text = pageIndex == pages.Count - 1 ? "DONE" : "NEXT";
        ResizeTipBubble();
        Canvas.ForceUpdateCanvases();
        HighlightTarget(page);
        PositionTipBubble(page);
    }

    private void NextPage()
    {
        if (isMainPageTourMode)
        {
            // The last configured Main Page entry explains how to begin the
            // game. Replace that passive page with the required letter click.
            if (pageIndex >= pages.Count - 2)
            {
                StartOpenLetterAction();
                return;
            }

            pageIndex++;
            ShowPage();
            return;
        }

        if (pageIndex >= pages.Count - 1)
        {
            CloseTutorial();
            return;
        }

        pageIndex++;
        ShowPage();
    }

    private void PreviousPage()
    {
        pageIndex = Mathf.Max(0, pageIndex - 1);
        ShowPage();
    }

    private void CloseTutorial()
    {
        PlayerPrefs.SetInt(SeenPrefix + sceneName, 1);
        PlayerPrefs.Save();
        StopRestrictedTutorialMode();
        SetTutorialVisible(false);
    }

    /// <summary>Lets a designer retest the first-visit flow from the component menu.</summary>
    [ContextMenu("Reset First-Visit Tutorial")]
    public void ResetFirstVisitTutorial()
    {
        string activeName = SceneManager.GetActiveScene().name;
        PlayerPrefs.DeleteKey(SeenPrefix + activeName);
        PlayerPrefs.Save();
    }

    [ContextMenu("Restore Default Tutorial Steps")]
    public void RestoreDefaultTutorialSteps()
    {
        pages.Clear();
        BuildPages(SceneManager.GetActiveScene().name);
    }

    private void DestroyOverlay()
    {
        StopRestrictedTutorialMode();

        if (overlay != null)
        {
            Destroy(overlay);
            overlay = null;
            card = null;
            cardRect = null;
            spotlight = null;
            inputBlockers = null;
        }

    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(Image));
        item.transform.SetParent(parent, false);
        Image image = item.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private GameObject CreateSpotlight(Transform parent)
    {
        GameObject container = new GameObject("Tutorial Spotlight", typeof(RectTransform));
        container.transform.SetParent(parent, false);
        for (int index = 0; index < 4; index++)
        {
            Image edge = CreateImage("Edge", container.transform, titleColor);
            edge.raycastTarget = false;
        }

        container.SetActive(false);
        return container;
    }

    private void HighlightTarget(TutorialStep page)
    {
        if (spotlight == null)
        {
            return;
        }

        RectTransform target = FindTarget(page);
        if (target == null)
        {
            spotlight.SetActive(false);
            return;
        }

        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Camera targetCamera = GetTargetCamera(target);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        Vector2 lowerLeft;
        Vector2 upperRight;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            overlayRect, RectTransformUtility.WorldToScreenPoint(targetCamera, corners[0]),
            null, out lowerLeft);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            overlayRect, RectTransformUtility.WorldToScreenPoint(targetCamera, corners[2]),
            null, out upperRight);

        float left = Mathf.Min(lowerLeft.x, upperRight.x) - spotlightPadding;
        float right = Mathf.Max(lowerLeft.x, upperRight.x) + spotlightPadding;
        float bottom = Mathf.Min(lowerLeft.y, upperRight.y) - spotlightPadding;
        float top = Mathf.Max(lowerLeft.y, upperRight.y) + spotlightPadding;
        float extraWidth = page.yellowBoxSizeOffset.x * .5f;
        float extraHeight = page.yellowBoxSizeOffset.y * .5f;
        left -= extraWidth;
        right += extraWidth;
        bottom -= extraHeight;
        top += extraHeight;
        const float thickness = 5f;
        RectTransform[] edges = spotlight.GetComponentsInChildren<RectTransform>(true);
        if (edges.Length < 5)
        {
            return;
        }

        SetSpotlightEdge(edges[1], new Vector2((left + right) * .5f, top),
            new Vector2(right - left, thickness));
        SetSpotlightEdge(edges[2], new Vector2((left + right) * .5f, bottom),
            new Vector2(right - left, thickness));
        SetSpotlightEdge(edges[3], new Vector2(left, (bottom + top) * .5f),
            new Vector2(thickness, top - bottom));
        SetSpotlightEdge(edges[4], new Vector2(right, (bottom + top) * .5f),
            new Vector2(thickness, top - bottom));
        spotlight.SetActive(true);
    }

    private void LateUpdate()
    {
        if (isGuidedActionMode &&
            overlay != null &&
            card != null &&
            card.activeSelf)
        {
            RefreshGuidedLayout();
        }
    }

    private void RefreshGuidedLayout()
    {
        if (guidedTarget == null)
        {
            return;
        }

        HighlightTarget(guidedLayoutStep);
        PositionTipBubble(guidedLayoutStep);
        PositionInputBlockers(guidedTarget);
    }

    private void SetInputBlockersActive(bool active)
    {
        if (inputBlockers == null)
        {
            return;
        }

        for (int index = 0; index < inputBlockers.Length; index++)
        {
            if (inputBlockers[index] != null)
            {
                inputBlockers[index].gameObject.SetActive(active);
            }
        }
    }

    private void ShowFullScreenInputBlocker()
    {
        if (inputBlockers == null || inputBlockers.Length == 0)
        {
            return;
        }

        SetInputBlockersActive(false);
        Stretch(inputBlockers[0].rectTransform);
        inputBlockers[0].gameObject.SetActive(true);
    }

    private void PositionInputBlockers(RectTransform target)
    {
        if (overlay == null ||
            inputBlockers == null ||
            inputBlockers.Length != 4 ||
            target == null)
        {
            return;
        }

        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Camera targetCamera = GetTargetCamera(target);
        Vector2 lowerLeft;
        Vector2 upperRight;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            overlayRect,
            RectTransformUtility.WorldToScreenPoint(targetCamera, corners[0]),
            null,
            out lowerLeft);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            overlayRect,
            RectTransformUtility.WorldToScreenPoint(targetCamera, corners[2]),
            null,
            out upperRight);

        float left = Mathf.Clamp(
            Mathf.Min(lowerLeft.x, upperRight.x),
            overlayRect.rect.xMin,
            overlayRect.rect.xMax);
        float right = Mathf.Clamp(
            Mathf.Max(lowerLeft.x, upperRight.x),
            overlayRect.rect.xMin,
            overlayRect.rect.xMax);
        float bottom = Mathf.Clamp(
            Mathf.Min(lowerLeft.y, upperRight.y),
            overlayRect.rect.yMin,
            overlayRect.rect.yMax);
        float top = Mathf.Clamp(
            Mathf.Max(lowerLeft.y, upperRight.y),
            overlayRect.rect.yMin,
            overlayRect.rect.yMax);

        SetBlockerRect(inputBlockers[0].rectTransform,
            overlayRect.rect.xMin, top,
            overlayRect.rect.xMax, overlayRect.rect.yMax);
        SetBlockerRect(inputBlockers[1].rectTransform,
            overlayRect.rect.xMin, overlayRect.rect.yMin,
            overlayRect.rect.xMax, bottom);
        SetBlockerRect(inputBlockers[2].rectTransform,
            overlayRect.rect.xMin, bottom, left, top);
        SetBlockerRect(inputBlockers[3].rectTransform,
            right, bottom, overlayRect.rect.xMax, top);
    }

    private static void SetBlockerRect(
        RectTransform blocker,
        float left,
        float bottom,
        float right,
        float top)
    {
        blocker.anchorMin = new Vector2(.5f, .5f);
        blocker.anchorMax = new Vector2(.5f, .5f);
        blocker.pivot = new Vector2(.5f, .5f);
        blocker.anchoredPosition = new Vector2(
            (left + right) * .5f,
            (bottom + top) * .5f);
        blocker.sizeDelta = new Vector2(
            Mathf.Max(0f, right - left),
            Mathf.Max(0f, top - bottom));
    }

    private void PositionTipBubble(TutorialStep page)
    {
        if (cardRect == null || overlay == null)
        {
            return;
        }

        RectTransform target = FindTarget(page);
        if (target == null)
        {
            cardRect.anchoredPosition = cardPosition;
            return;
        }

        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Camera targetCamera = GetTargetCamera(target);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        Vector2 lowerLeft;
        Vector2 upperRight;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            overlayRect, RectTransformUtility.WorldToScreenPoint(targetCamera, corners[0]),
            null, out lowerLeft);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            overlayRect, RectTransformUtility.WorldToScreenPoint(targetCamera, corners[2]),
            null, out upperRight);

        float halfWidth = cardRect.rect.width * .5f;
        float halfHeight = cardRect.rect.height * .5f;
        float desiredX = upperRight.x + tipGap + halfWidth;
        if (desiredX + halfWidth > overlayRect.rect.xMax - 16f)
        {
            desiredX = lowerLeft.x - tipGap - halfWidth;
        }

        if (desiredX - halfWidth < overlayRect.rect.xMin + 16f)
        {
            desiredX = (lowerLeft.x + upperRight.x) * .5f;
        }

        float desiredY = (lowerLeft.y + upperRight.y) * .5f;
        desiredY = Mathf.Clamp(desiredY, overlayRect.rect.yMin + halfHeight + 16f,
            overlayRect.rect.yMax - halfHeight - 16f);
        desiredX += page.bubbleOffset.x;
        desiredY += page.bubbleOffset.y;
        desiredX = Mathf.Clamp(desiredX, overlayRect.rect.xMin + halfWidth + 16f,
            overlayRect.rect.xMax - halfWidth - 16f);
        desiredY = Mathf.Clamp(desiredY, overlayRect.rect.yMin + halfHeight + 16f,
            overlayRect.rect.yMax - halfHeight - 16f);
        cardRect.anchoredPosition = new Vector2(desiredX, desiredY);
    }

    private void ResizeTipBubble()
    {
        if (cardRect == null || titleText == null || bodyText == null)
        {
            return;
        }

        float textWidth = Mathf.Max(100f, cardSize.x - 52f);
        float titleHeight = titleText.GetPreferredValues(titleText.text, textWidth, 0f).y;
        float bodyHeight = bodyText.GetPreferredValues(bodyText.text, textWidth, 0f).y;
        // Top and side padding plus the fixed footer which holds step/back/next.
        float desiredHeight = Mathf.Clamp(titleHeight + bodyHeight + 128f, 180f, 360f);
        cardRect.sizeDelta = new Vector2(cardSize.x, desiredHeight);
    }

    private RectTransform FindTarget(TutorialStep page)
    {
        if (page == null)
        {
            return null;
        }

        if (page.target != null && page.target.gameObject.scene == gameObject.scene)
        {
            return page.target;
        }

        if (string.IsNullOrEmpty(page.targetName))
        {
            return null;
        }

        RectTransform[] targets = FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int index = 0; index < targets.Length; index++)
        {
            RectTransform target = targets[index];
            if (target.gameObject.scene == gameObject.scene &&
                target.gameObject.name == page.targetName)
            {
                return target;
            }
        }
        return null;
    }

    private static Camera GetTargetCamera(RectTransform target)
    {
        Canvas targetCanvas = target != null
            ? target.GetComponentInParent<Canvas>()
            : null;

        if (targetCanvas == null ||
            targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return targetCanvas.worldCamera;
    }

    private static void SetSpotlightEdge(RectTransform edge, Vector2 position,
        Vector2 size)
    {
        edge.anchorMin = new Vector2(.5f, .5f);
        edge.anchorMax = new Vector2(.5f, .5f);
        edge.pivot = new Vector2(.5f, .5f);
        edge.anchoredPosition = position;
        edge.sizeDelta = size;
    }

    private static void ConfigureCardButton(Button button, Vector2 position)
    {
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(140f, 42f);
    }

    private static TMP_Text CreateText(string name, Transform parent, int size,
        TextAnchor alignment, Color color)
    {
        GameObject item = new GameObject(
            name,
            typeof(RectTransform),
            typeof(TextMeshProUGUI)
        );
        item.transform.SetParent(parent, false);
        TMP_Text text = item.GetComponent<TMP_Text>();
        text.font = ResolveTutorialFont();
        text.fontSize = size;
        text.alignment = ToTmpAlignment(alignment);
        text.color = color;
        return text;
    }

    private static Button CreateButton(string name, Transform parent,
        string label, Vector2 anchoredPosition, Color backgroundColor)
    {
        GameObject item = new GameObject(name, typeof(RectTransform),
            typeof(Image), typeof(Button));
        item.transform.SetParent(parent, false);
        Image image = item.GetComponent<Image>();
        image.color = backgroundColor;
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(145f, 52f);

        TMP_Text text = CreateText("Label", item.transform, 20,
            TextAnchor.MiddleCenter, Color.white);
        Stretch(text.rectTransform);
        text.text = label;
        return item.GetComponent<Button>();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetRect(RectTransform rect, Vector2 offsetMin,
        Vector2 offsetMax, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static TMP_FontAsset ResolveTutorialFont()
    {
        if (TMP_Settings.defaultFontAsset != null)
        {
            return TMP_Settings.defaultFontAsset;
        }

        TMP_Text existingText = FindFirstObjectByType<TMP_Text>(
            FindObjectsInactive.Include
        );
        return existingText != null ? existingText.font : null;
    }

    private static TextAlignmentOptions ToTmpAlignment(TextAnchor alignment)
    {
        switch (alignment)
        {
            case TextAnchor.UpperLeft:
                return TextAlignmentOptions.TopLeft;
            case TextAnchor.MiddleLeft:
                return TextAlignmentOptions.MidlineLeft;
            case TextAnchor.MiddleCenter:
                return TextAlignmentOptions.Center;
            default:
                return TextAlignmentOptions.TopLeft;
        }
    }
}
