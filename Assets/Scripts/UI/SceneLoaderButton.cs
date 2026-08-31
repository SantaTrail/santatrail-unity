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

        // The title Play button must use the same loading scene as a click
        // anywhere on the title screen.
        if (SceneManager.GetActiveScene().name == "TitleScene" &&
            sceneName == "MainPageScene")
        {
            TitleSceneStartupController startupController =
                FindFirstObjectByType<TitleSceneStartupController>();
            if (startupController != null)
            {
                startupController.BeginStartup();
                return;
            }
        }

        // LV1 owns its LoadingScreenUI and shows it while the
        // level's map and mission data finish initializing.
        SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
    }
}
