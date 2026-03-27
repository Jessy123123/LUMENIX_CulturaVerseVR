using UnityEngine;
using TMPro;

/// <summary>
/// DialogueDebugger — attach this to your NPC GameObject to verify
/// that dialogueText, AudioSource, and Animator are all correctly assigned.
///
/// HOW TO USE:
///   1. Attach this script to the same GameObject as VoicePipeline
///   2. In the Inspector, assign the same TMP Text object to 'dialogueText'
///   3. Press Play — check the Console for green ✓ or red ❌ messages
///   4. Remove this script once everything is confirmed working
/// </summary>
public class DialogueDebugger : MonoBehaviour
{
    [Header("Assign the same objects as VoicePipeline")]
    public TextMeshProUGUI dialogueText;
    public AudioSource characterVoice;
    public Animator characterAnimator;

    void Start()
    {
        Debug.Log("=== DialogueDebugger ===");

        // ── dialogueText ──────────────────────────────────────────────────
        if (dialogueText == null)
        {
            Debug.LogError("❌ dialogueText is NULL — drag your TMP Text object into the slot in the Inspector!");
        }
        else
        {
            Debug.Log("✅ dialogueText is assigned: " + dialogueText.gameObject.name);
            Debug.Log("   Is Active: " + dialogueText.gameObject.activeSelf);
            Debug.Log("   Current text: '" + dialogueText.text + "'");

            // Write a test message so you can see it on screen
            dialogueText.gameObject.SetActive(true);
            dialogueText.text = "✅ DialogueDebugger: Text is working!";
            Debug.Log("✅ Test message written to dialogueText.");
        }

        // ── AudioSource ───────────────────────────────────────────────────
        if (characterVoice == null)
            Debug.LogError("❌ characterVoice (AudioSource) is NULL — assign it in the Inspector!");
        else
            Debug.Log("✅ characterVoice is assigned: " + characterVoice.gameObject.name);

        // ── Animator ──────────────────────────────────────────────────────
        if (characterAnimator == null)
            Debug.LogWarning("⚠️ characterAnimator is NULL — animations will be skipped (optional).");
        else
            Debug.Log("✅ characterAnimator is assigned: " + characterAnimator.gameObject.name);

        // ── Microphone ────────────────────────────────────────────────────
#if !UNITY_WEBGL
        if (Microphone.devices.Length == 0)
            Debug.LogError("❌ No microphone found on this device!");
        else
        {
            Debug.Log("✅ Microphone(s) found:");
            foreach (string d in Microphone.devices)
                Debug.Log("   - " + d);
        }
#endif

        // ── config.json ───────────────────────────────────────────────────
        string configPath = Application.streamingAssetsPath + "/config.json";
        if (!System.IO.File.Exists(configPath))
            Debug.LogError("❌ config.json NOT found at: " + configPath);
        else
            Debug.Log("✅ config.json found at: " + configPath);

        Debug.Log("=== DialogueDebugger done ===");
    }
}