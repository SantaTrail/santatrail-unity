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

        //--------------------------------------------------------
        // ROLE
        //--------------------------------------------------------

        sb.AppendLine("You are writing a believable Christmas letter from a child to Santa.");
        sb.AppendLine();

        //--------------------------------------------------------
        // WRITING RULES
        //--------------------------------------------------------

        sb.AppendLine("RULES:");
        sb.AppendLine("- The letter must sound like it was written by a REAL child.");
        sb.AppendLine("- The entire letter must be between 35 and 50 words.");
        sb.AppendLine("- Never exceed 50 words.");  // change from 80 to 50
        sb.AppendLine("- Never reveal the exact toy name.");
        // sb.AppendLine("- Naturally include ALL 3 clues.");
        sb.AppendLine("- Mention the child's good deed.");
        sb.AppendLine("- Mention the child's feelings naturally.");
        sb.AppendLine("- Mention family, friends, or school if it fits naturally.");
        sb.AppendLine("- Do NOT list clues like a checklist.");

        sb.AppendLine();
        sb.AppendLine("- End with 'Love,' followed by the child's name.");
        sb.AppendLine();

        //--------------------------------------------------------
        // AGE WRITING STYLE
        //--------------------------------------------------------

        sb.AppendLine("WRITING STYLE:");

        if(child.age <= 4)
        {
            sb.AppendLine(
                "Write exactly like a 3-4 year old child.");

            sb.AppendLine(
                "Maximum 25 words.");

            sb.AppendLine(
                "Use very short sentences.");

            sb.AppendLine(
                "Use extremely simple vocabulary.");
        }
        else if(child.age <= 6)
        {
            sb.AppendLine(
                "Write like a 5-6 year old child.");

            sb.AppendLine(
                "Maximum 40 words.");

            sb.AppendLine(
                "The child can explain one reason.");
        }
        else if(child.age <= 8)
        {
            sb.AppendLine(
                "Write like a 7-8 year old child.");

            sb.AppendLine(
                "Maximum 55 words.");

            sb.AppendLine(
                "Show excitement naturally.");
        }
        else
        {
            sb.AppendLine(
                "Write like a thoughtful 9-11 year old.");
            sb.AppendLine("- Mention the child's feelings naturally.");

            sb.AppendLine(
                "Maximum 70 words.");

            sb.AppendLine(
                "The child reflects on why the gift matters.");
        }

        sb.AppendLine();

        //--------------------------------------------------------
        // CHILD PROFILE
        //--------------------------------------------------------

        sb.AppendLine("CHILD PROFILE");
        sb.AppendLine($"Name: {child.name}");
        sb.AppendLine($"Age: {child.age}");
        sb.AppendLine($"Personality: {child.personality.description}");
        sb.AppendLine($"Emotion: {child.emotion.description}");
        sb.AppendLine($"Good Deed: {child.goodDeed.description}");
        sb.AppendLine();

        sb.AppendLine("TARGET GIFT");
        sb.AppendLine($"Category: {child.targetToy.category}");
        sb.AppendLine($"Description: {child.targetToy.description}");
        sb.AppendLine($"Why children like it: {child.targetToy.whyChildrenLikeIt}");
        sb.AppendLine($"Reason the child wants it: {child.targetToy.christmasWishReason}");

        sb.AppendLine();

        //--------------------------------------------------------
        // GIFT INFORMATION
        //--------------------------------------------------------

        sb.AppendLine("TARGET GIFT");
        sb.AppendLine($"Category: {child.targetToy.category}");
        sb.AppendLine($"Description: {child.targetToy.description}");
        sb.AppendLine($"Why children like it: {child.targetToy.whyChildrenLikeIt}");
        sb.AppendLine();

        sb.AppendLine("Gift Information");
        sb.AppendLine($"Gift Category: {child.targetToy.category}");
        sb.AppendLine($"Reason for wanting this gift:");
        sb.AppendLine(child.targetToy.christmasWishReason);
        sb.AppendLine();

        //--------------------------------------------------------
        // HIDDEN CLUES
        //--------------------------------------------------------

        sb.AppendLine("HIDDEN GIFT CLUES");

        foreach (string clue in clues)
        {
            sb.AppendLine("- " + clue);
        }

        sb.AppendLine();

        //--------------------------------------------------------
        // FINAL INSTRUCTION
        //--------------------------------------------------------

        sb.AppendLine("Return ONLY the letter.");
        sb.AppendLine("Do not explain anything.");
        sb.AppendLine("Do not use bullet points.");
        sb.AppendLine("Do not include a title.");

        return sb.ToString();
    }
}