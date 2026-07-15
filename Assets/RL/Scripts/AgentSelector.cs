using UnityEngine;
using Unity.MLAgents.Policies;
using System.Text.RegularExpressions;
using Unity.InferenceEngine;
using System.Threading.Tasks;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class AgentSelector : MonoBehaviour
{
    [Header("Folder Settings")]
    public string runPrefix = "Run001";

    [Header("Agent Parameters")]
    [Range(50, 112)] public int targetSpeed = 50;
    [Range(0, 100)] public int targetDtC = 85;
    [Range(5, 15)] public int targetAccelTime = 10;
    [Range(1, 9)] public int targetSmoothness = 5;
    private int oldTargetSpeed;
    private int oldTargetDtC;
    private int oldTargetAccelTime;
    private int oldTargetSmoothness;

    [Header("Status")]
    [SerializeField] private string currentLoadedModel = "None";
    public string CurrentLoadedModel => currentLoadedModel;
    [HideInInspector] public bool isUpdatingModel = false;

    private BehaviorParameters behaviorParameters;
    private DriveYourselfAgent agent;

    private ModelAsset[] availableModels;
    private string lastRunPrefix;
    private string resourcesPath;


    private void OnEnable()
    {
        Initialize();
        FindAndAssignModel();
    }

    private void OnValidate()
    {
        if (!this.enabled) return;
        Initialize();

        if (targetSpeed != oldTargetSpeed || targetDtC != oldTargetDtC ||
            targetAccelTime != oldTargetAccelTime || targetSmoothness != oldTargetSmoothness)
        {
            oldTargetSpeed = targetSpeed;
            oldTargetDtC = targetDtC;
            oldTargetAccelTime = targetAccelTime;
            oldTargetSmoothness = targetSmoothness;
            FindAndAssignModel();
        }
    }

    private void Start()
    {        
        SearchInModelDatabase();
    }

    private void Initialize()
    {
        if (behaviorParameters == null)
            behaviorParameters = GetComponent<BehaviorParameters>();

        if (agent == null)
            agent = GetComponent<DriveYourselfAgent>();
    }

    public void SetValuesFromExternal(int extSpeed, int extDtC, int extAccel, int extSmoothness)
    {
        Initialize();
        
        targetSpeed = extSpeed;
        targetDtC = extDtC;
        targetAccelTime = extAccel;
        targetSmoothness = extSmoothness;
        targetSmoothness = extSmoothness;

        if (targetSpeed != oldTargetSpeed || targetDtC != oldTargetDtC ||
            targetAccelTime != oldTargetAccelTime || targetSmoothness != oldTargetSmoothness)
        {
            oldTargetSpeed = targetSpeed;
            oldTargetDtC = targetDtC;
            oldTargetAccelTime = targetAccelTime;
            oldTargetSmoothness = targetSmoothness;
            FindAndAssignModel();
        }
    }

    public async void FindAndAssignModel()
    {
        isUpdatingModel = true;

        try
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
                Debug.LogWarning(
                    $"[ModelSelector] No models found in Resources path: {resourcesPath}. Keeping previous model.");
                return;
            }

            string[] modelNames = new string[availableModels.Length];
            for (int i = 0; i < availableModels.Length; i++)
            {
                modelNames[i] = availableModels[i].name;
            }

            int currentTargetSpeed = targetSpeed;
            int currentTargetDtC = targetDtC;
            int currentTargetAccelTime = targetAccelTime;
            int currentTargetSmoothness = targetSmoothness;

            int bestIndex = await Task.Run(() =>
            {
                return GetBestModelIndex(modelNames, currentTargetSpeed, currentTargetDtC, currentTargetAccelTime,
                    currentTargetSmoothness);
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
        finally
        {
            isUpdatingModel = false;
        }
    }
    
    private int GetBestModelIndex(string[] names, int tSpeed, int tDtC, int tAccel, int tSmooth)
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
                int fileDtC = int.Parse(match.Groups[3].Value);
                int fileAcc = int.Parse(match.Groups[4].Value);
                int fileSmooth = int.Parse(match.Groups[5].Value);

                float dSpeed = (tSpeed - fileSpeed) / 100.0f;
                float dDtC = (tDtC - fileDtC) / 100.0f;
                float dAcc = (tAccel - fileAcc) / 20.0f;
                float dSmooth = (tSmooth - fileSmooth) / 10.0f;

                float dist = (dSpeed * dSpeed) + (dDtC * dDtC) + (dAcc * dAcc) + (dSmooth * dSmooth);

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
            int fileDtC = int.Parse(match.Groups[3].Value);
            int fileAcc = int.Parse(match.Groups[4].Value);
            int fileSmooth = int.Parse(match.Groups[5].Value);
            
            SetModelRewardStats(fileSpeed, fileDtC, fileAcc, fileSmooth);
        }
    }

    private void SearchInModelDatabase()
    {
        availableModels = Resources.LoadAll<ModelAsset>(resourcesPath);
    }

    private void SetModelRewardStats(int fileSpeed, int fileDtC, int fileAcc, int fileSmooth)
    {
        agent.targetSpeed = fileSpeed;
        agent.DtCRewardPercent = fileDtC;
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
}