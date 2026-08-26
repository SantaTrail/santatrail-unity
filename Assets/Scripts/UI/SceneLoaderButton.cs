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

        // LV1 owns its LoadingScreenUI and shows it while the
        // level's map and mission data finish initializing.
        SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
    }
}
