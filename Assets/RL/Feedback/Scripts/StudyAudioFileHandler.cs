using System.IO;
using UnityEngine;

public class StudyAudioFileHandler : MonoBehaviour
{
public static bool Save(string filename, AudioClip clip)
    {
        if (clip == null) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filename));
            using (FileStream fileStream = CreateEmpty(filename))
            {
                ConvertAndWrite(fileStream, clip);
                WriteHeader(fileStream, clip);
            }
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Error saving WAV-Audio: " + ex.Message);
            return false;
        }
    }

    private static FileStream CreateEmpty(string filepath)
    {
        FileStream fileStream = new FileStream(filepath, FileMode.Create);
        byte[] emptyByte = new byte[44]; 
        fileStream.Write(emptyByte, 0, 44);
        return fileStream;
    }

    private static void ConvertAndWrite(FileStream fileStream, AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        System.Int16[] intData = new System.Int16[samples.Length];
        byte[] bytesData = new byte[samples.Length * 2];
        int rescaleFactor = 32767;

        for (int i = 0; i < samples.Length; i++)
        {
            intData[i] = (short)(samples[i] * rescaleFactor);
            byte[] byteArr = new byte[2];
            byteArr = System.BitConverter.GetBytes(intData[i]);
            byteArr.CopyTo(bytesData, i * 2);
        }
        fileStream.Write(bytesData, 0, bytesData.Length);
    }

    private static void WriteHeader(FileStream fileStream, AudioClip clip)
    {
        int hz = clip.frequency;
        int channels = clip.channels;
        int samples = clip.samples;

        fileStream.Seek(0, SeekOrigin.Begin);

        byte[] riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
        fileStream.Write(riff, 0, 4);

        byte[] chunkSize = System.BitConverter.GetBytes(fileStream.Length - 8);
        fileStream.Write(chunkSize, 0, 4);

        byte[] wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
        fileStream.Write(wave, 0, 4);

        byte[] fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
        fileStream.Write(fmt, 0, 4);

        byte[] subChunk1 = System.BitConverter.GetBytes(16);
        fileStream.Write(subChunk1, 0, 4);

        byte[] audioFormat = System.BitConverter.GetBytes((ushort)1);
        fileStream.Write(audioFormat, 0, 2);

        byte[] numChannels = System.BitConverter.GetBytes((ushort)channels);
        fileStream.Write(numChannels, 0, 2);

        byte[] sampleRate = System.BitConverter.GetBytes(hz);
        fileStream.Write(sampleRate, 0, 4);

        byte[] byteRate = System.BitConverter.GetBytes(hz * channels * 2);
        fileStream.Write(byteRate, 0, 4);

        ushort blockAlign = (ushort)(channels * 2);
        fileStream.Write(System.BitConverter.GetBytes(blockAlign), 0, 2);

        ushort bps = 16;
        fileStream.Write(System.BitConverter.GetBytes(bps), 0, 2);

        byte[] datastring = System.Text.Encoding.UTF8.GetBytes("data");
        fileStream.Write(datastring, 0, 4);

        byte[] subChunk2 = System.BitConverter.GetBytes(samples * channels * 2);
        fileStream.Write(subChunk2, 0, 4);
    }
}
