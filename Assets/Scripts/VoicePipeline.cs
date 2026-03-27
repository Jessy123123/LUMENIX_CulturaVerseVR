using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
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
/// FIX: Uses manual JSON parsing instead of JsonUtility to handle
///      Unicode characters and long prompt strings in config.json.
/// FIX: EmotionDetector now uses HuggingFace + keyword fallback.
/// FIX: Improved language detection (2-tier Malay matching).
/// </summary>
public class VoicePipeline : MonoBehaviour
{
    public TextMeshProUGUI dialogueText;
    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("Environment Bridge")]
    public AI_bridge_YueFei bridge;

    [Header("NPC References")]
    public AudioSource characterVoice;
    public Animator characterAnimator;

    // ── Config (loaded from StreamingAssets/config.json) ──────────────────
    private string googleApiKey;
    private string huggingFaceApiKey;
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
    private string detectedLanguage = "zh-CN";   // Yue Fei defaults to Chinese

    private string statusMessage = "⏳ Yue Fei is speaking...";

    // ── EmotionDetector (auto-attached) ───────────────────────────────────
    private EmotionDetector emotionDetector;

    // ─────────────────────────────────────────────────────────────────────
    //  Lightweight JSON field extractor
    //  Replaces JsonUtility which silently drops fields containing
    //  Unicode characters, long strings, or special prompt text.
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
        LoadConfig();

        emotionDetector = gameObject.GetComponent<EmotionDetector>();
        if (emotionDetector == null)
            emotionDetector = gameObject.AddComponent<EmotionDetector>();

#if !UNITY_WEBGL
        foreach (string device in Microphone.devices)
            Debug.Log("Mic found: " + device);
#endif

        isProcessing = true;
        statusMessage = "⏳ Yue Fei is speaking...";
        StartCoroutine(WaitForIntro());
    }

    IEnumerator WaitForIntro()
    {
        yield return new WaitForSeconds(2.5f);

        while (characterVoice != null && characterVoice.isPlaying)
            yield return null;

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
            Debug.LogError("config.json not found at: " + path);
            return;
        }

        string json = File.ReadAllText(path, Encoding.UTF8);

        // ── Core ──────────────────────────────────────────────────────────
        googleApiKey = ExtractJsonField(json, "googleApiKey");
        huggingFaceApiKey = ExtractJsonField(json, "huggingFaceApiKey");
        ollamaUrl = ExtractJsonField(json, "ollamaUrl");
        ollamaModel = ExtractJsonField(json, "ollamaModel");

        // ── TTS voices ────────────────────────────────────────────────────
        ttsLanguageCodeZh = ExtractJsonField(json, "ttsLanguageCodeZh");
        ttsVoiceNameZh = ExtractJsonField(json, "ttsVoiceNameZh");
        ttsLanguageCodeEn = ExtractJsonField(json, "ttsLanguageCodeEn");
        ttsVoiceNameEn = ExtractJsonField(json, "ttsVoiceNameEn");
        ttsLanguageCodeMs = ExtractJsonField(json, "ttsLanguageCodeMs");
        ttsVoiceNameMs = ExtractJsonField(json, "ttsVoiceNameMs");

        // ── Character prompts ─────────────────────────────────────────────
        characterPromptZh = ExtractJsonField(json, "characterPromptZh");
        characterPromptEn = ExtractJsonField(json, "characterPromptEn");
        characterPromptMs = ExtractJsonField(json, "characterPromptMs");

        // ── Validation log ────────────────────────────────────────────────
        Debug.Log("VoicePipeline: config.json loaded.");
        Debug.Log("  googleApiKey     : " + (string.IsNullOrEmpty(googleApiKey) ? "MISSING ❌" : "OK ✓ (" + googleApiKey.Length + " chars)"));
        Debug.Log("  huggingFaceApiKey: " + (string.IsNullOrEmpty(huggingFaceApiKey) ? "MISSING ❌" : "OK ✓ (" + huggingFaceApiKey.Length + " chars)"));
        Debug.Log("  ollamaUrl        : " + (string.IsNullOrEmpty(ollamaUrl) ? "MISSING ❌" : ollamaUrl));
        Debug.Log("  ollamaModel      : " + (string.IsNullOrEmpty(ollamaModel) ? "MISSING ❌" : ollamaModel));
        Debug.Log("  promptZh         : " + (string.IsNullOrEmpty(characterPromptZh) ? "MISSING ❌" : "OK ✓ (" + characterPromptZh.Length + " chars)"));
        Debug.Log("  ttsVoiceZh       : " + (string.IsNullOrEmpty(ttsVoiceNameZh) ? "MISSING ❌" : ttsVoiceNameZh));
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
        detectedLanguage = "zh-CN";
        statusMessage = "🎙️ Recording... (release SPACE to send)";
        clip = Microphone.Start(Microphone.devices[0], false, 5, 16000);
        Debug.Log("Recording...");
