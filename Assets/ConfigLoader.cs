using System.IO;
using UnityEngine;

[System.Serializable]
public class ConfigData
{
    public string google_api_key;
    public string claude_api_key;
}

public class ConfigLoader : MonoBehaviour
{
    public static ConfigData config;

    void Awake()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "config.json");

        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            config = JsonUtility.FromJson<ConfigData>(json);

            Debug.Log("Config loaded successfully");
        }
        else
        {
            Debug.LogError("config.json not found!");
        }
    }
}