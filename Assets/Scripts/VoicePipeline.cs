using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

/// <summary>
/// VoicePipeline — voice pipeline for the Yue Fei NPC.
///
/// Pipeline:
///   SPACE held  →  Microphone (16 kHz, max 5 s)
///               →  Google STT  (primary: zh-CN, fallback: ms-MY, en-US)
///               →  DetectLanguage()
///               →  Ollama LLM  (character reply)
///               →  EmotionDetector  (HuggingFace primary, keyword fallback)
///               →  AIBridgeYueFei.OnAIResponseReceived(emotion)
///               →  Google TTS  (male voice, -2st pitch)
///               →  AudioSource playback + Animator IsTalking
///
/// FIX: Null-safe dialogueText with helper SetDialogue()
/// FIX: Inspector validation in Start() with clear error messages
/// FIX: Uses manual JSON parsing instead of JsonUtility to handle
///      Unicode characters and long prompt strings in config.json.
/// FIX: EmotionDetector now uses HuggingFace + keyword fallback.
/// FIX: Improved language detection (2-tier Malay matching).
/// </summary>
public class VoicePipeline : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("UI")]
    public TextMeshProUGUI dialogueText;
    public ScrollRect dialogueScrollView;
    public TMP_FontAsset chineseFont;
    public TMP_FontAsset defaultFont;

    [Header("Environment Bridge")]
    public AI_bridge_YueFei bridge;

    [Header("NPC References")]
    public AudioSource characterVoice;
    public Animator characterAnimator;
    [Range(0.25f, 4.0f)]
    public double ttsSpeakingRate = 1.0;

    [Header("Intro Sequence")]
    public AudioClip introVoiceClip;
    [TextArea(3, 5)]
    public string introText = "怒发冲冠，凭阑处、潇潇雨歇。抬望眼，仰天长啸，壮怀激烈。三十功名尘与土，八千里路云和月。莫等闲，白了少年头，空悲切。";

    // ── Config (loaded from StreamingAssets/config.json and .env) ──────────────
    private string googleApiKey;
    private string huggingFaceApiKey;
    private string groqApiKey;
    private float lastGroqRequestTime = -10f; // cooldown tracker
    private const float GroqCooldownSeconds = 2f; // min 2s between requests (Groq: 30 RPM)
    private string ollamaUrl;
    private string ollamaModel;

    private string ttsLanguageCodeZh;
    private string ttsVoiceNameZh;
    private string ttsLanguageCodeEn;
    private string ttsVoiceNameEn;
    private string ttsLanguageCodeMs;
    private string ttsVoiceNameMs;

    private string characterPromptZh;
    private string characterPromptEn;
    private string characterPromptMs;

    // ── Runtime state ─────────────────────────────────────────────────────
    private AudioClip clip;
    private bool recording = false;
    private bool isProcessing = false;
    private string detectedLanguage = "zh-CN";

#if UNITY_WEBGL && !UNITY_EDITOR
    // Holds the raw PCM16 base64 string delivered by the JS plugin
    private string webGLPcmBase64 = null;

    [DllImport("__Internal")]
    private static extern void MicWebGL_StartRecording(string gameObjectName);

    [DllImport("__Internal")]
    private static extern void MicWebGL_StopRecording();

    // Called by the JS plugin via Unity SendMessage when the mic opens
    public void OnWebGLMicStarted(string _)
    {
        Debug.Log("WebGL mic opened and recording.");
    }

    // Called by the JS plugin via Unity SendMessage when audio is ready
    public void OnWebGLAudioReady(string base64Pcm)
    {
        webGLPcmBase64 = base64Pcm;
        Debug.Log("WebGL audio received (" + base64Pcm.Length + " chars). Starting STT...");
        StartCoroutine(SendToSTT());
    }

    // Called by the JS plugin via Unity SendMessage on mic permission denial or error
    public void OnWebGLMicError(string error)
    {
        recording = false;
        isProcessing = false;
        Debug.LogError("WebGL Mic Error: " + error);
        statusMessage = "❌ Mic error: " + error;
        SetDialogue("❌ Microphone error. Please allow mic access and try again.");
        ResetToIdle();
    }
