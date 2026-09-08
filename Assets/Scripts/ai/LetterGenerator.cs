// ============================================================
// LetterGenerator.cs
// ============================================================

using UnityEngine;
using LLMUnity;
using System;
using System.Threading.Tasks;

public class LetterGenerator : MonoBehaviour
{
    public LLMAgent agent;
    public LetterUI ui;

    [Header("Offline fallback")]
    [SerializeField] private bool useOfflineFallback = true;

    [SerializeField] private float llmReadyTimeoutSeconds = 8f;

    private Task<bool> modelReadyTask;

    public async Task<string> GenerateLetterText(ChildData child)
    {
        if (child == null)
        {
            Debug.LogError(
                "LetterGenerator: child data is missing."
            );

            return null;
        }

        bool modelReady = await IsLocalModelReady();

        if (!modelReady)
        {
            if (!useOfflineFallback)
            {
                Debug.LogError(
                    "LetterGenerator: local model is unavailable."
                );

                return null;
            }

            Debug.LogWarning(
                "LetterGenerator: local model unavailable. " +
                "Using offline Christmas letter."
            );

            return BuildOfflineLetter(child);
        }

        try
        {
            string prompt = PromptBuilder.Build(child);

            Debug.Log(
                $"LetterGenerator: generating letter for {child.name}..."
            );

            float startTime =
                Time.realtimeSinceStartup;

            string generatedLetter =
                await agent.Chat(prompt);

            float elapsed =
                Time.realtimeSinceStartup - startTime;

            Debug.Log(
                $"LetterGenerator: AI Chat took {elapsed:F2}s"
            );

            if (string.IsNullOrWhiteSpace(generatedLetter))
            {
                if (!useOfflineFallback)
                {
                    Debug.LogError(
                        "LetterGenerator: AI returned an empty letter."
                    );

                    return null;
                }

                Debug.LogWarning(
                    "LetterGenerator: AI returned an empty letter. " +
                    "Using offline letter."
                );

                return BuildOfflineLetter(child);
            }

            generatedLetter = generatedLetter.Trim();
            if (ContainsToyName(generatedLetter, child.targetToy))
            {
                Debug.LogWarning(
                    "LetterGenerator: generated letter revealed the toy name. Using the safe offline letter."
                );
                return BuildOfflineLetter(child);
            }

            return generatedLetter;
        }
        catch (Exception e)
        {
            if (!useOfflineFallback)
            {
                Debug.LogException(e);
                return null;
            }

            Debug.LogWarning(
                $"LetterGenerator: AI generation failed " +
                $"({e.Message}). Using offline letter."
            );

            return BuildOfflineLetter(child);
        }
    }

    public async Task<bool> GenerateLetter(ChildData child)
    {
        if (child == null || ui == null)
        {
            Debug.LogError(
                "LetterGenerator: child data or LetterUI is missing."
            );

            return false;
        }

        string generatedLetter =
            await GenerateLetterText(child);

        if (string.IsNullOrWhiteSpace(generatedLetter))
        {
            return false;
        }

        ui.UpdateUI(
            child,
            generatedLetter
        );

        return true;
    }

    private Task<bool> IsLocalModelReady()
    {
        if (modelReadyTask != null)
        {
            return modelReadyTask;
        }

        modelReadyTask = CheckLocalModelReadyAsync();

        return modelReadyTask;
    }

    private async Task<bool> CheckLocalModelReadyAsync()
    {
        if (agent == null)
        {
            Debug.LogError(
                "LetterGenerator: LLMAgent is missing."
            );

            return false;
        }

        if (agent.remote)
        {
            return true;
        }

        if (agent.llm == null)
        {
            Debug.LogError(
                "LetterGenerator: LLM reference is missing."
            );

            return false;
        }

        Task readyTask;

        try
        {
            readyTask =
                agent.llm.WaitUntilReady();
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "LetterGenerator: could not check local model " +
                $"({exception.Message})."
            );

            return false;
        }

        Task timeoutTask =
            Task.Delay(
                TimeSpan.FromSeconds(
                    Mathf.Max(
                        1f,
                        llmReadyTimeoutSeconds
                    )
                )
            );

        Task completedTask =
            await Task.WhenAny(
                readyTask,
                timeoutTask
            );

        if (completedTask != readyTask)
        {
            Debug.LogWarning(
                "LetterGenerator: timed out waiting " +
                "for local model."
            );

            return false;
        }

        try
        {
            await readyTask;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "LetterGenerator: local model startup failed " +
                $"({exception.Message})."
            );

            return false;
        }

        bool ready =
            !agent.llm.failed &&
            agent.llm.llmService != null;

        if (!ready)
        {
            Debug.LogWarning(
                "LetterGenerator: local LLM is not ready."
            );
        }

        return ready;
    }

    private static string BuildOfflineLetter(
        ChildData child)
    {
        string childName =
            string.IsNullOrWhiteSpace(child.name)
                ? "friend"
                : child.name;

        string deed =
            child.goodDeed == null ||
            string.IsNullOrWhiteSpace(
                child.goodDeed.description)
                ? "I have been trying my best to be kind"
                : child.goodDeed.description
                    .Trim()
                    .TrimEnd('.');

        string feeling =
            child.emotion == null ||
            string.IsNullOrWhiteSpace(
                child.emotion.description)
                ? "I feel very excited"
                : child.emotion.description
                    .Trim()
                    .TrimEnd('.');

        string clue = GetFallbackClue(child);
        string[] bodies =
        {
            $"I hope you and the reindeer are having a cozy week. {deed}. " +
            $"{feeling}, especially when I think about {clue}. I have been imagining Christmas morning a lot!",
            $"This week was busy and fun. {deed}. After that I felt proud, and I kept thinking about {clue}. " +
            $"I hope there will be a little Christmas surprise for me.",
            $"I am counting the days until Christmas! {deed}. {feeling}. " +
            $"There is something about {clue} that makes my imagination feel extra big.",
            $"Can you tell the reindeer I said hello? {deed}. I was smiling because {clue} sounds so nice. " +
            $"{feeling}, and I promise to leave a tasty snack for everyone."
        };

        return
            $"Dear Santa,\n\n" +
            bodies[UnityEngine.Random.Range(0, bodies.Length)] + "\n\n" +
            $"Love,\n{childName}";
    }

    private static string GetFallbackClue(ChildData child)
    {
        if (child.targetToy == null)
        {
            return "a happy surprise";
        }

        string[] clues = child.difficulty == "hard"
            ? child.targetToy.hardClues
            : child.difficulty == "medium"
                ? child.targetToy.mediumClues
                : child.targetToy.easyClues;

        if (clues == null || clues.Length == 0)
        {
            return "a happy surprise";
        }

        string clue = clues[UnityEngine.Random.Range(0, clues.Length)].Trim();
        return ContainsToyName(clue, child.targetToy) ? "a happy surprise" : clue;
    }

    private static bool ContainsToyName(string letter, ToyData toy)
    {
        if (toy == null || string.IsNullOrWhiteSpace(toy.name) || string.IsNullOrWhiteSpace(letter))
        {
            return false;
        }

        return letter.IndexOf(toy.name.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
