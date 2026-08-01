using System;
using System.Collections;
using System.Collections.Generic;
using BOforUnity;
using TMPro;
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
    [SerializeField] private GameObject loadingScreen;
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
    
    [Header("Car 2")]
    private GameObject car2;
    private AgentSelector agentSelector2;
    private GetVehicleData vehicleData2;
    private bool isPairwiseMode = false;

    [Header("Bayesian Optimization")]
    //[SerializeField] public DemoBO demoBoManager;
    [SerializeField] public BoForUnityManager boManager;

    private int currentSpeed;
    private int currentDistanceToCenter;
    private int currentAcceleration;
    private int currentSmoothness;
    
    [Header("Pairwise Tracking")]
    private int prevSpeed = 50; // Default baseline values
    private int prevDtc = 85;
    private int prevAccel = 10;
    private int prevSmooth = 5;
    
    private float ctrlHoldTimer = 0f;
    private float roundStopTime = 0f;
    
    public enum ParameterAdjustment 
    {
        Ignore = -999,
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
        public float responseTime;
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
        AudioListener.volume = 0f;
        
        car.GetComponent<Rigidbody>().isKinematic = true;
        loadingScreen.SetActive(false);
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
        
        agentSelector.ExtractAvailableAgentsForOptimizer(boManager);
        
        studyDataHandler.StartCondition(participantID, condition);
        startupWindow.SetActive(false);
        
        boManager.userId = participantID;
        boManager.conditionId = condition;
        boManager.holdInitialization = false;
        
        isPairwiseMode = (condition == "D");
        if (isPairwiseMode)
        {
            if (car2 == null)
            {
                car2 = Instantiate(car, car.transform.parent);
                
                var cloneWheel = car2.GetComponent<RCC_LogitechSteeringWheel>();
                if (cloneWheel != null) Destroy(cloneWheel);
                
                car2.GetComponent<Rigidbody>().isKinematic = true;
                car2.transform.SetPositionAndRotation(startingPosition.position, startingPosition.rotation);
                Physics.SyncTransforms();
                agentSelector2 = car2.GetComponentInChildren<AgentSelector>();
                vehicleData2 = car2.GetComponentInChildren<GetVehicleData>();

                string[] layerNames = { "RCC_Vehicle", "RCC_WheelCollider", "RCC_DetachablePart", "RCC_Prop" };

                Dictionary<int, int> layerMapping = new Dictionary<int, int>();
                int car1Mask = 0;
                int car2Mask = 0;

                for (int i = 0; i < layerNames.Length; i++)
                {
                    int l1 = LayerMask.NameToLayer(layerNames[i]);
                    int l2 = LayerMask.NameToLayer(layerNames[i] + "2");

                    if (l1 != -1 && l2 != -1)
                    {
                        layerMapping[l1] = l2;
                        car1Mask |= (1 << l1);
                        car2Mask |= (1 << l2);
                    }
                    else
                    {
                        Debug.LogWarning($"Layer missing! Check spelling for {layerNames[i]}");
                    }
                }

                SetLayerRecursivelyMapped(car2, layerMapping);

                Camera cam1 = car.GetComponentInChildren<Camera>();
                Camera cam2 = car2.GetComponentInChildren<Camera>();

                if (cam1 != null && cam2 != null)
                {
                    cam1.cullingMask &= ~car2Mask;
                    cam2.cullingMask &= ~car1Mask;

                    cam1.rect = new Rect(0f, 0f, 0.5f, 1f);
                    cam2.rect = new Rect(0.5f, 0f, 0.5f, 1f);
                    
                    AudioListener audioListener = cam2.GetComponent<AudioListener>();
                    if (audioListener != null)
                    {
                        Destroy(audioListener);
                    }
                    
                    AudioSource[] car2Audio = car2.GetComponentsInChildren<AudioSource>(true);
                    foreach (AudioSource src in car2Audio)
                    {
                        src.spatialBlend = 0f;
                    }
                }
            }
            car2.SetActive(true);
        }
        else if (car2 != null)
        {
            car2.SetActive(false);
        }
        
        UpdateSteeringWheelState(true);
        
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
        yield return StartCoroutine(ExtractAgentParameters());
        RespawnCar();
        StartCoroutine(CheckForFinishedLap());
    }
    
    private IEnumerator ExtractAgentParameters()
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
        
        if (isPairwiseMode && car2 != null)
        {
            agentSelector.SetValuesFromExternal(prevSpeed, prevDtc, prevAccel, prevSmooth);
            agentSelector2.SetValuesFromExternal(requestedSpeed, requestedDtc, requestedAccel, requestedSmooth);
        }
        else
        {
            agentSelector.SetValuesFromExternal(requestedSpeed, requestedDtc, requestedAccel, requestedSmooth);
        }
        
        yield return new WaitUntil(() => !agentSelector.isUpdatingModel);
        if (isPairwiseMode && car2 != null)
        {
            yield return new WaitUntil(() => !agentSelector2.isUpdatingModel);
        }
        
        currentSpeed = isPairwiseMode ? agentSelector2.targetSpeed : agentSelector.targetSpeed; 
        currentDistanceToCenter = isPairwiseMode ? agentSelector2.targetDtC : agentSelector.targetDtC;
        currentAcceleration = isPairwiseMode ? agentSelector2.targetAccelTime : agentSelector.targetAccelTime;
        currentSmoothness = isPairwiseMode ? agentSelector2.targetSmoothness : agentSelector.targetSmoothness;
        
        prevSpeed = currentSpeed;
        prevDtc = currentDistanceToCenter;
        prevAccel = currentAcceleration;
        prevSmooth = currentSmoothness;
        
        Debug.Log($"[BO Requested] -> Speed: {requestedSpeed}, Dist: {requestedDtc}, Accel: {requestedAccel}, Smooth: {requestedSmooth}");
        Debug.Log($"[Nearest Model Loaded] -> {agentSelector.CurrentLoadedModel} (Speed: {currentSpeed}, Dist: {currentDistanceToCenter}, Accel: {currentAcceleration}, Smooth: {currentSmoothness})");
    }
    
    private IEnumerator CheckForFinishedLap()
    {
        float p1 = vehicleData.GetContinuousProgress();
        float p2 = isPairwiseMode ? vehicleData2.GetContinuousProgress() : 100.0f;
        
        while (p1 < 100.0f || p2 < 100.0f)
        {
            p1 = vehicleData.GetContinuousProgress();
            if (isPairwiseMode) p2 = vehicleData2.GetContinuousProgress();
            
            yield return new WaitForEndOfFrame();
            progressWindow.SetActive(true);
            progressRoundText.text = "Round " + boManager.currentIteration + "/" + boManager.totalIterations;
            
            progressRoundBar.value = Mathf.Min(p1, p2) / 100.0f;
            
            // Temporary break condition for testing
            if (Input.GetKey(KeyCode.Backspace)) break; 
        }
        StopRound();
    }
    
    public void StopRound()
    {
        activeConditionWindow.SetActive(true);
        UpdateSteeringWheelState(true);
        car.GetComponent<Rigidbody>().isKinematic = true;
        
        if (isPairwiseMode && car2 != null)
        {
            car2.GetComponent<Rigidbody>().isKinematic = true;
        }
        
        roundStopTime = Time.realtimeSinceStartup;
        AudioListener.volume = 0f;
    }

    private void RespawnCar()
    {
        ResetSingleCar(car);
       
        if (isPairwiseMode && car2 != null)
        {
            ResetSingleCar(car2);
        }
        
        UpdateSteeringWheelState(false);
        AudioListener.volume = 1f;
    }

    private void ResetSingleCar(GameObject targetCar)
    {
        Rigidbody carRb = targetCar.GetComponent<Rigidbody>();
        RCC_CarControllerV4 rcc = targetCar.GetComponent<RCC_CarControllerV4>();
        DriveYourselfAgent agent = targetCar.GetComponentInChildren<DriveYourselfAgent>();
        GetVehicleData vData = targetCar.GetComponentInChildren<GetVehicleData>();
        
        agent.EndEpisode();
        carRb.isKinematic = true;
        
        targetCar.transform.SetPositionAndRotation(startingPosition.position, startingPosition.rotation);
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

        if (vData != null)
        {
            vData.ResetVars();
            vData.SyncDiscreteSegmentToSpline();
            vData.InitContinuousSplineState();
        }
    }

    public void SubmitFeedback(AgentFeedback userFeedback, String transcript = null, List<string> audioFiles = null)
    {
        userFeedback.responseTime = Time.realtimeSinceStartup - roundStopTime;
        activeConditionWindow.SetActive(false);
        StartCoroutine(ProcessFeedbackSequence(userFeedback, transcript, audioFiles));
    }

    private IEnumerator ProcessFeedbackSequence(AgentFeedback feedback, String transcript, List<string> audioFiles)
    {
        int[] agentParams = new int[] { currentSpeed, currentDistanceToCenter, currentAcceleration, currentSmoothness };
        //int currentRound = (int)demoBoManager.ReturnIterations()[0];
        int currentRound = boManager.currentIteration;
        studyDataHandler.LogRoundData(currentRound, agentSelector.CurrentLoadedModel, agentParams, feedback, transcript, audioFiles);
        
        boManager.optimizer.AddObjectiveValue("Likeness", feedback.likenessScore);
        if (feedback.speedAdjustment != ParameterAdjustment.Ignore) 
            boManager.currentAdjustments["VehicleSpeed"] = (int)feedback.speedAdjustment;
            
        if (feedback.dtcAdjustment != ParameterAdjustment.Ignore) 
            boManager.currentAdjustments["VehicleDistanceToCenter"] = (int)feedback.dtcAdjustment;
            
        if (feedback.accelAdjustment != ParameterAdjustment.Ignore) 
            boManager.currentAdjustments["VehicleMaxAcceleration"] = -(int)feedback.accelAdjustment;
            
        if (feedback.smoothAdjustment != ParameterAdjustment.Ignore) 
            boManager.currentAdjustments["VehicleSmoothness"] = (int)feedback.smoothAdjustment;
        boManager.OptimizationStart();
        
        loadingScreen.SetActive(true);
        
        //demoBoManager.GetUserResponse(feedback);
        //yield return new WaitUntil(() => demoBoManager.hasNextParameterValue);
        //demoBoManager.hasNextParameterValue = false;
        yield return new WaitUntil(() => boManager.hasNewDesignParameterValues);
        boManager.hasNewDesignParameterValues = false;
        
        loadingScreen.SetActive(false);

        //if (demoBoManager.ReturnIterations()[0] >= demoBoManager.ReturnIterations()[1])
        if (boManager.currentIteration > boManager.totalIterations)
        {
            EndStudy();
            yield break;
        }
        
        yield return StartCoroutine(ExtractAgentParameters());
        
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
    
    private void SetLayerRecursivelyMapped(GameObject obj, Dictionary<int, int> layerMap)
    {
        if (layerMap.TryGetValue(obj.layer, out int newLayer))
        {
            obj.layer = newLayer;
        }

        foreach (Transform child in obj.transform)
        {
            SetLayerRecursivelyMapped(child.gameObject, layerMap);
        }
    }
    
    private void UpdateSteeringWheelState(bool stopMoving)
    {
        var steeringWheel = car.GetComponent<RCC_LogitechSteeringWheel>();
        if (steeringWheel != null)
        {
            steeringWheel.TurnOffFFB = isPairwiseMode ? true : stopMoving;
        }
    }
}
