using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;

public class MissionGenerator : MonoBehaviour
{
    [Header("Optional Auto Run")]
    public bool runOnStart;

    [Header("Timeout")]
    public float missionGenerateTimeoutSeconds = 10f;

    void Start()
    {
        if (runOnStart)
        {
            GenerateMission();
        }
    }

    public void GenerateMission()
    {
        StartCoroutine(GenerateMissionRoutine());
    }

    private IEnumerator GenerateMissionRoutine()
    {
        UnityEngine.Debug.Log("MissionGenerator: generating mission...");

        string missionDirectory = Path.Combine(
            Application.persistentDataPath,
            "Mission"
        );
        Directory.CreateDirectory(missionDirectory);

        string planPath = Path.Combine(missionDirectory, "auto_mission.plan");
        string pythonScriptPath = ResolveMissionScriptPath();

        if (!File.Exists(pythonScriptPath))
        {
            UnityEngine.Debug.LogError($"MissionGenerator: script not found at {pythonScriptPath}");
            yield break;
        }

        try
        {
            if (File.Exists(planPath))
            {
                File.Delete(planPath);
            }
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"MissionGenerator: failed to clean old mission file: {ex.Message}");
            yield break;
        }

        Process process = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ResolvePythonExecutable(),
                Arguments =
                    $"\"{pythonScriptPath}\" --output \"{planPath}\"",
                WorkingDirectory = missionDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process = Process.Start(psi);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"MissionGenerator: failed to start python process: {ex.Message}");
            yield break;
        }

        float elapsed = 0f;
        while (!File.Exists(planPath) && elapsed < missionGenerateTimeoutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!File.Exists(planPath))
        {
            UnityEngine.Debug.LogError("MissionGenerator: mission generation timed out.");
            yield break;
        }

        UnityEngine.Debug.Log("MissionGenerator: mission ready.");

        try
        {
            var openQgcInfo = new ProcessStartInfo
            {
                FileName = "open",
                Arguments = "-a /Applications/QGroundControl.app",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // Process.Start(openQgcInfo);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogWarning($"MissionGenerator: could not launch QGroundControl automatically: {ex.Message}");
        }

        if (process != null && !process.HasExited)
        {
            process.Dispose();
        }
    }

    private static string ResolveMissionScriptPath()
    {
        string packagedPath = Path.Combine(
            Application.streamingAssetsPath,
            "Mission",
            "generate_mission.py"
        );
        if (File.Exists(packagedPath))
        {
            return packagedPath;
        }

#if UNITY_EDITOR
        string editorPath = Path.Combine(
            Application.dataPath,
            "Scripts",
            "generate_mission.py"
        );
        if (File.Exists(editorPath))
        {
            return editorPath;
        }
#endif

        return packagedPath;
    }

    private static string ResolvePythonExecutable()
    {
        string environmentPath = (
            System.Environment.GetEnvironmentVariable(
                "SANTATRAIL_PYTHON"
            ) ?? ""
        ).Trim();
        if (!string.IsNullOrEmpty(environmentPath))
        {
            return environmentPath;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string packagedPath = Path.Combine(
            Application.streamingAssetsPath,
            "Python",
            "python.exe"
        );
        return File.Exists(packagedPath) ? packagedPath : "python";
#else
        string packagedPath = Path.Combine(
            Application.streamingAssetsPath,
            "Python",
            "bin",
            "python3"
        );
        return File.Exists(packagedPath) ? packagedPath : "python3";
#endif
    }
}
