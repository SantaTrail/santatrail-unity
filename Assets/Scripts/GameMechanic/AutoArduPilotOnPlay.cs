using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AutoArduPilotOnPlay
{
    private const string QgcMapTypeAtStartup = "Satellite";
    private static bool hasLaunchedThisSession;
    private static bool isLaunchInProgress;
    private static bool hasAttemptedWindowsFirstRunSetup;
    private static string pendingSceneName;
    private static Process ownedSitlProcess;
    private static SitlProcessPause sitlPause;
    private static int launchGeneration;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetLaunchState()
    {
        EndFlightSession();
        hasLaunchedThisSession = false;
        isLaunchInProgress = false;
        hasAttemptedWindowsFirstRunSetup = false;
        pendingSceneName = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.quitting -= StopOwnedSitl;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeAutoLaunch()
    {
#if UNITY_EDITOR || UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.quitting -= StopOwnedSitl;
        Application.quitting += StopOwnedSitl;
#else
        UnityEngine.Debug.LogWarning(
            "AutoArduPilotOnPlay: auto-launch is configured for macOS and Windows standalone builds only."
        );
#endif

        // RuntimeInitializeOnLoadMethod runs after the first scene can already
        // be loaded, so process that scene explicitly. Later scenes arrive via
        // SceneManager.sceneLoaded above.
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid())
        {
            OnSceneLoaded(activeScene, LoadSceneMode.Single);
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RequestLaunchForScene(scene.name);
    }

    public static void RequestLaunchForScene(string sceneName)
    {
#if UNITY_EDITOR || UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        if (IsExternalSitlManaged())
        {
            UnityEngine.Debug.Log(
                "AutoArduPilotOnPlay: an external launcher is managing SITL for this session."
            );
            return;
        }
#endif

        if (string.Equals(
                sceneName,
                "LoadingScene",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            if (SantaTrailSceneLoadRequest.HasPendingRequest)
            {
                UnityEngine.Debug.Log(
                    "SantaTrail: LoadingScene is handling a requested scene transition; " +
                    "skipping the flight setup."
                );
                return;
            }

            UnityEngine.Debug.Log(
                "SantaTrail: LoadingScene entered; starting the support-package check."
            );
            _ = SantaTrailWindowsFirstRunSetup.EnsureAsync();
            return;
        }

        if (PauseMenuController.IsPaused) return;

        if (hasLaunchedThisSession || isLaunchInProgress)
        {
            if (isLaunchInProgress && !string.IsNullOrEmpty(sceneName))
            {
                // The first-run download can still be running while the user
                // enters a level. Retry the newest level after setup finishes.
                pendingSceneName = sceneName;
            }
            return;
        }

        // The active level loader assigns its map home during Awake, before it
        // calls this method from Start.
        if (!GameManager.TryApplyLevelForScene(sceneName))
        {
            UnityEngine.Debug.Log(
                $"AutoArduPilotOnPlay: {sceneName} is not a configured flight level; " +
                "waiting for the user to select a level."
            );
            return;
        }

        if (IsWindowsRuntime() &&
            !hasAttemptedWindowsFirstRunSetup &&
            SantaTrailWindowsFirstRunSetup.IsNeeded())
        {
            hasAttemptedWindowsFirstRunSetup = true;
            isLaunchInProgress = true;
            _ = EnsureWindowsFirstRunSetupAndRetryAsync(sceneName, launchGeneration);
            return;
        }

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
            string homeLat = GameManager.homeLat.ToString(CultureInfo.InvariantCulture);
            string homeLon = GameManager.homeLon.ToString(CultureInfo.InvariantCulture);
            string homeAlt = GameManager.homeAlt.ToString(CultureInfo.InvariantCulture);
            string homeYaw = GameManager.homeYaw.ToString(CultureInfo.InvariantCulture);
            UnityEngine.Debug.Log(
                $"AutoArduPilotOnPlay: launching SITL for {sceneName} at " +
                $"{homeLat}, {homeLon}, {homeAlt}, {homeYaw}."
            );

            if (IsWindowsRuntime())
            {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                if (ResolveWindowsSitlExecutable() != null)
                {
                    ownedSitlProcess = StartWindowsSitl(homeLat, homeLon, homeAlt, homeYaw);
                    UnityEngine.Debug.Log("AutoArduPilotOnPlay: launched native Windows ArduPilot SITL.");
                }
                else
                {
                    StartWindowsGuidedFallback();
                    UnityEngine.Debug.LogWarning(
                        "AutoArduPilotOnPlay: native SITL is not bundled. " +
                        "Mission Planner was opened for the guided fallback."
                    );
                }
#else
                UnityEngine.Debug.LogError(
                    "AutoArduPilotOnPlay: Windows SITL is unavailable in this player."
                );
                return;
#endif
            }
            else
            {
#if UNITY_EDITOR || UNITY_STANDALONE_OSX
                string scriptPath = ResolveMacSitlScriptPath();
                if (!File.Exists(scriptPath))
                {
                    UnityEngine.Debug.LogError(
                        "AutoArduPilotOnPlay: start_ardupilot.sh was not found. " +
                        "Place it in StreamingAssets/SITL for standalone builds."
                    );
                    return;
                }

                ProcessStartInfo sitlStartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments =
                        $"{QuoteShellArgument(scriptPath)} " +
                        $"{homeLat} {homeLon} {homeAlt} {homeYaw}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                ownedSitlProcess = Process.Start(sitlStartInfo);
                if (ownedSitlProcess == null)
                {
                    UnityEngine.Debug.LogError(
                        "AutoArduPilotOnPlay: could not start the SITL process."
                    );
                    return;
                }

                UnityEngine.Debug.Log(
                    $"AutoArduPilotOnPlay: launched ArduPilot using {scriptPath}"
                );
#else
                UnityEngine.Debug.LogError(
                    "AutoArduPilotOnPlay: macOS SITL is unavailable in this player."
                );
                return;
#endif
            }

            if (ownedSitlProcess != null) sitlPause = new SitlProcessPause(ownedSitlProcess);
            hasLaunchedThisSession = true;
            _ = LaunchQgcAfterSITLBootAsync(launchGeneration);
        }
        catch (Exception exception)
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
            "AutoArduPilotOnPlay: auto-launch is configured for macOS and Windows standalone builds only."
        );
#endif
    }

    private static async Task EnsureWindowsFirstRunSetupAndRetryAsync(string sceneName, int generation)
    {
        try
        {
            UnityEngine.Debug.Log(
                "SantaTrail: first-run setup is downloading the missing Windows support files."
            );
            await SantaTrailWindowsFirstRunSetup.EnsureAsync();
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError(
                "SantaTrail: first-run setup failed. " + exception.Message
            );
        }
        finally
        {
            if (generation == launchGeneration) isLaunchInProgress = false;
        }

        if (generation != launchGeneration) return;

        string retrySceneName = string.IsNullOrEmpty(pendingSceneName)
            ? sceneName
            : pendingSceneName;
        pendingSceneName = null;
        RequestLaunchForScene(retrySceneName);
    }

    private static string ResolveMacSitlScriptPath()
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

    private static bool IsWindowsRuntime()
    {
#if UNITY_EDITOR
        return Application.platform == RuntimePlatform.WindowsEditor;
#elif UNITY_STANDALONE_WIN
        return true;
#else
        return false;
#endif
    }

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
    private static bool IsExternalSitlManaged()
    {
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            if (string.Equals(
                argument,
                "--santatrail-external-sitl",
                StringComparison.OrdinalIgnoreCase
            ))
            {
                return true;
            }
        }

        return false;
    }

    private static Process StartWindowsSitl(
        string homeLat,
        string homeLon,
        string homeAlt,
        string homeYaw
    )
    {
        string sitlExecutable = ResolveWindowsSitlExecutable();
        if (sitlExecutable == null)
        {
            string expectedLocation = Application.isEditor
                ? "the Unity project root"
                : "the folder beside DronSim.exe";
            throw new FileNotFoundException(
                "NativeSITL\\ArduCopter.exe was not found in " + expectedLocation + ". " +
                "Add a complete Windows ArduPilot SITL bundle before testing automatic flight."
            );
        }

        string defaultsFile = Path.Combine(
            Application.streamingAssetsPath,
            "SITL",
            "sitl_unity_safe.parm"
        );
        if (!File.Exists(defaultsFile))
        {
            throw new FileNotFoundException(
                "The SITL parameter file is missing from DronSim_Data\\StreamingAssets\\SITL."
            );
        }

        string stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SantaTrail",
            "SITL"
        );
        Directory.CreateDirectory(stateDirectory);
        string sitlLogPath = Path.Combine(stateDirectory, "ArduCopter.log");

        string home = $"{homeLat},{homeLon},{homeAlt},{homeYaw}";
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = sitlExecutable,
            Arguments = string.Join(" ", new[]
            {
                "--wipe",
                "--model quad",
                "--home=" + home,
                "--defaults=" + QuoteWindowsArgument(defaultsFile),
                "--serial0=udpclient:127.0.0.1:14550",
                "--serial1=udpclient:127.0.0.1:14551"
            }),
            // ArduPilot needs to resolve its bundled DLLs and support files
            // relative to NativeSITL, not the per-user diagnostics folder.
            WorkingDirectory = Path.GetDirectoryName(sitlExecutable),
            UseShellExecute = false,
            // Keep the console visible while validating a user's package. It
            // also makes an immediate ArduPilot failure obvious to the user.
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        Process process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Could not start the native Windows ArduPilot SITL process.");
        }

        UnityEngine.Debug.Log(
            "AutoArduPilotOnPlay: ArduPilot started (PID " + process.Id + ") from " +
            sitlExecutable + ". Diagnostics will be written to " + sitlLogPath
        );
        _ = CaptureWindowsSitlOutputAsync(process, sitlLogPath);
        _ = VerifyWindowsSitlAsync(process, sitlExecutable);
        return process;
    }

    private static async Task VerifyWindowsSitlAsync(Process process, string executablePath)
    {
        await Task.Delay(1500);

        try
        {
            if (process.HasExited)
            {
                UnityEngine.Debug.LogError(
                    "AutoArduPilotOnPlay: ArduPilot exited immediately (exit code " +
                    process.ExitCode + ") from " + executablePath + ". " +
                    "Check %LOCALAPPDATA%\\SantaTrail\\SITL\\ArduCopter.log."
                );
            }
            else
            {
                UnityEngine.Debug.Log(
                    "AutoArduPilotOnPlay: ArduPilot SITL is still running and ready for QGroundControl."
                );
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "AutoArduPilotOnPlay: could not verify ArduPilot startup. " +
                exception.Message
            );
        }
    }

    private static async Task CaptureWindowsSitlOutputAsync(Process process, string logPath)
    {
        try
        {
            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
            string[] output = await Task.WhenAll(standardOutputTask, standardErrorTask);

            string report =
                "Exit code: " + process.ExitCode + Environment.NewLine +
                "--- stdout ---" + Environment.NewLine + output[0] + Environment.NewLine +
                "--- stderr ---" + Environment.NewLine + output[1];
            await Task.Run(() => File.WriteAllText(logPath, report));
        }
        catch (Exception exception)
        {
            try
            {
                await Task.Run(() => File.AppendAllText(
                    logPath,
                    Environment.NewLine + "Could not capture ArduPilot output: " + exception.Message
                ));
            }
            catch
            {
                // The diagnostic process must never affect the game flow.
            }
        }
    }

    private static string ResolveWindowsSitlExecutable()
    {
        string preparedSitl = SantaTrailWindowsFirstRunSetup.FindNativeSitl();
        if (preparedSitl != null)
        {
            return preparedSitl;
        }

        string gameRoot = Directory.GetParent(Application.dataPath).FullName;
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        string roamingAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData
        );
        string commonAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData
        );
        string[] sitlDirectories =
        {
            Path.Combine(gameRoot, "NativeSITL"),
            Path.Combine(gameRoot, "MissionPlanner"),
            Path.Combine(localAppData, "SantaTrail", "MissionPlanner"),
            Path.Combine(localAppData, "Mission Planner"),
            Path.Combine(localAppData, "MissionPlanner"),
            Path.Combine(roamingAppData, "Mission Planner"),
            Path.Combine(roamingAppData, "MissionPlanner"),
            Path.Combine(commonAppData, "Mission Planner"),
            Path.Combine(commonAppData, "MissionPlanner"),
            Path.Combine(commonAppData, "MissionPlanner")
        };

        foreach (string sitlDirectory in sitlDirectories)
        {
            if (!Directory.Exists(sitlDirectory))
            {
                continue;
            }

            try
            {
                string[] candidates = Directory.GetFiles(
                    sitlDirectory,
                    "ArduCopter.exe",
                    SearchOption.AllDirectories
                );
                if (candidates.Length > 0)
                {
                    return candidates[0];
                }
            }
            catch (UnauthorizedAccessException)
            {
                UnityEngine.Debug.LogWarning(
                    "AutoArduPilotOnPlay: cannot inspect SITL folder " + sitlDirectory
                );
            }
        }

        return null;
    }

    private static void StartWindowsGuidedFallback()
    {
        string missionPlanner = SantaTrailWindowsFirstRunSetup.FindMissionPlanner();
        if (missionPlanner == null)
        {
            UnityEngine.Debug.LogError(
                "AutoArduPilotOnPlay: Mission Planner was not found after first-run setup. " +
                "Automatic flight requires NativeSITL\\ArduCopter.exe."
            );
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = missionPlanner,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(missionPlanner),
                CreateNoWindow = false
            });
            UnityEngine.Debug.Log(
                "AutoArduPilotOnPlay: Mission Planner opened. " +
                "Choose Simulation > Copter > Quad and start the simulation."
            );
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError(
                "AutoArduPilotOnPlay: could not open Mission Planner. " + exception.Message
            );
        }
    }

    private static string QuoteWindowsArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

