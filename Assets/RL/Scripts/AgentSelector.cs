using UnityEngine;
using Unity.MLAgents.Policies;
using System.IO;
using System.Text.RegularExpressions;
using Unity.InferenceEngine;
using BOforUnity;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class AgentSelector : MonoBehaviour
{
    [Header("Folder Settings")]
    [Tooltip("Path relative to project folder. E.g. Assets/MyModels")]
    public string rootFolder = "Assets/TrainedModels";
    public string runPrefix = "Run001";

    [Header("Agent Parameters")]
    [UnityEngine.Range(20, 100)] public int targetSpeed = 20;
    [UnityEngine.Range(0, 100)] public int targetWeight = 85;
    [UnityEngine.Range(5, 20)] public int targetAccelTime = 10;
    [UnityEngine.Range(0, 10)] public int targetSmoothness = 5;

    [Header("Status")]
    [SerializeField] private string currentLoadedModel = "None";
    [SerializeField] public bool boActive = false;

    private BehaviorParameters behaviorParameters;
    private DriveYourselfAgent agent;

    private BoForUnityManager boUnity;
    private Dictionary<string, float> boValues;
    [HideInInspector] public bool boStartCommandGiven = false;
    private int origEndEpisodeCarStuckSeconds = 0;
    private bool studyHasStarted = false;

    private void OnEnable()
    {
        Initialize();
        if (!boActive)
        {
            FindAndAssignModel();
        }
    }

    private void OnValidate()
    {
        if (!this.enabled) return;
        Initialize();
        if (!boActive)
        {
            FindAndAssignModel();
        }
    }

    private void Start()
    {
        if (GameObject.Find("BOforUnityManager") != null)
        {
            boUnity = GameObject.Find("BOforUnityManager").GetComponent<BoForUnityManager>();
        }

        if (boUnity == null)
        {
            boActive = false;
        }
        else if (boValues == null)
        {
            boValues = new Dictionary<string, float>();
            ReadValuesFromBO();
            SetValuesFromBO();
        }

        if (boActive)
        {
            origEndEpisodeCarStuckSeconds = agent.endEpisodeCarStuckSeconds;
            agent.endEpisodeCarStuckSeconds = int.MaxValue;
            agent.transform.parent.GetComponent<Rigidbody>().isKinematic = true;
        }
    }

    private void Update()
    {
        if (boActive && boStartCommandGiven)
        {
            ReadValuesFromBO();
            SetValuesFromBO();
            boStartCommandGiven = false;
        }

        if (boActive)
        {
            CheckForFeedback();
        }
    }

    private void Initialize()
    {
        if (behaviorParameters == null)
            behaviorParameters = GetComponent<BehaviorParameters>();

        if (agent == null)
            agent = GetComponent<DriveYourselfAgent>();
    }

    public void FindAndAssignModel()
    {
        if (behaviorParameters == null) return;
        string resourcesPath = "Models/" + runPrefix; 
        ModelAsset[] availableModels = Resources.LoadAll<ModelAsset>(resourcesPath);

        if (availableModels.Length == 0)
        {
            Debug.LogWarning($"[ModelSelector] No models found in Resources path: {resourcesPath}. Keeping previous model.");
            return;
        }

        string pattern = @"Agent_(\d+)-S(\d+)-W(\d+)-A(\d+)-Sm(\d+)";
        Regex regex = new Regex(pattern);

        ModelAsset bestModel = null;
        float minDistance = float.MaxValue;
        
        int bestFileSpeed = 0;
        int bestFileWeight = 0;
        int bestFileAcc = 0;
        int bestFileSmooth = 0;

        foreach (ModelAsset model in availableModels)
        {
            Match match = regex.Match(model.name);

            if (match.Success)
            {
                int fileSpeed = int.Parse(match.Groups[2].Value);
                int fileWeight = int.Parse(match.Groups[3].Value);
                int fileAcc = int.Parse(match.Groups[4].Value);
                int fileSmooth = int.Parse(match.Groups[5].Value);

                float dSpeed = (targetSpeed - fileSpeed) / 100.0f;
                float dWeight = (targetWeight - fileWeight) / 100.0f;
                float dAcc = (targetAccelTime - fileAcc) / 20.0f;
                float dSmooth = (targetSmoothness - fileSmooth) / 10.0f;

                float dist = (dSpeed * dSpeed) + (dWeight * dWeight) + (dAcc * dAcc) + (dSmooth * dSmooth);

                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestModel = model;
                    
                    bestFileSpeed = fileSpeed;
                    bestFileWeight = fileWeight;
                    bestFileAcc = fileAcc;
                    bestFileSmooth = fileSmooth;
                }
            }
        }

        if (bestModel != null)
        {
            if (currentLoadedModel == bestModel.name && behaviorParameters.Model != null) return;

            behaviorParameters.Model = bestModel;
            currentLoadedModel = bestModel.name;
            SetModelRewardStats(bestFileSpeed, bestFileWeight, bestFileAcc, bestFileSmooth);
            //Debug.Log($"[ModelSelector] Successfully loaded new model: {bestModel.name}");
        }
    }

    private void SetModelRewardStats(int fileSpeed, int fileWeight, int fileAcc, int fileSmooth)
    {
        int speedRewardPercent = fileWeight; 
        int accTime = fileAcc == 0 ? 10 : fileAcc; 
        float calculatedThreshold = 0.0f; 
        if (fileSmooth != 0) 
        {
            float smoothSeconds = (fileSmooth - 2.0f) / 8.0f * 6.0f;
            if (smoothSeconds > 0.1f)
            {
                calculatedThreshold = 1.0f / (smoothSeconds * 50.0f);
            }
        }
        
        agent.targetSpeed = fileSpeed;
        agent.DtCRewardPercent = speedRewardPercent;
        agent.accelTime0to100 = accTime;
        agent.inputSmoothnessThreshold = calculatedThreshold;
    }

    private void ReadValuesFromBO()
    {
        for (int parameterIdx = 0; parameterIdx < boUnity.parameters.Count; parameterIdx++)
        {
            var parameter = boUnity.parameters[parameterIdx];
            //Debug.Log("Added Parameter to BO-List: " + parameter.key + " = " + parameter.value.Value);
            boValues[parameter.key] = parameter.value.Value;
        }
    }

    private void SetValuesFromBO()
    {
        targetSpeed = Mathf.RoundToInt(boValues["VehicleSpeed"]);
        targetWeight = Mathf.RoundToInt(boValues["VehicleDistanceToCenter"]);
        targetAccelTime = Mathf.RoundToInt(boValues["VehicleMaxAcceleration"]);
        targetSmoothness = Mathf.RoundToInt(boValues["VehicleSmoothness"]);

        agent.endEpisodeCarStuckSeconds = origEndEpisodeCarStuckSeconds;
        agent.transform.parent.GetComponent<Rigidbody>().isKinematic = false;

        FindAndAssignModel();
    }

    public void IterationEnd()
    {
        agent.endEpisodeCarStuckSeconds = int.MaxValue;
        agent.transform.parent.GetComponent<Rigidbody>().isKinematic = true;
    }

    private void CheckForFeedback()
    {
        int trust = 0;
        bool buttonPressed = false;

        if (Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            trust = 5;
            Debug.Log("User trusts the agent!");
            buttonPressed = true;
        }
        if (Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            trust = 1;
            Debug.Log("User does not trust the agent!");
            buttonPressed = true;
        }

        if (buttonPressed)
        {
            GameObject.FindFirstObjectByType<BoForUnityManager>().optimizer.AddObjectiveValue("Trust", trust);
            GameObject.FindFirstObjectByType<BoForUnityManager>().optimizer.AddObjectiveValue("Comfort", 3);

            GameObject.FindFirstObjectByType<BoForUnityManager>().OptimizationStart();
        }

        if (Input.GetKey(KeyCode.Space) && !studyHasStarted)
        {
            studyHasStarted = true;
            GameObject.FindFirstObjectByType<BoForUnityManager>().ButtonNextIteration();
        }
    }
}