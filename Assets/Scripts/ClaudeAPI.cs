using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;

public class ClaudeAPI : MonoBehaviour
{
    string apiKey;

    void Start()
    {
        apiKey = ConfigLoader.config.claude_api_key;
    }

    public IEnumerator AskClaude(string userText)
    {
        string url = "https://api.anthropic.com/v1/messages";

        string json = "{"
        + "\"model\":\"claude-3-haiku-20240307\","
        + "\"max_tokens\":200,"
        + "\"messages\":[{\"role\":\"user\",\"content\":\"" + userText + "\"}]"
        + "}";

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("x-api-key", apiKey);
        request.SetRequestHeader("anthropic-version", "2023-06-01");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Claude Response: " + request.downloadHandler.text);
        }
        else
        {
            Debug.LogError("Claude Error: " + request.error);
        }
    }
}