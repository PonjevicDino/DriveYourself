using System;
using System.Collections;
using System.Collections.Generic;
using BOforUnity;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

public class StudyController : MonoBehaviour
{
    [Header("Setup")]
    [HideInInspector] public StudyDataHandler studyDataHandler;
    [HideInInspector] public String participantID = String.Empty;
    [SerializeField] private TMP_InputField participantIDInput;
    
    [Header("LLM Configuration")]
    [SerializeField] private TMP_Dropdown llmProviderDropdown;
    [SerializeField] private TMP_InputField geminiApiKeyInput;
    [SerializeField] private TMP_InputField openAiApiKeyInput;
    public enum LLMProvider { Gemini = 0, OpenAI = 1 }
    public LLMProvider ActiveLLM => (LLMProvider)llmProviderDropdown.value;
    
    [Header("Windows")]
    [SerializeField] private GameObject startupWindow;
    [SerializeField] private GameObject micError;
    [SerializeField] private GameObject llmSetup;
    [SerializeField] private GameObject llmSetupError;
    [SerializeField] private GameObject endStudyWindow;
    [SerializeField] private GameObject endSuccessText;
    [SerializeField] private GameObject endFailedText;
    [SerializeField] private TextMeshProUGUI endFailedTextReason;
    [SerializeField] private GameObject conditionAWindow;
    [SerializeField] private GameObject conditionBWindow;
    [SerializeField] private GameObject conditionCWindow;
    [SerializeField] private GameObject conditionDWindow;
    [SerializeField] private GameObject progressWindow;
    private GameObject introWindow;
    private GameObject activeConditionWindow;
    
    [Header("Progress")]
    [SerializeField] private TextMeshProUGUI progressRoundText;
    [SerializeField] private Slider progressRoundBar;
    
    [Header("Car Parameters")]
    [SerializeField] private GameObject car;
    [SerializeField] private Transform startingPosition;
    [SerializeField] private AgentSelector agentSelector;
    [SerializeField] private GetVehicleData vehicleData;

    [Header("Bayesian Optimization")]
    //[SerializeField] public DemoBO demoBoManager;
    [SerializeField] public BoForUnityManager boManager;

    private int currentSpeed;
    private int currentDistanceToCenter;
    private int currentAcceleration;
    private int currentSmoothness;
    
    private float ctrlHoldTimer = 0f;
    
    public enum ParameterAdjustment 
    {
        MuchLess = -2,
        SlightlyLess = -1,
        Keep = 0,
        SlightlyMore = 1,
        MuchMore = 2
    }

    public struct AgentFeedback 
    {
        public float likenessScore;
        public float llmLikenessScore;
        public ParameterAdjustment speedAdjustment;
        public ParameterAdjustment dtcAdjustment;
        public ParameterAdjustment accelAdjustment;
        public ParameterAdjustment smoothAdjustment;
    }
    
    private void Awake()
    {
        if (agentSelector != null)
        {
            DriveYourselfAgent agent = agentSelector.GetComponent<DriveYourselfAgent>();
            if (agent != null)
            {
                agent.boMode = true;
            }
        }
    }
    
    private void Start()
    {
        studyDataHandler = this.GetComponent<StudyDataHandler>();
        participantID = studyDataHandler.GetNextParticipantID();
        participantIDInput.text = participantID;
        studyDataHandler.UpdateCompletedConditionsUI(participantID);
        participantIDInput.onValueChanged.AddListener((newValue) => 
        {
            studyDataHandler.UpdateCompletedConditionsUI(newValue);
        });
        
        RespawnCar();
        car.GetComponent<Rigidbody>().isKinematic = true;
        startupWindow.SetActive(true);
        conditionAWindow.SetActive(false);
        conditionBWindow.SetActive(false);
        conditionCWindow.SetActive(false);
        conditionDWindow.SetActive(false);

        if (PlayerPrefs.HasKey("GeminiAPIKey"))
        {
            geminiApiKeyInput.text = PlayerPrefs.GetString("GeminiAPIKey");
        }
        if (PlayerPrefs.HasKey("OpenAIAPIKey"))
        {
            openAiApiKeyInput.text = PlayerPrefs.GetString("OpenAIAPIKey");
        }
        if (PlayerPrefs.HasKey("ActiveLLMProvider"))
        {
            llmProviderDropdown.value = PlayerPrefs.GetInt("ActiveLLMProvider"); 
        }
        llmProviderDropdown.onValueChanged.AddListener((val) => ValidateLLMKey());
        
        llmSetup.SetActive(false);
        ValidateLLMKey();
        StartCoroutine(ValidateMicCoroutine());
        
        startingPosition.GetChild(0).gameObject.SetActive(false);
    }
    