#endif

    public static void EndFlightSession()
    {
        launchGeneration++;
        StopOwnedSitl();
        hasLaunchedThisSession = false;
        isLaunchInProgress = false;
        pendingSceneName = null;
    }

    public static bool TryPauseFlight(bool paused)
    {
        if (sitlPause == null) return false;
        try
        {
            sitlPause.SetPaused(paused);
            return true;
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning("SantaTrail: could not change simulator pause state. " + exception.Message);
            return false;
        }
    }

    private static void StopOwnedSitl()
    {
        if (ownedSitlProcess == null)
        {
            return;
        }

        try
        {
            if (!ownedSitlProcess.HasExited)
            {
                ownedSitlProcess.Kill();
                ownedSitlProcess.WaitForExit(2000);
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "AutoArduPilotOnPlay: could not stop SantaTrail SITL. " + exception.Message
            );
        }
        finally
        {
            try { sitlPause?.Dispose(); }
            catch (Exception exception) { UnityEngine.Debug.LogWarning(exception.Message); }
            sitlPause = null;
            ownedSitlProcess.Dispose();
            ownedSitlProcess = null;
        }
    }

    private static async Task LaunchQgcAfterSITLBootAsync(int generation)
    {
        await Task.Delay(5000);
        while (generation == launchGeneration && PauseMenuController.IsPaused)
            await Task.Delay(250);
        if (generation != launchGeneration) return;

        Process[] existingQgc = Process.GetProcessesByName("QGroundControl");
        if (existingQgc != null && existingQgc.Length > 0)
        {
            UnityEngine.Debug.Log("AutoArduPilotOnPlay: QGroundControl is already running.");
            return;
        }

        if (IsWindowsRuntime())
        {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
            string qgcPath = ResolveWindowsQGroundControlPath();
            if (qgcPath == null)
            {
                UnityEngine.Debug.LogWarning(
                    "AutoArduPilotOnPlay: QGroundControl was not found. Run Setup SantaTrail.bat once."
                );
                return;
            }

            ConfigureWindowsQgcMapType();

            ProcessStartInfo qgcStartInfo = new ProcessStartInfo
            {
                FileName = qgcPath,
                UseShellExecute = qgcPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase),
                CreateNoWindow = true
            };
            Process.Start(qgcStartInfo);
            UnityEngine.Debug.Log($"AutoArduPilotOnPlay: launched QGroundControl from {qgcPath}.");
#endif
        }
        else
        {
#if UNITY_EDITOR || UNITY_STANDALONE_OSX
            string qgcAppPath = ResolveMacQGroundControlPath();
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
#endif
        }
    }

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
    private static void ConfigureWindowsQgcMapType()
    {
        try
        {
            string settingsPath = ResolveWindowsQgcSettingsPath();
            string settingsDirectory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(settingsDirectory))
            {
                Directory.CreateDirectory(settingsDirectory);
            }

            List<string> lines = File.Exists(settingsPath)
                ? new List<string>(File.ReadAllLines(settingsPath))
                : new List<string>();

            int flightMapSectionIndex = -1;
            int nextSectionIndex = lines.Count;
            bool insideFlightMapSection = false;
            bool mapTypeWritten = false;

            for (int index = 0; index < lines.Count; index++)
            {
                string trimmedLine = lines[index].Trim();
                if (trimmedLine.StartsWith("[", StringComparison.Ordinal) &&
                    trimmedLine.EndsWith("]", StringComparison.Ordinal))
                {
                    insideFlightMapSection = string.Equals(
                        trimmedLine,
                        "[FlightMap]",
                        StringComparison.OrdinalIgnoreCase
                    );

                    if (insideFlightMapSection)
                    {
                        flightMapSectionIndex = index;
                    }
                    else if (flightMapSectionIndex >= 0 && nextSectionIndex == lines.Count)
                    {
                        nextSectionIndex = index;
                    }

                    continue;
                }

                if (!insideFlightMapSection)
                {
                    continue;
                }

                int separatorIndex = trimmedLine.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = trimmedLine.Substring(0, separatorIndex).Trim();
                if (string.Equals(key, "mapType", StringComparison.OrdinalIgnoreCase))
                {
                    lines[index] = "mapType=" + QgcMapTypeAtStartup;
                    mapTypeWritten = true;
                }
            }

            if (flightMapSectionIndex < 0)
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                {
                    lines.Add(string.Empty);
                }

                lines.Add("[FlightMap]");
                lines.Add("mapType=" + QgcMapTypeAtStartup);
            }
            else if (!mapTypeWritten)
            {
                lines.Insert(nextSectionIndex, "mapType=" + QgcMapTypeAtStartup);
            }

            File.WriteAllLines(settingsPath, lines);
            UnityEngine.Debug.Log(
                $"AutoArduPilotOnPlay: QGroundControl map type set to {QgcMapTypeAtStartup}."
            );
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "AutoArduPilotOnPlay: could not set the QGroundControl map type. " +
                exception.Message
            );
        }
    }

    private static string ResolveWindowsQgcSettingsPath()
    {
        string roamingAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData
        );

        string[] candidates =
        {
            Path.Combine(roamingAppData, "QGroundControl", "QGroundControl.ini"),
            Path.Combine(roamingAppData, "QGroundControl.org", "QGroundControl.ini"),
            Path.Combine(roamingAppData, "QGroundControl.ini")
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }
#endif

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
    private static string ResolveWindowsQGroundControlPath()
    {
        string gameRoot = Directory.GetParent(Application.dataPath).FullName;
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string[] executableCandidates =
        {
            Path.Combine(gameRoot, "QGroundControl", "bin", "QGroundControl.exe"),
            Path.Combine(gameRoot, "QGroundControl", "QGroundControl.exe"),
            Path.Combine(gameRoot, "QGroundControl.exe"),
            Path.Combine(programFiles, "QGroundControl", "bin", "QGroundControl.exe"),
            Path.Combine(programFiles, "QGroundControl", "QGroundControl.exe"),
            Path.Combine(localAppData, "Programs", "QGroundControl", "bin", "QGroundControl.exe"),
            Path.Combine(localAppData, "Programs", "QGroundControl", "QGroundControl.exe")
        };

        foreach (string candidate in executableCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string[] shortcutCandidates =
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs", "QGroundControl", "QGroundControl.lnk"
            ),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs", "QGroundControl", "QGroundControl.lnk"
            )
        };

        foreach (string candidate in shortcutCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
#endif

#if UNITY_EDITOR || UNITY_STANDALONE_OSX
    private static string ResolveMacQGroundControlPath()
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
#endif
}
