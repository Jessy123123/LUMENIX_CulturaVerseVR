using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;


public class VoicePipelinePuteri : MonoBehaviour
{
    [Header("Environment Bridge")]
    public AIBridgePuteri bridge; 

    // ── Fields loaded from shared config.json ──
    private string googleApiKey;
    private string ollamaUrl;
    private string ollamaModel;

    private string ttsLanguageCodeMs;
    private string ttsVoiceNameMs;
    private string ttsLanguageCodeZh;
    private string ttsVoiceNameZh;
    private string ttsLanguageCodeEn;
    private string ttsVoiceNameEn;

    private string promptMs;
    private string promptZh;
    private string promptEn;

    // ── Language detection ──
    // Puteri default is Malay. Possible values: "ms-MY" | "zh-CN" | "en-US"
    private string detectedLanguage = "ms-MY";

    // ── Recording state ──
    private AudioClip clip;
    private bool recording = false;
    private bool isProcessing = false;
    private string statusMessage = "⏳ Puteri is speaking...";

    // ── Assign in Inspector ──
    public AudioSource characterVoice;
    public Animator characterAnimator;

    // ─────────────────────────────────────────────
    //  Data classes
    // ─────────────────────────────────────────────

    [System.Serializable]
    private class ConfigData
    {
        public string googleApiKey = "";
        public string ollamaUrl = "";
        public string ollamaModel = "";

        public string ttsLanguageCodeMs = "";
        public string ttsVoiceNameMs = "";
        public string ttsLanguageCodeZh = "";
        public string ttsVoiceNameZh = "";
        public string ttsLanguageCodeEn = "";
        public string ttsVoiceNameEn = "";

        public string puteriPromptMs = "";
        public string puteriPromptZh = "";
        public string puteriPromptEn = "";
    }

    [System.Serializable]
    private class STTResponse
    {
        public Result[] results = null;

        [System.Serializable]
        public class Result
        {
            public Alternative[] alternatives = null;

            [System.Serializable]
            public class Alternative
            {
                public string transcript = "";
            }
        }
    }

    [System.Serializable]
    private class OllamaRequest
    {
        public string model;
        public string prompt;
        public bool stream;
    }

    [System.Serializable]
    private class OllamaResponse
    {
        public string response = "";
    }

    [System.Serializable]
    private class TTSResponse
    {
        public string audioContent = "";
    }

    // ─────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────

    void Start()
    {
        LoadConfig();

        foreach (string device in Microphone.devices)
            Debug.Log("Mic found: " + device);

        isProcessing = true;
        statusMessage = "⏳ Puteri is speaking...";
        StartCoroutine(WaitForIntro());
    }

    IEnumerator WaitForIntro()
    {
        yield return new WaitForSeconds(2.5f);

        while (characterVoice.isPlaying)
            yield return null;

        yield return new WaitForSeconds(0.5f);

        ResetToIdle();
        Debug.Log("Intro finished — ready for questions!");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && !isProcessing)
            StartRecording();

