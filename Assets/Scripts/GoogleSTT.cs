using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;

public class GoogleSTT : MonoBehaviour
{
    string apiKey;

    void Start()
    {
        apiKey = ConfigLoader.config.google_api_key;
        Debug.Log("Google API key loaded");
    }

    public IEnumerator SendAudio(byte[] wavData)
    {
        string url = "https://speech.googleapis.com/v1/speech:recognize?key=" + apiKey;

        string audioBase64 = System.Convert.ToBase64String(wavData);

        string json =
        "{ \"config\": { \"encoding\": \"LINEAR16\", \"languageCode\": \"ms-MY\" }, " +
        "\"audio\": { \"content\": \"" + audioBase64 + "\" } }";

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string response = request.downloadHandler.text;

            Debug.Log("STT Response: " + response);

            // Extract transcript
            string transcript = ExtractTranscript(response);

            if (!string.IsNullOrEmpty(transcript))
            {
                Debug.Log("User said: " + transcript);

                ClaudeAPI claude = Object.FindFirstObjectByType<ClaudeAPI>();

                if (claude != null)
                {
                    StartCoroutine(claude.AskClaude(transcript));
                }
            }
        }
        else
        {
            Debug.LogError("STT Error: " + request.error);
        }
    }

    string ExtractTranscript(string json)
    {
        if (!json.Contains("transcript"))
            return "";

        int start = json.IndexOf("transcript");
        int quote1 = json.IndexOf(":", start) + 2;
        int quote2 = json.IndexOf("\"", quote1);

        return json.Substring(quote1, quote2 - quote1);
    }
}