#endif

    private string statusMessage = "⏳ Yue Fei is speaking...";
    private string lastUserText = "";

    // ── EmotionDetector (auto-attached) ───────────────────────────────────
    private EmotionDetector emotionDetector;

    // ─────────────────────────────────────────────────────────────────────
    //  NULL-SAFE dialogue helper — always use this instead of dialogueText.text directly
    // ─────────────────────────────────────────────────────────────────────
    private void SetDialogue(string message)
    {
        if (dialogueText != null)
        {
            dialogueText.gameObject.SetActive(true);

            if (chineseFont != null && defaultFont != null)
            {
                if (Regex.IsMatch(message, @"[\u4e00-\u9fff]"))
                    dialogueText.font = chineseFont;
                else
                    dialogueText.font = defaultFont;
            }

            dialogueText.text = message;
            Debug.Log("[Dialogue] " + message);
        }
        else
        {
            Debug.LogError("❌ dialogueText is NULL — drag your TMP Text object into the 'Dialogue Text' field in the Inspector on VoicePipeline!");
        }
    }

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

    void Start()
    {
        // ── Inspector null validation ──────────────────────────────────────
        if (dialogueText == null)
            Debug.LogError("❌ VoicePipeline: 'dialogueText' is not assigned in the Inspector! Drag your TMP Text object into this slot.");
        if (characterVoice == null)
            Debug.LogError("❌ VoicePipeline: 'characterVoice' (AudioSource) is not assigned in the Inspector!");
        if (characterAnimator == null)
            Debug.LogWarning("⚠️ VoicePipeline: 'characterAnimator' is not assigned — animations will be skipped.");
        if (bridge == null)
            Debug.LogWarning("⚠️ VoicePipeline: 'bridge' (AI_bridge_YueFei) is not assigned — emotion bridge will be skipped.");

        LoadConfig();
        LoadEnv();

        emotionDetector = gameObject.GetComponent<EmotionDetector>();
        if (emotionDetector == null)
            emotionDetector = gameObject.AddComponent<EmotionDetector>();

#if !UNITY_WEBGL
        if (Microphone.devices.Length == 0)
            Debug.LogError("❌ No microphone found!");
        else
            foreach (string device in Microphone.devices)
                Debug.Log("Mic found: " + device);
#endif

        isProcessing = true;
        statusMessage = "⏳ Yue Fei is speaking...";
        StartCoroutine(WaitForIntro());
    }

    IEnumerator WaitForIntro()
    {
        yield return new WaitForSeconds(1.0f); // Small delay to let game start

        if (introVoiceClip != null && characterVoice != null)
        {
            characterVoice.clip = introVoiceClip;
            characterVoice.Play();

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", true);

            StartCoroutine(TypewriterSubtitle("", introText, introVoiceClip.length));

            yield return new WaitForSeconds(introVoiceClip.length);

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", false);
        }
        else
        {
            SetDialogue("⏳ Yue Fei is preparing...");
            yield return new WaitForSeconds(2.5f);

            while (characterVoice != null && characterVoice.isPlaying)
                yield return null;
        }

        yield return new WaitForSeconds(0.5f);

        ResetToIdle();
        Debug.Log("Yue Fei intro finished — ready for questions!");
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

        if (!File.Exists(path))
        {
            Debug.LogError("❌ config.json not found at: " + path);
            return;
        }

        string json = File.ReadAllText(path, Encoding.UTF8);

        ollamaUrl = ExtractJsonField(json, "ollamaUrl");
        ollamaModel = ExtractJsonField(json, "ollamaModel");

        ttsLanguageCodeZh = ExtractJsonField(json, "ttsLanguageCodeZh");
        ttsVoiceNameZh = ExtractJsonField(json, "ttsVoiceNameZh");
        ttsLanguageCodeEn = ExtractJsonField(json, "ttsLanguageCodeEn");
        ttsVoiceNameEn = ExtractJsonField(json, "ttsVoiceNameEn");
        ttsLanguageCodeMs = ExtractJsonField(json, "ttsLanguageCodeMs");
        ttsVoiceNameMs = ExtractJsonField(json, "ttsVoiceNameMs");

        characterPromptZh = ExtractJsonField(json, "characterPromptZh");
        characterPromptEn = ExtractJsonField(json, "characterPromptEn");
        characterPromptMs = ExtractJsonField(json, "characterPromptMs");

        Debug.Log("VoicePipeline: config.json loaded.");
        Debug.Log("  ttsVoiceZh       : " + (string.IsNullOrEmpty(ttsVoiceNameZh) ? "MISSING ❌" : ttsVoiceNameZh));
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

        Debug.Log("VoicePipeline: .env loaded.");
        Debug.Log("  googleApiKey     : " + (string.IsNullOrEmpty(googleApiKey) ? "MISSING ❌" : "OK ✓ (" + googleApiKey.Length + " chars)"));
        Debug.Log("  groqApiKey       : " + (string.IsNullOrEmpty(groqApiKey) ? "MISSING ❌" : "OK ✓ (" + groqApiKey.Length + " chars)"));
        Debug.Log("  huggingFaceApiKey: " + (string.IsNullOrEmpty(huggingFaceApiKey) ? "MISSING ❌" : "OK ✓ (" + huggingFaceApiKey.Length + " chars)"));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 1 — Record microphone
    // ─────────────────────────────────────────────────────────────────────

    void StartRecording()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        webGLPcmBase64 = null;
        recording = true;
        isProcessing = false;
        detectedLanguage = "zh-CN";
        statusMessage = "🎙️ Recording... (release SPACE to send)";
        SetDialogue("🎙️ Listening...");
        MicWebGL_StartRecording(gameObject.name);
        Debug.Log("WebGL mic recording started.");
#else
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("❌ No microphone found!");
            return;
        }
        recording = true;
        isProcessing = false;
        detectedLanguage = "zh-CN";
        statusMessage = "🎙️ Recording... (release SPACE to send)";
        SetDialogue("🎙️ Listening...");
        clip = Microphone.Start(Microphone.devices[0], false, 5, 16000);
        Debug.Log("Recording started...");
