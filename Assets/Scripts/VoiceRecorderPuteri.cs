using UnityEngine;
using System.Collections;
using TMPro;

public class VoiceRecorderPuteri : MonoBehaviour
{
    [Header("Animation")]
    public Animator animator;

    [Header("Audio")]
    public AudioSource audioSource;

    [Header("Captions")]
    public TextMeshProUGUI captionText;

    [System.Serializable]
    public class VoiceClipWithCaption
    {
        public AudioClip clip;
        [TextArea(2, 5)]
        public string caption;
    }

    public VoiceClipWithCaption[] voiceLines;

    void Awake()
    {
        if (captionText != null)
            captionText.text = "";
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(2f);

        foreach (var line in voiceLines)
        {
            if (line.clip == null) continue;

            // Show caption
            if (captionText != null)
                captionText.text = line.caption;

            // Play animation + audio
            animator.SetBool("IsTalking", true);
            audioSource.clip = line.clip;
            audioSource.Play();

            yield return new WaitWhile(() => audioSource.isPlaying);

            animator.SetBool("IsTalking", false);

            // Small pause between lines
            yield return new WaitForSeconds(0.5f);
        }

        // Clear caption when done
        if (captionText != null)
            captionText.text = "";
    }
}