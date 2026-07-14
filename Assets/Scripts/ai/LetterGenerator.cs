using UnityEngine;
using LLMUnity;

public class LetterGenerator : MonoBehaviour
{
    public LLMAgent agent;

    public LetterUI ui;

    public async System.Threading.Tasks.Task GenerateLetter(
        ChildData child)
    {
        string prompt =
            PromptBuilder.Build(child);

        Debug.Log(prompt);

        string letter =
            await agent.Chat(prompt);

        ui.UpdateUI(child, letter);
    }
}