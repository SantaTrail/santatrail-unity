using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoaderButton : MonoBehaviour
{
    [SerializeField] private string sceneName = "";

    public void LoadScene()
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError($"{nameof(SceneLoaderButton)}: sceneName is empty.");
            return;
        }

        SceneManager.LoadScene(sceneName);
    }
}
