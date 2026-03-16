using UnityEngine;

public class VoiceRecorderPuteri : MonoBehaviour
{
    private AudioClip clip;
    private bool recording = false;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (!recording)
            {
                StartRecording();
            }
            else
            {
                StopRecording();
            }
        }
    }

    void StartRecording()
    {
        clip = Microphone.Start(null, false, 10, 44100);
        recording = true;

        Debug.Log("Recording started...");
    }

    void StopRecording()
    {
        Microphone.End(null);
        recording = false;

        Debug.Log("Recording stopped.");

        VoicePipelinePuteri pipeline = Object.FindFirstObjectByType<VoicePipelinePuteri>();

        if (pipeline != null)
        {
            pipeline.ProcessAudio(clip);
        }
        else
        {
            Debug.LogError("VoicePipelinePuteri not found in scene!");
        }
    }
}