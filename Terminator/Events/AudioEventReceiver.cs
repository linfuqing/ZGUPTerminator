using UnityEngine;

public class AudioEventReceiver : MonoBehaviour
{
    private AudioSource __audioSource;

    public AudioSource audioSource
    {
        get
        {
            if (__audioSource == null)
                __audioSource = GetComponent<AudioSource>();

            return __audioSource;
        }
    }
    
    [UnityEngine.Scripting.Preserve]
    public void PlayAudio(Object audioClip)
    {
        audioSource.PlayOneShot(audioClip as AudioClip);
    }
}
