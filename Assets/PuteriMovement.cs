using UnityEngine;
using System.Collections;

public class PuteriMovement : MonoBehaviour
{
    public Animator animator;
    public AudioSource audioSource;
    public AudioClip voiceClip;

    IEnumerator Start()
    {
        // Wait 2 seconds after scene starts
        yield return new WaitForSeconds(2f);

        StartTalking();
    }

    public void StartTalking()
    {
        StartCoroutine(TalkRoutine());
    }

    IEnumerator TalkRoutine()
    {
        // Start talking animation
        if (animator != null)
            animator.SetBool("IsTalking", true);

        // Play voice
        if (audioSource != null && voiceClip != null)
        {
            audioSource.clip = voiceClip;
            audioSource.Play();
        }

        // Wait until voice finishes
        yield return new WaitWhile(() => audioSource.isPlaying);

        // Return to idle
        if (animator != null)
            animator.SetBool("IsTalking", false);
    }
}