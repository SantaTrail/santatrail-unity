using UnityEngine;

public class ToyLoader : MonoBehaviour
{
    public ToyData[] toys;

    void Awake()
    {
        // Load the JSON file from Resources/Data/toys.json
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
}