        if (Input.GetKeyUp(KeyCode.Space) && recording)
            StopRecording();
    }

    void OnGUI()
    {
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.Box(new Rect(10, 10, 420, 60), "");

        if (recording) GUI.color = Color.red;
        else if (isProcessing) GUI.color = Color.yellow;
        else GUI.color = Color.green;

        GUIStyle style = new GUIStyle();
        style.fontSize = 20;
        style.normal.textColor = GUI.color;
        style.padding = new RectOffset(10, 10, 10, 10);

        GUI.Label(new Rect(15, 15, 410, 50), statusMessage, style);
    }

    // ─────────────────────────────────────────────
    //  Config — reads shared config.json
    // ─────────────────────────────────────────────

    void LoadConfig()
    {
        string path = Application.streamingAssetsPath + "/config.json";

        if (!File.Exists(path))
        {
            Debug.LogError("config.json not found at: " + path);
            return;
        }

        string json = File.ReadAllText(path, Encoding.UTF8);
        ConfigData config = JsonUtility.FromJson<ConfigData>(json);

        googleApiKey = config.googleApiKey;
        ollamaUrl = config.ollamaUrl;
        ollamaModel = config.ollamaModel;

        ttsLanguageCodeMs = config.ttsLanguageCodeMs;
        ttsVoiceNameMs = config.ttsVoiceNameMs;
        ttsLanguageCodeZh = config.ttsLanguageCodeZh;
        ttsVoiceNameZh = config.ttsVoiceNameZh;
        ttsLanguageCodeEn = config.ttsLanguageCodeEn;
        ttsVoiceNameEn = config.ttsVoiceNameEn;

        promptMs = config.puteriPromptMs;
        promptEn = config.puteriPromptEn;
        promptEn = config.puteriPromptZh;

        Debug.Log("Puteri: config.json loaded successfully.");
    }

    // ─────────────────────────────────────────────
    //  Step 1 — Record microphone
    // ─────────────────────────────────────────────

    void StartRecording()
    {
        recording = true;
        isProcessing = false;
        detectedLanguage = "ms-MY"; // reset to Malay default for each new question
        statusMessage = "🎙️ Recording... (release SPACE to send)";
        clip = Microphone.Start(null, false, 5, 16000);
        Debug.Log("Recording...");
    }

    void StopRecording()
    {
        recording = false;
        isProcessing = true;
        statusMessage = "⏳ Processing...";
        Microphone.End(null);
        Debug.Log("Recording stopped");
        StartCoroutine(SendToSTT());
    }

    // ─────────────────────────────────────────────
    // Convert AudioClip to raw PCM bytes (LINEAR16)
    // ─────────────────────────────────────────────
    byte[] AudioClipToPCM(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        byte[] bytes = new byte[samples.Length * 2];
        int offset = 0;

        foreach (var sample in samples)
        {
            float amplified = Mathf.Clamp(sample * 3f, -1f, 1f);
            short value = (short)(amplified * short.MaxValue);
            System.BitConverter.GetBytes(value).CopyTo(bytes, offset);
            offset += 2;
        }

        return bytes;
    }

    // ─────────────────────────────────────────────
    //  Step 2 — Google Speech-to-Text
    //  Primary: ms-MY   Alternatives: zh-CN, en-US
    // ─────────────────────────────────────────────

    IEnumerator SendToSTT()
    {
        if (string.IsNullOrEmpty(googleApiKey))
        {
            Debug.LogError("Google API key is not loaded.");
            ResetToIdle();
            yield break;
        }

        statusMessage = "⏳ Converting speech to text...";
        Debug.Log("Sending to STT...");
        
        byte[] audioData = VoicePipeline.AudioClipToWav (clip);
        string audioBase64 = System.Convert.ToBase64String(audioData);
        string url = "https://speech.googleapis.com/v1/speech:recognize?key=" + googleApiKey;

        // Primary language is ms-MY for Puteri
        string json = @"{
            ""config"": {
                ""encoding"": ""LINEAR16"",
                ""sampleRateHertz"": 16000,
                ""languageCode"": ""ms-MY"",
                ""alternativeLanguageCodes"": [""zh-CN"", ""en-US""]
            },
            ""audio"": {
                ""content"": """ + audioBase64 + @"""
            }
        }";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("STT Error: " + request.error);
            ResetToIdle();
            yield break;
        }

        string responseText = request.downloadHandler.text;
        Debug.Log("STT Response: " + responseText);

        STTResponse sttResponse = JsonUtility.FromJson<STTResponse>(responseText);

        if (sttResponse.results != null && sttResponse.results.Length > 0)
        {
            string transcript = sttResponse.results[0].alternatives[0].transcript;
            detectedLanguage = DetectLanguage(transcript);

            Debug.Log("Transcript: " + transcript);
            Debug.Log("Detected language: " + detectedLanguage);

            StartCoroutine(SendToOllama(transcript));
        }
        else
        {
            Debug.LogWarning("No transcript found in STT response.");
            ResetToIdle();
        }
    }

    // ─────────────────────────────────────────────
    //  Language detection — MALAY FIRST for Puteri
    //  1. Malay keywords  → ms-MY  (checked first!)
    //  2. Chinese chars   → zh-CN
    //  3. Fallback        → en-US
    // ─────────────────────────────────────────────

    string DetectLanguage(string text)
    {
        // 1. Chinese characters detected — but verify it is REAL Chinese.
        //    Google STT sometimes transcribes Malay speech as Chinese characters.
        //    Only trust it as Chinese if genuine Chinese words are present.
        if (Regex.IsMatch(text, @"[\u4e00-\u9fff]"))
        {
            // Only trust MEANINGFUL Chinese words — not single particles like 了,的,是
            // which Google often inserts when mishearing Malay
            string[] chineseMarkers = {
                "什么", "为什么", "怎么", "哪里", "谁是", "你好",
                "请问", "告诉我", "可以", "谢谢", "对不起",
                "岳飞", "公主", "将军", "中文", "汉语",
                "你叫", "你是", "他是", "她是", "我是",
                "这个", "那个", "他们", "她们", "我们", "你们"
            };

            foreach (string marker in chineseMarkers)
            {
                if (text.Contains(marker))
                {
                    Debug.Log("Genuine Chinese detected: " + marker);
                    return "zh-CN";
                }
            }

            // Chinese chars found but no real Chinese markers —
            // Google likely misread Malay as Chinese, so default to Malay
            Debug.Log("Chinese chars found but no real Chinese markers — defaulting ms-MY");
            return "ms-MY";
        }

        string lower = text.ToLower();

        // 2. Longer Malay words — safe to use Contains, unlikely to appear in English
        string[] malayMarkers = {
            // Question words
            "siapa", "kenapa", "bagaimana", "mengapa", "apakah",
            "siapakah", "bilakah", "manakah", "berapakah", "di mana",
            // Pronouns
            "awak", "kamu", "saya", "anda", "mereka", "kami", "kita",
            // Common verbs
            "dengan", "untuk", "tidak", "adalah", "boleh", "mahu",
            "sudah", "belum", "bukan", "juga", "punya", "pergi",
            "datang", "makan", "minum", "tidur", "belajar", "kerja",
            "tinggal", "duduk", "berdiri", "berlari", "berkata",
            // Adjectives / adverbs
            "sangat", "sangatlah", "sungguh", "amat", "lebih", "paling",
            "cantik", "bagus", "baik", "buruk", "besar", "kecil",
            "tinggi", "rendah", "cepat", "lambat", "jauh", "dekat",
            // Legend specific
            "syarat", "puteri", "gunung", "ledang", "tuah", "hang",
            "sultan", "kahwin", "pinang", "melayu", "kisah", "cerita",
            "lagenda", "kayangan", "jambatan", "emas", "perak", "darah",
            "hati", "nyamuk", "lalat", "tempayan", "semangkuk",
            // Common nouns
            "rumah", "sekolah", "negara", "bandar", "kampung", "keluarga",
            "ibu", "bapa", "abang", "kakak", "adik", "kawan", "musuh",
            "raja", "rakyat", "negeri", "tanah", "air", "bulan", "matahari",
            // Particles & connectors
            "kerana", "supaya", "walaupun", "meskipun", "tetapi", "namun",
            "oleh", "kepada", "daripada", "tentang", "mengenai", "seperti",
            "antara", "dalam", "luar", "atas", "bawah", "depan", "belakang"
        };

        foreach (string marker in malayMarkers)
        {
            if (lower.Contains(marker))
            {
                Debug.Log("Malay marker found: " + marker);
                return "ms-MY";
            }
        }

        // 3. Short Malay words — use word boundary to avoid false matches
        //    e.g. "apa" inside "capable", "ada" inside "canada"
        string[] shortMalayWords = {
            "apa", "ada", "ini", "itu", "yang", "kau", "dia",
            "nak", "dah", "tak", "pun", "lah", "kan", "kot",
            "mak", "pak", "abg", "sis"
        };
        foreach (string word in shortMalayWords)
        {
            if (Regex.IsMatch(lower, @"\b" + word + @"\b"))
            {
                Debug.Log("Short Malay word found: " + word);
                return "ms-MY";
            }
        }

        // 4. Last check — if transcript looks like broken/short English
        //    it is likely misheard Malay. Real English questions are
        //    longer and more grammatically structured.
        //    Rule: fewer than 5 words AND no common English question
        //    starters → treat as misheard Malay
        string[] englishStarters = {
            "what", "who", "where", "when", "why", "how",
            "is", "are", "do", "does", "did", "can", "could",
            "would", "should", "tell", "explain", "describe",
            "please", "i want", "i need", "give me"
        };

        bool hasEnglishStarter = false;
        foreach (string starter in englishStarters)
        {
            if (lower.StartsWith(starter) || lower.Contains(" " + starter + " "))
            {
                hasEnglishStarter = true;
                break;
            }
        }

        int wordCount = text.Trim().Split(' ').Length;
        if (!hasEnglishStarter && wordCount <= 5)
        {
            Debug.Log("Short ambiguous transcript — likely misheard Malay, defaulting ms-MY");
            return "ms-MY";
        }

        // 5. Default → English
        return "en-US";
    }

    // ─────────────────────────────────────────────
    //  Step 3 — Ollama local LLM
    // ─────────────────────────────────────────────

    IEnumerator SendToOllama(string text)
    {
        if (string.IsNullOrEmpty(ollamaUrl))
        {
            Debug.LogError("Ollama URL is not loaded.");
            ResetToIdle();
            yield break;
        }

        statusMessage = "🤖 Puteri is thinking...";
        Debug.Log("Sending to Ollama...");

        string characterPrompt =
            detectedLanguage == "ms-MY" ? promptMs :
            detectedLanguage == "zh-CN" ? promptZh :
            promptEn;

        var ollamaRequest = new OllamaRequest
        {
            // We add "User: " and "Puteri: " to tell the AI it's a dialogue
            model = ollamaModel,
            prompt = characterPrompt + "\n\nUser: " + text + "\nPuteri:",
            stream = false
        };

        string url = ollamaUrl + "/api/generate";
        string json = JsonUtility.ToJson(ollamaRequest);
        Debug.Log("Ollama JSON: " + json);

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Ollama Error: " + request.error);
            ResetToIdle();
            yield break;
        }

        OllamaResponse ollamaResponse = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
        string replyText = ollamaResponse.response
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("\"", "'")
            .Replace("\\", " ")
            .Trim();

        Debug.Log("Ollama reply: " + replyText);

        // Adding the emotion detection based on keywords in the reply
        string detectedEmotion = "Normal"; // Default
        string lowerReply = replyText.ToLower();

        if (lowerReply.Contains("sedih") || lowerReply.Contains("sad") || lowerReply.Contains("kecewa"))
        {
            detectedEmotion = "Sad";
        }
        else if (lowerReply.Contains("marah") || lowerReply.Contains("angry") || lowerReply.Contains("benci"))
        {
            detectedEmotion = "Angry";
        }

        // Tell the bridge to change the environment!
        if (bridge != null)
        {
            bridge.OnAIResponseReceived(detectedEmotion);
        }
        StartCoroutine(SendToTTS(replyText));
    }

    // ─────────────────────────────────────────────
    //  Step 4 — Google Text-to-Speech
    // ─────────────────────────────────────────────

    IEnumerator SendToTTS(string text)
    {
        if (string.IsNullOrEmpty(googleApiKey))
        {
            Debug.LogError("Google API key not loaded.");
            ResetToIdle();
            yield break;
        }

        statusMessage = "🔊 Generating voice...";
        Debug.Log("Sending to TTS...");

        string voiceLanguage =
            detectedLanguage == "ms-MY" ? ttsLanguageCodeMs :
            detectedLanguage == "zh-CN" ? ttsLanguageCodeZh :
            ttsLanguageCodeEn;

        string voiceName =
            detectedLanguage == "ms-MY" ? ttsVoiceNameMs :
            detectedLanguage == "zh-CN" ? ttsVoiceNameZh :
            ttsVoiceNameEn;

        string safeText = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string ssmlText = "<speak><prosody rate='slow' pitch='+2st'>" + safeText + "</prosody></speak>";

        string json = "{"
            + "\"input\":{ \"ssml\":\"" + ssmlText + "\" },"
            + "\"voice\":{"
                + "\"languageCode\":\"" + voiceLanguage + "\","
                + "\"name\":\"" + voiceName + "\","
                + "\"ssmlGender\":\"FEMALE\""
            + "},"
            + "\"audioConfig\":{ \"audioEncoding\":\"MP3\" }"
        + "}";

        Debug.Log("TTS JSON: " + json);

        string url = "https://texttospeech.googleapis.com/v1/text:synthesize?key=" + googleApiKey;

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("TTS Error: " + request.error);
            Debug.LogError("TTS Response: " + request.downloadHandler.text);
            ResetToIdle();
            yield break;
        }

        TTSResponse ttsResponse = JsonUtility.FromJson<TTSResponse>(request.downloadHandler.text);
        byte[] mp3Data = System.Convert.FromBase64String(ttsResponse.audioContent);

        StartCoroutine(PlayMp3(mp3Data));
    }

    // ─────────────────────────────────────────────
    //  Step 5 — Play voice on NPC
    // ─────────────────────────────────────────────

    IEnumerator PlayMp3(byte[] mp3Data)
    {
        statusMessage = "💬 Puteri is speaking...";

        string tempPath = Application.temporaryCachePath + "/puteri_voice.mp3";
        File.WriteAllBytes(tempPath, mp3Data);

        using (UnityWebRequest audioRequest = UnityWebRequestMultimedia.GetAudioClip("file://" + tempPath, AudioType.MPEG))
        {
            yield return audioRequest.SendWebRequest();

            if (audioRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Audio load error: " + audioRequest.error);
                ResetToIdle();
                yield break;
            }

            AudioClip ttsClip = DownloadHandlerAudioClip.GetContent(audioRequest);
            characterVoice.clip = ttsClip;
            characterVoice.Play();

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", true);

            Debug.Log("Puteri is speaking...");
            yield return new WaitForSeconds(ttsClip.length);

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", false);
            else
                Debug.LogError("characterAnimator is NULL — not assigned!");

            ResetToIdle();
        }
    }

    // ─────────────────────────────────────────────
    //  Helper
    // ─────────────────────────────────────────────

    void ResetToIdle()
    {
        isProcessing = false;
        statusMessage = "Press and Hold SPACE to talk";
    }
}