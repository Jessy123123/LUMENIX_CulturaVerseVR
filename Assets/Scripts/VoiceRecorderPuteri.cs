using UnityEngine;

public class VoiceRecorderPuteri : MonoBehaviour
{
    private AudioClip clip;
    private bool recording = false;

    public VoicePipelinePuteri pipeline;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            StartRecording();
        }

        if (Input.GetKeyUp(KeyCode.Space))
        {
            StopRecording();
        }
    }

    void StartRecording()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone found");
            return;
        }

        clip = Microphone.Start(null, false, 5, 16000);
        recording = true;

        Debug.Log("Recording...");
    }

    void StopRecording()
    {
        if (!recording) return;

        Microphone.End(null);
        recording = false;

        Debug.Log("Recording stopped");

        if (pipeline != null)
        {
            pipeline.ProcessAudio(clip);
        }
    }
}