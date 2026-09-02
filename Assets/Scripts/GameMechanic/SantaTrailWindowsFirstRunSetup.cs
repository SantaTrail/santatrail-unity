using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Downloads optional Windows support runtimes when the player is opened
/// directly, so users do not have to find and run a separate setup script.
/// </summary>
public static class SantaTrailWindowsFirstRunSetup
{
    private const string QGroundControlUrl =
        "https://github.com/mavlink/qgroundcontrol/releases/latest/download/QGroundControl-installer-AMD64.exe";
    private const string MissionPlannerUrl =
        "https://firmware.ardupilot.org/Tools/MissionPlanner/MissionPlanner-latest.zip";
    private const string PythonUrl =
        "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip";

    private static Task setupTask;

    public static bool IsNeeded()
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        if (!IsWindowsRuntime())
        {
            return false;
        }

        return GetMissingComponents().Count > 0;
#else
        return false;
#endif
    }

    public static string GetReadinessSummary()
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        if (!IsWindowsRuntime())
        {
            return "Windows setup is not required on this host.";
        }

        List<string> missingComponents = GetMissingComponents();
        return missingComponents.Count == 0
            ? "QGroundControl, Python and flight support are ready."
            : "Missing: " + string.Join(", ", missingComponents);
#else
        return "Windows setup is not required on this platform.";
#endif
    }

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
    private static List<string> GetMissingComponents()
    {
        List<string> missingComponents = new List<string>();
        if (FindQGroundControl() == null)
        {
            missingComponents.Add("QGroundControl");
        }

        if (FindPython() == null)
        {
            missingComponents.Add("portable Python");
        }

        if (FindNativeSitl() == null && FindMissionPlanner() == null)
        {
            missingComponents.Add("ArduPilot SITL or Mission Planner");
        }

        return missingComponents;
    }
#endif

    public static Task EnsureAsync()
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        if (!IsWindowsRuntime())
        {
            return Task.CompletedTask;
        }

        if (setupTask == null || setupTask.IsCompleted)
        {
            setupTask = EnsureWindowsRuntimesAsync();
        }

        return setupTask;
#else
        return Task.CompletedTask;
#endif
    }

    public static string FindMissionPlanner()
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        string releaseDirectory = GetGameRoot();
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        string[] directories =
        {
            Path.Combine(releaseDirectory, "MissionPlanner"),
            Path.Combine(localAppData, "SantaTrail", "MissionPlanner"),
            Path.Combine(localAppData, "Mission Planner"),
            Path.Combine(localAppData, "MissionPlanner")
        };

        foreach (string directory in directories)
        {
            string executable = FindFile(directory, "MissionPlanner.exe");
            if (executable != null)
            {
                return executable;
            }
        }
