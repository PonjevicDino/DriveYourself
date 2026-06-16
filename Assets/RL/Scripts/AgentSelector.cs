using UnityEngine;
using Unity.MLAgents.Policies;
using System.IO;
using System.Text.RegularExpressions;
using Unity.InferenceEngine;
using BOforUnity;
using System.Collections.Generic;
using System.Threading.Tasks;

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
    [UnityEngine.Range(50, 100)] public int targetSpeed = 50;
    [UnityEngine.Range(0, 100)] public int targetWeight = 85;
    [UnityEngine.Range(5, 15)] public int targetAccelTime = 10;
    [UnityEngine.Range(1, 9)] public int targetSmoothness = 5;
    private int oldTargetSpeed;
    private int oldTargetWeight;
    private int oldTargetAccelTime;
    private int oldTargetSmoothness;

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

    private ModelAsset[] availableModels;
    private string lastRunPrefix;
    private string resourcesPath;


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
            if (targetSpeed != oldTargetSpeed || targetWeight != oldTargetWeight ||
                targetAccelTime != oldTargetAccelTime || targetSmoothness != oldTargetSmoothness)
            {
                oldTargetSpeed = targetSpeed;
                oldTargetWeight = targetWeight;
                oldTargetAccelTime = targetAccelTime;
                oldTargetSmoothness = targetSmoothness;
                FindAndAssignModel();
            }
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
        
        SearchInModelDatabase();
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

    public async void FindAndAssignModel()
    {
        if (behaviorParameters == null) return;

        if (lastRunPrefix != runPrefix)
        {
            lastRunPrefix = runPrefix;
            resourcesPath = "Models/" + runPrefix;
            SearchInModelDatabase();
        }
        
        if (availableModels == null || availableModels.Length == 0)
        {
            Debug.LogWarning($"[ModelSelector] No models found in Resources path: {resourcesPath}. Keeping previous model.");
            return;
        }
        
        string[] modelNames = new string[availableModels.Length];
        for (int i = 0; i < availableModels.Length; i++)
        {
            modelNames[i] = availableModels[i].name;
        }
        
        int currentTargetSpeed = targetSpeed;
        int currentTargetWeight = targetWeight;
        int currentTargetAccelTime = targetAccelTime;
        int currentTargetSmoothness = targetSmoothness;
        
        int bestIndex = await Task.Run(() => 
        {
            return GetBestModelIndex(modelNames, currentTargetSpeed, currentTargetWeight, currentTargetAccelTime, currentTargetSmoothness);
        });
        
        if (bestIndex != -1)
        {
            ModelAsset bestModel = availableModels[bestIndex];

            if (currentLoadedModel == bestModel.name && behaviorParameters.Model != null) return;

            behaviorParameters.Model = bestModel;
            currentLoadedModel = bestModel.name;
            
            ApplyModelStatsFromName(bestModel.name);
        }
    }
    
    private int GetBestModelIndex(string[] names, int tSpeed, int tWeight, int tAccel, int tSmooth)
    {
        string pattern = @"Agent_(\d+)-S(\d+)-W(\d+)-A(\d+)-Sm(\d+)";
        Regex regex = new Regex(pattern);

        int bestIndex = -1;
        float minDistance = float.MaxValue;

        for (int i = 0; i < names.Length; i++)
        {
            Match match = regex.Match(names[i]);

            if (match.Success)
            {
                int fileSpeed = int.Parse(match.Groups[2].Value);
                int fileWeight = int.Parse(match.Groups[3].Value);
                int fileAcc = int.Parse(match.Groups[4].Value);
                int fileSmooth = int.Parse(match.Groups[5].Value);

                float dSpeed = (tSpeed - fileSpeed) / 100.0f;
                float dWeight = (tWeight - fileWeight) / 100.0f;
                float dAcc = (tAccel - fileAcc) / 20.0f;
                float dSmooth = (tSmooth - fileSmooth) / 10.0f;

                float dist = (dSpeed * dSpeed) + (dWeight * dWeight) + (dAcc * dAcc) + (dSmooth * dSmooth);

                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestIndex = i;
                }
            }
        }
        
        return bestIndex;
    }
    private void ApplyModelStatsFromName(string modelName)
    {
        string pattern = @"Agent_(\d+)-S(\d+)-W(\d+)-A(\d+)-Sm(\d+)";
        Match match = Regex.Match(modelName, pattern);
        
        if(match.Success)
        {
            int fileSpeed = int.Parse(match.Groups[2].Value);
            int fileWeight = int.Parse(match.Groups[3].Value);
            int fileAcc = int.Parse(match.Groups[4].Value);
            int fileSmooth = int.Parse(match.Groups[5].Value);
            
            SetModelRewardStats(fileSpeed, fileWeight, fileAcc, fileSmooth);
        }
    }

    private void SearchInModelDatabase()
    {
        availableModels = Resources.LoadAll<ModelAsset>(resourcesPath);
    }

    private void SetModelRewardStats(int fileSpeed, int fileWeight, int fileAcc, int fileSmooth)
    {
        agent.targetSpeed = fileSpeed;
        agent.DtCRewardPercent = fileWeight;
        agent.accelTime0to100 = fileAcc == 0 ? 10 : fileAcc; 
        
        float fullRange = 2.0f;
        float physicsFps = 50.0f;

        if (fileSmooth <= 0) 
        {
            agent.inputSmoothnessThreshold = fullRange;
        }
        else 
        {
            float totalFrames = fileSmooth * physicsFps;
            agent.inputSmoothnessThreshold = fullRange / totalFrames;
        }
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