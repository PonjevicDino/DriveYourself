using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Windows.Speech;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class StudyControllerConditionC : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private StudyController studyController;
    [SerializeField] private Slider feedbackSlider;

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
    [SerializeField] private StudyControllerLlmHandler llmHandler;
    
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
        page0Title.text = "DriveYourself - ID " + studyController.participantID + " - Condition C";
        //if (studyController.demoBoManager != null)
        if (studyController.boManager != null)
        {
            //page1Title.text = "Feedback for previous Driving Style: " + studyController.demoBoManager.ReturnIterations()[0] + "/" + studyController.demoBoManager.ReturnIterations()[1];
            page1Title.text = "Feedback for previous Driving Style: " + studyController.boManager.currentIteration + "/" + studyController.boManager.totalIterations;
        }
        
        ResetSteps();
        feedbackSlider.value = 0.5f;
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
        
        micHandler.StopRecording("C");

        processingStep.SetActive(true); 
        string textToSend = (finalTranscription + " " + currentHypothesis).Trim();

        if (string.IsNullOrWhiteSpace(textToSend))
        {
            TriggerErrorState();
            return;
        }

        StartCoroutine(llmHandler.ProcessIntent(
            textToSend, 
            HandleLLMSuccess, 
            HandleLLMError
        ));
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

        string mockText = $"[DEBUG] Set speed to {s}, distance to center to {d}, acceleration to {a}, and smoothness to {sm}.";
        
        liveTranscriptionText.text = "Sending Debug Mock:\n" + mockText;
        StartCoroutine(llmHandler.ProcessIntent(
            mockText, 
            HandleLLMSuccess, 
            HandleLLMError
        ));
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
            likenessScore = feedbackSlider.value,
            llmLikenessScore = mappedData.likenessScore, 
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
        feedbackSlider.value = 0.5f;
        
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

    private void HandleLLMSuccess(MappedFeedback mappedData)
    {
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

    private void HandleLLMError(string errorMessage)
    {
        Debug.LogError(errorMessage);
        TriggerErrorState();
    }
}