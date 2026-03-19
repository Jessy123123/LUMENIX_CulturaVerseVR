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
            case "joy":
            case "normal":
            case "neutral":
                return "Normal";

            case "sadness":
            case "sad":
            case "disgust":
                return "Sad";

            case "anger":
            case "angry":
                return "Angry";

            case "fear":
            case "fearful":
            case "surprise":
                return "Spiritual";

            default:
                return "Normal";
        }
    }

    // ─────────────────────────────────────────────
    //  Manual test keys (Play mode only)
    //  G = Sad   A = Angry   S = Spiritual   N = Normal
    // ─────────────────────────────────────────────
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.G)) OnAIResponseReceived("sadness");
        if (Input.GetKeyDown(KeyCode.A)) OnAIResponseReceived("anger");
        if (Input.GetKeyDown(KeyCode.S)) OnAIResponseReceived("surprise");
        if (Input.GetKeyDown(KeyCode.N)) OnAIResponseReceived("neutral");
    }
}