using UnityEngine;
using System.IO;
using System.Diagnostics;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using TMPro;

#if !UNITY_WEBGL
public class VoiceRecorder : MonoBehaviour
{
private AudioClip clip;
public TextMeshProUGUI instructionText;
public TextMeshProUGUI dialogueText;
private bool recording = false;
string AI_FOLDER = Application.dataPath + "/../AI/";

void Start()
{
Directory.CreateDirectory(AI_FOLDER);
UnityEngine.Debug.Log("VoiceRecorder script started");
}

void Update()
{
//UnityEngine.Debug.Log("UPDATE RUNNING");
if (Input.GetKeyDown(KeyCode.Space))
{
    UnityEngine.Debug.Log("START RECORDING");
    StartRecording();
}

if (Input.GetKeyUp(KeyCode.Space))
{
    UnityEngine.Debug.Log("STOP RECORDING");
    StopRecording();
    UnityEngine.Debug.Log("Recording finished");
}
}

void StartRecording()
{
if (Microphone.devices.Length > 0)
{
    clip = Microphone.Start(null, false, 10, 44100);
}
else
{
    UnityEngine.Debug.LogError("No microphone detected");
}
recording = true;
UnityEngine.Debug.Log("Recording...");
instructionText.gameObject.SetActive(false);
dialogueText.text = "Yue Fei is thinking...";
}

void StopRecording()
{
Microphone.End(null);
recording = false;

string path = AI_FOLDER + "question.wav";
SaveWav(path, clip);

UnityEngine.Debug.Log("Saved to: " + path);

RunSpeechToText();
}

    //void RunSpeechToText()
    //{
    //string python = "python";
    //string script = AI_FOLDER + "speech_to_text.py";
    //string audio = AI_FOLDER + "question.wav";

    //ProcessStartInfo start = new ProcessStartInfo();
    //start.FileName = python;
    //start.Arguments = $"-u \"{script}\" \"{audio}\"";
    //start.UseShellExecute = false;
    //start.RedirectStandardOutput = true;
    //start.RedirectStandardError = true;
    //start.CreateNoWindow = true;

    //Process process = Process.Start(start);

    //process.WaitForExit(10000);

    //string output = process.StandardOutput.ReadToEnd();
    //string error = process.StandardError.ReadToEnd();

    //string combined = output + "\n" + error;

    //UnityEngine.Debug.Log("PYTHON LOG:\n" + combined);

    // string userText = "hello test";  // 🔥 TEMP FIX

    //        foreach (string line in combined.Split('\n'))
    //{
    //    if (line.StartsWith("TRANSCRIPTION:"))
    //    {
    //        userText = line.Replace("TRANSCRIPTION:", "").Trim();
    //        break;
    //    }
    //}
    //UnityEngine.Debug.Log("USER TEXT = [" + userText + "]");//Test 2
    //UnityEngine.Debug.Log("User said: " + userText);


    //UnityEngine.Debug.Log("USER TEXT = [" + userText + "]");

    //// optional AI test

    //dialogueText.text = "You: " + userText + "\nYue Fei is thinking...";


    //        if (!string.IsNullOrEmpty(userText))
    //{
    //    StartCoroutine(SendToOllama(userText));
    //}
    //}
    void RunSpeechToText()
    {
        string userText = "who are you"; // Eventually link this back to your Python output
        UnityEngine.Debug.Log("FORCE USER TEXT: " + userText);

        // Initial UI update
        dialogueText.text = "You: " + userText + "\n<color=#AAAAAA>Yue Fei is thinking...</color>";

        StartCoroutine(SendToOllama(userText));
    }

    IEnumerator SendToOllama(string userText)
    {
        string url = "http://localhost:11434/api/generate";

        // Create safe JSON (handles quotes in user text)
        string safeUserText = userText.Replace("\"", "\\\"");
        string json = "{\"model\":\"qwen2.5:1.5b\",\"prompt\":\"You are Yue Fei, a strict ancient general. Speak briefly and clearly like a commander. Do NOT ask questions. Question: " + safeUserText + "\",\"stream\":false}";
        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        // 1. IF IT FAILS: Show error and stop
        if (request.result != UnityWebRequest.Result.Success)
        {
            UnityEngine.Debug.LogError("Ollama Connection Error: " + request.error);
            dialogueText.text = "You: " + userText + "\n<color=red>[Connection Error]</color>";
            yield break; // Exit the coroutine here
        }

        // 2. IF IT SUCCEEDS: Parse and Speak (Place this OUTSIDE the curly braces above)
        string responseJson = request.downloadHandler.text;
        OllamaResponse res = JsonUtility.FromJson<OllamaResponse>(responseJson);
        string aiText = res.response; // Now aiText is defined!

        dialogueText.text = "You: " + userText + "\nYue Fei: " + aiText;


        // Trigger the voice scripts
        GenerateVoice(aiText);
        StartCoroutine(PlayVoice());

        
    }

void GenerateVoice(string text)
{
    string filePath = AI_FOLDER + "yuefei_voice.mp3";

    // 🔥 DELETE OLD FILE (VERY IMPORTANT)
    if (File.Exists(filePath))
    {
        File.Delete(filePath);
    }

    ProcessStartInfo start = new ProcessStartInfo();

    start.FileName = "python";

    text = text.Replace("\"", "");
    text = text.Replace("\n", " ");

    start.Arguments = $"\"{AI_FOLDER}tts.py\" \"{text}\"";

    start.UseShellExecute = false;
    start.CreateNoWindow = true;

    Process.Start(start);
}

IEnumerator PlayVoice()
{
string filePath = AI_FOLDER + "yuefei_voice.mp3";

while (!File.Exists(filePath))
{
    yield return new WaitForSeconds(0.5f);
}

        string path = "file:///" + filePath;

using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(path, AudioType.MPEG))
{
    yield return www.SendWebRequest();

    if (www.result != UnityWebRequest.Result.Success)
    {
        UnityEngine.Debug.LogError(www.error);
    }
    else
    {
        AudioClip clip = DownloadHandlerAudioClip.GetContent(www);

        AudioSource audio = GetComponent<AudioSource>();
        audio.clip = clip;
        audio.Play();
    }
}
}

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