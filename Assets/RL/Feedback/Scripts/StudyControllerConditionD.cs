using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Windows.Speech;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class StudyControllerConditionD : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private StudyController studyController;

    [Header("Pages")]
    [SerializeField] private GameObject page0;
    [SerializeField] private TextMeshProUGUI page0Title;
    [SerializeField] private GameObject page1;
    [SerializeField] private TextMeshProUGUI page1Title;

    [Header("Speech & LLM UI")]
    [SerializeField] private TextMeshProUGUI liveTranscriptionText;
    [SerializeField] private GameObject inputStep;
    [SerializeField] private GameObject processingStep;
    [SerializeField] private GameObject doneStep;
    [SerializeField] private GameObject errorText;
    [SerializeField] private StudyControllerMicHandler micHandler;
    
    private DictationRecognizer dictationRecognizer;
    private string finalTranscription = "";
    private string currentHypothesis = "";
    
    private bool isRecording = false;
    private bool isProcessing = false;
    
    private float silenceTimer = 0f;
    private const float pauseThreshold = 1.5f;

    private void Start()
    {
        dictationRecognizer = new DictationRecognizer();
        
        dictationRecognizer.AutoSilenceTimeoutSeconds = 120f;
        dictationRecognizer.InitialSilenceTimeoutSeconds = 120f;

        dictationRecognizer.DictationHypothesis += (text) =>
        {
            silenceTimer = 0f;
            currentHypothesis = text;
            liveTranscriptionText.text = finalTranscription + " " + currentHypothesis + "...";
        };

        dictationRecognizer.DictationResult += (text, confidence) =>
        {
            if (!string.IsNullOrEmpty(currentHypothesis))
            {
                finalTranscription += text.Trim() + ". ";
                currentHypothesis = "";
                silenceTimer = 0f;
                liveTranscriptionText.text = finalTranscription;
            }
        };

        dictationRecognizer.DictationError += (error, hresult) =>
        {
            Debug.LogError("Dictation error: " + error);
            TriggerErrorState();
        };
    }

    public void OnEnable()
    {
        page0Title.text = "DriveYourself - ID " + studyController.participantID + " - Condition D";
        if (studyController.demoBoManager != null)
        {
            page1Title.text = "Feedback for previous Driving Style: " + studyController.demoBoManager.ReturnIterations()[0] + "/" +
                              studyController.demoBoManager.ReturnIterations()[1];
        }
        
        ResetSteps();
        liveTranscriptionText.text = "Hold [ENTER] to speak...";
        isProcessing = false;
        isRecording = false;
    }

    public void Update()
    {
        if (page0.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                page0.SetActive(false);
                page1.SetActive(true);
                studyController.StartFirstRound();
            }
        }
        else
        {
            if (!isProcessing)
            {
                if (Input.GetKeyDown(KeyCode.Return))
                {
                    StartRecording();
                }
                if (Input.GetKey(KeyCode.Return) && Input.GetKeyDown(KeyCode.Space))
                {
                    TriggerDebugMock();
                }
                else if (Input.GetKeyUp(KeyCode.Return))
                {
                    StopRecordingAndProcess();
                }

                if (isRecording && !string.IsNullOrEmpty(currentHypothesis))
                {
                    silenceTimer += Time.deltaTime;
                    if (silenceTimer > pauseThreshold)
                    {
                        finalTranscription += currentHypothesis.Trim() + ". ";
                        currentHypothesis = "";
                        silenceTimer = 0f;
                        liveTranscriptionText.text = finalTranscription;
                    }
                }
            }
        }
    }

    private void StartRecording()
    {
        isRecording = true;
        finalTranscription = "";
        currentHypothesis = "";
        silenceTimer = 0f;
        liveTranscriptionText.text = "Listening...";
        
        inputStep.SetActive(true);
        processingStep.SetActive(false);
        doneStep.SetActive(false);
        errorText.SetActive(false);

        micHandler.StartRecording();
        
        try
        {
            if (dictationRecognizer.Status != SpeechSystemStatus.Running)
            {
                dictationRecognizer.Start();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Windows Speech Error: " + ex.Message);
            TriggerErrorState();
            liveTranscriptionText.text = "Windows Speech disabled.\nPlease enable in OS Settings.";
        }
    }

    private void StopRecordingAndProcess()
    {
        if (!isRecording)
        {
            return;
        }
        
        isRecording = false;
        isProcessing = true;

        if (dictationRecognizer.Status == SpeechSystemStatus.Running)
        {
            dictationRecognizer.Stop();
        }
        
        micHandler.StopRecording("D");

        processingStep.SetActive(true); 
        string textToSend = (finalTranscription + " " + currentHypothesis).Trim();

        if (string.IsNullOrWhiteSpace(textToSend))
        {
            TriggerErrorState();
            return;
        }

        StartCoroutine(SendToGemini(textToSend));
    }
    
    private void TriggerDebugMock()
    {
        if (dictationRecognizer.Status == SpeechSystemStatus.Running)
        {
            dictationRecognizer.Stop();
        }

        isRecording = false;
        isProcessing = true;

        processingStep.SetActive(true);
        inputStep.SetActive(false);
        errorText.SetActive(false);
        
        string[] opts = { "much less", "slightly less", "keep", "slightly more", "much more" };
        string s = opts[Random.Range(0, opts.Length)];
        string d = opts[Random.Range(0, opts.Length)];
        string a = opts[Random.Range(0, opts.Length)];
        string sm = opts[Random.Range(0, opts.Length)];
        
        float mockRating = Random.Range(0f, 1f);

        string mockText = $"[DEBUG] I rate this driving style a {mockRating:F2} out of 1. Set speed to {s}, distance to center to {d}, acceleration to {a}, and smoothness to {sm}.";
        
        liveTranscriptionText.text = "Sending Debug Mock:\n" + mockText;
        StartCoroutine(SendToGemini(mockText));
    }

    private IEnumerator SendToGemini(string userText)
    {
        string systemPrompt = "You are an intent parser for an autonomous driving simulator. Extract the user's desired changes for 4 parameters: 'speed', 'distance to the center', 'acceleration', and 'smoothness'. Map their intent strictly to one of these exact values: 'much less', 'slightly less', 'keep', 'slightly more', 'much more'. If a parameter isn't mentioned, default to 'keep'. Additionally, deduce a 'likenessScore' as a float between 0.0 and 1.0 indicating how much the user liked the previous driving style based on their sentiment (e.g., highly critical/frustrated = 0.0-0.3, neutral/mixed = 0.4-0.6, highly positive = 0.7-1.0). If unsure, default to 0.5. If the text is completely unrelated or unintelligible, set 'isValid' to false. Reply ONLY with a JSON object matching this structure: {\"isValid\": true, \"likenessScore\": 0.5, \"speed\": \"keep\", \"dtc\": \"keep\", \"acceleration\": \"keep\", \"smoothness\": \"keep\"}";

        string combinedPrompt = $"{systemPrompt}\n\nUser Input: {userText}";
        combinedPrompt = combinedPrompt.Replace("\"", "\\\"");

        string requestData = $@"{{
            ""contents"": [
                {{
                    ""parts"": [
                        {{ ""text"": ""{combinedPrompt}"" }}
                    ]
                }}
            ],
            ""generationConfig"": {{
                ""temperature"": 0.0,
                ""responseMimeType"": ""application/json""
            }}
        }}";
        
        string requestUrl = studyController.geminiApiUrl + PlayerPrefs.GetString("GeminiAPIKey", "");;
        
        UnityWebRequest request = new UnityWebRequest(requestUrl, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(requestData);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError(request.error);
            TriggerErrorState();
        }
        else
        {
            ParseGeminiResponse(request.downloadHandler.text);
        }
    }

    private void ParseGeminiResponse(string jsonResponse)
    {
        try
        {
            GeminiResponse responseObj = JsonUtility.FromJson<GeminiResponse>(jsonResponse);
            string contentJson = responseObj.candidates[0].content.parts[0].text;
            
            MappedFeedback mappedData = JsonUtility.FromJson<MappedFeedback>(contentJson);

            if (!mappedData.isValid)
            {
                TriggerErrorState();
            }
            else
            {
                doneStep.SetActive(true);
                StartCoroutine(WaitAndSubmit(mappedData));
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to parse JSON: " + e.Message);
            TriggerErrorState();
        }
    }
    
    private StudyController.ParameterAdjustment StringToEnum(string val)
    {
        if (string.IsNullOrEmpty(val)) return StudyController.ParameterAdjustment.Keep;

        val = val.ToLower().Trim();
        switch (val)
        {
            case "much less": return StudyController.ParameterAdjustment.MuchLess;
            case "slightly less": return StudyController.ParameterAdjustment.SlightlyLess;
            case "slightly more": return StudyController.ParameterAdjustment.SlightlyMore;
            case "much more": return StudyController.ParameterAdjustment.MuchMore;
            default: return StudyController.ParameterAdjustment.Keep;
        }
    }
    
    private void TriggerErrorState()
    {
        ResetSteps();
        errorText.SetActive(true);
        isProcessing = false;
        isRecording = false;
    }

    private IEnumerator WaitAndSubmit(MappedFeedback mappedData)
    {
        yield return new WaitForSeconds(1.0f);

        StudyController.AgentFeedback feedback = new StudyController.AgentFeedback
        {
            likenessScore = mappedData.likenessScore,
            speedAdjustment = StringToEnum(mappedData.speed),
            dtcAdjustment = StringToEnum(mappedData.dtc),
            accelAdjustment = StringToEnum(mappedData.acceleration),
            smoothAdjustment = StringToEnum(mappedData.smoothness)
        };

        studyController.SubmitFeedback(feedback, finalTranscription, micHandler.GetAudioPaths());

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        ResetSteps();
        
        liveTranscriptionText.text = "\\\\\\\n\\\\\\\nYour input will be visible here\n\\\\\\\n\\\\\\";
        isProcessing = false;
        
        micHandler.ResetForNextRound();
        finalTranscription = "";
        currentHypothesis = "";
    }

    private void ResetSteps()
    {
        inputStep.SetActive(false);
        processingStep.SetActive(false);
        doneStep.SetActive(false);
        errorText.SetActive(false);
    }

    private void OnDestroy()
    {
        if (dictationRecognizer != null)
        {
            dictationRecognizer.Dispose();
        }
    }
    
    [System.Serializable]
    public class MappedFeedback
    {
        public bool isValid;
        public float likenessScore;
        public string speed;
        public string dtc;
        public string acceleration;
        public string smoothness;
    }

    [System.Serializable]
    public class GeminiResponse
    {
        public Candidate[] candidates;
    }

    [System.Serializable]
    public class Candidate
    {
        public Content content;
    }

    [System.Serializable]
    public class Content
    {
        public Part[] parts;
    }

    [System.Serializable]
    public class Part
    {
        public string text;
    }
}