using System.Text;

public static class PromptBuilder
{
    public static string Build(ChildData child)
    {
        string[] clues;

        switch (child.difficulty)
        {
            case "medium":
                clues = child.targetToy.mediumClues;
                break;

            case "hard":
                clues = child.targetToy.hardClues;
                break;

            default:
                clues = child.targetToy.easyClues;
                break;
        }

        StringBuilder sb = new StringBuilder();

        sb.AppendLine("You are generating a believable Christmas letter from a child to Santa.");
        sb.AppendLine();
        sb.AppendLine("STRICT RULES:");
        sb.AppendLine("- Maximum 60 words.");
        sb.AppendLine("- Use simple child-like language.");
        sb.AppendLine("- Make the letter warm and emotional.");
        sb.AppendLine("- Never reveal the exact toy name.");
        sb.AppendLine("- Naturally include all 3 clues.");
        sb.AppendLine("- End the letter with 'Love,' and the child's name.");
        sb.AppendLine();

        sb.AppendLine("CHILD PROFILE");
        sb.AppendLine($"Name: {child.name}");
        sb.AppendLine($"Age: {child.age}");
        sb.AppendLine($"Personality: {child.personality.description}");
        sb.AppendLine($"Emotion: {child.emotion.description}");
        sb.AppendLine($"Good deed: {child.goodDeed.description}");
        sb.AppendLine($"Difficulty: {child.difficulty}");
        sb.AppendLine();

        sb.AppendLine("Gift clues:");
        foreach (string clue in clues)
        {
            sb.AppendLine($"- {clue}");
        }

        sb.AppendLine();
        sb.AppendLine("Return ONLY the letter.");

        return sb.ToString();
    }
}