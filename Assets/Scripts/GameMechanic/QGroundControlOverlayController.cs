using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Adds a small in-game control for placing the separately-running QGroundControl
/// application over a SantaTrail flight level. QGroundControl is a native app, so
/// it cannot be embedded in a Unity Canvas; this controller manages its OS window.
/// </summary>
public sealed class QGroundControlOverlayController : MonoBehaviour
{
    private const float OverlaySizePercent = 0.30f;
    private const int WindowMarginPixels = 18;
    private const int ButtonWidth = 210;
    private const int ButtonHeight = 54;

    private static readonly IntPtr HwndTopmost = new IntPtr(-1);
    private static readonly IntPtr HwndBottom = new IntPtr(1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private Button toggleButton;
    private Text buttonLabel;
    private bool overlayIsVisible;
    private FullScreenMode fullscreenModeBeforeOverlay;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForRuntime()
    {
        GameObject existing = GameObject.Find("QGroundControl Overlay Controller");
        if (existing != null)
        {
            return;
        }

        GameObject root = new GameObject("QGroundControl Overlay Controller");
        DontDestroyOnLoad(root);
        root.AddComponent<QGroundControlOverlayController>();
    }

    private void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.quitting += ReturnQGroundControlToBackground;
    }

    private IEnumerator Start()
    {
        // Let the level's objects finish Awake/Start before deciding whether it is a flight level.
        yield return null;
        RefreshForActiveScene();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.quitting -= ReturnQGroundControlToBackground;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (overlayIsVisible)
        {
            ReturnQGroundControlToBackground();
        }

        StartCoroutine(RefreshAfterSceneLoad());
    }

    private IEnumerator RefreshAfterSceneLoad()
    {
        yield return null;
        RefreshForActiveScene();
    }

    private void RefreshForActiveScene()
    {
        bool isFlightLevel = LevelConfigLoader.TryFindLevel(
            SceneManager.GetActiveScene().name,
            out LevelConfig unusedLevel);

        if (isFlightLevel)
        {
            EnsureButton();
        }

        if (toggleButton != null)
        {
            toggleButton.gameObject.SetActive(isFlightLevel);
        }
    }

