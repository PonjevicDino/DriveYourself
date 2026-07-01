using System;
using System.Collections.Generic;
using UnityEngine;

public class StudyControllerMicHandler : MonoBehaviour
{
    private StudyController studyController;

    private AudioClip currentAudioClip;
    private string activeMicDevice = null;
    private int retryCounter = 1;
    private List<string> audioPaths = new List<string>();

    public void Start()
    {
        studyController = this.GetComponent<StudyController>();
    }

    public void StartRecording()
    {
        if (Microphone.devices.Length > 0)
        {
            try 
            {
                activeMicDevice = Microphone.devices[0];
                Microphone.GetDeviceCaps(activeMicDevice, out int minFreq, out int maxFreq);
                int recordingFrequency = (minFreq == 0 && maxFreq == 0) ? 44100 : maxFreq;
                
                Debug.Log($"Starting mic: {activeMicDevice} at {recordingFrequency}Hz");
                currentAudioClip = Microphone.Start(activeMicDevice, false, 120, recordingFrequency);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Microphone.Start failed: " + ex.Message);
                activeMicDevice = null; 
            }
        }
        else
        {
            activeMicDevice = null;
            Debug.LogWarning("Cannot start recording - No microphone devices found.");
        }
    }

    public void StopRecording(string conditionLetter)
    {
        if (!string.IsNullOrEmpty(activeMicDevice) && Microphone.IsRecording(activeMicDevice))
        {
            int position = Microphone.GetPosition(activeMicDevice);
            Microphone.End(activeMicDevice);
            
            if (position > 0 && currentAudioClip != null)
            {
                AudioClip trimmedClip = AudioClip.Create("Trimmed", position, currentAudioClip.channels, currentAudioClip.frequency, false);
                float[] data = new float[position * currentAudioClip.channels];
                currentAudioClip.GetData(data, 0);
                trimmedClip.SetData(data, 0);
                
                int currentRound = (int)studyController.demoBoManager.ReturnIterations()[0];
                string savedPath = studyController.studyDataHandler.SaveAudioAttempt(
                    studyController.participantID, conditionLetter, currentRound, retryCounter, trimmedClip);
        
                audioPaths.Add(savedPath);
                retryCounter++;
            }
        }
    }

    public List<string> GetAudioPaths()
    {
        return new List<string>(audioPaths); 
    }

    public void ResetForNextRound()
    {
        audioPaths.Clear();
        retryCounter = 1;
    }
}