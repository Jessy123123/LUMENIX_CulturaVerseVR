using UnityEngine;
using UnityEngine.UI;
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

    [Header("Scroll View")]
    [Tooltip("Assign the ScrollRect component from your Scroll View here")]
    public ScrollRect captionScrollRect;

    [Header("Fonts")]
    public TMP_FontAsset fontDefault;

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

            // ✅ Set default font for Malay/English intro lines
            if (captionText != null && fontDefault != null)
                captionText.font = fontDefault;

            // ✅ Play audio and typewriter simultaneously
            animator.SetBool("IsTalking", true);
            audioSource.clip = line.clip;
            audioSource.Play();

            // ✅ Run typewriter in parallel (non-blocking)
            StartCoroutine(TypewriterSync(line.caption, line.clip.length));

            // ✅ Wait for audio to finish (ensures animation stays on for full clip)
            yield return new WaitWhile(() => audioSource.isPlaying);

            animator.SetBool("IsTalking", false);

            yield return new WaitForSeconds(0.5f);
        }
    }

    // ✅ Typewriter synced to audio — appends to scroll history
    IEnumerator TypewriterSync(string fullText, float audioDuration)
    {
        if (captionText == null) yield break;

        int totalChars = fullText.Length;
        if (totalChars == 0) yield break;

        float delayPerChar = audioDuration / totalChars;
        delayPerChar = Mathf.Clamp(delayPerChar, 0.01f, 0.08f);

        // ✅ Preserve existing history, append new line
        string prefix = string.IsNullOrEmpty(captionText.text)
            ? ""
            : captionText.text + "\n";

        for (int i = 0; i < totalChars; i++)
        {
            captionText.text = prefix + fullText.Substring(0, i + 1);
            ScrollToBottom();
            yield return new WaitForSeconds(delayPerChar);
        }

        // Guarantee full text is shown
        captionText.text = prefix + fullText;
        ScrollToBottom();
    }

    void ScrollToBottom()
    {
        if (captionScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            captionScrollRect.verticalNormalizedPosition = 0f;
        }
    }
}