    private void Update()
    {
        if (llmSetup.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.RightControl))
            {
                CloseLLMSetup();
            }
        }
        else
        {
            if (Input.GetKey(KeyCode.RightControl))
            {
                ctrlHoldTimer += Time.deltaTime;
                if (ctrlHoldTimer >= 1.5f)
                {
                    OpenLLMSetup();
                    ctrlHoldTimer = 0f;
                }
            }
            else
            {
                ctrlHoldTimer = 0f; 
            }
        }
    }

    private void OpenLLMSetup()
    {
        llmSetup.SetActive(true);
        llmSetupError.SetActive(false);
    }

    private void CloseLLMSetup()
    {
        llmSetup.SetActive(false);
        PlayerPrefs.SetString("GeminiAPIKey", geminiApiKeyInput.text);
        PlayerPrefs.SetString("OpenAIAPIKey", openAiApiKeyInput.text);
        PlayerPrefs.SetInt("ActiveLLMProvider", llmProviderDropdown.value);
        PlayerPrefs.Save();
        ValidateLLMKey();
    }

    private void ValidateLLMKey()
    {
        bool isInvalid = false;

        if (ActiveLLM == LLMProvider.Gemini && string.IsNullOrWhiteSpace(geminiApiKeyInput.text))
        {
            isInvalid = true;
        }
        else if (ActiveLLM == LLMProvider.OpenAI && string.IsNullOrWhiteSpace(openAiApiKeyInput.text))
        {
            isInvalid = true;
        }
            
        llmSetupError.SetActive(isInvalid);
    }
    
    private IEnumerator ValidateMicCoroutine()
    {
        yield return new WaitForSeconds(0.5f);

        if (Microphone.devices.Length == 0)
        {
            if (micError != null) micError.SetActive(true);
            Debug.LogWarning("Startup Check: No microphone detected on this system!");
        }
        else
        {
            if (micError != null) micError.SetActive(false);
            Debug.Log($"Microphone found: {Microphone.devices[0]}");
        }
    }
    
    public void SelectCondition(String condition)
    {
        participantID = participantIDInput.text;
        if (participantID == String.Empty)
        {
            return;
        }
        
        PlayerPrefs.SetString("GeminiAPIKey", geminiApiKeyInput.text);
        PlayerPrefs.SetString("OpenAIAPIKey", openAiApiKeyInput.text);
        PlayerPrefs.SetInt("ActiveLLMProvider", llmProviderDropdown.value);
        PlayerPrefs.Save();
        
        studyDataHandler.StartCondition(participantID, condition);
        
        startupWindow.SetActive(false);
        switch (condition)
        {
            case "A":
                conditionAWindow.SetActive(true);
                activeConditionWindow = conditionAWindow;
                break;
            case "B":
                conditionBWindow.SetActive(true);
                activeConditionWindow = conditionBWindow;
                break;
            case "C":
                conditionCWindow.SetActive(true);
                activeConditionWindow = conditionCWindow;
                break;
            case "D":
                conditionDWindow.SetActive(true);
                activeConditionWindow = conditionDWindow;
                break;
            default:
                startupWindow.SetActive(true);
                return;
        }
    }

    public void StartFirstRound()
    {
        activeConditionWindow.SetActive(false);
        StartCoroutine(WaitForBOInitialization());
    }

    private IEnumerator WaitForBOInitialization()
    {
        //yield return new WaitUntil(() => demoBoManager.initialized);
        yield return new WaitUntil(() => boManager.initialized);
        ExtractAgentParameters();
        RespawnCar();
        StartCoroutine(CheckForFinishedLap());
    }
    
    private void ExtractAgentParameters()
    {
        //int4 parameterValues = demoBoManager.ReturnNextAgent();
        //currentSpeed = parameterValues[0];
        //currentDistanceToCenter = parameterValues[1];
        //currentAcceleration = parameterValues[2];
        //currentSmoothness = parameterValues[3];
        
        int requestedSpeed = Mathf.RoundToInt(boManager.optimizer.GetParameterValue("VehicleSpeed"));
        int requestedDtc = Mathf.RoundToInt(boManager.optimizer.GetParameterValue("VehicleDistanceToCenter"));
        int requestedAccel = Mathf.RoundToInt(boManager.optimizer.GetParameterValue("VehicleMaxAcceleration"));
        int requestedSmooth = Mathf.RoundToInt(boManager.optimizer.GetParameterValue("VehicleSmoothness"));
        
        agentSelector.SetValuesFromExternal(requestedSpeed, requestedDtc, requestedAccel, requestedSmooth);
        
        currentSpeed = agentSelector.targetSpeed; 
        currentDistanceToCenter = agentSelector.targetDtC;
        currentAcceleration = agentSelector.targetAccelTime;
        currentSmoothness = agentSelector.targetSmoothness;
        
        Debug.Log($"[BO Requested] -> Speed: {requestedSpeed}, Dist: {requestedDtc}, Accel: {requestedAccel}, Smooth: {requestedSmooth}");
        Debug.Log($"[Nearest Model Loaded] -> {agentSelector.CurrentLoadedModel} (Speed: {currentSpeed}, Dist: {currentDistanceToCenter}, Accel: {currentAcceleration}, Smooth: {currentSmoothness})");
    }
    
    private IEnumerator CheckForFinishedLap()
    {
        float currentLapProgressPercent = vehicleData.GetContinuousProgress();
        
        while (currentLapProgressPercent < 100.0f)
        {
            currentLapProgressPercent = vehicleData.GetContinuousProgress();
            
            yield return new WaitForEndOfFrame();
            progressWindow.SetActive(true);
            //progressRoundText.text = "Round " + demoBoManager.ReturnIterations()[0] + "/" + demoBoManager.ReturnIterations()[1];
            progressRoundText.text = "Round " + boManager.currentIteration + "/" + boManager.totalIterations;
            progressRoundBar.value = currentLapProgressPercent / 100.0f;
            
            // Temporary break condition for testing
            if (Input.GetKey(KeyCode.Backspace)) break; 
        }
        StopRound();
    }
    
    public void StopRound()
    {
        activeConditionWindow.SetActive(true);
        car.GetComponent<Rigidbody>().isKinematic = true;
    }

    private void RespawnCar()
    {
        Rigidbody carRb = car.GetComponent<Rigidbody>();
        RCC_CarControllerV4 rcc = car.GetComponent<RCC_CarControllerV4>();
        DriveYourselfAgent agent = agentSelector.GetComponent<DriveYourselfAgent>();
        
        agent.EndEpisode();
        carRb.isKinematic = true;
        car.transform.SetPositionAndRotation(startingPosition.position, startingPosition.rotation);
        Physics.SyncTransforms();
        carRb.isKinematic = false;
        carRb.Sleep();
        carRb.WakeUp();

        carRb.linearVelocity = Vector3.zero;
        carRb.angularVelocity = Vector3.zero;

        if (rcc != null)
        {
            rcc.engineRPMRaw = 0f;
            rcc.engineRPM = 0f;
            rcc.throttleInput = 0f;
            rcc.brakeInput = 0f;
            rcc.steerInput = 0f;
        }

        if (vehicleData != null)
        {
            vehicleData.ResetVars();
            vehicleData.SyncDiscreteSegmentToSpline();
            vehicleData.InitContinuousSplineState();
        }
    }

    public void SubmitFeedback(AgentFeedback userFeedback, String transcript = null, List<string> audioFiles = null)
    {
        activeConditionWindow.SetActive(false);
        StartCoroutine(ProcessFeedbackSequence(userFeedback, transcript, audioFiles));
    }

    private IEnumerator ProcessFeedbackSequence(AgentFeedback feedback, String transcript, List<string> audioFiles)
    {
        int[] agentParams = new int[] { currentSpeed, currentDistanceToCenter, currentAcceleration, currentSmoothness };
        //int currentRound = (int)demoBoManager.ReturnIterations()[0];
        int currentRound = boManager.currentIteration;
        studyDataHandler.LogRoundData(currentRound, agentSelector.CurrentLoadedModel, agentParams, feedback, transcript, audioFiles);
        
        boManager.optimizer.AddObjectiveValue("Comfort", feedback.likenessScore);
        boManager.currentAdjustments["VehicleSpeed"] = (int)feedback.speedAdjustment;
        boManager.currentAdjustments["VehicleDistanceToCenter"] = (int)feedback.dtcAdjustment;
        boManager.currentAdjustments["VehicleMaxAcceleration"] = (int)feedback.accelAdjustment;
        boManager.currentAdjustments["VehicleSmoothness"] = (int)feedback.smoothAdjustment;
        boManager.OptimizationStart();
        
        //demoBoManager.GetUserResponse(feedback);
        //yield return new WaitUntil(() => demoBoManager.hasNextParameterValue);
        //demoBoManager.hasNextParameterValue = false;
        yield return new WaitUntil(() => boManager.hasNewDesignParameterValues);
        boManager.hasNewDesignParameterValues = false;

        //if (demoBoManager.ReturnIterations()[0] >= demoBoManager.ReturnIterations()[1])
        if (boManager.currentIteration >= boManager.totalIterations)
        {
            EndStudy();
            yield return null;
        }
        
        ExtractAgentParameters();
        RespawnCar();
        StartCoroutine(CheckForFinishedLap());
    }

    private void EndStudy()
    {
        activeConditionWindow.SetActive(false);
        endStudyWindow.SetActive(true);
        
        if (studyDataHandler.hasFileError)
        {
            endSuccessText.SetActive(false);
            endFailedText.SetActive(true);
            endFailedTextReason.text = studyDataHandler.fileErrors;
        }
        else
        {
            endSuccessText.SetActive(true);
            endFailedText.SetActive(false);
        }
        
        StopAllCoroutines();
        this.enabled = false;
    }
}
