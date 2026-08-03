using UnityEngine;

public class ChildGenerator : MonoBehaviour
{
    private NameList names;
    // private ToyList toys;
    private PersonalityList personalities;
    private EmotionList emotions;
    private GoodDeedList deeds;

    void Awake()
    {
        names = JsonUtility.FromJson<NameList>(
            Resources.Load<TextAsset>("Data/names").text);

        // toys = JsonUtility.FromJson<ToyList>(
        //     Resources.Load<TextAsset>("Data/toys").text);

        personalities = JsonUtility.FromJson<PersonalityList>(
            Resources.Load<TextAsset>("Data/personalities").text);

        emotions = JsonUtility.FromJson<EmotionList>(
            Resources.Load<TextAsset>("Data/emotions").text);

        deeds = JsonUtility.FromJson<GoodDeedList>(
            Resources.Load<TextAsset>("Data/good_deeds").text);
    }

    public ChildData Generate(ToyData selectedToy)
    {
        ChildData child = new ChildData();

        child.name =
            names.names[Random.Range(0, names.names.Length)];

        child.age = 8;
        // child.age =
        //     Random.Range(
        //         selectedToy.minAge,
        //         selectedToy.maxAge + 1);

        string personalityName =
            selectedToy.suitablePersonalities[
                Random.Range(
                    0,
                    selectedToy.suitablePersonalities.Length)];

        child.personality = personalities.personalities[0]; // Default

        foreach (PersonalityData p in personalities.personalities)
        {
            if (string.Equals(p.type, personalityName, System.StringComparison.OrdinalIgnoreCase))
            {
                child.personality = p;
                break;
            }
        }

        string emotionName =
            selectedToy.suitableEmotions[
                Random.Range(0, selectedToy.suitableEmotions.Length)
            ];

        child.emotion = emotions.emotions[0]; // Default

        foreach (EmotionData e in emotions.emotions)
        {
            if (string.Equals(e.emotion, emotionName, System.StringComparison.OrdinalIgnoreCase))
            {
                child.emotion = e;
                break;
            }
        }

        child.goodDeed =
            deeds.goodDeeds[
                Random.Range(0, deeds.goodDeeds.Length)];

        child.targetToy = selectedToy;

        string[] diff =
        {
            "easy",
            "medium",
            "hard"
        };

        child.difficulty =
            diff[Random.Range(0,3)];

        
        Debug.Log("=== CHILD GENERATED ===");
        Debug.Log("Name: " + child.name);
        Debug.Log("Age: " + child.age);
        Debug.Log("Difficulty: " + child.difficulty);
        Debug.Log("Toy: " + child.targetToy.name);

        return child;
    }

}