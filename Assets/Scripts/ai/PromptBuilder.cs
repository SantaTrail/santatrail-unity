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
        string[] storyShapes =
        {
            "Tell a small moment from school or home, then connect it to Christmas.",
            "Start with the good deed, then share a funny or happy little moment.",
            "Write about a feeling first, then a memory from this week.",
            "Make it a cheerful thank-you note with one specific detail about the child's day.",
            "Start with something the child is looking forward to, then mention the good deed.",
            "Tell a tiny beginning-middle-ending story from the child's week.",
            "Make it sound like the child is chatting excitedly about their Christmas plans.",
            "Begin with a question for Santa, then tell a warm little story."
        };

        string[] greetings =
        {
            "Dear Santa,",
            "Hi Santa,",
            "Hello Santa,",
            "Dear Santa Claus,"
        };

        string[] signOffs =
        {
            "Love,",
            "From,",
            "Merry Christmas from,",
            "Your friend,"
        };

        sb.AppendLine("Write one short, warm Christmas letter from a real child to Santa.");
        sb.AppendLine("This is a guessing-game letter: the desired present must stay secret.");
        sb.AppendLine("Never write the toy name, a synonym, its category, or an obvious answer.");
        sb.AppendLine("Never say 'I want', 'I would love', or 'please bring me' followed by a present.");
        sb.AppendLine("Use only 1-2 clues as background details, not as a list or a description of the item.");
        sb.AppendLine("Return only the letter: no title, explanation, bullets, or labels.");
        sb.AppendLine("Story shape: " + storyShapes[UnityEngine.Random.Range(0, storyShapes.Length)]);
        sb.AppendLine("Greeting: " + greetings[UnityEngine.Random.Range(0, greetings.Length)]);
        sb.AppendLine("Sign-off: " + signOffs[UnityEngine.Random.Range(0, signOffs.Length)] + " followed by the child's name.");

        sb.AppendLine();

        sb.AppendLine("Child: " + (string.IsNullOrWhiteSpace(child.name) ? "a child" : child.name) + ", age " + child.age + ".");
        sb.AppendLine("Personality: " + (child.personality != null ? child.personality.description : "cheerful and playful") + ".");
        sb.AppendLine("Feeling: " + (child.emotion != null ? child.emotion.description : "very excited") + ".");
        sb.AppendLine("Good deed: " + (child.goodDeed != null ? child.goodDeed.description : "being kind and helpful") + ".");
        sb.AppendLine("Private clue details (weave in subtly):");

        if (clues != null)
        {
            foreach (string clue in clues)
            {
                if (!string.IsNullOrWhiteSpace(clue))
                {
                    sb.AppendLine(
                        "- " + clue
                    );
                }
            }
        }

        sb.AppendLine();

        if (child.age <= 4)
        {
            sb.AppendLine("Style: authentic 3-4-year-old voice; simple words; 40-50 words.");
        }
        else if (child.age <= 6)
        {
            sb.AppendLine("Style: authentic 5-6-year-old voice; simple and excited; 50-65 words.");
        }
        else if (child.age <= 8)
        {
            sb.AppendLine("Style: authentic 7-8-year-old voice; natural and playful; 60-75 words.");
        }
        else
        {
            sb.AppendLine("Style: authentic 9-13-year-old voice; natural but clearly child-like; 70-85 words.");
        }

        sb.AppendLine();
        sb.AppendLine("Silently verify the secret present is not named before replying.");

        return sb.ToString();
    }
}
