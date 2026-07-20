using System;
using System.Diagnostics;
using System.Text;
using System.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ---- Data classes matching the JSON printed by `python child_letters.py --json` ----

[Serializable]
public class ToyOption
{
    public string id;
    public string label;
    public string description;
}

[Serializable]
public class LetterResponse
{
    public string child_name;
    public int child_age;
    public string difficulty;
    public string letter;
    public string[] hidden_clues;
    public ToyOption[] toy_options;
    public string correct_toy_id;
    public string emotion_tone;
    public string[] narrative_tags;
}

/// <summary>
/// Runs child_letters.py as a subprocess and parses its JSON output.
/// No server, no open port — just calls Python, waits, reads the result.
///
/// IMPORTANT SETUP:
/// - pythonExecutablePath must be the FULL path to the python interpreter
///   that has your packages installed (openai, sentence-transformers, etc).
///   Find it with `where python` (Windows) or `which python3` (Mac/Linux).
///   Unity apps often don't inherit your shell's PATH, so "python" alone
///   may fail to resolve even if it works fine in your terminal.
/// - workingDirectory must be the folder containing child_letters.py AND
///   its data/ subfolder, since the script loads data/*.json with
///   relative paths.
/// </summary>
public class SantaLetterProcessRunner : MonoBehaviour
{
    [Header("Paths — set these for your machine")]
    [Tooltip("Full path to python(.exe), e.g. C:/Users/you/venv/Scripts/python.exe")]
    [SerializeField] private string pythonExecutablePath = "python";

    [Tooltip("Full path to child_letters.py")]
    [SerializeField] private string scriptPath = "child_letters.py";

    [Tooltip("Folder containing child_letters.py and its data/ subfolder")]
    [SerializeField] private string workingDirectory = "";

    [Tooltip("Give up after this many seconds (LLM generation can be slow)")]
    [SerializeField] private int timeoutSeconds = 60;

    public IEnumerator FetchLetter(Action<LetterResponse> onSuccess, Action<string> onError)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = pythonExecutablePath,
            Arguments = $"\"{scriptPath}\" --json --no-save",
            WorkingDirectory = string.IsNullOrEmpty(workingDirectory)
                ? System.IO.Path.GetDirectoryName(scriptPath)
                : workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        // Read output asynchronously to avoid deadlocking on a full pipe buffer.
        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null) stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null) stderr.AppendLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception e)
        {
            onError?.Invoke(
                $"Failed to start python process. Check pythonExecutablePath " +
                $"('{pythonExecutablePath}') is correct. Error: {e.Message}");
            yield break;
        }

        float elapsed = 0f;
        while (!process.HasExited)
        {
            elapsed += Time.deltaTime;
            if (elapsed > timeoutSeconds)
            {
                try { process.Kill(); } catch { /* already exited */ }
                onError?.Invoke($"Timed out after {timeoutSeconds}s waiting for Python.");
                yield break;
            }
            yield return null;
        }

        // Make sure buffered async reads are fully flushed before reading strings.
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            onError?.Invoke($"Python exited with code {process.ExitCode}:\n{stderr}");
            yield break;
        }

        string json = stdout.ToString().Trim();

        if (string.IsNullOrEmpty(json))
        {
            onError?.Invoke($"Python produced no output. Stderr:\n{stderr}");
            yield break;
        }

        LetterResponse data;
        try
        {
            data = JsonUtility.FromJson<LetterResponse>(json);
        }
        catch (Exception e)
        {
            onError?.Invoke($"Failed to parse JSON: {e.Message}\nRaw output:\n{json}");
            yield break;
        }

        onSuccess?.Invoke(data);
    }
}