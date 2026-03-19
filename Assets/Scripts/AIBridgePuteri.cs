using UnityEngine;

public class AIBridgePuteri : MonoBehaviour
{
    [Header("Environment Manager")]
    public EnvironmentManagerPuteri envManager;

    // ─────────────────────────────────────────────
    //  Called by VoicePipelinePuteri after emotion
    //  detection. Accepts both formats:
    //    EmotionDetector → "joy", "sadness", "anger",
    //                      "fear", "surprise", "disgust", "neutral"
    //    Manual test keys → "Sad", "Angry", "Normal"
    // ─────────────────────────────────────────────
    public void OnAIResponseReceived(string emotion)
    {
        string normalized = NormalizeEmotion(emotion);

        Debug.Log("AIBridgePuteri: emotion = " + emotion + " → " + normalized);

        if (envManager != null)
            envManager.UpdateEnvironment(normalized);
        else
            Debug.LogError("AIBridgePuteri: EnvironmentManagerPuteri slot is EMPTY!");
    }

    // ─────────────────────────────────────────────
    //  Maps all emotion labels to the 3 states
    //  your EnvironmentManagerPuteri understands:
    //    Normal | Sad | Angry
    //  Note: Fearful → Sad (reuses GloomyEnvironment)
    // ─────────────────────────────────────────────
    private string NormalizeEmotion(string raw)
    {
        switch (raw.ToLower().Trim())
        {
            case "joy":
            case "surprise":
            case "normal":
            case "neutral":
                return "Normal";

            case "sadness":
            case "sad":
            case "disgust":
            case "fear":        // reuses GloomyEnvironment
            case "fearful":     // reuses GloomyEnvironment
                return "Sad";

            case "anger":
            case "angry":
                return "Angry";

            default:
                return "Normal";
        }
    }

    // ─────────────────────────────────────────────
    //  Manual test keys (Editor / demo use only)
    //  G = Gloomy/Sad, A = Angry, N = Normal
    // ─────────────────────────────────────────────
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.G)) OnAIResponseReceived("sadness");   // Gloomy
        if (Input.GetKeyDown(KeyCode.A)) OnAIResponseReceived("anger");     // Angry
        if (Input.GetKeyDown(KeyCode.N)) OnAIResponseReceived("neutral");   // Normal
    }
}