using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

public class VoicePipelinePuteri : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("Environment Bridge")]
    public AIBridgePuteri bridge;

    [Header("NPC References")]
    public AudioSource characterVoice;
    public Animator characterAnimator;

    [Header("Captions")]
    public TextMeshProUGUI captionText;

    [Header("Scroll View")]
    [Tooltip("Assign the ScrollRect component from your Scroll View here")]
    public ScrollRect captionScrollRect;

    [Header("Fonts")]
    public TMP_FontAsset fontChinese;
    public TMP_FontAsset fontDefault;

    // ── Config (loaded from StreamingAssets/config.json and .env) ──────────────
    private string googleApiKey;
    private string huggingFaceApiKey;
    private string groqApiKey;
    private float lastGroqRequestTime = -10f; // cooldown tracker
    private const float GroqCooldownSeconds = 2f; // min 2s between requests (Groq: 30 RPM)
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

    // ── Runtime state ─────────────────────────────────────────────────────
    private AudioClip clip;
    private bool recording = false;
    private bool isProcessing = false;
    private string detectedLanguage = "ms-MY";
    private string currentReplyText = "";

    public string statusMessage = "⏳ Puteri is speaking...";

    // ── Parallel task results ──────────────────────────────────────────────
    private string parallelOllamaReply = null;
    private bool ollamaDone = false;
    private string parallelEmotion = null;
    private bool emotionDone = false;

    // ── EmotionDetector (auto-attached) ───────────────────────────────────
    private EmotionDetector emotionDetector;

    // ─────────────────────────────────────────────────────────────────────
    //  Lightweight JSON field extractor
    // ─────────────────────────────────────────────────────────────────────
    private string ExtractJsonField(string json, string fieldName)
    {
        string pattern = "\"" + fieldName + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"";
        Match m = Regex.Match(json, pattern);
        if (!m.Success) return "";

        string val = m.Groups[1].Value;
        val = val.Replace("\\n", "\n")
                 .Replace("\\r", "\r")
                 .Replace("\\t", "\t")
                 .Replace("\\\"", "\"")
                 .Replace("\\\\", "\\");
        return val;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (captionText == null || captionScrollRect == null)
        {
            VoiceRecorderPuteri recorder = FindFirstObjectByType<VoiceRecorderPuteri>();
            if (recorder != null)
            {
                if (captionText == null && recorder.captionText != null)
                    captionText = recorder.captionText;
                if (captionScrollRect == null && recorder.captionScrollRect != null)
                    captionScrollRect = recorder.captionScrollRect;
                Debug.Log("VoicePipelinePuteri: auto-linked caption references from VoiceRecorderPuteri");
            }
        }
    }

    void Start()
    {
        LoadConfig();
        LoadEnv();

        emotionDetector = gameObject.GetComponent<EmotionDetector>();
        if (emotionDetector == null)
            emotionDetector = gameObject.AddComponent<EmotionDetector>();

#if !UNITY_WEBGL
        foreach (string device in Microphone.devices)
            Debug.Log("Mic found: " + device);
#endif

        isProcessing = true;
        statusMessage = "⏳ Puteri is speaking...";
        StartCoroutine(WaitForIntro());
    }

    IEnumerator WaitForIntro()
    {
        yield return new WaitForSeconds(2.5f);
        while (characterVoice != null && characterVoice.isPlaying)
            yield return null;
        yield return new WaitForSeconds(0.5f);
        ResetToIdle();
        Debug.Log("Puteri intro finished — ready for questions!");
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

    // ─────────────────────────────────────────────────────────────────────
    //  Config loader
    // ─────────────────────────────────────────────────────────────────────

    void LoadConfig()
    {
        string path = Application.streamingAssetsPath + "/config.json";
        if (!File.Exists(path)) { Debug.LogError("config.json not found at: " + path); return; }

        string json = File.ReadAllText(path, Encoding.UTF8);

        ollamaUrl = ExtractJsonField(json, "ollamaUrl");
        ollamaModel = ExtractJsonField(json, "ollamaModel");

        ttsLanguageCodeMs = ExtractJsonField(json, "puteriTtsLanguageCodeMs");
        ttsVoiceNameMs = ExtractJsonField(json, "puteriTtsVoiceNameMs");
        ttsLanguageCodeZh = ExtractJsonField(json, "puteriTtsLanguageCodeZh");
        ttsVoiceNameZh = ExtractJsonField(json, "puteriTtsVoiceNameZh");
        ttsLanguageCodeEn = ExtractJsonField(json, "puteriTtsLanguageCodeEn");
        ttsVoiceNameEn = ExtractJsonField(json, "puteriTtsVoiceNameEn");

        promptMs = ExtractJsonField(json, "puteriPromptMs");
        promptZh = ExtractJsonField(json, "puteriPromptZh");
        promptEn = ExtractJsonField(json, "puteriPromptEn");

        Debug.Log("  ttsVoiceMs       : " + (string.IsNullOrEmpty(ttsVoiceNameMs) ? "MISSING ❌" : ttsVoiceNameMs));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  .env loader
    // ─────────────────────────────────────────────────────────────────────

    void LoadEnv()
    {
        string path = Application.streamingAssetsPath + "/.env";

        if (!File.Exists(path))
        {
            Debug.LogWarning("⚠️ .env file not found at: " + path + ". API keys might be missing.");
            return;
        }

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        foreach (string line in lines)
        {
            string trimLine = line.Trim();
            if (string.IsNullOrEmpty(trimLine) || trimLine.StartsWith("#")) continue;

            if (trimLine.StartsWith("GOOGLE_API_KEY="))
                googleApiKey = trimLine.Substring("GOOGLE_API_KEY=".Length).Trim();
            else if (trimLine.StartsWith("HUGGINGFACE_API_KEY="))
                huggingFaceApiKey = trimLine.Substring("HUGGINGFACE_API_KEY=".Length).Trim();
            else if (trimLine.StartsWith("GROQ_API_KEY="))
                groqApiKey = trimLine.Substring("GROQ_API_KEY=".Length).Trim();
        }

        Debug.Log("VoicePipelinePuteri: .env loaded.");
        Debug.Log("  googleApiKey     : " + (string.IsNullOrEmpty(googleApiKey) ? "MISSING ❌" : "OK ✓ (" + googleApiKey.Length + " chars)"));
        Debug.Log("  groqApiKey       : " + (string.IsNullOrEmpty(groqApiKey) ? "MISSING ❌" : "OK ✓ (" + groqApiKey.Length + " chars)"));
        Debug.Log("  huggingFaceApiKey: " + (string.IsNullOrEmpty(huggingFaceApiKey) ? "MISSING ❌" : "OK ✓ (" + huggingFaceApiKey.Length + " chars)"));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 1 — Record microphone
    // ─────────────────────────────────────────────────────────────────────

    void StartRecording()
    {
#if UNITY_WEBGL
        Debug.LogWarning("Microphone not supported in WebGL");
        statusMessage = "❌ Microphone not supported on Web";
        return;
#else
        recording = true;
        isProcessing = false;
        detectedLanguage = "ms-MY";
        statusMessage = "🎙️ Recording... (release SPACE to send)";
        clip = Microphone.Start(null, false, 5, 16000);
        Debug.Log("Recording...");
#endif
    }

    void StopRecording()
    {
#if UNITY_WEBGL
        return;
#else
        recording = false;
        isProcessing = true;
        statusMessage = "⏳ Processing...";
        Microphone.End(null);
        Debug.Log("Recording stopped");
        StartCoroutine(SendToSTT());
#endif
    }

    // ── WAV helpers ───────────────────────────────────────────────────────

    public static byte[] AudioClipToPcm16(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        byte[] bytes = new byte[samples.Length * 2];
        int offset = 0;
        foreach (float s in samples)
        {
            float clamped = Mathf.Clamp(s * 3f, -1f, 1f);
            short value = (short)(clamped * short.MaxValue);
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            offset += 2;
        }
        return bytes;
    }

    public static byte[] PcmToWav(byte[] pcmData, int sampleRate, int channels)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            int byteRate = sampleRate * channels * 2;
            int blockAlign = channels * 2;
            int dataLength = pcmData.Length;
            int riffLength = 36 + dataLength;

            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(riffLength);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));

            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)channels);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write((short)blockAlign);
            bw.Write((short)16);

            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataLength);
            bw.Write(pcmData);

            return ms.ToArray();
        }
    }

    public static AudioClip WavBytesToAudioClip(byte[] wavData, string clipName = "tts_clip")
    {
        int channels = wavData[22] | (wavData[23] << 8);
        int sampleRate = wavData[24] | (wavData[25] << 8) | (wavData[26] << 16) | (wavData[27] << 24);

        int dataOffset = 12;
        while (dataOffset < wavData.Length - 8)
        {
            string chunkId = Encoding.ASCII.GetString(wavData, dataOffset, 4);
            int chunkSize = wavData[dataOffset + 4] | (wavData[dataOffset + 5] << 8)
                             | (wavData[dataOffset + 6] << 16) | (wavData[dataOffset + 7] << 24);
            if (chunkId == "data") { dataOffset += 8; break; }
            dataOffset += 8 + chunkSize;
        }

        int sampleCount = (wavData.Length - dataOffset) / 2;
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short raw = (short)(wavData[dataOffset + i * 2] | (wavData[dataOffset + i * 2 + 1] << 8));
            samples[i] = raw / (float)short.MaxValue;
        }

        AudioClip audioClip = AudioClip.Create(clipName, sampleCount / channels, channels, sampleRate, false);
        audioClip.SetData(samples, 0);
        return audioClip;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 2 — Google Speech-to-Text
    // ─────────────────────────────────────────────────────────────────────

    [System.Serializable] private class STTResponse { public Result[] results = null; [System.Serializable] public class Result { public Alternative[] alternatives = null; [System.Serializable] public class Alternative { public string transcript = ""; } } }
    [System.Serializable] private class OllamaRequest { public string model; public string prompt; public bool stream; }
    [System.Serializable] private class OllamaResponse { public string response = ""; }
    [System.Serializable] private class TTSResponse { public string audioContent = ""; }

    IEnumerator SendToSTT()
    {
        if (string.IsNullOrEmpty(googleApiKey))
        {
            Debug.LogError("Google API key not loaded. Check config.json googleApiKey field.");
            ResetToIdle();
            yield break;
        }

        statusMessage = "⏳ Converting speech to text...";

        byte[] pcmData = AudioClipToPcm16(clip);
        string audioB64 = System.Convert.ToBase64String(pcmData);
        string url = "https://speech.googleapis.com/v1/speech:recognize?key=" + googleApiKey;

        string json = @"{
            ""config"": {
                ""encoding"": ""LINEAR16"",
                ""sampleRateHertz"": 16000,
                ""languageCode"": ""ms-MY"",
                ""alternativeLanguageCodes"": [""zh-CN"", ""en-US""]
            },
            ""audio"": { ""content"": """ + audioB64 + @""" }
        }";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.SetRequestHeader("Content-Type", "application/json");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("STT Error: " + request.error);
            Debug.LogError("STT Response: " + request.downloadHandler.text);
            ResetToIdle();
            yield break;
        }

        string responseText = request.downloadHandler.text;
        STTResponse sttResponse = JsonUtility.FromJson<STTResponse>(responseText);
        Debug.Log("STT Response: " + responseText);

        if (sttResponse.results == null || sttResponse.results.Length == 0)
        {
            Debug.LogWarning("No transcript found — speak louder or closer to mic.");
            ResetToIdle();
            yield break;
        }

        string transcript = sttResponse.results[0].alternatives[0].transcript;
        detectedLanguage = DetectLanguage(transcript);
        Debug.Log("Transcript: " + transcript + "  |  Lang: " + detectedLanguage);

        StartCoroutine(ParallelPipeline(transcript));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Language detection
    // ─────────────────────────────────────────────────────────────────────

    string DetectLanguage(string text)
    {
        if (Regex.IsMatch(text, @"[\u4e00-\u9fff]")) return "zh-CN";

        string lower = text.ToLower();
        string[] strongMalay = { "awak", "kamu", "saya", "anda", "tidak", "adalah", "boleh", "mahu", "sudah", "belum", "bukan", "siapa", "kenapa", "bagaimana", "mengapa", "puteri", "gunung", "ledang", "hikayat", "encik", "cikgu", "tuan", "puan" };
        foreach (string m in strongMalay) if (lower.Contains(m)) return "ms-MY";

        string[] weakMalay = { "apa", "ada", "ini", "itu", "yang", "dia", "dengan", "untuk", "juga", "kita", "kami", "raja", "cerita", "selamat", "terima" };
        int weakCount = 0;
        foreach (string m in weakMalay) if (lower.Contains(m)) weakCount++;
        if (weakCount >= 2) return "ms-MY";

        return "en-US";
    }

    // ─────────────────────────────────────────────────────────────────────
    //  PARALLEL PIPELINE
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator ParallelPipeline(string transcript)
    {
        statusMessage = "🤖 Puteri is thinking...";

        parallelOllamaReply = null;
        ollamaDone = false;
        parallelEmotion = null;
        emotionDone = false;

        StartCoroutine(RunGroqWithFallback(transcript));

        while (!ollamaDone) yield return null;

        if (string.IsNullOrEmpty(parallelOllamaReply))
        {
            ResetToIdle();
            yield break;
        }

        currentReplyText = parallelOllamaReply;
        statusMessage = "🎭 Reading emotion & 🔊 generating voice...";

        StartCoroutine(RunEmotionDetection(currentReplyText, transcript));
        StartCoroutine(RunTTSParallel(currentReplyText));

        while (!emotionDone) yield return null;

        if (bridge != null)
            bridge.OnAIResponseReceived(parallelEmotion ?? "neutral");

        Debug.Log("Emotion detected: " + parallelEmotion);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 3 — Gemini with Ollama fallback
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator RunGroqWithFallback(string userText)
    {
        if (string.IsNullOrEmpty(groqApiKey))
        {
            Debug.LogWarning("⚠️ Groq API key missing. Falling back to Ollama.");
            yield return StartCoroutine(RunOllama(userText));
            yield break;
        }

        // Cooldown to avoid 429 Too Many Requests (Groq: 30 RPM = 1 per 2s)
        float elapsed = Time.time - lastGroqRequestTime;
        if (elapsed < GroqCooldownSeconds)
        {
            float wait = GroqCooldownSeconds - elapsed;
            Debug.Log($"⏳ Groq cooldown: waiting {wait:F1}s...");
            yield return new WaitForSeconds(wait);
        }
        lastGroqRequestTime = Time.time;

        string prompt = detectedLanguage == "ms-MY" ? promptMs :
                        detectedLanguage == "zh-CN" ? promptZh : promptEn;

        string langInstruction = detectedLanguage == "ms-MY"
            ? "IMPORTANT: Reply ONLY in Malay (Bahasa Melayu). Do NOT use any English or Chinese words."
            : detectedLanguage == "zh-CN"
            ? "重要：只用中文回答。不要混入任何英文或马来文。"
            : "IMPORTANT: Reply ONLY in English. Do NOT use any Malay or Chinese words.";

        string safePrompt = prompt.Replace("\"", "'").Replace("\n", " ").Replace("\r", "");
        string safeInst = langInstruction.Replace("\"", "'").Replace("\n", " ").Replace("\r", "");
        string safeUser = userText.Replace("\"", "'").Replace("\n", " ").Replace("\r", "");

        string fullPrompt = safePrompt + " " + safeInst + " User: " + safeUser + " Puteri:";

        // Groq uses OpenAI-compatible chat completions format
        string json = "{\"model\":\"llama-3.3-70b-versatile\",\"messages\":[{\"role\":\"user\",\"content\":\"" + fullPrompt + "\"}],\"max_tokens\":150,\"temperature\":0.7}";
        string url = "https://api.groq.com/openai/v1/chat/completions";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + groqApiKey);
        request.timeout = 10;

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("⚠️ Groq failed (" + request.error + "). Falling back to Ollama.");
            yield return StartCoroutine(RunOllama(userText));
            yield break;
        }

        string raw = request.downloadHandler.text;
        string replyText = ExtractJsonField(raw, "content");

        if (string.IsNullOrEmpty(replyText))
        {
            Debug.LogWarning("⚠️ Groq returned empty. Falling back to Ollama.");
            yield return StartCoroutine(RunOllama(userText));
            yield break;
        }

        parallelOllamaReply = replyText.Replace("\\n", " ").Replace("\\\"", "\"").Replace("\\", " ").Replace("*", "").Trim();

        Debug.Log("======================================");
        Debug.Log(">>> ⚡ LLM USED: GROQ llama-3.3-70b-versatile (Puteri)");
        Debug.Log(">>> REPLY: " + parallelOllamaReply);
        Debug.Log("======================================");

        ollamaDone = true;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 3b — Ollama fallback
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator RunOllama(string userText)
    {
        if (string.IsNullOrEmpty(ollamaUrl))
        {
            Debug.LogError("Ollama URL not loaded. Check config.json ollamaUrl field.");
            ollamaDone = true;
            yield break;
        }

        string prompt = detectedLanguage == "ms-MY" ? promptMs :
                        detectedLanguage == "zh-CN" ? promptZh : promptEn;

        string langInstruction = detectedLanguage == "ms-MY"
            ? "IMPORTANT: Reply ONLY in Malay (Bahasa Melayu). Do NOT use any English or Chinese words."
            : detectedLanguage == "zh-CN"
            ? "重要：只用中文回答。不要混入任何英文或马来文。"
            : "IMPORTANT: Reply ONLY in English. Do NOT use any Malay or Chinese words.";

        var ollamaReq = new OllamaRequest
        {
            model = ollamaModel,
            prompt = prompt + "\n\n" + langInstruction + "\n\nUser: " + userText + "\nPuteri:",
            stream = false
        };

        string url = ollamaUrl + "/api/generate";
        string json = JsonUtility.ToJson(ollamaReq);

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Ollama Error: " + request.error + " — is Ollama running? Run: ollama serve");
            ollamaDone = true;
            yield break;
        }

        OllamaResponse resp = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
        parallelOllamaReply = resp.response
            .Replace("\n", " ").Replace("\r", " ")
            .Replace("\"", "'").Replace("\\", " ")
            .Trim();

        Debug.Log("======================================");
        Debug.Log(">>> 🤖 LLM USED: LOCAL OLLAMA (" + ollamaModel + ") (Puteri)");
        Debug.Log(">>> REPLY: " + parallelOllamaReply);
        Debug.Log("======================================");

        ollamaDone = true;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 3c — Emotion detection (parallel with TTS)
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator RunEmotionDetection(string replyText, string userQuestion = null)
    {
        string emotionSource = !string.IsNullOrEmpty(userQuestion) ? userQuestion : replyText;
        string shortText = emotionSource.Length > 200 ? emotionSource.Substring(0, 200) : emotionSource;
        Debug.Log("🎭 Analysing emotion from: " + shortText);

        yield return StartCoroutine(
            emotionDetector.DetectSentimentGoogle(
                shortText,
                googleApiKey,
                result =>
                {
                    parallelEmotion = result;
                    if (result == "neutral")
                        Debug.LogWarning("⚠️ Puteri: Emotion came back neutral.");
                    else
                        Debug.Log("💯 Puteri Emotion from NLP: " + result);
                }
            )
        );

        emotionDone = true;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 4 — Google TTS (parallel with emotion detection)
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator RunTTSParallel(string text)
    {
        if (string.IsNullOrEmpty(googleApiKey))
        {
            Debug.LogError("Google API key not loaded.");
            ResetToIdle();
            yield break;
        }

        string voiceLanguage = detectedLanguage == "ms-MY" ? ttsLanguageCodeMs :
                               detectedLanguage == "zh-CN" ? ttsLanguageCodeZh : ttsLanguageCodeEn;
        string voiceName = detectedLanguage == "ms-MY" ? ttsVoiceNameMs :
                               detectedLanguage == "zh-CN" ? ttsVoiceNameZh : ttsVoiceNameEn;

        string safeText = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string ssmlText = "<speak><prosody rate='slow' pitch='+2st'>" + safeText + "</prosody></speak>";

        string json = "{"
            + "\"input\":{ \"ssml\":\"" + ssmlText + "\" },"
            + "\"voice\":{"
                + "\"languageCode\":\"" + voiceLanguage + "\","
                + "\"name\":\"" + voiceName + "\","
                + "\"ssmlGender\":\"FEMALE\""
            + "},"
            + "\"audioConfig\":{ \"audioEncoding\":\"LINEAR16\" }"
        + "}";

        string url = "https://texttospeech.googleapis.com/v1/text:synthesize?key=" + googleApiKey;
        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
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

        byte[] pcmData = System.Convert.FromBase64String(ttsResponse.audioContent);
        byte[] wavData = PcmToWav(pcmData, sampleRate: 24000, channels: 1);

        StartCoroutine(PlayWav(wavData));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 5 — Play WAV in-memory
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator PlayWav(byte[] wavData)
    {
        statusMessage = "💬 Puteri is speaking...";

        AudioClip ttsClip = WavBytesToAudioClip(wavData, "puteri_tts");

        if (ttsClip == null || ttsClip.length == 0)
        {
            Debug.LogError("WAV AudioClip is null or empty!");
            ResetToIdle();
            yield break;
        }

        Debug.Log("WAV AudioClip loaded — length: " + ttsClip.length + "s  samples: " + ttsClip.samples);

        // Apply correct font before speaking
        if (captionText != null)
        {
            if (detectedLanguage == "zh-CN" && fontChinese != null)
                captionText.font = fontChinese;
            else if (fontDefault != null)
                captionText.font = fontDefault;
        }

        characterVoice.clip = ttsClip;
        characterVoice.Play();

        if (characterAnimator != null)
            characterAnimator.SetBool("IsTalking", true);

        if (captionText != null)
            StartCoroutine(TypewriterSync(currentReplyText, ttsClip.length));

        yield return new WaitForSeconds(ttsClip.length);

        if (characterAnimator != null)
            characterAnimator.SetBool("IsTalking", false);

        ResetToIdle();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Typewriter synced to audio — FIXED SCROLLING
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator TypewriterSync(string fullText, float audioDuration)
    {
        if (captionText == null || fullText.Length == 0) yield break;

        float delayPerChar = Mathf.Clamp(audioDuration / fullText.Length, 0.01f, 0.08f);

        // Separate each conversation turn with a blank line
        string prefix = string.IsNullOrEmpty(captionText.text)
            ? ""
            : captionText.text + "\n\n";

        for (int i = 0; i < fullText.Length; i++)
        {
            captionText.text = prefix + fullText.Substring(0, i + 1);

            // Rebuild layout every character so Content height stays current
            LayoutRebuilder.ForceRebuildLayoutImmediate(captionText.rectTransform);

            ScrollToBottom();
            yield return new WaitForSeconds(delayPerChar);
        }

        // Ensure final text + layout is correct
        captionText.text = prefix + fullText;
        LayoutRebuilder.ForceRebuildLayoutImmediate(captionText.rectTransform);
        ScrollToBottom();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────

    void ResetToIdle()
    {
        isProcessing = false;
        statusMessage = "Press and Hold SPACE to talk";
    }

    // FIXED: rebuild Content rect before setting scroll position
    void ScrollToBottom()
    {
        if (captionScrollRect == null) return;

        Canvas.ForceUpdateCanvases();

        // Rebuild the Content RectTransform so its height reflects new text
        LayoutRebuilder.ForceRebuildLayoutImmediate(
            (RectTransform)captionScrollRect.content);

        captionScrollRect.verticalNormalizedPosition = 0f;
    }
}