using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;

public class VoicePipelinePuteri : MonoBehaviour
{
    [Header("API Keys")]
    public string googleApiKey;
    public string claudeApiKey;

    [Header("Character Components")]
    public AudioSource characterVoice;
    public Animator characterAnimator;

    // Conversation history for multi-turn memory
    private List<MessageEntry> conversationHistory = new List<MessageEntry>();

    [System.Serializable]
    private class MessageEntry
    {
        public string role;
        public string content;
    }

    // ─────────────────────────────────────────────
    // Entry point called by VoiceRecorderPuteri
    // ─────────────────────────────────────────────
    public void ProcessAudio(AudioClip clip)
    {
        StartCoroutine(SendToSTT(clip));
    }

    // ─────────────────────────────────────────────
    // Convert AudioClip to raw PCM bytes (LINEAR16)
    // ─────────────────────────────────────────────
    byte[] AudioClipToPCM(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        byte[] bytes = new byte[samples.Length * 2];
        int offset = 0;

        foreach (var sample in samples)
        {
            // Clamp to avoid overflow
            float clamped = Mathf.Clamp(sample, -1f, 1f);
            short value = (short)(clamped * short.MaxValue);
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            offset += 2;
        }

        return bytes;
    }

    // ─────────────────────────────────────────────
    // STEP 1: Speech-to-Text (Google STT)
    // ─────────────────────────────────────────────
    IEnumerator SendToSTT(AudioClip clip)
    {
        byte[] audioData = AudioClipToPCM(clip);
        string audioBase64 = System.Convert.ToBase64String(audioData);

        // Build JSON safely using StringBuilder
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append("\"config\":{");
        sb.Append("\"encoding\":\"LINEAR16\",");
        sb.Append("\"sampleRateHertz\":").Append(clip.frequency).Append(",");
        sb.Append("\"languageCode\":\"ms-MY\"");
        sb.Append("},");
        sb.Append("\"audio\":{\"content\":\"").Append(audioBase64).Append("\"}");
        sb.Append("}");

        string url = "https://speech.googleapis.com/v1/speech:recognize?key=" + googleApiKey;

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(sb.ToString());

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

        if (string.IsNullOrEmpty(transcript))
        {
            Debug.LogWarning("STT: Empty transcript received.");
            yield break;
        }

        StartCoroutine(AskClaude(transcript));
    }

    // ─────────────────────────────────────────────
    // Parse transcript from Google STT JSON
    // ─────────────────────────────────────────────
    string ExtractTranscript(string json)
    {
        // Google STT response: {"results":[{"alternatives":[{"transcript":"...","confidence":...}]}]}
        const string key = "\"transcript\":\"";
        int index = json.IndexOf(key);

        if (index == -1)
        {
            Debug.LogError("Transcript key not found in STT response.");
            return "";
        }

        int start = index + key.Length;
        int end = json.IndexOf("\"", start);

        if (end == -1) return "";

        // Unescape JSON string content
        return UnescapeJsonString(json.Substring(start, end - start));
    }

    // ─────────────────────────────────────────────
    // STEP 2: Send to Claude API
    // ─────────────────────────────────────────────
    IEnumerator AskClaude(string userMessage)
    {
        Debug.Log("User asked: " + userMessage);

        // Add user message to history
        conversationHistory.Add(new MessageEntry { role = "user", content = userMessage });

        // Build messages array from history
        var messagesJson = new StringBuilder();
        messagesJson.Append("[");
        for (int i = 0; i < conversationHistory.Count; i++)
        {
            if (i > 0) messagesJson.Append(",");
            messagesJson.Append("{\"role\":\"");
            messagesJson.Append(EscapeJsonString(conversationHistory[i].role));
            messagesJson.Append("\",\"content\":\"");
            messagesJson.Append(EscapeJsonString(conversationHistory[i].content));
            messagesJson.Append("\"}");
        }
        messagesJson.Append("]");

        string systemPrompt =
            "You are Puteri Gunung Ledang, a legendary Malay princess from the folklore of Gunung Ledang. " +
            "Speak gracefully, poetically, and briefly in Malay or English depending on the user's language. " +
            "Stay in character at all times. Keep replies under 3 sentences.";

        var bodyBuilder = new StringBuilder();
        bodyBuilder.Append("{");
        bodyBuilder.Append("\"model\":\"claude-haiku-4-5-20251001\",");
        bodyBuilder.Append("\"max_tokens\":150,");
        bodyBuilder.Append("\"system\":\"").Append(EscapeJsonString(systemPrompt)).Append("\",");
        bodyBuilder.Append("\"messages\":").Append(messagesJson);
        bodyBuilder.Append("}");

        UnityWebRequest request = new UnityWebRequest("https://api.anthropic.com/v1/messages", "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(bodyBuilder.ToString());

        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("x-api-key", claudeApiKey);
        request.SetRequestHeader("anthropic-version", "2023-06-01");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Claude Error: " + request.error);
            Debug.LogError("Claude Response Body: " + request.downloadHandler.text);
            yield break;
        }

        string response = request.downloadHandler.text;
        Debug.Log("Claude Response: " + response);

        string answer = ExtractClaudeReply(response);

        if (string.IsNullOrEmpty(answer))
        {
            Debug.LogWarning("Claude: Empty answer parsed.");
            yield break;
        }

        // Add assistant reply to history for context in next turn
        conversationHistory.Add(new MessageEntry { role = "assistant", content = answer });

        StartCoroutine(SendToTTS(answer));
    }

