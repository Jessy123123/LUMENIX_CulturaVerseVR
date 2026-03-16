using UnityEngine;

public class VoicePipelinePuteri : MonoBehaviour
{
    public AudioSource speaker;
    public Animator animator;

    public void ProcessAudio(AudioClip clip)
    {
        Debug.Log("Processing user audio...");

        byte[] wavData = WavUtility.FromAudioClip(clip);

        Debug.Log("Sending to Google STT...");

        GoogleSTT stt = Object.FindFirstObjectByType<GoogleSTT>();

        if (stt != null)
        {
            StartCoroutine(stt.SendAudio(wavData));
        }
    }
}