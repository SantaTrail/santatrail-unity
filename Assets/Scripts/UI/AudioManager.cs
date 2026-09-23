using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    public AudioSource sfxSource;

    [Header("UI")]
    public AudioClip clickClip;

    [Header("Game")]
    public AudioClip correctClip;
    public AudioClip wrongClip;

    private float baseSoundEffectsVolume = 1f;

    // to keep the click sound playing across scenes, we can use the singleton pattern
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (sfxSource != null)
            {
                baseSoundEffectsVolume = sfxSource.volume;
            }
            SantaTrailAudioSettings.SettingsChanged += ApplyVolume;
            ApplyVolume();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void PlayClick()
    {
        if (sfxSource != null && clickClip != null) sfxSource.PlayOneShot(clickClip);
    }

    public void PlayCorrect()
    {
        if (sfxSource != null && correctClip != null) sfxSource.PlayOneShot(correctClip);
    }

    public void PlayWrong()
    {
        if (sfxSource != null && wrongClip != null) sfxSource.PlayOneShot(wrongClip);
    }

    private void ApplyVolume()
    {
        if (sfxSource != null)
        {
            sfxSource.volume = baseSoundEffectsVolume *
                SantaTrailAudioSettings.SoundEffectsVolume;
        }
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        SantaTrailAudioSettings.SettingsChanged -= ApplyVolume;
        Instance = null;
    }
}
