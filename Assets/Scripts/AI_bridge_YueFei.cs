using UnityEngine;

public class AI_bridge_YueFei : MonoBehaviour 
{
    [Header("Environment Manager")]
    public EnvironmentManager_YueFei envManager;

    // ─────────────────────────────────────────────
    //  Called by VoicePipeline after emotion
    //  detection. Accepts EmotionDetector labels:
    //  "joy","sadness","anger","fear","surprise",
    //  "disgust","neutral"
    // ─────────────────────────────────────────────
    public void OnAIResponseReceived(string emotion)
    {
        string normalized = NormalizeEmotion(emotion);

        Debug.Log("AIBridgeYueFei: " + emotion + " → " + normalized);

        if (envManager != null)
            envManager.UpdateEnvironment(normalized);
        else
            Debug.LogError("AIBridgeYueFei: envManager slot is EMPTY in Inspector!");
    }

    // ─────────────────────────────────────────────
    //  Maps emotion labels → environment states
    //    Normal    — Golden dawn, calm battlefield
    //    Sad       — Grey rain, mourning
    //    Angry     — Red sky, fire and smoke
    //    Spiritual — Ethereal glow, petals
    // ─────────────────────────────────────────────
    private string NormalizeEmotion(string raw)
    {
        switch (raw.ToLower().Trim())
        {
            case "anger":
            case "angry":
                return "Angry";

            case "sadness":
            case "sad":
            case "disgust":
            case "fear":
            case "fearful":
                return "Sad";

            case "joy":
            case "normal":
            case "neutral":
            case "surprise":
            default:
                return "Normal";
        }
    }

    // ─────────────────────────────────────────────
    //  Manual test keys (Play mode only)
    //  A = Angry   S = Sad   N = Normal
    // ─────────────────────────────────────────────
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.A)) OnAIResponseReceived("anger");     // A -> Angry
        if (Input.GetKeyDown(KeyCode.S)) OnAIResponseReceived("sadness");   // S -> Sad
        if (Input.GetKeyDown(KeyCode.N)) OnAIResponseReceived("neutral");   // N -> Normal
    }
}