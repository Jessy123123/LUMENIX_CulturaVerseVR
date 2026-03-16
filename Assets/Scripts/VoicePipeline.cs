using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class VoicePipeline : MonoBehaviour
{
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
    // Possible values: "ms-MY" | "zh-CN" | "en-US"
    private string detectedLanguage = "zh-CN";

    // ── Recording state ──
    private AudioClip clip;
    private bool recording = false;
    private bool isProcessing = false;
    private string statusMessage = "⏳ Yuefei is speaking...";

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

        public string yuefeiPromptMs = "";
        public string yuefeiPromptZh = "";
        public string yuefeiPromptEn = "";
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
        statusMessage = "⏳ Yuefei is speaking...";
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

        promptMs = config.yuefeiPromptMs;
        promptZh = config.yuefeiPromptZh;
        promptEn = config.yuefeiPromptEn;

        Debug.Log("Yuefei: config.json loaded successfully.");
        Debug.Log("Prompt ZH: " + promptZh);
    }

    // ─────────────────────────────────────────────
    //  Step 1 — Record microphone
    // ─────────────────────────────────────────────

    void StartRecording()
    {
        recording = true;
        isProcessing = false;
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
    //  Primary: zh-CN   Alternatives: ms-MY, en-US
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
    //  Language detection
    //  Chinese chars  → zh-CN
    //  Malay keywords → ms-MY
    //  Fallback       → en-US
    // ─────────────────────────────────────────────

    string DetectLanguage(string text)
    {
        // 1. Chinese characters → Mandarin
        if (Regex.IsMatch(text, @"[\u4e00-\u9fff]"))
            return "zh-CN";

        // 2. Common Malay words → Malay
        string lower = text.ToLower();
        string[] malayMarkers = {
            "apa", "siapa", "kenapa", "bagaimana", "di mana",
            "awak", "kamu", "saya", "anda", "dengan", "untuk",
            "tidak", "ada", "adalah", "ini", "itu", "yang",
            "boleh", "mahu", "sudah", "belum", "bukan", "juga",
            "cerita", "siapakah", "apakah", "mengapa", "kau",
            "dia", "mereka", "kami", "kita", "punya", "sangat"
        };

        foreach (string marker in malayMarkers)
        {
            if (lower.Contains(marker))
                return "ms-MY";
        }

        // 3. Default → English
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

        statusMessage = "🤖 AI is thinking...";
        Debug.Log("Sending to Ollama...");

        string characterPrompt =
            detectedLanguage == "zh-CN" ? promptZh :
            detectedLanguage == "ms-MY" ? promptMs :
            promptEn;

        var ollamaRequest = new OllamaRequest
        {
            model = ollamaModel,
            prompt = characterPrompt + text,
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
        StartCoroutine(SendToTTS(replyText));
    }

    // ─────────────────────────────────────────────
    //  Step 4 — Google Text-to-Speech
    //  FIX: Build JSON manually to avoid JsonUtility
    //  serializing both "text" and "ssml" fields,
    //  which causes a 400 "oneof input_source" error.
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
            detectedLanguage == "zh-CN" ? ttsLanguageCodeZh :
            detectedLanguage == "ms-MY" ? ttsLanguageCodeMs :
            ttsLanguageCodeEn;

        string voiceName =
            detectedLanguage == "zh-CN" ? ttsVoiceNameZh :
            detectedLanguage == "ms-MY" ? ttsVoiceNameMs :
            ttsVoiceNameEn;

        string safeText = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string ssmlText = "<speak><prosody rate='slow' pitch='-2st'>" + safeText + "</prosody></speak>";

        string json = "{"
            + "\"input\":{ \"ssml\":\"" + ssmlText + "\" },"
            + "\"voice\":{"
                + "\"languageCode\":\"" + voiceLanguage + "\","
                + "\"name\":\"" + voiceName + "\","
                + "\"ssmlGender\":\"MALE\""
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
        statusMessage = "💬 Character is speaking...";

        string tempPath = Application.temporaryCachePath + "/tts_output.mp3";
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

            Debug.Log("Character is speaking...");
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