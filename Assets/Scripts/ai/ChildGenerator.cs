using UnityEngine;

public class ChildGenerator : MonoBehaviour
{
    private NameList names;
    private ToyList toys;
    private PersonalityList personalities;
    private EmotionList emotions;
    private GoodDeedList deeds;

    void Awake()
    {
        names = JsonUtility.FromJson<NameList>(
            Resources.Load<TextAsset>("Data/names").text);

        toys = JsonUtility.FromJson<ToyList>(
            Resources.Load<TextAsset>("Data/toys").text);

        personalities = JsonUtility.FromJson<PersonalityList>(
            Resources.Load<TextAsset>("Data/personalities").text);

        emotions = JsonUtility.FromJson<EmotionList>(
            Resources.Load<TextAsset>("Data/emotions").text);

        deeds = JsonUtility.FromJson<GoodDeedList>(
            Resources.Load<TextAsset>("Data/good_deeds").text);
    }

    public ChildData Generate()
    {
        ChildData child = new ChildData();

        child.name =
            names.names[Random.Range(0, names.names.Length)];

        child.age =
            Random.Range(5,11);

        child.personality =
            personalities.personalities[
                Random.Range(0, personalities.personalities.Length)];

        child.emotion =
            emotions.emotions[
                Random.Range(0, emotions.emotions.Length)];

        child.goodDeed =
            deeds.goodDeeds[
                Random.Range(0, deeds.goodDeeds.Length)];

        child.targetToy =
            toys.toys[
                Random.Range(0, toys.toys.Length)];

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