using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[AddComponentMenu("SantaTrail/UI/Pause Menu")]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10000)]
public sealed class PauseMenuController : MonoBehaviour
{
    public const string DefaultPrefabResource = "SantaTrailPauseMenu";

    [Header("UI References")]
    [Tooltip("The menu Canvas. Keep the complete menu under this component's root so it persists across scenes.")]
    [SerializeField] private Canvas menuCanvas;
    [SerializeField] private GameObject popup;
    [SerializeField] private RectTransform menuPanel;
    [Tooltip("Optional on-screen shortcut. Its click action is connected automatically.")]
    [SerializeField] private Button pauseButton;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Controls")]
    [SerializeField] private bool enableKeyboardShortcut = true;
    [SerializeField] private Key pauseKey = Key.Escape;
    [SerializeField] private bool showPauseButton = true;
    [SerializeField] private bool pauseAudio = true;
    [Tooltip("Pause the simulator process started by SantaTrail while this menu is open.")]
    [SerializeField] private bool pauseSimulator = true;

    [Header("Scenes")]
    [SerializeField] private string mainMenuSceneName = "MainPageScene";
    [Tooltip("Configured flight levels are included automatically. Add other gameplay scenes here.")]
    [SerializeField] private string[] additionalGameplayScenes = { "letter" };
    [SerializeField] private bool allowPauseInAllScenes;

    [Header("Status Messages")]
    [TextArea(2, 3)]
    [SerializeField] private string pausedMessage = "Take a breather. The presents can wait.";
    [TextArea(2, 3)]
    [SerializeField] private string externalFlightMessage = "Game paused. Your external flight controller is still running.";
    [TextArea(2, 3)]
    [SerializeField] private string resumeFailedMessage = "The flight could not resume. Try again or restart the mission.";
    [TextArea(2, 3)]
    [SerializeField] private string sceneUnavailableMessage = "This destination is unavailable. Resume the mission or exit the game.";

    public bool HasConfiguredUI => menuCanvas != null && popup != null &&
        resumeButton != null && statusText != null;
    private static PauseMenuController instance;
    public static bool IsPaused { get; private set; }

