using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// EmotionDetector — HuggingFace primary, keyword-based fallback.
///
/// Primary:  HuggingFace router API (j-hartmann/emotion-english-distilroberta-base)
/// Fallback: Keyword matching (English + Malay) — runs instantly, no internet needed.
///
/// Returns one of 7 emotions:
///   joy | sadness | anger | fear | surprise | disgust | neutral
/// </summary>
public class EmotionDetector : MonoBehaviour
{
    private const string HF_MODEL_URL =
        "https://router.huggingface.co/hf-inference/models/j-hartmann/emotion-english-distilroberta-base";

    private static readonly string[] ValidEmotions =
        { "joy", "sadness", "anger", "fear", "surprise", "disgust", "neutral" };

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Compatibility overload — no HF key, goes straight to keyword fallback.
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
    /// Main method — tries HuggingFace first, falls back to keywords on any failure.
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

        // ── Try HuggingFace first ─────────────────────────────────────────
        if (!string.IsNullOrEmpty(hfApiKey))
        {
            string hfResult = null;
            yield return StartCoroutine(TryHuggingFace(replyText, hfApiKey,
                result => hfResult = result));

            if (hfResult != null)
            {
                Debug.Log("EmotionDetector: HuggingFace = " + hfResult);
                onResult?.Invoke(hfResult);
                yield break;
            }

            Debug.LogWarning("EmotionDetector: HuggingFace failed — using keyword fallback.");
        }
        else
        {
            Debug.Log("EmotionDetector: No HF key — using keyword fallback.");
        }

        // ── Fallback: keyword-based ───────────────────────────────────────
        string keywordResult = DetectEmotionKeyword(replyText);
        Debug.Log("EmotionDetector: keyword fallback = " + keywordResult);
        onResult?.Invoke(keywordResult);
    }

    // ── HuggingFace request ───────────────────────────────────────────────

    private IEnumerator TryHuggingFace(
        string replyText,
        string hfApiKey,
        System.Action<string> onResult)
    {
        // Strip non-ASCII and truncate
        string cleanText = Regex.Replace(replyText, @"[^\x20-\x7E]", " ").Trim();
        if (cleanText.Length > 200)
            cleanText = cleanText.Substring(0, 200);

        if (string.IsNullOrEmpty(cleanText))
        {
            onResult?.Invoke(null);
            yield break;
        }

        string safeText = cleanText.Replace("\"", "'").Replace("\\", " ");
        string jsonBody = "{\"inputs\":\"" + safeText + "\"}";

        UnityWebRequest request = new UnityWebRequest(HF_MODEL_URL, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + hfApiKey);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("EmotionDetector: HF error — " + request.error
                + " | " + request.downloadHandler.text);
            onResult?.Invoke(null); // null = signal to use fallback
            yield break;
        }

        string emotion = ParseHFResponse(request.downloadHandler.text);
        onResult?.Invoke(emotion ?? null);
    }

    // ── HuggingFace response parser ───────────────────────────────────────

    private string ParseHFResponse(string json)
    {
        try
        {
            MatchCollection matches = Regex.Matches(json,
                "\"label\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"score\"\\s*:\\s*([0-9.eE+\\-]+)");

            string bestLabel = null;
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

            if (bestLabel != null)
                Debug.Log("EmotionDetector: HF best = " + bestLabel
                    + " (" + bestScore.ToString("F2") + ")");

            return bestLabel; // null if nothing matched
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("EmotionDetector: HF parse error — " + e.Message);
            return null;
        }
    }

    // ── Keyword-based fallback ────────────────────────────────────────────

    private string DetectEmotionKeyword(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "neutral";

        string lower = text.ToLower();

        // Joy
        if (ContainsAny(lower,
            "happy", "happiness", "joy", "joyful", "glad", "delight", "delighted",
            "excited", "wonderful", "beautiful", "love", "lovely", "blessed",
            "grateful", "thankful", "celebrate", "smile", "laugh", "laughter",
            "gembira", "bahagia", "suka", "sukacita", "ceria", "seronok", "indah",
            "syukur", "bersyukur", "cantik", "senyum", "ketawa"))
            return "joy";

        // Sadness
        if (ContainsAny(lower,
            "sad", "sadness", "sorrow", "sorrowful", "grief", "grieve", "mourn",
            "mourning", "tears", "weep", "weeping", "cry", "crying", "heartbroken",
            "lonely", "loneliness", "despair", "hopeless", "miss", "missing", "loss",
            "sedih", "duka", "kesedihan", "menangis", "tangis", "rindu", "hilang",
            "putus asa", "sunyi", "sepi", "kehilangan", "pilu", "sengsara"))
            return "sadness";

        // Anger
        if (ContainsAny(lower,
            "anger", "angry", "furious", "fury", "rage", "enraged", "outraged",
            "hate", "hatred", "hostile", "mad", "upset", "frustrat", "betrayed",
            "marah", "kemarahan", "geram", "benci", "dendam", "murka", "berang",
            "jengkel", "naik angin", "melenting"))
            return "anger";

        // Fear
        if (ContainsAny(lower,
            "fear", "afraid", "frighten", "frightened", "scared", "terrified",
            "terror", "horror", "horrified", "dread", "panic", "anxious", "anxiety",
            "worry", "worried", "nervous", "danger", "dangerous", "threat",
            "takut", "ketakutan", "gerun", "ngeri", "seram", "panik", "bimbang",
            "risau", "cemas", "bahaya", "ancaman"))
            return "fear";

        // Surprise
        if (ContainsAny(lower,
            "surprise", "surprised", "shocking", "shocked", "amazed", "amazement",
            "astonished", "astonishment", "unexpected", "suddenly", "unbelievable",
            "terkejut", "hairan", "kagum", "ajaib", "tidak sangka",
            "mengejutkan", "luar biasa"))
            return "surprise";

        // Disgust
        if (ContainsAny(lower,
            "disgust", "disgusting", "repulsed", "repulsive", "revolting", "gross",
            "horrible", "vile", "awful", "terrible", "nasty", "wicked", "shameful",
            "jijik", "hodoh", "buruk", "keji", "hina", "menjijikkan", "kotor",
            "busuk", "teruk", "jahat", "zalim"))
            return "disgust";

        return "neutral";
    }

    private bool ContainsAny(string text, params string[] keywords)
    {
        foreach (string k in keywords)
            if (text.Contains(k))
                return true;
        return false;
    }
}