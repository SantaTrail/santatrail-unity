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

    // to keep the click sound playing across scenes, we can use the singleton pattern
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void PlayClick()
    {
        sfxSource.PlayOneShot(clickClip);
    }

    public void PlayCorrect()
    {
        sfxSource.PlayOneShot(correctClip);
    }

    public void PlayWrong()
    {
        sfxSource.PlayOneShot(wrongClip);
    }
}