#endif

        return null;
    }

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
    private static async Task EnsureWindowsRuntimesAsync()
    {
        try
        {
            string dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SantaTrail"
            );
            string downloadDirectory = Path.Combine(dataDirectory, "Downloads");
            Directory.CreateDirectory(downloadDirectory);

            if (FindPython() == null)
            {
                string pythonArchive = Path.Combine(
                    downloadDirectory,
                    "python-3.12.10-embed-amd64.zip"
                );
                await DownloadFileAsync(
                    PythonUrl,
                    pythonArchive,
                    "portable Python"
                );

                string pythonDirectory = Path.Combine(dataDirectory, "Python");
                Directory.CreateDirectory(pythonDirectory);
                await Task.Run(() =>
                    ZipFile.ExtractToDirectory(pythonArchive, pythonDirectory, true)
                );
            }

            if (FindQGroundControl() == null)
            {
                string installer = Path.Combine(
                    downloadDirectory,
                    "QGroundControl-installer-AMD64.exe"
                );
                await DownloadFileAsync(
                    QGroundControlUrl,
                    installer,
                    "QGroundControl"
                );

                UnityEngine.Debug.Log(
                    "SantaTrail: QGroundControl is not installed. " +
                    "Opening the official installer; finish it once, then SantaTrail will continue."
                );
                await RunInstallerAndWaitAsync(installer);
            }

            if (FindNativeSitl() == null && FindMissionPlanner() == null)
            {
                string missionPlannerArchive = Path.Combine(
                    downloadDirectory,
                    "MissionPlanner-latest.zip"
                );
                await DownloadFileAsync(
                    MissionPlannerUrl,
                    missionPlannerArchive,
                    "Mission Planner"
                );

                string missionPlannerDirectory = Path.Combine(
                    dataDirectory,
                    "MissionPlanner"
                );
                Directory.CreateDirectory(missionPlannerDirectory);
                await Task.Run(() =>
                    ZipFile.ExtractToDirectory(
                        missionPlannerArchive,
                        missionPlannerDirectory,
                        true
                    )
                );
            }

            if (FindQGroundControl() == null)
            {
                UnityEngine.Debug.LogWarning(
                    "SantaTrail: QGroundControl is still not available after setup. " +
                    "The game can run, but flight connection will remain unavailable."
                );
            }

            if (FindNativeSitl() == null && FindMissionPlanner() == null)
            {
                UnityEngine.Debug.LogWarning(
                    "SantaTrail: Mission Planner could not be prepared. " +
                    "Automatic flight requires NativeSITL\\ArduCopter.exe."
                );
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError(
                "SantaTrail first-run setup failed: " + exception.Message
            );
        }
    }

    private static async Task DownloadFileAsync(
        string url,
        string destination,
        string description
    )
    {
        if (File.Exists(destination) && new FileInfo(destination).Length > 1024)
        {
            UnityEngine.Debug.Log(
                $"SantaTrail: using cached {description} download."
            );
            return;
        }

        string temporaryDestination = destination + ".download";
        if (File.Exists(temporaryDestination))
        {
            File.Delete(temporaryDestination);
        }

        UnityEngine.Debug.Log($"SantaTrail: downloading {description}...");
        try
        {
            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "SantaTrail-FirstRun-Setup";
                await client.DownloadFileTaskAsync(new Uri(url), temporaryDestination);
            }

            if (!File.Exists(temporaryDestination) ||
                new FileInfo(temporaryDestination).Length <= 1024)
            {
                throw new InvalidDataException(
                    $"The {description} download was empty or incomplete."
                );
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            File.Move(temporaryDestination, destination);
        }
        catch
        {
            if (File.Exists(temporaryDestination))
            {
                File.Delete(temporaryDestination);
            }
            throw;
        }
    }

    private static Task RunInstallerAndWaitAsync(string installer)
    {
        Process process = Process.Start(new ProcessStartInfo
        {
            FileName = installer,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installer),
            CreateNoWindow = false
        });

        if (process == null)
        {
            throw new InvalidOperationException(
                "The QGroundControl installer could not be opened."
            );
        }

        return Task.Run(() => process.WaitForExit());
    }

    private static string FindQGroundControl()
    {
        string gameRoot = GetGameRoot();
        string programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles
        );
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        string[] candidates =
        {
            Path.Combine(gameRoot, "QGroundControl", "bin", "QGroundControl.exe"),
            Path.Combine(gameRoot, "QGroundControl", "QGroundControl.exe"),
            Path.Combine(gameRoot, "QGroundControl.exe"),
            Path.Combine(programFiles, "QGroundControl", "bin", "QGroundControl.exe"),
            Path.Combine(programFiles, "QGroundControl", "QGroundControl.exe"),
            Path.Combine(localAppData, "Programs", "QGroundControl", "bin", "QGroundControl.exe"),
            Path.Combine(localAppData, "Programs", "QGroundControl", "QGroundControl.exe")
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return FindShortcutTarget("QGroundControl.lnk");
    }

    private static string FindPython()
    {
        string bundledPython = Path.Combine(
            Application.streamingAssetsPath,
            "Python",
            "python.exe"
        );
        if (File.Exists(bundledPython))
        {
            return bundledPython;
        }

        string installedPython = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SantaTrail",
            "Python",
            "python.exe"
        );
        return File.Exists(installedPython) ? installedPython : null;
    }

    private static string FindNativeSitl()
    {
        string gameRoot = GetGameRoot();
        string nativeSitlDirectory = Path.Combine(gameRoot, "NativeSITL");
        return FindFile(nativeSitlDirectory, "ArduCopter.exe");
    }

    private static string FindFile(string directory, string fileName)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            string[] files = Directory.GetFiles(
                directory,
                fileName,
                SearchOption.AllDirectories
            );
            return files.Length > 0 ? files[0] : null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string FindShortcutTarget(string shortcutName)
    {
        string[] shortcutDirectories =
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs", "QGroundControl"
            ),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs", "QGroundControl"
            )
        };

        foreach (string directory in shortcutDirectories)
        {
            string shortcut = Path.Combine(directory, shortcutName);
            if (!File.Exists(shortcut))
            {
                continue;
            }

            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    continue;
                }

                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcut }
                );
                string target = link == null
                    ? null
                    : link.GetType().InvokeMember(
                        "TargetPath",
                        BindingFlags.GetProperty,
                        null,
                        link,
                        null
                    ) as string;
                if (File.Exists(target))
                {
                    return target;
                }
            }
            catch
            {
                // The direct executable locations above are sufficient when
                // Windows does not allow shortcut inspection.
            }
        }

        return null;
    }

    private static string GetGameRoot()
    {
        if (Application.isEditor)
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        return Directory.GetParent(Application.dataPath).FullName;
    }

    private static bool IsWindowsRuntime()
    {
#if UNITY_EDITOR
        return Application.platform == RuntimePlatform.WindowsEditor;
#else
        return true;
#endif
    }
#endif
}
