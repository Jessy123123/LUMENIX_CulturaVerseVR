using System.Collections;
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

    // ── Config (loaded from StreamingAssets/config.json) ──────────────────
    private string googleApiKey;
    private string huggingFaceApiKey;
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
        // Don't clear — VoiceRecorderPuteri may have already written pre-recorded captions
    }

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

        if (!File.Exists(path))
        {
            Debug.LogError("config.json not found at: " + path);
            return;
        }

        string json = File.ReadAllText(path, Encoding.UTF8);

        googleApiKey = ExtractJsonField(json, "googleApiKey");
        huggingFaceApiKey = ExtractJsonField(json, "huggingFaceApiKey");
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

        Debug.Log("Puteri: config.json loaded.");
        Debug.Log("  googleApiKey     : " + (string.IsNullOrEmpty(googleApiKey) ? "MISSING ❌" : "OK ✓ (" + googleApiKey.Length + " chars)"));
        Debug.Log("  huggingFaceApiKey: " + (string.IsNullOrEmpty(huggingFaceApiKey) ? "MISSING ❌" : "OK ✓ (" + huggingFaceApiKey.Length + " chars)"));
        Debug.Log("  ollamaUrl        : " + (string.IsNullOrEmpty(ollamaUrl) ? "MISSING ❌" : ollamaUrl));
        Debug.Log("  ollamaModel      : " + (string.IsNullOrEmpty(ollamaModel) ? "MISSING ❌" : ollamaModel));
        Debug.Log("  promptMs         : " + (string.IsNullOrEmpty(promptMs) ? "MISSING ❌" : "OK ✓ (" + promptMs.Length + " chars)"));
        Debug.Log("  ttsVoiceMs       : " + (string.IsNullOrEmpty(ttsVoiceNameMs) ? "MISSING ❌" : ttsVoiceNameMs));
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

        string[] strongMalayMarkers =
        {
            "awak", "kamu", "saya", "anda", "tidak", "adalah",
            "boleh", "mahu", "sudah", "belum", "bukan",
            "siapa", "kenapa", "bagaimana", "mengapa",
            "puteri", "gunung", "ledang", "hikayat",
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

        // ✅ Strictly enforce single language — no mixing allowed
        string languageInstruction =
            detectedLanguage == "ms-MY" ? "IMPORTANT: Reply ONLY in Malay (Bahasa Melayu). Do NOT use any English or Chinese words." :
            detectedLanguage == "zh-CN" ? "重要：只用中文回答。不要混入任何英文或马来文。" :
            "IMPORTANT: Reply ONLY in English. Do NOT use any Malay or Chinese words.";

        if (string.IsNullOrEmpty(prompt))
            Debug.LogWarning("Prompt is empty for language: " + detectedLanguage);

        var ollamaRequest = new OllamaRequest
        {
            model = ollamaModel,
            prompt = prompt + "\n\n" + languageInstruction + "\n\nUser: " + userText + "\nPuteri:",
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

        // ✅ Save reply so PlayMp3 can show it as caption
        currentReplyText = replyText;

        string shortText = replyText.Length > 200
            ? replyText.Substring(0, 200)
            : replyText;

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

        StartCoroutine(SendToTTS(replyText));
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

        string tempPath = Path.Combine(Application.temporaryCachePath, "puteri_tts.mp3");
        File.WriteAllBytes(tempPath, mp3Data);
        string fileUrl = "file:///" + tempPath.Replace("\\", "/");

        Debug.Log("Loading audio from: " + fileUrl);

        using (UnityWebRequest audioRequest = UnityWebRequestMultimedia.GetAudioClip(
                fileUrl, AudioType.MPEG))
        {
            ((DownloadHandlerAudioClip)audioRequest.downloadHandler).streamAudio = true;

            yield return audioRequest.SendWebRequest();

            Debug.Log("Audio request result: " + audioRequest.result);

            if (audioRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Audio load error: " + audioRequest.error);
                StartCoroutine(PlayMp3Fallback(tempPath));
                yield break;
            }

            AudioClip ttsClip = DownloadHandlerAudioClip.GetContent(audioRequest);

            if (ttsClip == null || ttsClip.length == 0)
            {
                Debug.LogError("AudioClip is null or empty!");
                ResetToIdle();
                yield break;
            }

            Debug.Log("AudioClip loaded! Length: " + ttsClip.length + "s");

            // ✅ Set font before playing
            if (captionText != null)
            {
                if (detectedLanguage == "zh-CN" && fontChinese != null)
                    captionText.font = fontChinese;
                else if (fontDefault != null)
                    captionText.font = fontDefault;
            }

            // ✅ Start audio and typewriter AT THE SAME TIME
            characterVoice.clip = ttsClip;
            characterVoice.Play();

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", true);

            // ✅ Run typewriter synced to audio duration (appends to history)
            if (captionText != null)
                StartCoroutine(TypewriterSync(currentReplyText, ttsClip.length));

            Debug.Log("characterVoice.isPlaying: " + characterVoice.isPlaying);

            yield return new WaitForSeconds(ttsClip.length);

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", false);

            ResetToIdle();
        }
    }

    // ✅ Typewriter that finishes exactly when audio ends (appends to history)
    IEnumerator TypewriterSync(string fullText, float audioDuration)
    {
        if (captionText == null) yield break;

        int totalChars = fullText.Length;
        if (totalChars == 0) yield break;

        float delayPerChar = audioDuration / totalChars;

        // Clamp so it doesn't feel too slow or too fast
        delayPerChar = Mathf.Clamp(delayPerChar, 0.01f, 0.08f);

        // Add a newline separator before appending if there's existing text
        string prefix = string.IsNullOrEmpty(captionText.text) ? "" : captionText.text + "\n";

        for (int i = 0; i < totalChars; i++)
        {
            captionText.text = prefix + fullText.Substring(0, i + 1);
            ScrollToBottom();
            yield return new WaitForSeconds(delayPerChar);
        }

        // Ensure full text is shown by the end
        captionText.text = prefix + fullText;
        ScrollToBottom();
    }

    // Fallback: use AudioSource.PlayOneShot with Resources if above fails
    IEnumerator PlayMp3Fallback(string tempPath)
    {
        Debug.Log("Fallback: trying UnityWebRequest loader...");

        string fileUrl = "file:///" + tempPath.Replace("\\", "/");

        using (UnityWebRequest audioRequest = UnityWebRequestMultimedia.GetAudioClip(
                fileUrl, AudioType.MPEG))
        {
            yield return audioRequest.SendWebRequest();

            if (audioRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Fallback audio error: " + audioRequest.error);
                ResetToIdle();
                yield break;
            }

            AudioClip ttsClip = DownloadHandlerAudioClip.GetContent(audioRequest);

            if (ttsClip == null)
            {
                Debug.LogError("Fallback AudioClip is null!");
                ResetToIdle();
                yield break;
            }

            if (captionText != null)
            {
                if (string.IsNullOrEmpty(captionText.text))
                    captionText.text = currentReplyText;
                else
                    captionText.text += "\n" + currentReplyText;
                ScrollToBottom();
            }

            characterVoice.clip = ttsClip;
            characterVoice.Play();

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", true);

            yield return new WaitForSeconds(ttsClip.length);

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", false);

            ResetToIdle();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────

    void ResetToIdle()
    {
        isProcessing = false;
        statusMessage = "Press and Hold SPACE to talk";
        // Caption history is preserved — not cleared
    }

    /// <summary>
    /// Scrolls the caption ScrollRect to the bottom so the latest line is visible.
    /// </summary>
    void ScrollToBottom()
    {
        if (captionScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            captionScrollRect.verticalNormalizedPosition = 0f;
        }
    }
}