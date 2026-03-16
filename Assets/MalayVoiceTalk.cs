using UnityEngine;
using System.Collections;

public class MalayVoiceTalk : MonoBehaviour
{
    public Animator animator;
    public AudioSource audioSource;
    public AudioClip voiceClip;

    IEnumerator Start()
    {
        // Wait 2 seconds after entering environment
        yield return new WaitForSeconds(2f);

        // Start talking animation + play voice
        animator.SetBool("IsTalking", true);
        audioSource.clip = voiceClip;
        audioSource.Play();

        // Wait until the voice finishes
        yield return new WaitWhile(() => audioSource.isPlaying);

        // Return to idle
        animator.SetBool("IsTalking", false);
    }
}