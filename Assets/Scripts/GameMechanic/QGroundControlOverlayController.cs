using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds a small in-game control for placing the separately-running QGroundControl
/// application over a SantaTrail flight level. QGroundControl is a native app, so
/// it cannot be embedded in a Unity Canvas; this controller manages its OS window.
/// </summary>
public sealed class QGroundControlOverlayController : MonoBehaviour
{
    private const float OverlaySizePercent = 0.30f;
    private const int WindowMarginPixels = 18;
    private static readonly IntPtr HwndTopmost = new IntPtr(-1);
    private static readonly IntPtr HwndBottom = new IntPtr(1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private bool overlayIsVisible;
    private bool isFlightLevel;
    private FullScreenMode fullscreenModeBeforeOverlay;

    public static QGroundControlOverlayController Active { get; private set; }
    public bool OverlayIsVisible => overlayIsVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        Active = null;
    }

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
        if (Active != null && Active != this)
        {
            Destroy(gameObject);
            return;
        }

        Active = this;
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
        if (Active == this)
        {
            Active = null;
        }
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
        isFlightLevel = LevelConfigLoader.TryFindLevel(
            SceneManager.GetActiveScene().name,
            out LevelConfig unusedLevel);
        RefreshButtonComponents();
    }

    public void RefreshButtonComponents()
    {
        QGroundControlButtonUI[] buttons = FindObjectsByType<QGroundControlButtonUI>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].SetAvailable(isFlightLevel);
            buttons[i].SetOpen(overlayIsVisible);
        }
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
        RefreshButtonComponents();
    }

    public void ReturnQGroundControlToBackground()
    {
        if (!overlayIsVisible)
        {
            return;
        }

        KeepQGroundControlBehindGame();

        Screen.fullScreenMode = fullscreenModeBeforeOverlay;
        overlayIsVisible = false;
        RefreshButtonComponents();
    }

    /// <summary>
    /// Places QGroundControl behind Unity without changing overlay state. This
    /// is also used while QGC starts underneath the game's loading screen.
    /// </summary>
    public bool KeepQGroundControlBehindGame()
    {
        if (IsWindowsRuntime())
        {
            IntPtr qgcWindow = FindQGroundControlWindow();
            if (qgcWindow == IntPtr.Zero)
            {
                return false;
            }

            SetWindowPos(qgcWindow, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

            IntPtr unityWindow = Process.GetCurrentProcess().MainWindowHandle;
            if (unityWindow != IntPtr.Zero)
            {
                SetForegroundWindow(unityWindow);
            }

            return true;
        }

        return RunAppleScript(
            "tell application \"System Events\" to set visible of process \"QGroundControl\" to false"
        );
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
