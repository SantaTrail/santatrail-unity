using UnityEngine;
using LLMUnity;
using System;
using System.Threading.Tasks;

public class LetterGenerator : MonoBehaviour
{
    public LLMAgent agent;
    public LetterUI ui;

    [Header("Offline fallback")]
    [Tooltip("Keep the letter scene playable when the optional local GGUF model is not installed.")]
    [SerializeField] private bool useOfflineFallback = true;
    [Tooltip("Maximum time to wait for the local model before using the offline letter.")]
    [SerializeField] private float llmReadyTimeoutSeconds = 8f;

    public async Task<bool> GenerateLetter(ChildData child)
    {
        if (child == null || ui == null)
        {
            Debug.LogError("LetterGenerator: child data or LetterUI is missing.");
            return false;
        }

        if (!await IsLocalModelReady())
        {
            return UseFallbackOrFail(
                child,
                "The local letter model is unavailable. Using the offline Christmas letter.");
        }

        try
        {
            string prompt = PromptBuilder.Build(child);
            Debug.Log(prompt);

            string letter = await agent.Chat(prompt);

            if (string.IsNullOrWhiteSpace(letter))
            {
                return UseFallbackOrFail(
                    child,
                    "The local letter model returned an empty response. Using the offline Christmas letter.");
            }

            ui.UpdateUI(child, letter);

            return true;
        }
        catch (Exception e)
        {
            if (!useOfflineFallback)
            {
                Debug.LogException(e);
                return false;
            }

            Debug.LogWarning(
                $"LetterGenerator: AI letter generation failed ({e.Message}). " +
                "Using the offline Christmas letter.");
            ui.UpdateUI(child, BuildOfflineLetter(child));
            return true;
        }
    }

    private async Task<bool> IsLocalModelReady()
    {
        if (agent == null)
        {
            return false;
        }

        // Remote agents do not use the local GGUF model check.
        if (agent.remote)
        {
            return true;
        }

        if (agent.llm == null)
        {
            return false;
        }

        Task readyTask;
        try
        {
            readyTask = agent.llm.WaitUntilReady();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"LetterGenerator: could not check the local model ({exception.Message}).");
            return false;
        }

        Task timeoutTask = Task.Delay(
            TimeSpan.FromSeconds(Mathf.Max(1f, llmReadyTimeoutSeconds)));
        Task completedTask = await Task.WhenAny(readyTask, timeoutTask);
        if (completedTask != readyTask)
        {
            Debug.LogWarning("LetterGenerator: timed out waiting for the local letter model.");
            return false;
        }

        try
        {
            await readyTask;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"LetterGenerator: local model startup failed ({exception.Message}).");
            return false;
        }

        return !agent.llm.failed && agent.llm.llmService != null;
    }

    private bool UseFallbackOrFail(ChildData child, string warning)
    {
        if (!useOfflineFallback)
        {
            Debug.LogError(warning);
            return false;
        }

        Debug.LogWarning(warning);
        ui.UpdateUI(child, BuildOfflineLetter(child));
        return true;
    }

    private static string BuildOfflineLetter(ChildData child)
    {
        string childName = string.IsNullOrWhiteSpace(child.name) ? "friend" : child.name;
        string giftName = child.targetToy == null || string.IsNullOrWhiteSpace(child.targetToy.name)
            ? "a special Christmas present"
            : child.targetToy.name;
        string deed = child.goodDeed == null || string.IsNullOrWhiteSpace(child.goodDeed.description)
            ? "I have been trying my best to be kind"
            : child.goodDeed.description.Trim().TrimEnd('.');
        string feeling = child.emotion == null || string.IsNullOrWhiteSpace(child.emotion.description)
            ? "I feel very excited"
            : child.emotion.description.Trim().TrimEnd('.');

        return $"Dear Santa,\n\nI hope you and the reindeer are happy. I would love {giftName}. " +
               $"{deed}. {feeling}, and I cannot wait for Christmas!\n\nLove,\n{childName}";
    }
}
