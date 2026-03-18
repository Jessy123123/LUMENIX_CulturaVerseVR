using UnityEngine;
using System.Collections;
using System.Text;
using UnityEngine.Networking;

public class VoicePipelinePuteri : MonoBehaviour
{
    public string googleApiKey;
    public AudioClip clip;

    public string statusMessage = "";

    public void StartSTT()
    {
        StartCoroutine(SendToSTT());
    }

    IEnumerator SendToSTT()
    {
        if (string.IsNullOrEmpty(googleApiKey))
        {
            Debug.LogError("Google API key is not loaded.");
            yield break;
        }

        statusMessage = "Converting speech to text...";
        Debug.Log("Sending to STT...");

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
            statusMessage = "STT Failed";
        }
        else
        {
            Debug.Log("STT Response: " + request.downloadHandler.text);
            statusMessage = "STT Completed";
        }
    }

    // ===============================
    // 🔊 AUDIO CONVERSION
    // ===============================

    byte[] AudioClipToWav(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        return ConvertToWav(samples, clip.channels, clip.frequency);
    }

    byte[] ConvertToWav(float[] samples, int channels, int sampleRate)
    {
        byte[] pcmData = new byte[samples.Length * 2];

        int index = 0;
        foreach (float sample in samples)
        {
            short intSample = (short)(sample * short.MaxValue);
            byte[] bytes = System.BitConverter.GetBytes(intSample);

            pcmData[index++] = bytes[0];
            pcmData[index++] = bytes[1];
        }

        return pcmData;
    }
}