using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// EmotionDetector — Uses HuggingFace's dedicated emotion classification model.
/// Model: j-hartmann/emotion-english-distilroberta-base
/// Returns one of 7 emotions: joy | sadness | anger | fear | surprise | disgust | neutral
///
/// Why HuggingFace over Ollama:
///   - Specialized emotion model — much more accurate
///   - Zero RAM usage on your PC
///   - No Ollama needed for emotion detection
///   - Free tier: 30,000 requests/month
/// </summary>
public class EmotionDetector : MonoBehaviour
{
    private const string HF_MODEL_URL =
        "https://api-inference.huggingface.co/models/j-hartmann/emotion-english-distilroberta-base";

    private static readonly string[] ValidEmotions =
        { "joy", "sadness", "anger", "fear", "surprise", "disgust", "neutral" };

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Detects the dominant emotion in replyText using HuggingFace API.
    /// Calls onResult(emotion) when done. Falls back to "neutral" on any error.
    /// kept ollamaUrl and ollamaModel params for API compatibility with VoicePipelinePuteri.
    /// </summary>
    public IEnumerator DetectEmotion(
        string replyText,
        string ollamaUrl,       // not used — kept for compatibility
        string ollamaModel,     // not used — kept for compatibility
        System.Action<string> onResult)
    {
        yield return DetectEmotionHF(replyText, null, onResult);
    }

    /// <summary>
    /// Full version with HuggingFace API key.
    /// Called directly by VoicePipelinePuteri passing hfApiKey from config.
    /// </summary>
    public IEnumerator DetectEmotionHF(
        string replyText,
        string hfApiKey,
        System.Action<string> onResult)
    {
        if (string.IsNullOrEmpty(replyText))
        {
            onResult?.Invoke("neutral");
            yield break;
        }

        // Strip non-ASCII and truncate — HF model works best with clean English
        string cleanText = Regex.Replace(replyText, @"[^\x20-\x7E]", " ").Trim();
        if (cleanText.Length > 200)
            cleanText = cleanText.Substring(0, 200);

        if (string.IsNullOrEmpty(cleanText))
        {
            onResult?.Invoke("neutral");
            yield break;
        }

        string safeText = cleanText.Replace("\"", "'").Replace("\\", " ");
        string jsonBody = "{\"inputs\":\"" + safeText + "\"}";

        UnityWebRequest request = new UnityWebRequest(HF_MODEL_URL, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        if (!string.IsNullOrEmpty(hfApiKey))
            request.SetRequestHeader("Authorization", "Bearer " + hfApiKey);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("EmotionDetector: HuggingFace request failed — " + request.error);
            Debug.LogWarning("EmotionDetector: Response — " + request.downloadHandler.text);
            onResult?.Invoke("neutral");
            yield break;
        }

        string emotion = ParseHFResponse(request.downloadHandler.text);
        Debug.Log("EmotionDetector: detected = " + emotion);
        onResult?.Invoke(emotion);
    }

    // ── Internal parsing ──────────────────────────────────────────────────

    /// <summary>
    /// Parses HuggingFace response format:
    /// [[{"label":"joy","score":0.98},{"label":"neutral","score":0.01},...]]
    /// Returns the label with the highest score.
    /// </summary>
    private string ParseHFResponse(string json)
    {
        try
        {
            MatchCollection matches = Regex.Matches(json,
                "\"label\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"score\"\\s*:\\s*([0-9.eE+\\-]+)");

            string bestLabel = "neutral";
            float bestScore = -1f;

            foreach (Match m in matches)
            {
                string label = m.Groups[1].Value.ToLower().Trim();
                float score;
                if (!float.TryParse(m.Groups[2].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out score))
                    continue;

                bool valid = false;
                foreach (string e in ValidEmotions)
                    if (label == e) { valid = true; break; }

                if (valid && score > bestScore)
                {
                    bestScore = score;
                    bestLabel = label;
                }
            }

            Debug.Log("EmotionDetector: best = " + bestLabel + " (" + bestScore.ToString("F2") + ")");
            return bestLabel;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("EmotionDetector: parse error — " + e.Message);
            return "neutral";
        }
    }
}
