#if UNITY_EDITOR && UNITY_EDITOR_OSX
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class DroneVideoBridgeLauncher
{
    private static readonly object bridgeLock = new object();
    private static Process bridgeProcess;

    static DroneVideoBridgeLauncher()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= StopBridge;
        AssemblyReloadEvents.beforeAssemblyReload += StopBridge;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            StartBridge();
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            StopBridge();
        }
    }

    private static void StartBridge()
    {
        StopBridge();

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string scriptPath = Path.Combine(
            projectRoot,
            "Assets",
            "Scripts",
            "Drone",
            "DroneCamera",
            "stream_to_qgc.py"
        );

        if (!File.Exists(scriptPath))
        {
            UnityEngine.Debug.LogError(
                "DroneVideoBridgeLauncher: bridge script not found: " + scriptPath
            );
            return;
        }

        string pythonPath = File.Exists("/opt/homebrew/bin/python3")
            ? "/opt/homebrew/bin/python3"
            : File.Exists("/usr/local/bin/python3")
                ? "/usr/local/bin/python3"
                : "python3";
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        string ffmpegDirectories = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = "\"" + scriptPath + "\"",
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.EnvironmentVariables["PATH"] = ffmpegDirectories + ":" + path;

        try
        {
            Process process = Process.Start(startInfo);
            if (process == null)
            {
                UnityEngine.Debug.LogError(
                    "DroneVideoBridgeLauncher: Python did not start the video bridge."
                );
                return;
            }

            lock (bridgeLock)
            {
                bridgeProcess = process;
            }

            process.Exited += OnBridgeExited;
            process.EnableRaisingEvents = true;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            UnityEngine.Debug.Log(
                "DroneVideoBridgeLauncher: started the QGC video bridge."
            );
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError(
                "DroneVideoBridgeLauncher: could not start Python: " + exception.Message
            );
            StopBridge();
        }
    }

    private static void OnBridgeExited(object sender, EventArgs args)
    {
        Process process = sender as Process;
        if (process == null) return;

        int exitCode;
        lock (bridgeLock)
        {
            // StopBridge may already own and be disposing this process.
            if (!ReferenceEquals(bridgeProcess, process)) return;

            bridgeProcess = null;
            process.Exited -= OnBridgeExited;
            exitCode = process.ExitCode;
            process.Dispose();
        }

        if (EditorApplication.isPlaying)
        {
            UnityEngine.Debug.LogError(
                "DroneVideoBridgeLauncher: video bridge exited with code " + exitCode + "."
            );
        }
    }

    private static void StopBridge()
    {
        Process process;
        lock (bridgeLock)
        {
            process = bridgeProcess;
            bridgeProcess = null;
            if (process != null)
            {
                // Exiting play mode intentionally terminates the bridge. Detach
                // first so its Exited callback cannot race this cleanup.
                process.Exited -= OnBridgeExited;
            }
        }

        if (process == null) return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(1500);
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "DroneVideoBridgeLauncher: could not stop video bridge cleanly: " +
                exception.Message
            );
        }
        finally
        {
            process.Dispose();
        }
    }
}
#endif
