using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Sources")]
    public AudioSource sfxSource;

    [Header("Audio Clips")]
    public AudioClip moveSound;
    public AudioClip winSound;
    public AudioClip loseSound;
    public AudioClip checkSound;
    public AudioClip invalidMoveSound;


    private void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // giữ lại khi đổi scene
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void PlayMoveSound()
    {
        PlaySFX(moveSound);
    }

    public void PlayCheckSound()
    {
        PlaySFX(checkSound);
    }

    public void PlayWinSound()
    {
        PlaySFX(winSound);
    }

    //public void PlayInvalidMoveSound()
    //{
    //    PlaySFX(invalidMoveSound);
    //}

    private void PlaySFX(AudioClip clip)
    {
        if (clip != null && sfxSource != null)
            sfxSource.PlayOneShot(clip);
    }
}
