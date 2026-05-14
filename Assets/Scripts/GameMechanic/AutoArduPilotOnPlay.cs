using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public static class AutoArduPilotOnPlay
{
    private static bool hasLaunchedThisSession;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void LaunchArduPilotOnPlay()
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        if (hasLaunchedThisSession)
        {
            return;
        }

        hasLaunchedThisSession = true;

        string scriptPath = Path.Combine(Application.dataPath, "Scripts/start_ardupilot.sh");
        if (!File.Exists(scriptPath))
        {
            UnityEngine.Debug.LogError($"AutoArduPilotOnPlay: script not found at {scriptPath}");
            return;
        }

        ProcessStartInfo sitlStartInfo = new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        string escapedPath = scriptPath.Replace("\"", "\\\"");
        sitlStartInfo.Arguments = $"-e \"tell application \\\"Terminal\\\" to do script \\\"{escapedPath}\\\"\"";

        Process.Start(sitlStartInfo);
        UnityEngine.Debug.Log($"AutoArduPilotOnPlay: launched ArduPilot using {scriptPath}");

        _ = LaunchQgcAfterSITLBootAsync();
#else
        UnityEngine.Debug.LogWarning("AutoArduPilotOnPlay: auto-launch is configured for macOS only.");
#endif
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

        ProcessStartInfo qgcStartInfo = new ProcessStartInfo
        {
            FileName = "open",
            Arguments = "-a /Applications/QGroundControl.app",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(qgcStartInfo);
        UnityEngine.Debug.Log("AutoArduPilotOnPlay: launched QGroundControl.");
    }
}