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

        string planPath = Path.Combine(Application.dataPath, "auto_mission.plan");
        string pythonScriptPath = Path.Combine(Application.dataPath, "Scripts/generate_mission.py");

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
                FileName = "/usr/bin/python3",
                Arguments = $"\"{pythonScriptPath}\"",
                WorkingDirectory = Application.dataPath,
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
            Process.Start(openQgcInfo);
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
}
