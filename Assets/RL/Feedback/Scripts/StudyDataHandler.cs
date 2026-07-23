using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class StudyDataHandler : MonoBehaviour
{
    [SerializeField] private GameObject conditionACompleted;
    [SerializeField] private GameObject conditionBCompleted;
    [SerializeField] private GameObject conditionCCompleted;
    [SerializeField] private GameObject conditionDCompleted;

    [SerializeField] private String resultsPath = "RL/Feedback/Results";
    private string basePath;
    
    private ConditionData currentConditionData;
    
    [HideInInspector] public bool hasFileError = false;
    [HideInInspector] public string fileErrors = "Data was not (fully) saved - check output folder...\n";

    private void Awake()
    {
        basePath = Path.Combine(Application.dataPath, resultsPath);
        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }
    }
    
    public string GetNextParticipantID()
    {
        int highestID = 0;
        string[] directories = Directory.GetDirectories(basePath, "PID_*");

        foreach (string dir in directories)
        {
            string folderName = new DirectoryInfo(dir).Name;
            string idString = folderName.Replace("PID_", "");
            
            if (int.TryParse(idString, out int id))
            {
                if (id > highestID) highestID = id;
            }
        }

        return (highestID + 1).ToString();
    }
    
    public void UpdateCompletedConditionsUI(string participantID)
    {
        string pPath = Path.Combine(basePath, "Participant_" + participantID);
        
        conditionACompleted.SetActive(File.Exists(Path.Combine(pPath, "Condition_A.json")));
        conditionBCompleted.SetActive(File.Exists(Path.Combine(pPath, "Condition_B.json")));
        conditionCCompleted.SetActive(File.Exists(Path.Combine(pPath, "Condition_C.json")));
        conditionDCompleted.SetActive(File.Exists(Path.Combine(pPath, "Condition_D.json")));
    }
    
    public void StartCondition(string participantID, string condition)
    {
        hasFileError = false;
        fileErrors = "Data was not (fully) saved - check output folder...\n";
        
        currentConditionData = new ConditionData
        {
            participantID = participantID,
            condition = condition,
            rounds = new List<RoundData>()
        };

        try
        {
            string pPath = Path.Combine(basePath, "Participant_" + participantID);
            if (!Directory.Exists(pPath)) Directory.CreateDirectory(pPath);
        }
        catch (Exception ex)
        {
            Debug.LogError("Directory Error: " + ex.Message);
            hasFileError = true;
            fileErrors += "Could not create Participant Folder: " + ex.Message + "\n";
        }
    }
    
    public void LogRoundData(int roundNr, string agentName, int[] agentParams, StudyController.AgentFeedback feedback, string transcript = "", List<string> audioFiles = null)
    {
        if (currentConditionData == null) return;

        RoundData round = new RoundData
        {
            roundNumber = roundNr,
            
            selectedAgent = agentName,
            speedValue = agentParams[0],
            dtcValue = agentParams[1],
            accValue = agentParams[2],
            smoothValue = agentParams[3],

            likenessScore = feedback.likenessScore,
            llmLikenessScore = feedback.llmLikenessScore,
            responseTime = feedback.responseTime,
            speedAdjustment = (int)feedback.speedAdjustment,
            dtcAdjustment = (int)feedback.dtcAdjustment,
            accAdjustment = (int)feedback.accelAdjustment,
            smoothAdjustment = (int)feedback.smoothAdjustment,
            
            transcription = transcript,
            audioFilePaths = audioFiles ?? new List<string>()
        };

        currentConditionData.rounds.Add(round);
        SaveCurrentConditionToDisk();
    }
    
    public string SaveAudioAttempt(string participantID, string condition, int roundNum, int attemptNum, AudioClip clip)
    {
        string pPath = Path.Combine(basePath, "Participant_" + participantID);
        string audioDir = Path.Combine(pPath, "Audio");
        try
        {
            if (!Directory.Exists(audioDir)) Directory.CreateDirectory(audioDir);
        }
        catch (Exception ex)
        {
            hasFileError = true;
            fileErrors += "Could not create Audio folder: " + ex.Message + "\n";
            return "";
        }

        string fileName = $"Cond{condition}_R{roundNum}_Attempt{attemptNum}.wav";
        string fullPath = Path.Combine(audioDir, fileName);
        
        bool success = StudyAudioFileHandler.Save(fullPath, clip);
        if (!success)
        {
            hasFileError = true;
            fileErrors += "Failed to save Audio File: " + fileName + "\n";
        }
        return fileName;
    }

    private void SaveCurrentConditionToDisk()
    {
        try
        {
            string pPath = Path.Combine(basePath, "Participant_" + currentConditionData.participantID);
            string filePath = Path.Combine(pPath, $"Condition_{currentConditionData.condition}.json");

            string json = JsonUtility.ToJson(currentConditionData, true);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogError("JSON Save Error: " + ex.Message);
            hasFileError = true;
            fileErrors += "Failed to save JSON data: " + ex.Message + "\n";
        }
    }
}

[System.Serializable]
public class ConditionData
{
    public string participantID;
    public string condition;
    public List<RoundData> rounds;
}

[System.Serializable]
public class RoundData
{
    public int roundNumber;
    
    [Header("Agent Settings")]
    public string selectedAgent;
    public int speedValue;
    public int dtcValue;
    public int accValue;
    public int smoothValue;

    [Header("User Feedback")]
    public float likenessScore;
    public float llmLikenessScore;
    public float responseTime;
    public int speedAdjustment;
    public int dtcAdjustment;
    public int accAdjustment;
    public int smoothAdjustment;

    [Header("Speech Data")]
    public string transcription;
    public List<string> audioFilePaths;
}