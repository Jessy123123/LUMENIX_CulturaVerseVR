using UnityEngine;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine.Networking;
using System.Collections;
using TMPro;

/// <summary>
/// VoiceRecorder — legacy recorder now connected to config.json.
/// NOTE: If you are using VoicePipeline.cs, DISABLE this script.
/// Only use VoiceRecorder as a standalone fallback.
/// </summary>

#if !UNITY_WEBGL
public class VoiceRecorder : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────
    public TextMeshProUGUI instructionText;
    public TextMeshProUGUI dialogueText;

    // ── Config (loaded from StreamingAssets/config.json) ──────────────────
    private string ollamaUrl;
    private string ollamaModel;
    private string characterPromptEn;

    // ── Runtime ───────────────────────────────────────────────────────────
    private AudioClip clip;
    private bool recording = false;
    string AI_FOLDER;

    // ─────────────────────────────────────────────────────────────────────
    //  Null-safe dialogue helper
    // ─────────────────────────────────────────────────────────────────────
    private void SetDialogue(string message)
    {
        if (dialogueText != null)
        {
            dialogueText.gameObject.SetActive(true);
            dialogueText.text = message;
        }
        else
        {
            Debug.LogError("❌ dialogueText is NULL on VoiceRecorder — assign it in the Inspector!");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  JSON field extractor
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
        characterPromptEn = ExtractJsonField(json, "characterPromptEn");

        Debug.Log("VoiceRecorder: config loaded.");
        Debug.Log("  ollamaUrl  : " + (string.IsNullOrEmpty(ollamaUrl) ? "MISSING ❌" : ollamaUrl));
        Debug.Log("  ollamaModel: " + (string.IsNullOrEmpty(ollamaModel) ? "MISSING ❌" : ollamaModel));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────

    void Start()
    {
        AI_FOLDER = Application.dataPath + "/../AI/";
        Directory.CreateDirectory(AI_FOLDER);

        if (dialogueText == null)
            Debug.LogError("❌ VoiceRecorder: 'dialogueText' is not assigned in the Inspector!");
        if (instructionText == null)
            Debug.LogWarning("⚠️ VoiceRecorder: 'instructionText' is not assigned.");

        LoadConfig();
        Debug.Log("VoiceRecorder started. Hold SPACE to record.");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log("START RECORDING");
            StartRecording();
        }

        if (Input.GetKeyUp(KeyCode.Space))
        {
            Debug.Log("STOP RECORDING");
            StopRecording();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Recording
    // ─────────────────────────────────────────────────────────────────────

    void StartRecording()
    {
        if (Microphone.devices.Length > 0)
        {
            clip = Microphone.Start(null, false, 10, 44100);
        }
        else
        {
            Debug.LogError("❌ No microphone detected");
            return;
        }

        recording = true;

        if (instructionText != null)
            instructionText.gameObject.SetActive(false);

        SetDialogue("🎙️ Listening...");
    }

    void StopRecording()
    {
        Microphone.End(null);
        recording = false;

        if (clip == null)
        {
            Debug.LogError("❌ AudioClip is null after recording.");
            return;
        }

        string path = AI_FOLDER + "question.wav";
        SaveWav(path, clip);

        Debug.Log("Saved WAV to: " + path);
        SetDialogue("⏳ Processing...");
        RunSpeechToText();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  STT → Ollama pipeline
    // ─────────────────────────────────────────────────────────────────────

    void RunSpeechToText()
    {
        // ⚠️ Replace this with real Google STT or Whisper integration.
        // For now uses a hardcoded test string.
        string userText = "who are you";
        Debug.Log("⚠️ HARDCODED USER TEXT (replace with real STT): " + userText);

        SetDialogue("You: " + userText + "\n<color=#AAAAAA>Yue Fei is thinking...</color>");
        StartCoroutine(SendToOllama(userText));
    }

    IEnumerator SendToOllama(string userText)
    {
        if (string.IsNullOrEmpty(ollamaUrl))
        {
            Debug.LogError("❌ Ollama URL missing. Check config.json.");
            SetDialogue("❌ Ollama URL missing.");
            yield break;
        }

        string url = ollamaUrl + "/api/generate";

        string prompt = string.IsNullOrEmpty(characterPromptEn)
            ? "You are Yue Fei, a strict ancient general. Speak briefly. Question: "
            : characterPromptEn;

        string safeUserText = userText.Replace("\"", "\\\"");
        string json = "{\"model\":\"" + ollamaModel + "\",\"prompt\":\"" + prompt + safeUserText + "\",\"stream\":false}";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("❌ Ollama Error: " + request.error);
            SetDialogue("❌ Ollama not responding. Is it running?");
            yield break;
        }

        string responseJson = request.downloadHandler.text;
        OllamaResponse res = JsonUtility.FromJson<OllamaResponse>(responseJson);

        if (res == null || string.IsNullOrEmpty(res.response))
        {
            Debug.LogError("❌ Empty response from Ollama.");
            SetDialogue("❌ No reply from Yue Fei.");
            yield break;
        }

        string aiText = res.response;
        SetDialogue("You: " + userText + "\nYue Fei: " + aiText);

        GenerateVoice(aiText);
        StartCoroutine(PlayVoice());
    }

    // ─────────────────────────────────────────────────────────────────────
    //  TTS (local Python)
    // ─────────────────────────────────────────────────────────────────────

    void GenerateVoice(string text)
    {
        string filePath = AI_FOLDER + "yuefei_voice.mp3";

        if (File.Exists(filePath))
            File.Delete(filePath);

        text = text.Replace("\"", "").Replace("\n", " ");

        System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo();
        start.FileName = "python";
        start.Arguments = $"\"{AI_FOLDER}tts.py\" \"{text}\"";
        start.UseShellExecute = false;
        start.CreateNoWindow = true;

        System.Diagnostics.Process.Start(start);
    }

    IEnumerator PlayVoice()
    {
        string filePath = AI_FOLDER + "yuefei_voice.mp3";

        float timeout = 10f;
        float elapsed = 0f;
        while (!File.Exists(filePath))
        {
            yield return new WaitForSeconds(0.5f);
            elapsed += 0.5f;
            if (elapsed >= timeout)
            {
                Debug.LogError("❌ Timed out waiting for yuefei_voice.mp3");
                yield break;
            }
        }

        string path = "file:///" + filePath;

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(path, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("❌ Audio load error: " + www.error);
            }
            else
            {
                AudioClip audioClip = DownloadHandlerAudioClip.GetContent(www);
                AudioSource audio = GetComponent<AudioSource>();

                if (audio == null)
                {
                    Debug.LogError("❌ No AudioSource on this GameObject!");
                    yield break;
                }

                audio.clip = audioClip;
                audio.Play();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  WAV saver
    // ─────────────────────────────────────────────────────────────────────

    void SaveWav(string filepath, AudioClip clip)
    {
        var samples = new float[clip.samples];
        clip.GetData(samples, 0);

        using (FileStream fs = new FileStream(filepath, FileMode.Create))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            int sampleCount = samples.Length;
            int frequency = clip.frequency;

            bw.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"));
            bw.Write(36 + sampleCount * 2);
            bw.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.UTF8.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(frequency);
            bw.Write(frequency * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.UTF8.GetBytes("data"));
            bw.Write(sampleCount * 2);

            foreach (var sample in samples)
            {
                short value = (short)(sample * short.MaxValue);
                bw.Write(value);
            }
        }
    }
}

[System.Serializable]
public class OllamaResponse
{
    public string response;
}
#endif