#endif
    }

    void StopRecording()
    {
        Debug.Log("🎙️ Samples recorded: " + clip.samples);
        Debug.Log("🎙️ Frequency: " + clip.frequency);
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

    // ─────────────────────────────────────────────────────────────────────
    //  Step 2 — Google Speech-to-Text
    //  Primary: zh-CN   Alternatives: ms-MY, en-US
    // ─────────────────────────────────────────────────────────────────────

    [System.Serializable] private class STTResponse { public Result[] results = null; [System.Serializable] public class Result { public Alternative[] alternatives = null; [System.Serializable] public class Alternative { public string transcript = ""; } } }
    [System.Serializable] private class OllamaRequest { public string model; public string prompt; public bool stream; }
    [System.Serializable] private class OllamaResponse { public string response = ""; }
    [System.Serializable] private class TTSResponse { public string audioContent = ""; }

    IEnumerator SendToSTT()
    {
        if (string.IsNullOrEmpty(googleApiKey))
        {
            Debug.LogError("Google API key is not loaded. Check config.json googleApiKey field.");
            ResetToIdle();
            yield break;
        }

        statusMessage = "⏳ Converting speech to text...";

        byte[] audioData = AudioClipToWav(clip);
        string audioBase64 = System.Convert.ToBase64String(audioData);
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
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

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
            Debug.LogWarning("No transcript found — speak louder or closer to mic.");
            ResetToIdle();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Language detection — 2-tier Malay matching to avoid false positives
    // ─────────────────────────────────────────────────────────────────────

    string DetectLanguage(string text)
    {
        // Chinese characters — instant match
        if (Regex.IsMatch(text, @"[\u4e00-\u9fff]"))
            return "zh-CN";

        string lower = text.ToLower();

        // Strong Malay-only markers — 1 match is enough
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

        // Weak markers — need 2+ matches to count as Malay
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
    //  Step 3 — Ollama local LLM
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator SendToOllama(string userText)
    {
        if (string.IsNullOrEmpty(ollamaUrl))
        {
            Debug.LogError("Ollama URL is not loaded. Check config.json ollamaUrl field.");
            ResetToIdle();
            yield break;
        }

        statusMessage = "🤖 Yue Fei is thinking...";

        string prompt =
            detectedLanguage == "zh-CN" ? characterPromptZh :
            detectedLanguage == "ms-MY" ? characterPromptMs :
            characterPromptEn;

        if (string.IsNullOrEmpty(prompt))
            Debug.LogWarning("Prompt is empty for language: " + detectedLanguage);

        var ollamaRequest = new OllamaRequest
        {
            model = ollamaModel,
            prompt = prompt + "\n\nUser: " + userText + "\nYue Fei:",
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
            Debug.LogError("Ollama Error: " + request.error + " — is Ollama running? Run: ollama serve");
            ResetToIdle();
            yield break;
        }

        string responseText = request.downloadHandler.text;

        // 🔥 EXTRACT FULL RESPONSE MANUALLY
        string raw = request.downloadHandler.text;

        // 🔥 HARD EXTRACT (NO REGEX LIMIT)
        int start = raw.IndexOf("\"response\":\"");
        int end = raw.LastIndexOf("\"}");

        string replyText = "";

        // ✅ SAFE CHECK
        if (start != -1 && end != -1 && end > start)
        {
            start += 12; // move after "response":"
            replyText = raw.Substring(start, end - start);
        }
        else
        {
            Debug.LogWarning("⚠️ Failed to parse response safely, using fallback");
            replyText = raw; // fallback
        }

        // 🔥 CLEAN ESCAPES
        replyText = replyText
            .Replace("\\n", " ")
            .Replace("\\\"", "\"")
            .Replace("\\", "")
            .Trim();

        // fallback if extraction fails
        if (string.IsNullOrEmpty(replyText))
        {
            Debug.LogWarning("⚠️ Fallback to raw response");
            replyText = responseText;
        }

        // 🔥 FORCE USE FULL TEXT (REMOVE FIRST SENTENCE TRUNCATION)
        replyText = replyText.Replace("\n", " ").Replace("\r", " ").Trim();

        // 🔥 REMOVE SHORT INTRO LIKE "My name is Yue Fei."
        if (replyText.StartsWith("My name is Yue Fei"))
        {
            int index = replyText.IndexOf(".");
            if (index > 0 && index < replyText.Length - 1)
            {
                replyText = replyText.Substring(index + 1).Trim();
            }
        }

        Debug.Log("Ollama reply: " + replyText);
        if (dialogueText != null)
        {
            dialogueText.text = "Yue Fei: " + replyText;
        }

        StartCoroutine(DetectEmotionThenSpeak(replyText));
        Debug.Log("🧠 FINAL REPLY USED EVERYWHERE: " + replyText);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 4 — Emotion detection
    //  HuggingFace primary → keyword fallback
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator DetectEmotionThenSpeak(string originalText)
    {
        statusMessage = "🎭 Reading emotion...";

        // 🔒 LOCK ORIGINAL TEXT
        string lockedText = originalText;

        string shortText = lockedText.Length > 200
            ? lockedText.Substring(0, 200)
            : lockedText;

        string dominantEmotion = "neutral";

        yield return StartCoroutine(
            emotionDetector.DetectEmotionHF(
                shortText,
                huggingFaceApiKey,
                result => dominantEmotion = result
            )
        );

        Debug.Log("Emotion detected: " + dominantEmotion);

        if (bridge != null)
            bridge.OnAIResponseReceived(dominantEmotion);

        // 🔥 ALWAYS USE LOCKED TEXT
        Debug.Log("🔒 FINAL TEXT SENT TO TTS: " + lockedText);

        StartCoroutine(SendToTTS(lockedText));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 5 — Google Text-to-Speech (male, -2st pitch)
    // ─────────────────────────────────────────────────────────────────────

    [System.Serializable]
    public class GoogleTTSRequest
    {
        public Input input;
        public Voice voice;
        public AudioConfig audioConfig;

        [System.Serializable]
        public class Input
        {
            public string text;
        }

        [System.Serializable]
        public class Voice
        {
            public string languageCode;
            public string name;
        }

        [System.Serializable]
        public class AudioConfig
        {
            public string audioEncoding;
        }
    }

    IEnumerator SendToTTS(string text)
    {
        Debug.Log("🔊 FINAL INPUT TO TTS: " + text);
        if (string.IsNullOrEmpty(googleApiKey))
        {
            Debug.LogError("❌ Google API key missing");
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

        // 🔥 CLEAN TEXT (avoid JSON/SSML issues)
        string cleanText = text.Replace("\"", "").Replace("\\", "");

        // 🔥 BUILD SAFE REQUEST
        GoogleTTSRequest ttsRequest = new GoogleTTSRequest
        {
            input = new GoogleTTSRequest.Input
            {
                text = cleanText
            },
            voice = new GoogleTTSRequest.Voice
            {
                languageCode = voiceLanguage,
                name = voiceName
            },
            audioConfig = new GoogleTTSRequest.AudioConfig
            {
                audioEncoding = "MP3"
            }
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
            ResetToIdle();
            yield break;
        }

        // 🔥 Parse response manually (SAFER than JsonUtility)
        string responseText = request.downloadHandler.text;
        string audioBase64 = ExtractJsonField(responseText, "audioContent");

        if (string.IsNullOrEmpty(audioBase64))
        {
            Debug.LogError("❌ No audioContent in response!");
            ResetToIdle();
            yield break;
        }

        byte[] mp3Data = System.Convert.FromBase64String(audioBase64);

        StartCoroutine(PlayMp3(mp3Data));
        Debug.Log("🔊 TEXT RECEIVED BY TTS: " + text);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 6 — Play voice on NPC
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator PlayMp3(byte[] mp3Data)
    {
        statusMessage = "💬 Yue Fei is speaking...";

        string tempPath = Application.temporaryCachePath + "/yuefei_voice.mp3";
        File.WriteAllBytes(tempPath, mp3Data);

        using (UnityWebRequest audioRequest = UnityWebRequestMultimedia.GetAudioClip(
                   "file://" + tempPath, AudioType.MPEG))
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

            Debug.Log("Yue Fei is speaking...");
            yield return new WaitForSeconds(ttsClip.length);

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", false);
            else
                Debug.LogError("characterAnimator is NULL — assign it in the Inspector!");

            ResetToIdle();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helper
    // ─────────────────────────────────────────────────────────────────────

    void ResetToIdle()
    {
        isProcessing = false;
        statusMessage = "Press and Hold SPACE to talk";
    }
}