    // ─────────────────────────────────────────────
    // Parse Claude's reply from API JSON
    // ─────────────────────────────────────────────
    string ExtractClaudeReply(string json)
    {
        // Claude response: {"content":[{"type":"text","text":"..."}], ...}
        const string key = "\"text\":\"";
        int index = json.IndexOf(key);

        if (index == -1)
        {
            Debug.LogError("Claude: 'text' key not found. Full response: " + json);
            return "";
        }

        int start = index + key.Length;

        // Find the closing quote, but skip escaped quotes \"
        int end = start;
        while (end < json.Length)
        {
            if (json[end] == '"' && json[end - 1] != '\\')
                break;
            end++;
        }

        return UnescapeJsonString(json.Substring(start, end - start));
    }

    // ─────────────────────────────────────────────
    // STEP 3: Text-to-Speech (Google TTS)
    // ─────────────────────────────────────────────
    IEnumerator SendToTTS(string text)
    {
        string url = "https://texttospeech.googleapis.com/v1/text:synthesize?key=" + googleApiKey;

        var bodyBuilder = new StringBuilder();
        bodyBuilder.Append("{");
        bodyBuilder.Append("\"input\":{\"text\":\"").Append(EscapeJsonString(text)).Append("\"},");
        bodyBuilder.Append("\"voice\":{");
        bodyBuilder.Append("\"languageCode\":\"ms-MY\",");
        bodyBuilder.Append("\"ssmlGender\":\"FEMALE\"");   // Feminine voice for Puteri
        bodyBuilder.Append("},");
        bodyBuilder.Append("\"audioConfig\":{\"audioEncoding\":\"MP3\"}");
        bodyBuilder.Append("}");

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(bodyBuilder.ToString());

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

        const string audioKey = "\"audioContent\":\"";
        int start = response.IndexOf(audioKey);

        if (start == -1)
        {
            Debug.LogError("TTS: audioContent key not found.");
            yield break;
        }

        start += audioKey.Length;
        int end = response.IndexOf("\"", start);

        if (end == -1)
        {
            Debug.LogError("TTS: Could not find end of audioContent.");
            yield break;
        }

        string base64 = response.Substring(start, end - start);

        byte[] audioBytes;
        try
        {
            audioBytes = System.Convert.FromBase64String(base64);
        }
        catch (System.Exception e)
        {
            Debug.LogError("TTS: Base64 decode failed: " + e.Message);
            yield break;
        }

        StartCoroutine(PlayAudio(audioBytes));
    }

    // ─────────────────────────────────────────────
    // STEP 4: Play audio and trigger animation
    // ─────────────────────────────────────────────
    IEnumerator PlayAudio(byte[] mp3)
    {
        string path = Application.temporaryCachePath + "/puteri_voice.mp3";

        System.IO.File.WriteAllBytes(path, mp3);

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("PlayAudio Error: " + www.error);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(www);

            if (clip == null)
            {
                Debug.LogError("PlayAudio: Failed to get AudioClip from TTS response.");
                yield break;
            }

            characterVoice.clip = clip;

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", true);

            characterVoice.Play();

            yield return new WaitForSeconds(clip.length);

            if (characterAnimator != null)
                characterAnimator.SetBool("IsTalking", false);
        }
    }

    // ─────────────────────────────────────────────
    // Utility: Escape a string for safe JSON embedding
    // ─────────────────────────────────────────────
    string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    // ─────────────────────────────────────────────
    // Utility: Unescape a JSON-encoded string value
    // ─────────────────────────────────────────────
    string UnescapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        return s
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t")
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\");
    }

    // ─────────────────────────────────────────────
    // Optional: Clear conversation history
    // ─────────────────────────────────────────────
    public void ClearHistory()
    {
        conversationHistory.Clear();
        Debug.Log("Conversation history cleared.");
    }
}