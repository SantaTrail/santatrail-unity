using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class BackgroundMusic : MonoBehaviour
{
    private static BackgroundMusic instance;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.2f;
    [SerializeField] private bool playAutomatically = true;

    private AudioSource musicSource;

    private void Awake()
    {
        musicSource = GetComponent<AudioSource>();

        if (instance != null && instance != this)
        {
            // Keep one persistent player, but adopt the new scene's music.
            instance.ApplySceneMusic(
                musicSource.clip,
                musicVolume,
                playAutomatically
            );
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        ApplySceneMusic(musicSource.clip, musicVolume, playAutomatically);
    }

    private void ApplySceneMusic(
        AudioClip clip,
        float volume,
        bool shouldPlay)
    {
        if (musicSource == null)
        {
            musicSource = GetComponent<AudioSource>();
        }

        bool clipChanged = clip != null && musicSource.clip != clip;
        if (clipChanged)
        {
            musicSource.Stop();
            musicSource.clip = clip;
        }

        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.mute = false;
        musicSource.volume = Mathf.Clamp01(volume);

        if (shouldPlay && musicSource.clip != null)
        {
            if (clipChanged || !musicSource.isPlaying)
            {
                musicSource.Play();
            }

            Debug.Log(
                $"Background music playing: {musicSource.clip.name} " +
                $"(volume {musicSource.volume:0.00})"
            );
        }
        else if (shouldPlay)
        {
            Debug.LogWarning("BackgroundMusic has no AudioClip assigned.");
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}
