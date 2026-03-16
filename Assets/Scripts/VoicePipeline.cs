using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class VoicePipeline : MonoBehaviour
{
    // ── Private fields loaded from config.json ──
    public string googleApiKey;
    public string ollamaUrl;
    public string ollamaModel;
    public string ttsLanguageCode;
    public string ttsVoiceName;
    public string ttsLanguageCodeEn;
    public string ttsVoiceNameEn;
    public string characterPromptZh;
    public string characterPromptEn;

    // ── Language detection ──
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
        public string ttsLanguageCode = "";
        public string ttsVoiceName = "";
        public string ttsLanguageCodeEn = "";
        public string ttsVoiceNameEn = "";
        public string characterPromptZh = "";
        public string characterPromptEn = "";
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
        {
            Debug.Log("Mic found: " + device);
        }

        isProcessing = true;
        statusMessage = "⏳ Yuefei is speaking...";
        StartCoroutine(WaitForIntro());
    }

    IEnumerator WaitForIntro()
    {
        yield return new WaitForSeconds(2.5f);

        while (characterVoice.isPlaying)
        {
            yield return null;
        }

        yield return new WaitForSeconds(0.5f);

        isProcessing = false;
        statusMessage = "Press and Hold SPACE to talk";
        Debug.Log("Intro finished — ready for questions!");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && !isProcessing)
        {
            StartRecording();
        }
        if (Input.GetKeyUp(KeyCode.Space) && recording)
        {
            StopRecording();
        }
    }

    void OnGUI()
    {
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.Box(new Rect(10, 10, 420, 60), "");

        if (recording)
            GUI.color = Color.red;
        else if (isProcessing)
            GUI.color = Color.yellow;
        else
            GUI.color = Color.green;

        GUIStyle style = new GUIStyle();
        style.fontSize = 20;
        style.normal.textColor = GUI.color;
        style.padding = new RectOffset(10, 10, 10, 10);

        GUI.Label(new Rect(15, 15, 410, 50), statusMessage, style);
    }

    // ─────────────────────────────────────────────
    //  Config
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
        ttsLanguageCode = config.ttsLanguageCode;
        ttsVoiceName = config.ttsVoiceName;
        ttsLanguageCodeEn = config.ttsLanguageCodeEn;
        ttsVoiceNameEn = config.ttsVoiceNameEn;
        characterPromptZh = config.characterPromptZh;
        characterPromptEn = config.characterPromptEn;

        Debug.Log("Config loaded successfully.");
        Debug.Log("Prompt ZH: " + characterPromptZh);
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

    byte[] AudioClipToWav(AudioClip clip)
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
    // ─────────────────────────────────────────────

    IEnumerator SendToSTT()
    {
        if (string.IsNullOrEmpty(googleApiKey))
        {
            Debug.LogError("Google API key is not loaded.");
            isProcessing = false;
            statusMessage = "Press and Hold SPACE to talk";
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
                ""alternativeLanguageCodes"": [""en-US""]
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
            isProcessing = false;
            statusMessage = "Press and Hold SPACE to talk";
            yield break;
        }

        string responseText = request.downloadHandler.text;
        Debug.Log("STT Response: " + responseText);

        STTResponse sttResponse = JsonUtility.FromJson<STTResponse>(responseText);

        if (sttResponse.results != null && sttResponse.results.Length > 0)
        {
            string transcript = sttResponse.results[0].alternatives[0].transcript;
            bool isChinese = Regex.IsMatch(transcript, @"[\u4e00-\u9fff]");
            detectedLanguage = isChinese ? "zh-CN" : "en-US";
            Debug.Log("Transcript: " + transcript);
            Debug.Log("Detected language: " + detectedLanguage);
            StartCoroutine(SendToOllama(transcript));
        }
        else
        {
            Debug.LogWarning("No transcript found in STT response.");
            isProcessing = false;
            statusMessage = "Press and Hold SPACE to talk";
        }
    }

    // ─────────────────────────────────────────────
    //  Step 3 — Ollama local LLM
    // ─────────────────────────────────────────────

    IEnumerator SendToOllama(string text)
    {
        if (string.IsNullOrEmpty(ollamaUrl))
        {
            Debug.LogError("Ollama URL is not loaded.");
            isProcessing = false;
            statusMessage = "Press and Hold SPACE to talk";
            yield break;
        }

        statusMessage = "🤖 AI is thinking...";
        Debug.Log("Sending to Ollama...");

        string characterPrompt = detectedLanguage == "zh-CN"
            ? characterPromptZh
            : characterPromptEn;

        string fullPrompt = characterPrompt + text;

        var ollamaRequest = new OllamaRequest
        {
            model = ollamaModel,
            prompt = fullPrompt,
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
            isProcessing = false;
            statusMessage = "Press and Hold SPACE to talk";
            yield break;
        }

        OllamaResponse ollamaResponse = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
        string replyText = ollamaResponse.response;

        replyText = replyText.Replace("\n", " ")
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
            isProcessing = false;
            statusMessage = "Press and Hold SPACE to talk";
            yield break;
        }

        statusMessage = "🔊 Generating voice...";
        Debug.Log("Sending to TTS...");

        string voiceLanguage = detectedLanguage == "zh-CN" ? ttsLanguageCode : ttsLanguageCodeEn;
        string voiceName = detectedLanguage == "zh-CN" ? ttsVoiceName : ttsVoiceNameEn;

        // Escape any double-quotes or backslashes inside the reply text
        string safeText = text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // Build SSML string
        string ssmlText = "<speak><prosody rate='slow' pitch='-2st'>" + safeText + "</prosody></speak>";

        // Manually build JSON so only "ssml" is present inside "input" (no "text" field).
        // Using JsonUtility would serialize BOTH fields even when one is null/empty,
        // which triggers Google's "oneof field 'input_source' is already set" 400 error.
        string json = "{"
            + "\"input\":{"
                + "\"ssml\":\"" + ssmlText + "\""
            + "},"
            + "\"voice\":{"
                + "\"languageCode\":\"" + voiceLanguage + "\","
                + "\"name\":\"" + voiceName + "\","
                + "\"ssmlGender\":\"MALE\""
            + "},"
            + "\"audioConfig\":{"
                + "\"audioEncoding\":\"MP3\""
            + "}"
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
            isProcessing = false;
            statusMessage = "Press and Hold SPACE to talk";
            yield break;
        }

        string responseText = request.downloadHandler.text;
        TTSResponse ttsResponse = JsonUtility.FromJson<TTSResponse>(responseText);
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

        string fileUrl = "file://" + tempPath;
        using (UnityWebRequest audioRequest = UnityWebRequestMultimedia.GetAudioClip(fileUrl, AudioType.MPEG))
        {
            yield return audioRequest.SendWebRequest();

            if (audioRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Audio load error: " + audioRequest.error);
                isProcessing = false;
                statusMessage = "Press and Hold SPACE to talk";
                yield break;
            }

            AudioClip ttsClip = DownloadHandlerAudioClip.GetContent(audioRequest);
            characterVoice.clip = ttsClip;
            characterVoice.Play();

            // Start talking animation
            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", true);

            Debug.Log("Character is speaking...");

            yield return new WaitForSeconds(ttsClip.length);

            // Return to idle
            if (characterAnimator != null)
            {
                characterAnimator.SetBool("IsTalking", false);
                Debug.Log("Returned to idle.");
            }
            else
            {
                Debug.LogError("characterAnimator is NULL — not assigned!");
            }

            isProcessing = false;
            statusMessage = "Press and Hold SPACE to talk";
        }
    }
}