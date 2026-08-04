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
        public string sceneName = "LV1";
        public double homeLatitude = 13.8455;
        public double homeLongitude = 100.5688;
        public double homeAltitude = 10;
        public double homeYaw = 0;
    }

    [Header("Level Select (Optional Dropdown)")]
    public TMP_Dropdown levelDropdown;
    public List<LevelEntry> levels = new List<LevelEntry>
    {
        new LevelEntry { label = "Level 1 - Guided Tutorial", sceneName = "LV1", homeLatitude = 52.15603852403063, homeLongitude = 4.963989431212162, homeAltitude = 0, homeYaw = 0 },
        new LevelEntry { label = "Level 2 - Demo", sceneName = "DroneSimulateDemo", homeLatitude = 13.8455, homeLongitude = 100.5688, homeAltitude = 10, homeYaw = 0 },
        new LevelEntry { label = "Level 3 - Sandbox", sceneName = "SampleScene", homeLatitude = 13.8455, homeLongitude = 100.5688, homeAltitude = 10, homeYaw = 0 }
    };

    [Header("Buttons (Optional Auto-Wire)")]
    public Button playButton;

    [Header("Story Text (Optional)")]
    [TextArea(4, 8)] public string gameStory =
        "Christmas is at risk. The reindeer are grounded and you are the last hope. " +
        "Your only option is a delivery drone.\n\n" +
        "Level 1 walks you through QGroundControl setup, Guided mode, takeoff, and " +
        "basic control before the first delivery.\n\n" +
        "Fly over a real OpenStreetMap-based city, locate buildings, fly close, and hover " +
        "stably for a few seconds to complete each delivery. The steadier you fly and the " +
        "more buildings you reach, the higher your score.";

    [TextArea(3, 6)] public string gameplayLoop =
        "Gameplay Loop: open QGroundControl -> switch to Guided -> take off -> practice " +
        "control -> reach the delivery target -> earn reward and progress.";

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
            ApplySelectedLevelHome();
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
            ApplySelectedLevelHome();
        }
        RefreshUI();
    }

    void ApplySelectedLevelHome()
    {
        if (levels.Count == 0)
        {
            return;
        }

        LevelEntry level = levels[Mathf.Clamp(selectedLevelIndex, 0, levels.Count - 1)];
        GameManager.SetHomeLocation(level.homeLatitude, level.homeLongitude, level.homeAltitude, level.homeYaw);
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
        ApplySelectedLevelHome();
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
            ApplySelectedLevelHome();
            if (levelDropdown != null) levelDropdown.value = selectedLevelIndex;
        }
        RefreshUI();
    }

    public void SelectLevel1()
    {
        SelectLevelByScene("LV1");
    }

    public void OnPlayPressed()
    {
        string targetScene = selectedSceneName;
        if (string.IsNullOrWhiteSpace(targetScene) && levels.Count > 0)
        {
            targetScene = levels[Mathf.Clamp(selectedLevelIndex, 0, levels.Count - 1)].sceneName;
        }

        ApplySelectedLevelHome();

        if (string.IsNullOrWhiteSpace(targetScene))
        {
            Debug.LogError("PrototypeMenuController: No selected scene.");
            return;
        }

        Debug.Log($"PrototypeMenuController: loading {targetScene}");
        SceneManager.LoadScene(targetScene);
    }
}
