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

        // ── Joy ───────────────────────────────────────────────────────────
        if (ContainsAny(lower,
            // English
            "happy", "happiness", "joy", "joyful", "glad", "delight", "delighted",
            "excited", "wonderful", "beautiful", "love", "lovely", "blessed",
            "grateful", "thankful", "celebrate", "smile", "laugh", "laughter",
            // Malay
            "gembira", "bahagia", "suka", "sukacita", "ceria", "seronok", "indah",
            "syukur", "bersyukur", "cantik", "senyum", "ketawa") ||
            ContainsAny(text,
            // Chinese Simplified
            "高兴", "快乐", "开心", "喜悦", "幸福", "欢喜", "兴奋", "感激",
            "感谢", "美好", "欢笑", "爱", "喜欢", "庆祝", "微笑",
            // Chinese Traditional
            "高興", "快樂", "開心", "喜悅", "幸福", "歡喜", "興奮", "感激",
            "感謝", "美好", "歡笑", "愛", "喜歡", "慶祝", "微笑"))
            return "joy";

        // ── Sadness ───────────────────────────────────────────────────────
        if (ContainsAny(lower,
            // English
            "sad", "sadness", "sorrow", "sorrowful", "grief", "grieve", "mourn",
            "mourning", "tears", "weep", "weeping", "cry", "crying", "heartbroken",
            "lonely", "loneliness", "despair", "hopeless", "miss", "missing", "loss",
            // Malay
            "sedih", "duka", "kesedihan", "menangis", "tangis", "rindu", "hilang",
            "putus asa", "sunyi", "sepi", "kehilangan", "pilu", "sengsara") ||
            ContainsAny(text,
            // Chinese Simplified
            "悲伤", "伤心", "难过", "哭泣", "哭", "忧愁", "孤独", "绝望",
            "痛苦", "失落", "思念", "心碎", "哀愁", "泪水",
            // Chinese Traditional
            "悲傷", "傷心", "難過", "哭泣", "哭", "憂愁", "孤獨", "絕望",
            "痛苦", "失落", "思念", "心碎", "哀愁", "淚水"))
            return "sadness";

        // ── Anger (includes Passion / Ambition / Determination) ────────────────────
        if (ContainsAny(lower,
            // English
            "anger", "angry", "furious", "fury", "rage", "enraged", "outraged",
            "hate", "hatred", "hostile", "mad", "upset", "frustrat", "betrayed",
            "ambitious", "ambition", "determined", "determination", "passionate", "passion",
            // Malay
            "marah", "kemarahan", "geram", "benci", "dendam", "murka", "berang",
            "jengkel", "naik angin", "melenting", "bersemangat", "semangat", "berazam", "azam",
            // Chinese Simplified
            "愤怒", "生气", "发火", "恼火", "愤慨", "怒火", "仇恨", "憎恨",
            "暴怒", "气愤", "不满", "讨厌", "志气", "抱负", "野心", "雄心", "决心", "激情", "坚定",
            // Chinese Traditional
            "憤怒", "生氣", "發火", "惱火", "憤慨", "怒火", "仇恨", "憎恨",
            "暴怒", "氣憤", "不滿", "討厭", "志氣", "抱負", "野心", "雄心", "決心", "激情", "堅定"))
            return "anger";

        // ── Fear ──────────────────────────────────────────────────────────
        if (ContainsAny(lower,
            // English
            "fear", "afraid", "frighten", "frightened", "scared", "terrified",
            "terror", "horror", "horrified", "dread", "panic", "anxious", "anxiety",
            "worry", "worried", "nervous", "danger", "dangerous", "threat",
            // Malay
            "takut", "ketakutan", "gerun", "ngeri", "seram", "panik", "bimbang",
            "risau", "cemas", "bahaya", "ancaman") ||
            ContainsAny(text,
            // Chinese Simplified
            "害怕", "恐惧", "恐慌", "惊恐", "担心", "忧虑", "紧张", "危险",
            "威胁", "战战兢兢", "毛骨悚然",
            // Chinese Traditional
            "害怕", "恐懼", "恐慌", "驚恐", "擔心", "憂慮", "緊張", "危險",
            "威脅", "戰戰兢兢", "毛骨悚然"))
            return "fear";

        // ── Surprise ──────────────────────────────────────────────────────
        if (ContainsAny(lower,
            // English
            "surprise", "surprised", "shocking", "shocked", "amazed", "amazement",
            "astonished", "astonishment", "unexpected", "suddenly", "unbelievable",
            // Malay
            "terkejut", "hairan", "kagum", "ajaib", "tidak sangka",
            "mengejutkan", "luar biasa") ||
            ContainsAny(text,
            // Chinese Simplified
            "惊讶", "惊喜", "震惊", "吃惊", "意外", "没想到", "不可思议",
            "突然", "出乎意料",
            // Chinese Traditional
            "驚訝", "驚喜", "震驚", "吃驚", "意外", "沒想到", "不可思議",
            "突然", "出乎意料"))
            return "surprise";

        // ── Disgust ───────────────────────────────────────────────────────
        if (ContainsAny(lower,
            // English
            "disgust", "disgusting", "repulsed", "repulsive", "revolting", "gross",
            "horrible", "vile", "awful", "terrible", "nasty", "wicked", "shameful",
            // Malay
            "jijik", "hodoh", "buruk", "keji", "hina", "menjijikkan", "kotor",
            "busuk", "teruk", "jahat", "zalim") ||
            ContainsAny(text,
            // Chinese Simplified
            "恶心", "厌恶", "反感", "恶劣", "丑陋", "肮脏", "令人作呕",
            "可耻", "卑鄙", "糟糕",
            // Chinese Traditional
            "噁心", "厭惡", "反感", "惡劣", "醜陋", "骯髒", "令人作嘔",
            "可恥", "卑鄙", "糟糕"))
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