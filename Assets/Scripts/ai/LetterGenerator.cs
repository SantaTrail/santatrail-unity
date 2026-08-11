using UnityEngine;
using LLMUnity;
using System;
using System.Threading.Tasks;

public class LetterGenerator : MonoBehaviour
{
    public LLMAgent agent;
    public LetterUI ui;

    public async Task<bool> GenerateLetter(ChildData child)
    {
        try
        {
            string prompt = PromptBuilder.Build(child);

            Debug.Log(prompt);

            string letter = await agent.Chat(prompt);

            ui.UpdateUI(child, letter);

            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return false;
        }
    }
}