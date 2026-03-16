using UnityEngine;

public class VoiceRecorderPuteri : MonoBehaviour
{
    [Header("Settings")]
    public int maxRecordingSeconds = 10;   // Increased from 5 — short sentences may be fine but questions can run longer
    public int sampleRate = 16000;         // Must match what you send to Google STT

    [Header("Pipeline Reference")]
    public VoicePipelinePuteri pipeline;

    private AudioClip clip;
    private bool recording = false;
    private float recordingStartTime;

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

        // Safety: auto-stop if user holds Space beyond the max duration
        if (recording && Time.time - recordingStartTime >= maxRecordingSeconds)
        {
            Debug.LogWarning("Max recording duration reached — stopping automatically.");
            StopRecording();
        }
    }

    void StartRecording()
    {
        if (recording)
        {
            Debug.LogWarning("Already recording — ignoring.");
            return;
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone found.");
            return;
        }

        if (pipeline == null)
        {
            Debug.LogError("VoiceRecorderPuteri: Pipeline is not assigned! Drag VoicePipelinePuteri onto the Pipeline field in the Inspector.");
            return;
        }

        clip = Microphone.Start(null, false, maxRecordingSeconds, sampleRate);
        recording = true;
        recordingStartTime = Time.time;

        Debug.Log("Recording started...");
    }

    void StopRecording()
    {
        if (!recording) return;

        // Trim the clip to actual recorded length so we don't send silence to STT
        int samplesRecorded = Microphone.GetPosition(null);
        Microphone.End(null);
        recording = false;

        Debug.Log($"Recording stopped — {samplesRecorded} samples captured.");

        if (samplesRecorded <= 0)
        {
            Debug.LogWarning("No audio was captured.");
            return;
        }

        // Trim silence: create a new clip with only the recorded portion
        float[] data = new float[samplesRecorded * clip.channels];
        clip.GetData(data, 0);

        AudioClip trimmed = AudioClip.Create("recorded", samplesRecorded, clip.channels, sampleRate, false);
        trimmed.SetData(data, 0);

        pipeline.ProcessAudio(trimmed);
    }
}