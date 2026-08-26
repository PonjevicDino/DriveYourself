using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Runtime.InteropServices; 

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
    
    [Header("Speech Input")]
    [SerializeField] private TMP_InputField hiddenSpeechInput; 
    
    private string finalSentText = "";
    private MappedFeedback lastMappedData; 
    
    private bool isRecording = false;
    private bool isProcessing = false;
    private bool isDictationOpen = false;
    private bool isSpoofingOS = false; 
    
    private Coroutine recordingRoutine;   
    private bool startedWithMouse = false;
    
    [DllImport("user32.dll", SetLastError = true)]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
    
    [DllImport("user32.dll", SetLastError = true)]
    static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
    
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    private const byte VK_LWIN = 0x5B; 
    private const byte VK_H = 0x48;    
    private const uint KEYEVENTF_KEYUP = 0x0002; 
    
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const int VK_RBUTTON = 0x02;

    public void OnEnable()
    {
        page0Title.text = "DriveYourself - ID " + studyController.participantID + " - Condition C";
        if (studyController.boManager != null)
        {
            page1Title.text = "Feedback for previous Driving Style: " + studyController.boManager.currentIteration + "/" + studyController.boManager.totalIterations;
        }
        
        ResetSteps();
        feedbackSlider.value = 0.5f;
        isProcessing = false;
        isRecording = false;
        isDictationOpen = false;
        isSpoofingOS = false;
        lastMappedData = null;
    }
    
    public void OnStartRoundButtonClicked()
    {
        page0.SetActive(false);
        page1.SetActive(true);
        studyController.StartFirstRound();
    }
    
    private bool IsRightMouseButtonPhysicallyDown()
    {
        return (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
    }

    public void Update()
    {
        if (page0.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Return)) OnStartRoundButtonClicked();
        }
        else
        {
            if (isRecording && !isSpoofingOS)
            {
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != hiddenSpeechInput.gameObject)
                {
                    EventSystem.current.SetSelectedGameObject(hiddenSpeechInput.gameObject);
                    hiddenSpeechInput.ActivateInputField();
                    hiddenSpeechInput.MoveTextEnd(false);
                }
            }
            
            if (!isProcessing && !isSpoofingOS)
            {
                bool startTriggerMouse = Input.GetMouseButtonDown(1);
                bool startTriggerKey = Input.GetKeyDown(KeyCode.Return);
                bool mockTrigger = (Input.GetKey(KeyCode.Return) || Input.GetMouseButton(1)) && Input.GetKeyDown(KeyCode.Space);

                if ((startTriggerMouse || startTriggerKey) && !isRecording)
                {
                    startedWithMouse = startTriggerMouse;
                    if (recordingRoutine != null) StopCoroutine(recordingRoutine);
                    recordingRoutine = StartCoroutine(StartRecordingRoutine(startTriggerMouse));
                }
                else if (mockTrigger)
                {
                    TriggerDebugMock();
                }
                else if (isRecording)
                {
                    bool shouldStop = false;
                    
                    if (startedWithMouse && !IsRightMouseButtonPhysicallyDown()) 
                    {
                        shouldStop = true;
                    }
                    else if (!startedWithMouse && Input.GetKeyUp(KeyCode.Return)) 
                    {
                        shouldStop = true;
                    }

                    if (shouldStop)
                    {
                        if (recordingRoutine != null) StopCoroutine(recordingRoutine);
                        StopRecordingAndProcess();
                    }
                }
            }
        }
    }

    private void ToggleWindowsDictation()
    {
        keybd_event(VK_LWIN, 0, 0, 0); 
        keybd_event(VK_H, 0, 0, 0);    
        
        keybd_event(VK_H, 0, KEYEVENTF_KEYUP, 0);    
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0); 
    }

    private IEnumerator StartRecordingRoutine(bool isMouseTrigger)
    {
        isRecording = true;
        isDictationOpen = false;
        isSpoofingOS = true;
        
        finalSentText = "";
        liveTranscriptionText.text = "Listening...";
        
        inputStep.SetActive(true);
        processingStep.SetActive(false);
        doneStep.SetActive(false);
        errorText.SetActive(false);
        lastMappedData = null;

        micHandler.StartRecording();
        
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(hiddenSpeechInput.gameObject);
        hiddenSpeechInput.Select();
        hiddenSpeechInput.ActivateInputField();
        hiddenSpeechInput.MoveTextEnd(false);
        
        yield return new WaitForSeconds(0.2f); 
        
        if (isMouseTrigger)
        {
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
            yield return new WaitForSeconds(0.05f);
        }
        
        ToggleWindowsDictation(); 
        isDictationOpen = true; 
        
        yield return new WaitForSeconds(0.2f);
        if (isMouseTrigger)
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
        }

        isSpoofingOS = false;

        // Mirror the text live
        hiddenSpeechInput.onValueChanged.RemoveAllListeners();
        hiddenSpeechInput.onValueChanged.AddListener((newText) => 
        {
            finalSentText = newText;
            liveTranscriptionText.text = finalSentText;
        });
    }

    private void StopRecordingAndProcess()
    {
        isRecording = false;
        isProcessing = true;
        processingStep.SetActive(true);
        inputStep.SetActive(false);
        
        micHandler.StopRecording("C");
        
        if (isDictationOpen)
        {
            ToggleWindowsDictation();
            isDictationOpen = false;
        }

        hiddenSpeechInput.onValueChanged.RemoveAllListeners();
        hiddenSpeechInput.DeactivateInputField();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        finalSentText = finalSentText.Trim();

        if (string.IsNullOrWhiteSpace(finalSentText))
        {
            TriggerErrorState();
            return;
        }
        
        StartCoroutine(llmHandler.ProcessIntent(finalSentText, HandleLLMSuccess, HandleLLMError));
    }
    
    private void TriggerDebugMock()
    {
        if (recordingRoutine != null) StopCoroutine(recordingRoutine);
        
        if (isDictationOpen)
        {
            ToggleWindowsDictation();
            isDictationOpen = false;
        }
        
        isRecording = false;
        isProcessing = true;
        isSpoofingOS = false;

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
        
        StartCoroutine(llmHandler.ProcessIntent(finalSentText, HandleLLMSuccess, HandleLLMError));
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
        isSpoofingOS = false;
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

        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        ResetSteps();
        
        liveTranscriptionText.text = "\\\\\\\n\\\\\\\nYour input will be visible here\n\\\\\\\n\\\\\\";
        feedbackSlider.value = 0.5f;
        
        isProcessing = false;
        
        micHandler.ResetForNextRound();
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