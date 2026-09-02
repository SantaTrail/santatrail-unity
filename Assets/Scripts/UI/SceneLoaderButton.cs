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

        // Prepare the letter while LoadingScene is visible so the player
        // enters the letter scene with its child, text, and choices ready.
        if (SceneManager.GetActiveScene().name == "MainPageScene" &&
            sceneName == "letter")
        {
            SantaTrailSceneLoadRequest.Request(sceneName, true);
            Debug.Log("SceneLoaderButton: opening LoadingScene to prepare the letter.");
            SceneManager.LoadSceneAsync("LoadingScene", LoadSceneMode.Single);
            return;
        }

        // PreviewLV uses the same loading screen before opening any configured
        // flight level. Applying the entry here keeps the selected home and
        // mission settings in sync with the level that is about to open.
        if (SceneManager.GetActiveScene().name == "PreviewLV" &&
            LevelConfigLoader.TryFindLevel(sceneName, out LevelConfig previewLevel))
        {
            GameManager.ApplyLevel(previewLevel);
            SantaTrailSceneLoadRequest.Request(sceneName, false);
            Debug.Log(
                $"SceneLoaderButton: opening LoadingScene before {previewLevel.label}."
            );
            SceneManager.LoadSceneAsync("LoadingScene", LoadSceneMode.Single);
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
        Debug.Log($"SceneLoaderButton: loading scene '{sceneName}'.");
        SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
    }
}
