using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AutoArduPilotOnPlay
{
    private static bool hasLaunchedThisSession;
    private static bool isLaunchInProgress;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLaunchState()
    {
        hasLaunchedThisSession = false;
        isLaunchInProgress = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeAutoLaunch()
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
#else
        UnityEngine.Debug.LogWarning("AutoArduPilotOnPlay: auto-launch is configured for macOS only.");
#endif
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RequestLaunchForScene(scene.name);
    }

    public static void RequestLaunchForScene(string sceneName)
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        if (hasLaunchedThisSession || isLaunchInProgress)
        {
            return;
        }

        // The active LV1 loader assigns its map home during Awake, before it
        // calls this method from Start.
        GameManager.TryApplyLevelForScene(sceneName);

        if (!GameManager.hasHomeLocation)
        {
            UnityEngine.Debug.Log(
                $"AutoArduPilotOnPlay: no home location for scene {sceneName}; SITL was not launched."
            );
            return;
        }

        isLaunchInProgress = true;

        try
        {
            string scriptPath = ResolveSitlScriptPath();
            if (!File.Exists(scriptPath))
            {
                UnityEngine.Debug.LogError(
                    "AutoArduPilotOnPlay: start_ardupilot.sh was not found. " +
                    "Place it in StreamingAssets/SITL for standalone builds."
                );
                return;
            }

            string homeLat = GameManager.homeLat.ToString(CultureInfo.InvariantCulture);
            string homeLon = GameManager.homeLon.ToString(CultureInfo.InvariantCulture);
            string homeAlt = GameManager.homeAlt.ToString(CultureInfo.InvariantCulture);
            string homeYaw = GameManager.homeYaw.ToString(CultureInfo.InvariantCulture);
            UnityEngine.Debug.Log(
                $"AutoArduPilotOnPlay: launching SITL for {sceneName} at " +
                $"{homeLat}, {homeLon}, {homeAlt}, {homeYaw}."
            );

            ProcessStartInfo sitlStartInfo = new ProcessStartInfo
            {
                // Run without Terminal/AppleScript so a packaged app does not need
                // macOS Automation permission on the player's computer.
                FileName = "/bin/bash",
                Arguments =
                    $"{QuoteShellArgument(scriptPath)} " +
                    $"{homeLat} {homeLon} {homeAlt} {homeYaw}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process sitlProcess = Process.Start(sitlStartInfo);
            if (sitlProcess == null)
            {
                UnityEngine.Debug.LogError(
                    "AutoArduPilotOnPlay: could not start the SITL process."
                );
                return;
            }

            hasLaunchedThisSession = true;
            UnityEngine.Debug.Log(
                $"AutoArduPilotOnPlay: launched ArduPilot using {scriptPath}"
            );

            _ = LaunchQgcAfterSITLBootAsync();
        }
        catch (System.Exception exception)
        {
            UnityEngine.Debug.LogError(
                "AutoArduPilotOnPlay: failed to launch SITL. " +
                exception.Message
            );
        }
        finally
        {
            isLaunchInProgress = false;
        }
#else
        UnityEngine.Debug.LogWarning(
            "AutoArduPilotOnPlay: auto-launch is configured for macOS only."
        );
#endif
    }

    private static string ResolveSitlScriptPath()
    {
        string packagedPath = Path.Combine(
            Application.streamingAssetsPath,
            "SITL",
            "start_ardupilot.sh"
        );

        if (File.Exists(packagedPath))
        {
            return packagedPath;
        }

#if UNITY_EDITOR
        string editorPath = Path.Combine(
            Application.dataPath,
            "Scripts",
            "start_ardupilot.sh"
        );
        if (File.Exists(editorPath))
        {
            return editorPath;
        }
#endif

        return packagedPath;
    }

    private static string QuoteShellArgument(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'") + "'";
    }

    private static async Task LaunchQgcAfterSITLBootAsync()
    {
        await Task.Delay(5000);

        Process[] existingQgc = Process.GetProcessesByName("QGroundControl");
        if (existingQgc != null && existingQgc.Length > 0)
        {
            UnityEngine.Debug.Log("AutoArduPilotOnPlay: QGroundControl is already running.");
            return;
        }

        string qgcAppPath = ResolveQGroundControlPath();
        if (qgcAppPath == null)
        {
            UnityEngine.Debug.LogWarning(
                "AutoArduPilotOnPlay: QGroundControl.app was not found beside the game or in /Applications."
            );
            return;
        }

        ProcessStartInfo qgcStartInfo = new ProcessStartInfo
        {
            FileName = "open",
            Arguments = QuoteShellArgument(qgcAppPath),
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(qgcStartInfo);
        UnityEngine.Debug.Log($"AutoArduPilotOnPlay: launched QGroundControl from {qgcAppPath}.");
    }

    private static string ResolveQGroundControlPath()
    {
        if (!Application.isEditor)
        {
            DirectoryInfo directory = new DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 4 && directory != null; i++)
            {
                directory = directory.Parent;
            }

            if (directory != null)
            {
                string bundledPath = Path.Combine(directory.FullName, "QGroundControl.app");
                if (Directory.Exists(bundledPath))
                {
                    return bundledPath;
                }
            }
        }

        const string applicationsPath = "/Applications/QGroundControl.app";
        return Directory.Exists(applicationsPath) ? applicationsPath : null;
    }

}