    private GameObject fallbackEventSystem;
    private GameObject previousSelection;
    private bool gameplayScene;
    private bool changingScene;
    private bool simulatorPaused;
    private float previousTimeScale;
    private bool previousAudioPause;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLock;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        instance = null;
        IsPaused = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        GameObject prefab = Resources.Load<GameObject>(DefaultPrefabResource);
        if (prefab == null)
        {
            Debug.LogError("Pause menu prefab is missing from Resources/" + DefaultPrefabResource + ".");
            return;
        }
        Instantiate(prefab);
    }

    private void Awake()
    {
        if (!HasConfiguredUI)
        {
            Debug.LogError("Pause Menu: assign Canvas, Popup, Resume Button, and Status Text in the Inspector.", this);
            enabled = false;
            return;
        }
        if (instance != null && instance != this)
        {
            // A menu placed in a newly loaded scene takes precedence over the
            // persistent default, so its Inspector overrides actually apply.
            instance.enabled = false;
            Destroy(instance.gameObject);
        }
        instance = this;
        // The menu owns a Canvas and must persist as one root object.
        if (transform.parent != null) transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);
        BindButtons(true);
        menuCanvas.enabled = enabled;
        popup.SetActive(false);
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // LoadingScene preloads the letter scene additively before activating it.
        if (scene != SceneManager.GetActiveScene()) return;
        RefreshForScene(scene);
    }

    private void OnActiveSceneChanged(Scene previous, Scene current)
    {
        RefreshForScene(current);
    }

    private void RefreshForScene(Scene scene)
    {
        if (!isActiveAndEnabled) return;
        RestoreGame();
        changingScene = false;
        gameplayScene = allowPauseInAllScenes ||
            (additionalGameplayScenes != null && Array.Exists(additionalGameplayScenes,
                name => string.Equals(name, scene.name, StringComparison.OrdinalIgnoreCase))) ||
            LevelConfigLoader.TryFindLevel(scene.name, out _);
        SetPauseButtonVisible(gameplayScene);
        EnsureEventSystem();
    }

    private void Update()
    {
        if (gameplayScene && !changingScene && enableKeyboardShortcut &&
            pauseKey != Key.None && Keyboard.current != null &&
            Keyboard.current[pauseKey].wasPressedThisFrame)
        {
            TogglePause();
        }
    }

    public void TogglePause()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    public void Pause()
    {
        if (IsPaused || changingScene || !gameplayScene) return;
        EnsureEventSystem();
        previousTimeScale = Time.timeScale;
        previousAudioPause = AudioListener.pause;
        previousCursorVisible = Cursor.visible;
        previousCursorLock = Cursor.lockState;
        previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        IsPaused = true;
        Time.timeScale = 0f;
        if (pauseAudio) AudioListener.pause = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        simulatorPaused = pauseSimulator && AutoArduPilotOnPlay.TryPauseFlight(true);
        bool externalFlight = !simulatorPaused && MAVLinkReceiver.Active != null &&
            MAVLinkReceiver.Active.HasRecentTelemetry;
        statusText.text = externalFlight
            ? externalFlightMessage
            : pausedMessage;
        FindFirstObjectByType<QGroundControlOverlayController>()?.ReturnQGroundControlToBackground();
        popup.SetActive(true);
        SetPauseButtonVisible(false);
        resumeButton.Select();
    }

    public void Resume()
    {
        if (!IsPaused || changingScene) return;
        if (simulatorPaused && !AutoArduPilotOnPlay.TryPauseFlight(false))
        {
            statusText.text = resumeFailedMessage;
            return;
        }
        simulatorPaused = false;
        RestoreGame();
        // A first-run download may have finished while the popup was open.
        AutoArduPilotOnPlay.RequestLaunchForScene(SceneManager.GetActiveScene().name);
    }

    public void Restart()
    {
        ChangeScene(SceneManager.GetActiveScene().name);
    }

    public void BackToMenu()
    {
        ChangeScene(mainMenuSceneName);
    }

    private void ChangeScene(string destination)
    {
        if (!IsPaused || changingScene) return;
        if (!Application.CanStreamedLevelBeLoaded(destination))
        {
            statusText.text = sceneUnavailableMessage;
            return;
        }
        SaveFlight();
        changingScene = true;
        AutoArduPilotOnPlay.EndFlightSession();
        simulatorPaused = false;
        RestoreGame();
        SetPauseButtonVisible(false);
        SceneManager.LoadSceneAsync(destination, LoadSceneMode.Single);
    }

    public void ExitGame()
    {
        if (changingScene) return;
        SaveFlight();
        changingScene = true;
        AutoArduPilotOnPlay.EndFlightSession();
        simulatorPaused = false;
        RestoreGame();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void SaveFlight()
    {
        foreach (DeliveryScoreManager manager in FindObjectsByType<DeliveryScoreManager>(FindObjectsSortMode.None))
            manager.SaveCurrentProgress();
    }

    private void RestoreGame()
    {
        if (!IsPaused) return;
        if (simulatorPaused) AutoArduPilotOnPlay.TryPauseFlight(false);
        simulatorPaused = false;
        IsPaused = false;
        Time.timeScale = previousTimeScale;
        AudioListener.pause = previousAudioPause;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        if (popup != null) popup.SetActive(false);
        SetPauseButtonVisible(gameplayScene);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(previousSelection);
        previousSelection = null;
    }

    private void OnApplicationQuit()
    {
        if (instance != this) return;
        SaveFlight();
        AutoArduPilotOnPlay.EndFlightSession();
        simulatorPaused = false;
        RestoreGame();
    }

    private void OnEnable()
    {
        if (instance != this) return;
        menuCanvas.enabled = true;
        BindButtons(true);
        RefreshForScene(SceneManager.GetActiveScene());
    }

    private void OnDisable()
    {
        if (instance != this) return;
        RestoreGame();
        BindButtons(false);
        if (menuCanvas != null) menuCanvas.enabled = false;
    }

    private void OnDestroy()
    {
        BindButtons(false);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (instance == this)
        {
            RestoreGame();
            instance = null;
        }
    }

    private void EnsureEventSystem()
    {
        foreach (EventSystem system in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
        {
            if (system.gameObject == fallbackEventSystem) continue;
            if (fallbackEventSystem != null) fallbackEventSystem.SetActive(false);
            return;
        }
        if (fallbackEventSystem == null)
        {
            fallbackEventSystem = new GameObject("Pause Menu EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            fallbackEventSystem.transform.SetParent(transform, false);
            fallbackEventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        fallbackEventSystem.SetActive(true);
    }

    private void SetPauseButtonVisible(bool visible)
    {
        if (pauseButton != null) pauseButton.gameObject.SetActive(visible && showPauseButton);
    }

    private void BindButtons(bool bind)
    {
        Bind(pauseButton, TogglePause, bind);
        Bind(resumeButton, Resume, bind);
        Bind(restartButton, Restart, bind);
        Bind(mainMenuButton, BackToMenu, bind);
        Bind(exitButton, ExitGame, bind);
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action, bool bind)
    {
        if (button == null) return;
        button.onClick.RemoveListener(action);
        if (bind) button.onClick.AddListener(action);
    }
}