#endif
    }

    void StopRecording()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        recording = false;
        isProcessing = true;
        statusMessage = "⏳ Processing...";
        SetDialogue("⏳ Processing your question...");
        MicWebGL_StopRecording();
        // SendToSTT() is triggered from OnWebGLAudioReady once the JS delivers the audio
        Debug.Log("WebGL mic stopped, awaiting audio data from JS...");
#else
        if (clip == null)
        {
            Debug.LogError("❌ AudioClip is null — microphone may not have started.");
            ResetToIdle();
            return;
        }
        Debug.Log("🎙️ Samples recorded: " + clip.samples);
        Debug.Log("🎙️ Frequency: " + clip.frequency);

        recording = false;
        isProcessing = true;
        statusMessage = "⏳ Processing...";
        SetDialogue("⏳ Processing your question...");
        Microphone.End(Microphone.devices[0]);
        Debug.Log("Recording stopped");
        StartCoroutine(SendToSTT());
#endif
    }

    // ─────────────────────────────────────────────────────────────────────
    //  WAV converter
    // ─────────────────────────────────────────────────────────────────────

    public static byte[] AudioClipToWav(AudioClip clip)
    {
        float[] samples = new float[clip.samples];
        clip.GetData(samples, 0);
        byte[] bytes = new byte[samples.Length * 2];
        int offset = 0;

        foreach (var sample in samples)
        {
            float amplified = Mathf.Clamp(sample * 10f, -1f, 1f);
            short value = (short)(amplified * short.MaxValue);
            System.BitConverter.GetBytes(value).CopyTo(bytes, offset);
            offset += 2;
        }

        return bytes;
    }

    private byte[] AddWavHeader(byte[] pcmData, int channels, int sampleRate, int bitDepth)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            int byteRate = sampleRate * channels * bitDepth / 8;
            short blockAlign = (short)(channels * bitDepth / 8);

            bw.Write(Encoding.UTF8.GetBytes("RIFF"));
            bw.Write(36 + pcmData.Length);
            bw.Write(Encoding.UTF8.GetBytes("WAVE"));
            bw.Write(Encoding.UTF8.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);           // PCM format
            bw.Write((short)channels);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write(blockAlign);
            bw.Write((short)bitDepth);
            bw.Write(Encoding.UTF8.GetBytes("data"));
            bw.Write(pcmData.Length);
            bw.Write(pcmData);
            return ms.ToArray();
        }
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
            Debug.LogError("❌ Google API key is missing. Check config.json 'googleApiKey' field.");
            SetDialogue("❌ Google API key missing. Check config.json.");
            ResetToIdle();
            yield break;
        }

        statusMessage = "⏳ Converting speech to text...";
        SetDialogue("⏳ Converting speech to text...");

#if UNITY_WEBGL && !UNITY_EDITOR
        string audioBase64 = webGLPcmBase64;
        if (string.IsNullOrEmpty(audioBase64))
        {
            Debug.LogError("❌ WebGL: no audio data received from microphone.");
            ResetToIdle();
            yield break;
        }
