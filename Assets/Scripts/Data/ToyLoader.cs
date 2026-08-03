using UnityEngine;

public class ToyLoader : MonoBehaviour
{
    public ToyData[] toys;

    void Awake()
    {
        TextAsset json = Resources.Load<TextAsset>("Data/toys");

        if (json == null)
        {
            Debug.LogError("Cannot find Resources/Data/toys.json");
            return;
        }

        ToyList toyList = JsonUtility.FromJson<ToyList>(json.text);

        toys = toyList.toys;

        Debug.Log("Loaded " + toys.Length + " toys.");
    }

    public ToyData GetRandomToy()
    {
        if (toys == null || toys.Length == 0)
        {
            Debug.LogError("No toys loaded!");
            return null;
        }

        return toys[Random.Range(0, toys.Length)];
    }
}