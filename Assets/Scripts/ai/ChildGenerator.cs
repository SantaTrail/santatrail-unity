using UnityEngine;

public class ChildGenerator : MonoBehaviour
{
    private NameList names;
    private PersonalityList personalities;
    private EmotionList emotions;
    private GoodDeedList deeds;

    private void Awake()
    {
        TextAsset namesAsset =
            Resources.Load<TextAsset>(
                "Data/names"
            );

        TextAsset personalitiesAsset =
            Resources.Load<TextAsset>(
                "Data/personalities"
            );

        TextAsset emotionsAsset =
            Resources.Load<TextAsset>(
                "Data/emotions"
            );

        TextAsset deedsAsset =
            Resources.Load<TextAsset>(
                "Data/good_deeds"
            );

        if (namesAsset != null)
        {
            names =
                JsonUtility.FromJson<NameList>(
                    namesAsset.text
                );
        }

        if (personalitiesAsset != null)
        {
            personalities =
                JsonUtility.FromJson<PersonalityList>(
                    personalitiesAsset.text
                );
        }

        if (emotionsAsset != null)
        {
            emotions =
                JsonUtility.FromJson<EmotionList>(
                    emotionsAsset.text
                );
        }

        if (deedsAsset != null)
        {
            deeds =
                JsonUtility.FromJson<GoodDeedList>(
                    deedsAsset.text
                );
        }
    }

    public ChildData Generate(
        ToyData selectedToy)
    {
        if (selectedToy == null)
        {
            return null;
        }

        if (names == null ||
            personalities == null ||
            emotions == null ||
            deeds == null)
        {
            Debug.LogError(
                "ChildGenerator: " +
                "required data is missing."
            );

            return null;
        }

        ChildData child =
            new ChildData();

        child.name =
            names.names[
                Random.Range(
                    0,
                    names.names.Length
                )
            ];

        child.age = Random.Range(3, 14);

        string personalityName =
            selectedToy.suitablePersonalities[
                Random.Range(
                    0,
                    selectedToy
                        .suitablePersonalities
                        .Length
                )
            ];

        child.personality =
            personalities.personalities[0];

        foreach (
            PersonalityData personality
            in personalities.personalities
        )
        {
            if (
                string.Equals(
                    personality.type,
                    personalityName,
                    System.StringComparison
                        .OrdinalIgnoreCase
                )
            )
            {
                child.personality =
                    personality;

                break;
            }
        }

        string emotionName =
            selectedToy.suitableEmotions[
                Random.Range(
                    0,
                    selectedToy
                        .suitableEmotions
                        .Length
                )
            ];

        child.emotion =
            emotions.emotions[0];

        foreach (
            EmotionData emotion
            in emotions.emotions
        )
        {
            if (
                string.Equals(
                    emotion.emotion,
                    emotionName,
                    System.StringComparison
                        .OrdinalIgnoreCase
                )
            )
            {
                child.emotion =
                    emotion;

                break;
            }
        }

        child.goodDeed =
            deeds.goodDeeds[
                Random.Range(
                    0,
                    deeds.goodDeeds.Length
                )
            ];

        child.targetToy =
            selectedToy;

        string[] difficulties =
        {
            "easy",
            "medium",
            "hard"
        };

        child.difficulty =
            difficulties[
                Random.Range(
                    0,
                    difficulties.Length
                )
            ];

        Debug.Log(
            "=== CHILD GENERATED ==="
        );

        Debug.Log(
            "Name: " +
            child.name
        );

        Debug.Log(
            "Age: " +
            child.age
        );

        Debug.Log(
            "Difficulty: " +
            child.difficulty
        );

        Debug.Log(
            "Toy: " +
            child.targetToy.name
        );

        return child;
    }

}
