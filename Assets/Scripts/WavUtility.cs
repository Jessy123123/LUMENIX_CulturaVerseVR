using System;
using System.IO;
using UnityEngine;

public static class WavUtility
{
    const int HEADER_SIZE = 44;

    public static byte[] FromAudioClip(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        byte[] bytes = Convert(samples);

        MemoryStream stream = new MemoryStream();

        WriteHeader(stream, clip);

        stream.Write(bytes, 0, bytes.Length);

        return stream.ToArray();
    }

    static byte[] Convert(float[] samples)
    {
        short[] intData = new short[samples.Length];
        byte[] bytesData = new byte[samples.Length * 2];

        const float rescaleFactor = 32767;

        for (int i = 0; i < samples.Length; i++)
        {
            intData[i] = (short)(samples[i] * rescaleFactor);
            byte[] byteArr = BitConverter.GetBytes(intData[i]);
            byteArr.CopyTo(bytesData, i * 2);
        }

        return bytesData;
    }

    static void WriteHeader(Stream stream, AudioClip clip)
    {
        int hz = clip.frequency;
        int channels = clip.channels;
        int samples = clip.samples;

        stream.Seek(0, SeekOrigin.Begin);

        byte[] riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
        stream.Write(riff, 0, 4);

        byte[] chunkSize = BitConverter.GetBytes(samples * channels * 2 + 36);
        stream.Write(chunkSize, 0, 4);

        byte[] wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
        stream.Write(wave, 0, 4);

        byte[] fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
        stream.Write(fmt, 0, 4);

        byte[] subChunk1 = BitConverter.GetBytes(16);
        stream.Write(subChunk1, 0, 4);

        ushort audioFormat = 1;
        byte[] format = BitConverter.GetBytes(audioFormat);
        stream.Write(format, 0, 2);

        byte[] numChannels = BitConverter.GetBytes((ushort)channels);
        stream.Write(numChannels, 0, 2);

        byte[] sampleRate = BitConverter.GetBytes(hz);
        stream.Write(sampleRate, 0, 4);

        byte[] byteRate = BitConverter.GetBytes(hz * channels * 2);
        stream.Write(byteRate, 0, 4);

        ushort blockAlign = (ushort)(channels * 2);
        stream.Write(BitConverter.GetBytes(blockAlign), 0, 2);

        ushort bitsPerSample = 16;
        stream.Write(BitConverter.GetBytes(bitsPerSample), 0, 2);

        byte[] dataString = System.Text.Encoding.UTF8.GetBytes("data");
        stream.Write(dataString, 0, 4);

        byte[] dataSize = BitConverter.GetBytes(samples * channels * 2);
        stream.Write(dataSize, 0, 4);
    }
}