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
        public MissionDifficulty missionDifficulty = MissionDifficulty.Easy;
        public int deliveryCount = 5;
        public bool showTutorial = true;
        public bool autoLoadNextScene = false;
        public string nextSceneName = "";
        public float nextSceneDelaySeconds = 2f;
    }

    [Header("Level Select (Optional Dropdown)")]
    public TMP_Dropdown levelDropdown;
    public List<LevelEntry> levels = new List<LevelEntry>
    {
        new LevelEntry { label = "Level 1 - Guided Tutorial", sceneName = "LV1", homeLatitude = 52.155719, homeLongitude = 4.964212, homeAltitude = 0, homeYaw = 0 },
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
    private bool levelLockedByLauncher;
    private List<LevelConfig> resolvedLevels = new List<LevelConfig>();

    void Awake()
    {
        ResolveLevels();

        if (resolvedLevels.Count > 0)
        {
            int launchLevelIndex = FindLaunchLevelIndex();
            levelLockedByLauncher = launchLevelIndex >= 0;
            selectedLevelIndex = launchLevelIndex >= 0
                ? launchLevelIndex
                : levelDropdown != null
                    ? Mathf.Clamp(levelDropdown.value, 0, resolvedLevels.Count - 1)
                    : 0;
            ApplySelectedLevel(resolvedLevels[selectedLevelIndex]);
        }

        SetupLevelDropdown();
        SetupButtons();
        RefreshUI();
    }

    void ResolveLevels()
    {
        resolvedLevels = new List<LevelConfig>();

        if (LevelConfigLoader.TryLoadLevels(out List<LevelConfig> loadedLevels))
        {
            resolvedLevels.AddRange(loadedLevels);
            Debug.Log(
                $"PrototypeMenuController: loaded {resolvedLevels.Count} levels from the level database."
            );
            return;
        }

        foreach (LevelEntry legacyLevel in levels)
        {
            resolvedLevels.Add(ConvertLegacyLevel(legacyLevel));
        }

        Debug.Log($"PrototypeMenuController: using {resolvedLevels.Count} legacy scene levels.");
    }

    void SetupLevelDropdown()
    {
        if (levelDropdown == null) return;

        levelDropdown.ClearOptions();
        var options = new List<TMP_Dropdown.OptionData>();
        for (int i = 0; i < resolvedLevels.Count; i++)
        {
            options.Add(new TMP_Dropdown.OptionData(resolvedLevels[i].label));
        }

        levelDropdown.AddOptions(options);
        levelDropdown.onValueChanged.RemoveListener(OnDropdownChanged);
        levelDropdown.onValueChanged.AddListener(OnDropdownChanged);
        selectedLevelIndex = Mathf.Clamp(selectedLevelIndex, 0, Mathf.Max(0, resolvedLevels.Count - 1));
        levelDropdown.SetValueWithoutNotify(selectedLevelIndex);
        levelDropdown.interactable = !levelLockedByLauncher;

        if (resolvedLevels.Count > 0)
        {
            ApplySelectedLevel(resolvedLevels[selectedLevelIndex]);
        }
    }

    private int FindLaunchLevelIndex()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        string requestedLevel = null;

        for (int i = 0; i < arguments.Length; i++)
        {
            const string option = "--santatrail-level";
            if (string.Equals(arguments[i], option, StringComparison.OrdinalIgnoreCase) &&
                i + 1 < arguments.Length)
            {
                requestedLevel = arguments[i + 1];
                break;
            }

            string prefix = option + "=";
            if (arguments[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                requestedLevel = arguments[i].Substring(prefix.Length);
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(requestedLevel))
        {
            return -1;
        }

        return resolvedLevels.FindIndex(level =>
            string.Equals(level.id, requestedLevel, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(level.sceneName, requestedLevel, StringComparison.OrdinalIgnoreCase));
    }

    void SetupButtons()
    {
        if (playButton == null) return;

        playButton.onClick.RemoveListener(OnPlayPressed);
        playButton.onClick.AddListener(OnPlayPressed);
    }

    void OnDropdownChanged(int index)
    {
        if (levelLockedByLauncher) return;

        selectedLevelIndex = index;
        if (resolvedLevels.Count > 0)
        {
            ApplySelectedLevel(resolvedLevels[Mathf.Clamp(selectedLevelIndex, 0, resolvedLevels.Count - 1)]);
        }
        RefreshUI();
    }

    void ApplySelectedLevel(LevelConfig level)
    {
        if (level == null)
        {
            return;
        }

        selectedSceneName = level.sceneName;
        GameManager.ApplyLevel(level);
    }

    void RefreshUI()
    {
        if (storyText != null) storyText.text = gameStory;
        if (loopText != null) loopText.text = gameplayLoop;

        if (selectedLevelText != null)
        {
            if (resolvedLevels.Count == 0)
            {
                selectedLevelText.text = "No levels configured";
            }
            else
            {
                var lvl = resolvedLevels[Mathf.Clamp(selectedLevelIndex, 0, resolvedLevels.Count - 1)];
                selectedLevelText.text = $"Selected: {lvl.label}";
            }
        }
    }

    public void SelectLevel(int index)
    {
        if (levelLockedByLauncher || resolvedLevels.Count == 0) return;

        selectedLevelIndex = Mathf.Clamp(index, 0, resolvedLevels.Count - 1);
        ApplySelectedLevel(resolvedLevels[selectedLevelIndex]);
        if (levelDropdown != null) levelDropdown.value = selectedLevelIndex;
        RefreshUI();
    }

    public void SelectLevelByScene(string sceneName)
    {
        if (levelLockedByLauncher) return;

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("PrototypeMenuController: sceneName is empty.");
            return;
        }

        selectedSceneName = sceneName;
        int foundIndex = resolvedLevels.FindIndex(l => l.sceneName == sceneName);
        if (foundIndex >= 0)
        {
            selectedLevelIndex = foundIndex;
            ApplySelectedLevel(resolvedLevels[selectedLevelIndex]);
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
        if (string.IsNullOrWhiteSpace(targetScene) && resolvedLevels.Count > 0)
        {
            targetScene = resolvedLevels[Mathf.Clamp(selectedLevelIndex, 0, resolvedLevels.Count - 1)].sceneName;
        }

        if (resolvedLevels.Count > 0)
        {
            ApplySelectedLevel(resolvedLevels[Mathf.Clamp(selectedLevelIndex, 0, resolvedLevels.Count - 1)]);
        }

        if (string.IsNullOrWhiteSpace(targetScene))
        {
            Debug.LogError("PrototypeMenuController: No selected scene.");
            return;
        }

        Debug.Log($"PrototypeMenuController: loading {targetScene}");
        SceneManager.LoadScene(targetScene);
    }

    private static LevelConfig ConvertLegacyLevel(LevelEntry legacyLevel)
    {
        return new LevelConfig
        {
            label = legacyLevel.label,
            sceneName = legacyLevel.sceneName,
            homeLatitude = legacyLevel.homeLatitude,
            homeLongitude = legacyLevel.homeLongitude,
            homeAltitude = legacyLevel.homeAltitude,
            homeYaw = legacyLevel.homeYaw,
            missionDifficulty = MissionDifficulty.Easy,
            deliveryCount = 5,
            showTutorial = true,
            autoLoadNextScene = false,
            nextSceneName = "",
            nextSceneDelaySeconds = 2f
        };
    }
}
