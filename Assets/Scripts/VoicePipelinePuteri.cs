using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// VoicePipelinePuteri — voice pipeline for the Puteri Gunung Ledang NPC.
///
/// Pipeline:
///   SPACE held  →  Microphone (16 kHz, max 5 s)
///               →  Google STT  (primary: ms-MY, fallback: zh-CN, en-US)
///               →  DetectLanguage()
///               →  Ollama LLM  (character reply)
///               →  EmotionDetector  (Ollama second call — zero cost, local)
///               →  AIBridgePuteri.OnAIResponseReceived(emotion)
///               →  Google TTS  (female voice, +2st pitch)
///               →  AudioSource playback + Animator IsTalking
///
/// FIX: Uses manual JSON parsing instead of JsonUtility to handle
///      Unicode characters and long prompt strings in config.json.
/// </summary>
public class VoicePipelinePuteri : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("Environment Bridge")]
    public AIBridgePuteri bridge;

    [Header("NPC References")]
    public AudioSource characterVoice;
    public Animator characterAnimator;

    // ── Config (loaded from StreamingAssets/config.json) ──────────────────
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

    // ── Runtime state ─────────────────────────────────────────────────────
    private AudioClip clip;
    private bool recording = false;
    private bool isProcessing = false;
    private string detectedLanguage = "ms-MY";

    public string statusMessage = "⏳ Puteri is speaking...";

    // ── EmotionDetector (auto-attached) ───────────────────────────────────
    private EmotionDetector emotionDetector;

    // ─────────────────────────────────────────────────────────────────────
    //  Lightweight JSON field extractor
    //  Replaces JsonUtility which silently drops fields containing
    //  Unicode characters, long strings, or special prompt text.
    // ─────────────────────────────────────────────────────────────────────
    private string ExtractJsonField(string json, string fieldName)
    {
        // Matches: "fieldName": "value"  (handles escaped quotes inside value)
        string pattern = "\"" + fieldName + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"";
        Match m = Regex.Match(json, pattern);
        if (!m.Success) return "";

        // Unescape standard JSON escape sequences
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

        foreach (string device in Microphone.devices)
            Debug.Log("Mic found: " + device);

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
    //  Config loader — uses manual regex extraction instead of JsonUtility
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
        ollamaUrl = ExtractJsonField(json, "ollamaUrl");
        ollamaModel = ExtractJsonField(json, "ollamaModel");

        // ── Puteri TTS voices ─────────────────────────────────────────────
        ttsLanguageCodeMs = ExtractJsonField(json, "puteriTtsLanguageCodeMs");
        ttsVoiceNameMs = ExtractJsonField(json, "puteriTtsVoiceNameMs");
        ttsLanguageCodeZh = ExtractJsonField(json, "puteriTtsLanguageCodeZh");
        ttsVoiceNameZh = ExtractJsonField(json, "puteriTtsVoiceNameZh");
        ttsLanguageCodeEn = ExtractJsonField(json, "puteriTtsLanguageCodeEn");
        ttsVoiceNameEn = ExtractJsonField(json, "puteriTtsVoiceNameEn");

        // ── Puteri prompts ────────────────────────────────────────────────
        promptMs = ExtractJsonField(json, "puteriPromptMs");
        promptZh = ExtractJsonField(json, "puteriPromptZh");
        promptEn = ExtractJsonField(json, "puteriPromptEn");

        // ── Validation log ────────────────────────────────────────────────
        Debug.Log("Puteri: config.json loaded.");
        Debug.Log("  googleApiKey : " + (string.IsNullOrEmpty(googleApiKey) ? "MISSING ❌" : "OK ✓ (" + googleApiKey.Length + " chars)"));
        Debug.Log("  ollamaUrl    : " + (string.IsNullOrEmpty(ollamaUrl) ? "MISSING ❌" : ollamaUrl));
        Debug.Log("  ollamaModel  : " + (string.IsNullOrEmpty(ollamaModel) ? "MISSING ❌" : ollamaModel));
        Debug.Log("  promptMs     : " + (string.IsNullOrEmpty(promptMs) ? "MISSING ❌" : "OK ✓ (" + promptMs.Length + " chars)"));
        Debug.Log("  ttsVoiceMs   : " + (string.IsNullOrEmpty(ttsVoiceNameMs) ? "MISSING ❌" : ttsVoiceNameMs));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 1 — Record microphone
    // ─────────────────────────────────────────────────────────────────────

    void StartRecording()
    {
        recording = true;
        isProcessing = false;
        detectedLanguage = "ms-MY";
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

    public static byte[] AudioClipToWav(AudioClip clip)
    {
        float[] samples = new float[clip.samples];
        clip.GetData(samples, 0);
        byte[] bytes = new byte[samples.Length * 2];
        int offset = 0;

        foreach (float sample in samples)
        {
            float amplified = Mathf.Clamp(sample * 3f, -1f, 1f);
            short value = (short)(amplified * short.MaxValue);
            System.BitConverter.GetBytes(value).CopyTo(bytes, offset);
            offset += 2;
        }

        return bytes;
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
    //  Language detection
    // ─────────────────────────────────────────────────────────────────────

    string DetectLanguage(string text)
    {
        if (Regex.IsMatch(text, @"[\u4e00-\u9fff]"))
            return "zh-CN";

        string lower = text.ToLower();
        string[] malayMarkers =
        {
        "apa", "siapa", "kenapa", "bagaimana", "di mana",
        "awak", "kamu", "saya", "anda", "dengan", "untuk",
        "tidak", "ada", "adalah", "ini", "itu", "yang",
        "boleh", "mahu", "sudah", "belum", "bukan", "juga",
        "cerita", "siapakah", "apakah", "mengapa", "kau",
        "dia", "mereka", "kami", "kita", "punya", "sangat",
        "encik", "cikgu", "tuan", "puan", "selamat", "terima",
        "puteri", "gunung", "ledang", "raja", "hikayat"
    };

        int malayCount = 0;
        foreach (string marker in malayMarkers)
            if (lower.Contains(marker))
                malayCount++;

        if (malayCount >= 2)   // ← needs 2+ matches to be Malay
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

        statusMessage = "🤖 Puteri is thinking...";

        string prompt =
            detectedLanguage == "ms-MY" ? promptMs :
            detectedLanguage == "zh-CN" ? promptZh :
            promptEn;

        if (string.IsNullOrEmpty(prompt))
            Debug.LogWarning("Prompt is empty for language: " + detectedLanguage);

        var ollamaRequest = new OllamaRequest
        {
            model = ollamaModel,
            prompt = prompt + "\n\nUser: " + userText + "\nPuteri:",
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

        OllamaResponse ollamaResponse = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
        string replyText = ollamaResponse.response
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("\"", "'")
            .Replace("\\", " ")
            .Trim();

        Debug.Log("Ollama reply: " + replyText);

        StartCoroutine(DetectEmotionThenSpeak(replyText));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 4 — Emotion detection
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator DetectEmotionThenSpeak(string replyText)
    {
        statusMessage = "🎭 Reading emotion...";

        // ── Truncate to first 200 chars to avoid Ollama 400 error ──
        string shortText = replyText.Length > 200
            ? replyText.Substring(0, 200)
            : replyText;

        string dominantEmotion = "neutral";

        yield return StartCoroutine(
            emotionDetector.DetectEmotion(
                shortText,        // ← use shortText instead of replyText
                ollamaUrl,
                ollamaModel,
                result => dominantEmotion = result
            )
        );

        Debug.Log("Emotion detected: " + dominantEmotion);

        if (bridge != null)
            bridge.OnAIResponseReceived(dominantEmotion);

        StartCoroutine(SendToTTS(replyText));  // ← still speak full reply
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step 5 — Google Text-to-Speech
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator SendToTTS(string text)
    {
        if (string.IsNullOrEmpty(googleApiKey))
        {
            Debug.LogError("Google API key not loaded. Check config.json googleApiKey field.");
            ResetToIdle();
            yield break;
        }

        statusMessage = "🔊 Generating voice...";

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

    // ─────────────────────────────────────────────────────────────────────
    //  Step 6 — Play voice on NPC
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator PlayMp3(byte[] mp3Data)
    {
        statusMessage = "💬 Puteri is speaking...";

        string tempPath = Application.temporaryCachePath + "/puteri_voice.mp3";
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

            Debug.Log("Puteri is speaking...");
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