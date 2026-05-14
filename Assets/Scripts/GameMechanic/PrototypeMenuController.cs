using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PrototypeMenuController : MonoBehaviour
{
    [Serializable]
    public class LevelEntry
    {
        public string label = "Level 1";
        public string sceneName = "DroneSimulateArcGIS";
    }

    [Header("Level Select (Optional Dropdown)")]
    public TMP_Dropdown levelDropdown;
    public List<LevelEntry> levels = new List<LevelEntry>
    {
        new LevelEntry { label = "Level 1 - Training", sceneName = "DroneSimulateArcGIS" },
        new LevelEntry { label = "Level 2 - Demo", sceneName = "DroneSimulateDemo" },
        new LevelEntry { label = "Level 3 - Sandbox", sceneName = "SampleScene" }
    };

    [Header("Buttons (Optional Auto-Wire)")]
    public Button playButton;

    [Header("Story Text (Optional)")]
    [TextArea(4, 8)] public string gameStory =
        "Christmas is at risk. The reindeer are grounded and you are the last hope. " +
        "Your only option is a delivery drone.\n\n" +
        "Fly over a real OpenStreetMap-based city, locate buildings, fly close, and hover " +
        "stably for a few seconds to complete each delivery. The steadier you fly and the " +
        "more buildings you reach, the higher your score.";

    [TextArea(3, 6)] public string gameplayLoop =
        "Gameplay Loop: receive letter -> select gift -> check requirements -> plan route -> " +
        "fly to target -> get stability evaluation -> earn reward and progress.";

    public TMP_Text storyText;
    public TMP_Text loopText;
    public TMP_Text selectedLevelText;

    private int selectedLevelIndex;
    private string selectedSceneName;

    void Awake()
    {
        if (levels.Count > 0)
        {
            selectedLevelIndex = 0;
            selectedSceneName = levels[0].sceneName;
        }

        SetupLevelDropdown();
        SetupButtons();
        RefreshUI();
    }

    void SetupLevelDropdown()
    {
        if (levelDropdown == null) return;

        levelDropdown.ClearOptions();
        var options = new List<TMP_Dropdown.OptionData>();
        for (int i = 0; i < levels.Count; i++)
        {
            options.Add(new TMP_Dropdown.OptionData(levels[i].label));
        }

        levelDropdown.AddOptions(options);
        levelDropdown.onValueChanged.RemoveListener(OnDropdownChanged);
        levelDropdown.onValueChanged.AddListener(OnDropdownChanged);
        selectedLevelIndex = Mathf.Clamp(levelDropdown.value, 0, Mathf.Max(0, levels.Count - 1));
    }

    void SetupButtons()
    {
        if (playButton == null) return;

        playButton.onClick.RemoveListener(OnPlayPressed);
        playButton.onClick.AddListener(OnPlayPressed);
    }

    void OnDropdownChanged(int index)
    {
        selectedLevelIndex = index;
        if (levels.Count > 0)
        {
            selectedSceneName = levels[Mathf.Clamp(selectedLevelIndex, 0, levels.Count - 1)].sceneName;
        }
        RefreshUI();
    }

    void RefreshUI()
    {
        if (storyText != null) storyText.text = gameStory;
        if (loopText != null) loopText.text = gameplayLoop;

        if (selectedLevelText != null)
        {
            if (levels.Count == 0)
            {
                selectedLevelText.text = "No levels configured";
            }
            else
            {
                var lvl = levels[Mathf.Clamp(selectedLevelIndex, 0, levels.Count - 1)];
                selectedLevelText.text = $"Selected: {lvl.label}";
            }
        }
    }

    public void SelectLevel(int index)
    {
        if (levels.Count == 0) return;

        selectedLevelIndex = Mathf.Clamp(index, 0, levels.Count - 1);
        selectedSceneName = levels[selectedLevelIndex].sceneName;
        if (levelDropdown != null) levelDropdown.value = selectedLevelIndex;
        RefreshUI();
    }

    public void SelectLevelByScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("PrototypeMenuController: sceneName is empty.");
            return;
        }

        selectedSceneName = sceneName;
        int foundIndex = levels.FindIndex(l => l.sceneName == sceneName);
        if (foundIndex >= 0)
        {
            selectedLevelIndex = foundIndex;
            if (levelDropdown != null) levelDropdown.value = selectedLevelIndex;
        }
        RefreshUI();
    }

    public void SelectLevel1()
    {
        SelectLevelByScene("DroneSimulateArcGIS");
    }

    public void OnPlayPressed()
    {
        string targetScene = selectedSceneName;
        if (string.IsNullOrWhiteSpace(targetScene) && levels.Count > 0)
        {
            targetScene = levels[Mathf.Clamp(selectedLevelIndex, 0, levels.Count - 1)].sceneName;
        }

        if (string.IsNullOrWhiteSpace(targetScene))
        {
            Debug.LogError("PrototypeMenuController: No selected scene.");
            return;
        }

        Debug.Log($"PrototypeMenuController: loading {targetScene}");
        SceneManager.LoadScene(targetScene);
    }
}
