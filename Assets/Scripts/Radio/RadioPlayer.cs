using System.Collections;
using UnityEngine;
using LibVLCSharp;
using System;
using TMPro;

public class RadioPlayer : MonoBehaviour
{
    private LibVLC libVLC;
    private MediaPlayer mediaPlayer;

    private float intVolume = 0.1f;
    private bool isRadioPlaying = false;

    public void Awake()
    {
        try 
        {
            Core.Initialize(Application.dataPath); 
            libVLC = new LibVLC(enableDebugLogs: true);
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        }
        catch (Exception ex)
        {
            Debug.LogError($"LibVLC Initialization Failed: {ex.Message}");
        }
    }

    void OnDisable()
    {
        StopRadio();
        
        mediaPlayer?.Dispose();
        mediaPlayer = null;

        libVLC?.Dispose();
        libVLC = null;
    }

    public void PlayRadioStation(string url)
    {
        if (libVLC == null)
        {
            Debug.LogError("LibVLC was not initialized. Check your path in Awake!");
            return;
        }

        StopRadio();
        
        if (mediaPlayer == null)
        {
            mediaPlayer = new MediaPlayer(libVLC);
        }
        
        using (var media = new Media(new Uri(url)))
        {
            mediaPlayer.Media = media;
        }
        
        mediaPlayer.Play();
        mediaPlayer.SetVolume((int)(intVolume * 100));
        isRadioPlaying = true;
    }

    public void StopRadio()
    {
        isRadioPlaying = false;
        mediaPlayer?.Stop();
    }

    public void ChangeVolume(float volume)
    {
        intVolume = Mathf.Clamp(volume, 0.0f, 0.5f);
        mediaPlayer?.SetVolume((int)(intVolume * 100));
    }

    public bool GetRadioPlayingState() { return isRadioPlaying; }
    public float GetVolume() { return intVolume; }
}