#else
        byte[] audioData = AudioClipToWav(clip);
        string audioBase64 = System.Convert.ToBase64String(audioData);
#endif
        string url = "https://speech.googleapis.com/v1/speech:recognize?key=" + googleApiKey;

        string json = @"{
            ""config"": {
                ""encoding"": ""LINEAR16"",
                ""sampleRateHertz"": 16000,
                ""languageCode"": ""zh-CN"",
                ""alternativeLanguageCodes"": [""ms-MY"", ""en-US""]
            },
            ""audio"": {
                ""content"": """ + audioBase64 + @"""
            }
        }";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.SetRequestHeader("Content-Type", "application/json");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("❌ STT Error: " + request.error);
            Debug.LogError("❌ STT Response: " + request.downloadHandler.text);
            SetDialogue("❌ Speech recognition failed. Check console.");
            ResetToIdle();
            yield break;
        }

        string responseText = request.downloadHandler.text;
        STTResponse sttResponse = JsonUtility.FromJson<STTResponse>(responseText);
        Debug.Log("STT Response: " + responseText);

        if (sttResponse.results != null && sttResponse.results.Length > 0)
        {
            string transcript = sttResponse.results[0].alternatives[0].transcript;
            detectedLanguage = DetectLanguage(transcript);

            Debug.Log("Transcript: " + transcript);
            Debug.Log("Detected language: " + detectedLanguage);

            SetDialogue("You: " + transcript + "\n<color=#AAAAAA>Yue Fei is thinking...</color>");
            StartCoroutine(SendToGroqWithFallback(transcript));
        }
        else
        {
            Debug.LogWarning("⚠️ No transcript found — speak louder or closer to mic.");
            SetDialogue("⚠️ Couldn't hear you. Please try again.");
            ResetToIdle();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Language detection
    // ─────────────────────────────────────────────────────────────────────

    string DetectLanguage(string text)
    {
        if (Regex.IsMatch(text, @"[\u4e00-\u9fff]"))
            return "zh-CN";

        string lower = text.ToLower();

        string[] strongMalayMarkers =
        {
            "awak", "kamu", "saya", "anda", "tidak", "adalah",
            "boleh", "mahu", "sudah", "belum", "bukan",
            "siapa", "kenapa", "bagaimana", "mengapa",
            "encik", "cikgu", "tuan", "puan"
        };

        foreach (string marker in strongMalayMarkers)
            if (lower.Contains(marker))
                return "ms-MY";

        string[] weakMalayMarkers =
        {
            "apa", "ada", "ini", "itu", "yang", "dia",
            "dengan", "untuk", "juga", "kita", "kami",
            "raja", "cerita", "selamat", "terima"
        };

        int weakCount = 0;
        foreach (string marker in weakMalayMarkers)
            if (lower.Contains(marker))
                weakCount++;

        if (weakCount >= 2)
            return "ms-MY";

        return "en-US";
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 3 — Gemini API (Fast Cloud LLM) -> Fallback to Ollama
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator SendToGroqWithFallback(string userText)
    {
        if (string.IsNullOrEmpty(groqApiKey))
        {
            Debug.LogWarning("⚠️ Groq API key missing. Falling back to Ollama.");
            yield return StartCoroutine(SendToOllama(userText));
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

        statusMessage = "🧠 Yue Fei is thinking (Groq)...";

        string prompt =
            detectedLanguage == "zh-CN" ? characterPromptZh :
            detectedLanguage == "ms-MY" ? characterPromptMs :
            characterPromptEn;

        string langInstruction = detectedLanguage == "ms-MY"
            ? "IMPORTANT: Reply ONLY in Malay (Bahasa Melayu). Do NOT use any English or Chinese words."
            : detectedLanguage == "zh-CN"
            ? "重要：只用中文回答。不要混入任何英文或马来文。"
            : "IMPORTANT: Reply ONLY in English. Do NOT use any Malay or Chinese words.";

        // Strip non-JSON safe characters
        string safePrompt = prompt.Replace("\"", "'").Replace("\n", " ").Replace("\r", "");
        string safeInst = langInstruction.Replace("\"", "'").Replace("\n", " ").Replace("\r", "");
        string safeUser = userText.Replace("\"", "'").Replace("\n", " ").Replace("\r", "");

        string fullPrompt = safePrompt + " " + safeInst + " User: " + safeUser + " Yue Fei:";

        // Groq uses OpenAI-compatible chat completions format
        string json = "{\"model\":\"llama-3.3-70b-versatile\",\"messages\":[{\"role\":\"user\",\"content\":\"" + fullPrompt + "\"}],\"max_tokens\":150,\"temperature\":0.7}";

        string url = "https://api.groq.com/openai/v1/chat/completions";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + groqApiKey);
        request.timeout = 10;

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("⚠️ Groq failed (" + request.error + "). Falling back to Ollama.");
            yield return StartCoroutine(SendToOllama(userText));
            yield break;
        }

        string raw = request.downloadHandler.text;
        string replyText = ExtractJsonField(raw, "content");

        if (string.IsNullOrEmpty(replyText))
        {
            Debug.LogWarning("⚠️ Groq returned empty. Falling back to Ollama.");
            yield return StartCoroutine(SendToOllama(userText));
            yield break;
        }

        // Clean up markdown
        replyText = replyText.Replace("\\n", " ").Replace("\\\"", "\"").Replace("\\", "").Replace("*", "").Trim();

        Debug.Log("======================================");
        Debug.Log(">>> ⚡ LLM USED: GROQ llama-3.3-70b-versatile");
        Debug.Log(">>> REPLY: " + replyText);
        Debug.Log("======================================");

        lastUserText = userText;
        SetDialogue("You: " + userText + "\nYue Fei: <color=#AAAAAA>...</color>");

        // Detect emotion from USER's question (more reliable than AI's stoic reply)
        StartCoroutine(DetectEmotionThenSpeak(replyText, userText));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 3b — Ollama local LLM (Fallback)
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator SendToOllama(string userText)
    {
        if (string.IsNullOrEmpty(ollamaUrl))
        {
            Debug.LogError("❌ Ollama URL is missing. Check config.json 'ollamaUrl' field.");
            SetDialogue("❌ Ollama URL missing. Check config.json.");
            ResetToIdle();
            yield break;
        }

        statusMessage = "🤖 Yue Fei is thinking (Local Ollama)...";

        string prompt =
            detectedLanguage == "zh-CN" ? characterPromptZh :
            detectedLanguage == "ms-MY" ? characterPromptMs :
            characterPromptEn;

        string langInstruction = detectedLanguage == "ms-MY"
            ? "IMPORTANT: Reply ONLY in Malay (Bahasa Melayu). Do NOT use any English or Chinese words."
            : detectedLanguage == "zh-CN"
            ? "重要：只用中文回答。不要混入任何英文或马来文。"
            : "IMPORTANT: Reply ONLY in English. Do NOT use any Malay or Chinese words.";

        if (string.IsNullOrEmpty(prompt))
            Debug.LogWarning("⚠️ Prompt is empty for language: " + detectedLanguage);

        var ollamaRequest = new OllamaRequest
        {
            model = ollamaModel,
            prompt = prompt + "\n\n" + langInstruction + "\n\nUser: " + userText + "\nYue Fei:",
            stream = false
        };

        string url = ollamaUrl + "/api/generate";
        string json = JsonUtility.ToJson(ollamaRequest);

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("❌ Ollama Error: " + request.error + " — is Ollama running? Run: ollama serve");
            SetDialogue("❌ Ollama not responding. Is it running?");
            ResetToIdle();
            yield break;
        }

        string raw = request.downloadHandler.text;

        string replyText = ExtractJsonField(raw, "response");

        if (string.IsNullOrEmpty(replyText))
        {
            Debug.LogWarning("⚠️ ExtractJsonField failed, trying JsonUtility...");
            try
            {
                OllamaResponse res = JsonUtility.FromJson<OllamaResponse>(raw);
                if (res != null && !string.IsNullOrEmpty(res.response))
                    replyText = res.response;
            }
            catch { }

            if (string.IsNullOrEmpty(replyText))
            {
                Debug.LogWarning("⚠️ JsonUtility also failed. Using raw reply.");
                replyText = raw;
            }
        }

        replyText = replyText
            .Replace("\\n", " ")
            .Replace("\\\"", "\"")
            .Replace("\\", "")
            .Trim();

        Debug.Log("======================================");
        Debug.Log(">>> 🤖 LLM USED: LOCAL OLLAMA (" + ollamaModel + ")");
        Debug.Log(">>> REPLY: " + replyText);
        Debug.Log("======================================");

        if (string.IsNullOrEmpty(replyText))
        {
            Debug.LogWarning("⚠️ Reply text is empty after parsing!");
            SetDialogue("⚠️ Yue Fei had nothing to say.");
            ResetToIdle();
            yield break;
        }

        replyText = replyText.Replace("\n", " ").Replace("\r", " ").Trim();

        // ── FORCE CROP TO 2 SENTENCES MAXIMUM ──
        MatchCollection matches = Regex.Matches(replyText, @"[^.!?。！？]+[.!?。！？]+");
        if (matches.Count >= 2)
            replyText = matches[0].Value + " " + matches[1].Value;
        else if (matches.Count == 1)
            replyText = matches[0].Value;

        replyText = replyText.Trim();

        if (replyText.StartsWith("My name is Yue Fei"))
        {
            int index = replyText.IndexOf(".");
            if (index > 0 && index < replyText.Length - 1)
                replyText = replyText.Substring(index + 1).Trim();
        }

        Debug.Log("Ollama reply: " + replyText);

        lastUserText = userText;
        SetDialogue("You: " + userText + "\nYue Fei: <color=#AAAAAA>...</color>");

        // Detect emotion from USER's question (more reliable than AI's stoic reply)
        StartCoroutine(DetectEmotionThenSpeak(replyText, userText));
        Debug.Log("🧠 FINAL REPLY: " + replyText);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 4 — Emotion detection
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator DetectEmotionThenSpeak(string originalText, string userQuestion = null)
    {
        statusMessage = "🎭 Reading emotion & generating voice...";

        string lockedText = originalText;
        string shortText = lockedText.Length > 200 ? lockedText.Substring(0, 200) : lockedText;

        // Analyze the USER's question for emotion — more reliable than the NPC's stoic reply
        string emotionSource = !string.IsNullOrEmpty(userQuestion) ? userQuestion : shortText;
        Debug.Log("🎭 Analysing emotion from: " + emotionSource);

        string dominantEmotion = "neutral";
        bool emotionDone = false;

        // ── Launch emotion detection in parallel (don't yield yet) ──
        StartCoroutine(emotionDetector.DetectSentimentGoogle(
            emotionSource,
            googleApiKey,
            result =>
            {
                dominantEmotion = result;
                if (result == "neutral")
                    Debug.LogWarning("⚠️ Emotion came back neutral — Google NLP may have returned Score=0 or failed. Check that 'Cloud Natural Language API' is enabled in your Google Cloud project.");
                else
                    Debug.Log("💯 Emotion detected from Google NLP: " + result);
                emotionDone = true;
            }
        ));

        // ── Launch TTS immediately without waiting for emotion ──
        Debug.Log("🔒 FINAL TEXT SENT TO TTS: " + lockedText);
        yield return StartCoroutine(SendToTTS(lockedText));

        // ── Wait for emotion to finish if it hasn't yet ──
        float timeout = 5f;
        float elapsed = 0f;
        while (!emotionDone && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!emotionDone)
            Debug.LogWarning("⚠️ Emotion detection timed out — using fallback: neutral");

        Debug.Log("Emotion detected: " + dominantEmotion);
        if (bridge != null)
            bridge.OnAIResponseReceived(dominantEmotion);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 5 — Google Text-to-Speech
    // ─────────────────────────────────────────────────────────────────────

    [System.Serializable]
    public class GoogleTTSRequest
    {
        public Input input;
        public Voice voice;
        public AudioConfig audioConfig;

        [System.Serializable] public class Input { public string text; }
        [System.Serializable] public class Voice { public string languageCode; public string name; }
        [System.Serializable] public class AudioConfig { public string audioEncoding; public double speakingRate; }
    }

    IEnumerator SendToTTS(string text)
    {
        Debug.Log("🔊 FINAL INPUT TO TTS: " + text);

        if (string.IsNullOrEmpty(googleApiKey))
        {
            Debug.LogError("❌ Google API key missing");
            SetDialogue("❌ Google API key missing.");
            ResetToIdle();
            yield break;
        }

        if (string.IsNullOrEmpty(text))
        {
            Debug.LogError("❌ TTS text is EMPTY");
            ResetToIdle();
            yield break;
        }

        statusMessage = "🔊 Generating voice...";

        string voiceLanguage =
            detectedLanguage == "zh-CN" ? ttsLanguageCodeZh :
            detectedLanguage == "ms-MY" ? ttsLanguageCodeMs :
            ttsLanguageCodeEn;

        string voiceName =
            detectedLanguage == "zh-CN" ? ttsVoiceNameZh :
            detectedLanguage == "ms-MY" ? ttsVoiceNameMs :
            ttsVoiceNameEn;

        string cleanText = text.Replace("\"", "").Replace("\\", "");

        GoogleTTSRequest ttsRequest = new GoogleTTSRequest
        {
            input = new GoogleTTSRequest.Input { text = cleanText },
            voice = new GoogleTTSRequest.Voice { languageCode = voiceLanguage, name = voiceName },
            audioConfig = new GoogleTTSRequest.AudioConfig { audioEncoding = "LINEAR16", speakingRate = ttsSpeakingRate }
        };

        string json = JsonUtility.ToJson(ttsRequest);
        Debug.Log("📤 FINAL TTS JSON: " + json);

        string url = "https://texttospeech.googleapis.com/v1/text:synthesize?key=" + googleApiKey;

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("❌ TTS Error: " + request.error);
            Debug.LogError("❌ TTS Response: " + request.downloadHandler.text);
            SetDialogue("❌ TTS failed. Check console.");
            ResetToIdle();
            yield break;
        }

        string responseText = request.downloadHandler.text;
        string audioBase64 = ExtractJsonField(responseText, "audioContent");

        if (string.IsNullOrEmpty(audioBase64))
        {
            Debug.LogError("❌ No audioContent in TTS response!");
            SetDialogue("❌ No audio returned from Google TTS.");
            ResetToIdle();
            yield break;
        }

        byte[] rawPcm = System.Convert.FromBase64String(audioBase64);
        byte[] wavData = AddWavHeader(rawPcm, 1, 24000, 16);
        StartCoroutine(PlayWav(wavData, text));
        Debug.Log("🔊 TTS complete for: " + text);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 6 — Play voice on NPC
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator PlayWav(byte[] wavData, string spokenText)
    {
        statusMessage = "💬 Yue Fei is speaking...";

        string tempPath = Application.temporaryCachePath + "/yuefei_voice.wav";
        File.WriteAllBytes(tempPath, wavData);

        using (UnityWebRequest audioRequest = UnityWebRequestMultimedia.GetAudioClip(
                   "file://" + tempPath, AudioType.WAV))
        {
            yield return audioRequest.SendWebRequest();

            if (audioRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("❌ Audio load error: " + audioRequest.error);
                ResetToIdle();
                yield break;
            }

            AudioClip ttsClip = DownloadHandlerAudioClip.GetContent(audioRequest);

            if (characterVoice == null)
            {
                Debug.LogError("❌ characterVoice is NULL — assign AudioSource in Inspector!");
                ResetToIdle();
                yield break;
            }

            characterVoice.clip = ttsClip;
            characterVoice.Play();

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", true);

            Debug.Log("Yue Fei is speaking...");

            StartCoroutine(TypewriterSubtitle("You: " + lastUserText + "\n<color=#FFFF55>Yue Fei: ", spokenText + "</color>", ttsClip.length));

            yield return new WaitForSeconds(ttsClip.length);

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", false);

            ResetToIdle();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helper
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator TypewriterSubtitle(string prefix, string spokenText, float duration)
    {
        float timePerChar = duration / Mathf.Max(1, spokenText.Length);
        string currentText = "";

        if (dialogueText != null)
        {
            if (chineseFont != null && defaultFont != null)
            {
                if (Regex.IsMatch(spokenText, @"[\u4e00-\u9fff]"))
                    dialogueText.font = chineseFont;
                else
                    dialogueText.font = defaultFont;
            }

            for (int i = 0; i < spokenText.Length; i++)
            {
                currentText += spokenText[i];
                dialogueText.text = prefix + currentText;

                if (dialogueScrollView != null)
                {
                    Canvas.ForceUpdateCanvases();
                    dialogueScrollView.verticalNormalizedPosition = 0f;
                }

                yield return new WaitForSeconds(timePerChar * 0.9f); // Try to finish slightly before audio ends
            }
            dialogueText.text = prefix + spokenText;
        }
    }

    void ResetToIdle()
    {
        isProcessing = false;
        statusMessage = "Press and Hold SPACE to talk";
        if (dialogueText != null && string.IsNullOrEmpty(dialogueText.text))
            SetDialogue("Press and hold SPACE to speak to Yue Fei.");
    }
}