using System;
using UnityEngine;

/// <summary>
/// Stores the player's audio preferences and applies the global volume.
/// Music and sound-effect sources use their matching category values.
/// </summary>
public static class SantaTrailAudioSettings
{
    private const string MasterVolumeKey = "SantaTrail.Audio.MasterVolume";
    private const string MusicVolumeKey = "SantaTrail.Audio.MusicVolume";
    private const string SoundEffectsVolumeKey = "SantaTrail.Audio.SoundEffectsVolume";

    private static bool loaded;
    private static float masterVolume = 1f;
    private static float musicVolume = 1f;
    private static float soundEffectsVolume = 1f;

    public static event Action SettingsChanged;

    public static float MasterVolume
    {
        get
        {
            EnsureLoaded();
            return masterVolume;
        }
        set => SetVolume(MasterVolumeKey, ref masterVolume, value, true);
    }

    public static float MusicVolume
    {
        get
        {
            EnsureLoaded();
            return musicVolume;
        }
        set => SetVolume(MusicVolumeKey, ref musicVolume, value, false);
    }

    public static float SoundEffectsVolume
    {
        get
        {
            EnsureLoaded();
            return soundEffectsVolume;
        }
        set => SetVolume(SoundEffectsVolumeKey, ref soundEffectsVolume, value, false);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        loaded = false;
        masterVolume = 1f;
        musicVolume = 1f;
        soundEffectsVolume = 1f;
        SettingsChanged = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedSettings()
    {
        EnsureLoaded();
    }

    public static void Save()
    {
        PlayerPrefs.Save();
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;

        masterVolume = ReadVolume(MasterVolumeKey);
        musicVolume = ReadVolume(MusicVolumeKey);
        soundEffectsVolume = ReadVolume(SoundEffectsVolumeKey);
        loaded = true;
        AudioListener.volume = masterVolume;
    }

    private static float ReadVolume(string key)
    {
        return Mathf.Clamp01(PlayerPrefs.GetFloat(key, 1f));
    }

    private static void SetVolume(
        string key,
        ref float currentValue,
        float requestedValue,
        bool applyToListener)
    {
        EnsureLoaded();
        float clampedValue = Mathf.Clamp01(requestedValue);
        if (Mathf.Approximately(currentValue, clampedValue)) return;

        currentValue = clampedValue;
        PlayerPrefs.SetFloat(key, clampedValue);
        if (applyToListener)
        {
            AudioListener.volume = clampedValue;
        }

        SettingsChanged?.Invoke();
    }
}
