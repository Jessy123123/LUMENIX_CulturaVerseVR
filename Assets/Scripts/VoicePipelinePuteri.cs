using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;

public class VoicePipelinePuteri : MonoBehaviour
{
    public string googleApiKey;
    public string claudeApiKey;

    public AudioSource characterVoice;
    public Animator characterAnimator;

    public void ProcessAudio(AudioClip clip)
    {
        StartCoroutine(SendToSTT(clip));
    }

    byte[] AudioClipToWav(AudioClip clip)
    {
        float[] samples = new float[clip.samples];
        clip.GetData(samples, 0);

        byte[] bytes = new byte[samples.Length * 2];
        int offset = 0;

        foreach (var sample in samples)
        {
            short value = (short)(sample * short.MaxValue);
            System.BitConverter.GetBytes(value).CopyTo(bytes, offset);
            offset += 2;
        }

        return bytes;
    }

    IEnumerator SendToSTT(AudioClip clip)
    {
        byte[] audioData = AudioClipToWav(clip);

        string audioBase64 = System.Convert.ToBase64String(audioData);

        string json = "{ \"config\": { \"encoding\": \"LINEAR16\", \"sampleRateHertz\":16000, \"languageCode\": \"ms-MY\" }, \"audio\": { \"content\": \"" + audioBase64 + "\" } }";

        string url = "https://speech.googleapis.com/v1/speech:recognize?key=" + googleApiKey;

        UnityWebRequest request = new UnityWebRequest(url, "POST");

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("STT Error: " + request.error);
            yield break;
        }

        string response = request.downloadHandler.text;

        Debug.Log("STT Response: " + response);

        string transcript = ExtractTranscript(response);

        StartCoroutine(AskClaude(transcript));
    }

    string ExtractTranscript(string json)
    {
        int index = json.IndexOf("\"transcript\":");

        if (index == -1)
        {
            Debug.LogError("Transcript not found");
            return "";
        }

        int start = index + 14;
        int end = json.IndexOf("\"", start);

        return json.Substring(start, end - start);
    }

    IEnumerator AskClaude(string question)
    {
        Debug.Log("User asked: " + question);

        string prompt =
        "You are Puteri Gunung Ledang, a Malay legendary princess. " +
        "Answer politely and briefly: " + question;

        string json =
        "{ \"model\":\"claude-3-haiku-20240307\", " +
        "\"max_tokens\":100, " +
        "\"messages\":[{\"role\":\"user\",\"content\":\"" + prompt + "\"}] }";

        UnityWebRequest request = new UnityWebRequest("https://api.anthropic.com/v1/messages", "POST");

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("x-api-key", claudeApiKey);
        request.SetRequestHeader("anthropic-version", "2023-06-01");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Claude Error: " + request.error);
            yield break;
        }

        string response = request.downloadHandler.text;

        Debug.Log("Claude Response: " + response);

        string answer = ExtractClaudeReply(response);

        StartCoroutine(SendToTTS(answer));
    }

    string ExtractClaudeReply(string json)
    {
        int index = json.IndexOf("\"text\":\"");

        if (index == -1)
        {
            Debug.LogError("Claude text not found");
            return "";
        }

        int start = index + 8;
        int end = json.IndexOf("\"", start);

        return json.Substring(start, end - start);
    }

    IEnumerator SendToTTS(string text)
    {
        string url = "https://texttospeech.googleapis.com/v1/text:synthesize?key=" + googleApiKey;

        string json =
        "{ \"input\":{\"text\":\"" + text + "\"}," +
        "\"voice\":{\"languageCode\":\"ms-MY\"}," +
        "\"audioConfig\":{\"audioEncoding\":\"MP3\"}}";

        UnityWebRequest request = new UnityWebRequest(url, "POST");

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("TTS Error: " + request.error);
            yield break;
        }

        string response = request.downloadHandler.text;

        int start = response.IndexOf("audioContent") + 15;
        int end = response.IndexOf("\"", start);

        string base64 = response.Substring(start, end - start);

        byte[] audioBytes = System.Convert.FromBase64String(base64);

        StartCoroutine(PlayAudio(audioBytes));
    }

    IEnumerator PlayAudio(byte[] mp3)
    {
        string path = Application.temporaryCachePath + "/puteri_voice.mp3";

        System.IO.File.WriteAllBytes(path, mp3);

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            AudioClip clip = DownloadHandlerAudioClip.GetContent(www);

            characterVoice.clip = clip;

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", true);

            characterVoice.Play();

            yield return new WaitForSeconds(clip.length);

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", false);
        }
    }
}