using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// EmotionDetector — HuggingFace primary, keyword-based fallback.
///
/// Primary:  HuggingFace router API (j-hartmann/emotion-english-distilroberta-base)
/// Fallback: Keyword matching (English + Malay + Chinese Simplified/Traditional)
///
/// Returns one of 7 emotions:
///   joy | sadness | anger | fear | surprise | disgust | neutral
///
/// Added DetectSentimentGoogle for Google Cloud NLP API integration.
/// </summary>
public class EmotionDetector : MonoBehaviour
{
    public string currentEmotion = "neutral";
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
        string ollamaUrl,
        string ollamaModel,
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
            currentEmotion = "neutral";
            onResult?.Invoke("neutral");
            yield break;
        }

        // ── Try HuggingFace first ─────────────────────────────────────────
        // The HuggingFace model is English-only. If the text has Chinese characters, skip directly to keywords!
        bool isChinese = Regex.IsMatch(replyText, @"[\u4e00-\u9fff]");

        if (!string.IsNullOrEmpty(hfApiKey) && !isChinese)
        {
            string hfResult = null;
            yield return StartCoroutine(TryHuggingFace(replyText, hfApiKey,
                result => hfResult = result));

            if (hfResult != null)
            {
                currentEmotion = hfResult;
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
        currentEmotion = keywordResult;
        onResult?.Invoke(keywordResult);
    }

    // ── Google Cloud NLP Sentiment Analysis ────────────────────────────────
    public IEnumerator DetectSentimentGoogle(
        string replyText,
        string googleApiKey,
        System.Action<string> onResult)
    {
        if (string.IsNullOrEmpty(replyText))
        {
            onResult?.Invoke("neutral");
            currentEmotion= "neutral";
            yield break;
        }

        if (string.IsNullOrEmpty(googleApiKey))
        {
            Debug.LogWarning("EmotionDetector: No Google API key — using keyword fallback.");
            onResult?.Invoke(DetectEmotionKeyword(replyText));
            currentEmotion = DetectEmotionKeyword(replyText);
            yield break;
        }

        // Clean text
        string cleanText = Regex.Replace(replyText, @"[\x00-\x1F\x7F]", " ").Trim();
        if (cleanText.Length > 500)
            cleanText = cleanText.Substring(0, 500);

        string safeText = cleanText
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");

        string url = "https://language.googleapis.com/v1/documents:analyzeSentiment?key=" + googleApiKey;
        string json = "{ \"document\": { \"type\": \"PLAIN_TEXT\", \"content\": \"" + safeText + "\" }, \"encodingType\": \"UTF8\" }";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("EmotionDetector: Google NLP error — " + request.error + " | " + request.downloadHandler.text);
            string keywordFallback = DetectEmotionKeyword(replyText);
            onResult?.Invoke(keywordFallback);
            currentEmotion = keywordFallback;
            yield break;
        }

        string result = request.downloadHandler.text;
        
        // Parse score and magnitude using regex
        Match scoreMatch = Regex.Match(result, "\"score\"\\s*:\\s*([0-9.eE+\\-]+)");
        Match magMatch = Regex.Match(result, "\"magnitude\"\\s*:\\s*([0-9.eE+\\-]+)");

        if (scoreMatch.Success && magMatch.Success)
        {
            float score = float.Parse(scoreMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            float mag = float.Parse(magMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

            Debug.Log($"EmotionDetector: Google NLP Score={score}, Magnitude={mag}");

            if (score >= 0.1f)
            {
                currentEmotion = "joy";
                onResult?.Invoke("joy");
            }
            else if (score < -0.1f && mag > 0.4f)
            {
                currentEmotion = "anger";
                onResult?.Invoke("anger");
            }
            else if (score < -0.1f && mag <= 0.4f)
            {
                currentEmotion = "sadness";
                onResult?.Invoke("sadness");
            }
            else
            {
                // Score is between -0.15 and 0.1 (Mixed or Neutral)
                if (mag > 0.2f)
                {
                    string keywordFallback = DetectEmotionKeyword(replyText);
                    if (keywordFallback != "neutral")
                    {
                        Debug.Log($"EmotionDetector: Mixed NLP Score! using Keyword Tie-breaker -> {keywordFallback}");
                        onResult?.Invoke(keywordFallback);
                        currentEmotion = keywordFallback;
                        yield break;
                    }
                }
                
                currentEmotion = "neutral";
                onResult?.Invoke("neutral");
            }
        }
        else
        {
            onResult?.Invoke(DetectEmotionKeyword(replyText));
                currentEmotion = DetectEmotionKeyword(replyText);
                Debug.LogWarning("EmotionDetector: Google NLP parse error — missing score/magnitude. Using keyword fallback.");
        }
    }

    // ── HuggingFace request ───────────────────────────────────────────────

    private IEnumerator TryHuggingFace(
        string replyText,
        string hfApiKey,
        System.Action<string> onResult)
    {
        // FIX: Allow Unicode (Chinese, Malay accents, etc.) — only strip actual control chars
        // Old code used [^\x20-\x7E] which stripped ALL non-ASCII including Chinese characters.
        string cleanText = Regex.Replace(replyText, @"[\x00-\x1F\x7F]", " ").Trim();

        if (cleanText.Length > 500)
            cleanText = cleanText.Substring(0, 500);

        if (string.IsNullOrEmpty(cleanText))
        {
            onResult?.Invoke(null);
            yield break;
        }

        // Escape only what JSON requires
        string safeText = cleanText
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");

        string jsonBody = "{\"inputs\":\"" + safeText + "\"}";

        // FIX: Use UTF-8 encoding to preserve Chinese characters in the request body
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        UnityWebRequest request = new UnityWebRequest(HF_MODEL_URL, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
        request.SetRequestHeader("Authorization", "Bearer " + hfApiKey);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("EmotionDetector: HF error — " + request.error
                + " | " + request.downloadHandler.text);
            onResult?.Invoke(null);
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

            return bestLabel;
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
        
        int joyScore = CountKeywords(text, lower,
            "happy", "happiness", "joy", "joyful", "glad", "delight", "delighted", "excited", "wonderful", "beautiful", "love", "lovely", "blessed", "grateful", "thankful", "celebrate", "smile", "laugh", "laughter",
            "gembira", "bahagia", "suka", "sukacita", "ceria", "seronok", "indah", "syukur", "bersyukur", "cantik", "senyum", "ketawa",
            "高兴", "快乐", "开心", "喜悦", "幸福", "欢喜", "兴奋", "感激", "感谢", "美好", "欢笑", "爱", "喜欢", "庆祝", "微笑", "欣慰",
            "高興", "快樂", "開心", "喜悅", "幸福", "歡喜", "興奮", "感激", "感謝", "美好", "歡笑", "愛", "喜歡", "慶祝", "微笑", "欣慰");

        int sadScore = CountKeywords(text, lower,
            "sad", "sadness", "sorrow", "sorrowful", "grief", "grieve", "mourn", "mourning", "tears", "weep", "weeping", "cry", "crying", "heartbroken", "lonely", "loneliness", "despair", "hopeless", "miss", "missing", "loss",
            "sedih", "duka", "kesedihan", "menangis", "tangis", "rindu", "hilang", "putus asa", "sunyi", "sepi", "kehilangan", "pilu", "sengsara",
            "悲伤", "伤心", "难过", "哭泣", "哭", "忧愁", "孤独", "绝望", "痛苦", "失落", "思念", "心碎", "哀愁", "泪水", "遗憾", "痛心",
            "悲傷", "傷心", "難過", "哭泣", "哭", "憂愁", "孤獨", "絕望", "痛苦", "失落", "思念", "心碎", "哀愁", "淚水", "遺憾", "痛心");

        int angerScore = CountKeywords(text, lower,
            "anger", "angry", "furious", "fury", "rage", "enraged", "outraged", "hate", "hatred", "hostile", "mad", "upset", "frustrat", "betrayed", "ambitious", "ambition", "determined", "determination", "passionate", "passion",
            "marah", "kemarahan", "geram", "benci", "dendam", "murka", "berang", "jengkel", "naik angin", "melenting", "bersemangat", "semangat", "berazam", "azam",
            "愤怒", "生气", "发火", "恼火", "愤慨", "怒火", "仇恨", "憎恨", "暴怒", "气愤", "不满", "讨厌", "志气", "抱负", "野心", "雄心", "决心", "激情", "坚定",
            "憤怒", "生氣", "發火", "惱火", "憤慨", "怒火", "仇恨", "憎恨", "暴怒", "氣憤", "不滿", "討厭", "志氣", "抱負", "野心", "雄心", "決心", "激情", "堅定");

        int fearScore = CountKeywords(text, lower,
            "fear", "afraid", "frighten", "frightened", "scared", "terrified", "terror", "horror", "horrified", "dread", "panic", "anxious", "anxiety", "worry", "worried", "nervous", "danger", "dangerous", "threat",
            "takut", "ketakutan", "gerun", "ngeri", "seram", "panik", "bimbang", "risau", "cemas", "bahaya", "ancaman",
            "害怕", "恐惧", "恐慌", "惊恐", "担心", "忧虑", "紧张", "危险", "威胁", "战战兢兢", "毛骨悚然",
            "害怕", "恐懼", "恐慌", "驚恐", "擔心", "憂慮", "緊張", "危險", "威脅", "戰戰兢兢", "毛骨悚然");

        // Priority logic for mixed emotions:
        // Anger/Sadness (Negative) > Joy/Fear
        int maxScore = 0;
        string result = "neutral";

        if (angerScore > maxScore) { maxScore = angerScore; result = "anger"; }
        if (sadScore > maxScore) { maxScore = sadScore; result = "sadness"; }
        
        // If joy ties with negative emotions, negative emotion wins (priority).
        // Only override if joy is STRICTLY greater.
        if (joyScore > maxScore) { maxScore = joyScore; result = "joy"; }
        if (fearScore > maxScore) { maxScore = fearScore; result = "fear"; }

        return maxScore > 0 ? result : "neutral";
    }

    private int CountKeywords(string text, string lowerText, params string[] keywords)
    {
        int count = 0;
        foreach (string k in keywords)
        {
            if (lowerText.Contains(k.ToLower()) || text.Contains(k))
                count++;
        }
        return count;
    }
}