    private void EnsureButton()
    {
        if (toggleButton != null)
        {
            return;
        }

        if (EventSystem.current == null)
        {
            GameObject eventSystem = new GameObject("QGC Overlay EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
        }

        GameObject canvasObject = new GameObject("QGC Overlay Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(canvasObject);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject buttonObject = new GameObject("Toggle QGroundControl", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(canvasObject.transform, false);

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1f, 1f);
        buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.pivot = new Vector2(1f, 1f);
        buttonRect.anchoredPosition = new Vector2(-24f, -24f);
        buttonRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = new Color(0.05f, 0.19f, 0.30f, 0.94f);

        toggleButton = buttonObject.GetComponent<Button>();
        ColorBlock colors = toggleButton.colors;
        colors.highlightedColor = new Color(0.12f, 0.36f, 0.54f, 1f);
        colors.pressedColor = new Color(0.03f, 0.11f, 0.18f, 1f);
        toggleButton.colors = colors;
        toggleButton.onClick.AddListener(ToggleOverlay);

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        buttonLabel = labelObject.GetComponent<Text>();
        buttonLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        buttonLabel.fontSize = 20;
        buttonLabel.alignment = TextAnchor.MiddleCenter;
        buttonLabel.color = Color.white;
        UpdateButtonLabel();
    }

    public void ToggleOverlay()
    {
        if (PauseMenuController.IsPaused) return;
        if (overlayIsVisible)
        {
            ReturnQGroundControlToBackground();
        }
        else
        {
            ShowQGroundControlOverlay();
        }
    }

    private void ShowQGroundControlOverlay()
    {
        fullscreenModeBeforeOverlay = Screen.fullScreenMode;
        // Borderless fullscreen allows another app window to appear above Unity.
        Screen.fullScreenMode = FullScreenMode.FullScreenWindow;

        bool positioned = IsWindowsRuntime()
            ? ShowWindowsOverlay()
            : ShowMacOverlay();

        if (!positioned)
        {
            Screen.fullScreenMode = fullscreenModeBeforeOverlay;
            UnityEngine.Debug.LogWarning(
                "QGroundControl overlay: QGroundControl is not running or its window is not ready yet."
            );
            return;
        }

        overlayIsVisible = true;
        UpdateButtonLabel();
    }

    public void ReturnQGroundControlToBackground()
    {
        if (!overlayIsVisible)
        {
            return;
        }

        if (IsWindowsRuntime())
        {
            IntPtr qgcWindow = FindQGroundControlWindow();
            if (qgcWindow != IntPtr.Zero)
            {
                SetWindowPos(qgcWindow, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            }

            IntPtr unityWindow = Process.GetCurrentProcess().MainWindowHandle;
            if (unityWindow != IntPtr.Zero)
            {
                SetForegroundWindow(unityWindow);
            }
        }
        else
        {
            RunAppleScript("tell application \"System Events\" to set visible of process \"QGroundControl\" to false");
        }

        Screen.fullScreenMode = fullscreenModeBeforeOverlay;
        overlayIsVisible = false;
        UpdateButtonLabel();
    }

    private static bool ShowWindowsOverlay()
    {
        IntPtr qgcWindow = FindQGroundControlWindow();
        IntPtr unityWindow = Process.GetCurrentProcess().MainWindowHandle;
        if (qgcWindow == IntPtr.Zero || unityWindow == IntPtr.Zero || !GetWindowRect(unityWindow, out Rect gameRect))
        {
            return false;
        }

        int gameWidth = Math.Max(1, gameRect.Right - gameRect.Left);
        int gameHeight = Math.Max(1, gameRect.Bottom - gameRect.Top);
        int qgcWidth = Math.Max(420, Mathf.RoundToInt(gameWidth * OverlaySizePercent));
        int qgcHeight = Math.Max(300, Mathf.RoundToInt(gameHeight * OverlaySizePercent));
        int qgcX = gameRect.Right - qgcWidth - WindowMarginPixels;
        // Keep the top-right Unity button exposed.
        int qgcY = gameRect.Bottom - qgcHeight - WindowMarginPixels;

        SetWindowPos(qgcWindow, HwndTopmost, qgcX, qgcY, qgcWidth, qgcHeight, SwpShowWindow);
        SetForegroundWindow(qgcWindow);
        return true;
    }

    private static bool ShowMacOverlay()
    {
        int width = Math.Max(420, Mathf.RoundToInt(Screen.width * OverlaySizePercent));
        int height = Math.Max(300, Mathf.RoundToInt(Screen.height * OverlaySizePercent));
        int x = Math.Max(WindowMarginPixels, Screen.width - width - WindowMarginPixels);
        int y = Math.Max(WindowMarginPixels, Screen.height - height - WindowMarginPixels);

        // System Events requires macOS Accessibility permission for the game/editor.
        string script =
            "tell application \"System Events\"\n" +
            "  tell process \"QGroundControl\"\n" +
            "    set position of window 1 to {" + x + ", " + y + "}\n" +
            "    set size of window 1 to {" + width + ", " + height + "}\n" +
            "    set visible to true\n" +
            "    set frontmost to true\n" +
            "  end tell\n" +
            "end tell";
        return RunAppleScript(script);
    }

    private static bool RunAppleScript(string script)
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        try
        {
            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                Arguments = "-e " + QuoteShellArgument(script),
                UseShellExecute = false,
                CreateNoWindow = true
            }))
            {
                process.WaitForExit(1500);
                return process.ExitCode == 0;
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning("QGroundControl overlay: macOS window command failed. " + exception.Message);
        }
#endif
        return false;
    }

    private static string QuoteShellArgument(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static IntPtr FindQGroundControlWindow()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try
        {
            foreach (Process process in Process.GetProcessesByName("QGroundControl"))
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        return process.MainWindowHandle;
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning("QGroundControl overlay: could not find QGroundControl. " + exception.Message);
        }
#endif
        return IntPtr.Zero;
    }

    private static bool IsWindowsRuntime()
    {
        return Application.platform == RuntimePlatform.WindowsEditor ||
               Application.platform == RuntimePlatform.WindowsPlayer;
    }

    private void UpdateButtonLabel()
    {
        if (buttonLabel != null)
        {
            buttonLabel.text = overlayIsVisible ? "Hide Flight Planner" : "Show Flight Planner";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
