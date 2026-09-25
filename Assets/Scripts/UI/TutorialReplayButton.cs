using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connects the scene-saved TutorialButton to the tutorial in this scene.
/// The button can therefore be moved, resized, recoloured, and found in the
/// Hierarchy before Play mode.
/// </summary>
[RequireComponent(typeof(Button))]
public sealed class TutorialReplayButton : MonoBehaviour
{
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OpenTutorial);
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OpenTutorial);
        }
    }

    private void OpenTutorial()
    {
        MainGameTutorialGuide[] guides = FindObjectsByType<MainGameTutorialGuide>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        for (int index = 0; index < guides.Length; index++)
        {
            MainGameTutorialGuide guide = guides[index];
            if (guide != null && guide.gameObject.scene == gameObject.scene)
            {
                guide.OpenTutorial();
                return;
            }
        }
    }
}
