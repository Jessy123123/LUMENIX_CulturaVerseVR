using UnityEngine;
using System.Collections;

public class VoiceRecorderPuteri : MonoBehaviour
{
    public Animator animator;
    public AudioSource audioSource;
    public AudioClip voiceClip;

    IEnumerator Start()
    {
        yield return new WaitForSeconds(2f);

        animator.SetBool("IsTalking", true);
        audioSource.clip = voiceClip;
        audioSource.Play();

        yield return new WaitWhile(() => audioSource.isPlaying);

        animator.SetBool("IsTalking", false);
    }
}