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
    private string finalSentText = "";
    
    private MappedFeedback lastMappedData; 
    
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
        lastMappedData = null;
    }
    
    public void OnStartRoundButtonClicked()
    {
        page0.SetActive(false);
        page1.SetActive(true);
        studyController.StartFirstRound();
    }

    public void Update()
    {
        if (page0.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                OnStartRoundButtonClicked();
            }
        }
        else
        {
            if (!isProcessing)
            {
                bool startTrigger = Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(1);
                bool stopTrigger = Input.GetKeyUp(KeyCode.Return) || Input.GetMouseButtonUp(1);
                bool mockTrigger = (Input.GetKey(KeyCode.Return) || Input.GetMouseButton(1)) && Input.GetKeyDown(KeyCode.Space);

                if (startTrigger)
                {
                    StartRecording();
                }
                
                if (mockTrigger)
                {
                    TriggerDebugMock();
                }
                else if (stopTrigger)
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
        lastMappedData = null;

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
        processingStep.SetActive(true);
        inputStep.SetActive(false);
        micHandler.StopRecording("C");
        StartCoroutine(GracefulStopAndProcess());
    }

    private IEnumerator GracefulStopAndProcess()
    {
        yield return new WaitForSeconds(1.0f);
        
        if (dictationRecognizer.Status == SpeechSystemStatus.Running)
        {
            dictationRecognizer.Stop();
        }

        finalSentText = (finalTranscription + " " + currentHypothesis).Trim();
        liveTranscriptionText.text = finalSentText;

        if (string.IsNullOrWhiteSpace(finalSentText))
        {
            TriggerErrorState();
            yield break;
        }
        
        StartCoroutine(llmHandler.ProcessIntent(
            finalSentText, 
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
        
        string[] opts = { "much less", "slightly less", "keep", "slightly more", "much more", "ignore" };
        string s = opts[Random.Range(0, opts.Length)];
        string d = opts[Random.Range(0, opts.Length)];
        string a = opts[Random.Range(0, opts.Length)];
        string sm = opts[Random.Range(0, opts.Length)];

        string mockText = $"[DEBUG] Set speed to {s}, distance to center to {d}, acceleration to {a}, and smoothness to {sm}.";
        
        finalSentText = mockText;
        
        liveTranscriptionText.text = "Sending Debug Mock:\n" + finalSentText;
        StartCoroutine(llmHandler.ProcessIntent(
            finalSentText, 
            HandleLLMSuccess, 
            HandleLLMError
        ));
    }
    
    private StudyController.ParameterAdjustment StringToEnum(string val)
    {
        if (string.IsNullOrEmpty(val)) return StudyController.ParameterAdjustment.Ignore;

        val = val.ToLower().Trim();
        switch (val)
        {
            case "much less": return StudyController.ParameterAdjustment.MuchLess;
            case "slightly less": return StudyController.ParameterAdjustment.SlightlyLess;
            case "keep": return StudyController.ParameterAdjustment.Keep;
            case "slightly more": return StudyController.ParameterAdjustment.SlightlyMore;
            case "much more": return StudyController.ParameterAdjustment.MuchMore;
            default: return StudyController.ParameterAdjustment.Ignore;
        }
    }
    
    private void TriggerErrorState()
    {
        ResetSteps();
        errorText.SetActive(true);
        isProcessing = false;
        isRecording = false;
    }

    public void OnSubmitFeedbackButtonClicked()
    {
        if (lastMappedData == null) return;

        StudyController.AgentFeedback feedback = new StudyController.AgentFeedback
        {
            likenessScore = feedbackSlider.value,
            llmLikenessScore = lastMappedData.likenessScore, 
            speedAdjustment = StringToEnum(lastMappedData.speed),
            dtcAdjustment = StringToEnum(lastMappedData.dtc),
            accelAdjustment = StringToEnum(lastMappedData.acceleration),
            smoothAdjustment = StringToEnum(lastMappedData.smoothness)
        };

        studyController.SubmitFeedback(feedback, finalSentText, micHandler.GetAudioPaths());

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
        finalSentText = "";
        lastMappedData = null;
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
            lastMappedData = mappedData;
            doneStep.SetActive(true);
            processingStep.SetActive(false);
            isProcessing = false; 
        }
    }

    private void HandleLLMError(string errorMessage)
    {
        Debug.LogError(errorMessage);
        TriggerErrorState();
    }
}