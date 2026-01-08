using UnityEngine;
using Unity.MLAgents.Policies;
using System.IO;
using System.Text.RegularExpressions;
using Unity.InferenceEngine;

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
    [Range(0, 200)] public int targetSpeed = 20;
    [Range(0, 100)] public int targetWeight = 85;

    [Header("Status")]
    [SerializeField] private string currentLoadedModel = "None";

    private BehaviorParameters behaviorParameters;
    private DriveYourselfAgent agent;

    private void OnEnable()
    {
        Initialize();
        FindAndAssignModel();
    }

    private void OnValidate()
    {
        if (!this.enabled) return;
        Initialize();
        FindAndAssignModel();
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
#if UNITY_EDITOR
        if (behaviorParameters == null) return;

        string assetFolderPath = Path.Combine(rootFolder, runPrefix);

        string systemPath = "";

        if (rootFolder.StartsWith("Assets"))
        {
            systemPath = Path.Combine(Application.dataPath, rootFolder.Substring(7), runPrefix);
        }
        else
        {
            Debug.LogError($"[ModelSelector] Root folder must start with 'Assets/' (Current: {rootFolder})");
            return;
        }
        if (!Directory.Exists(systemPath))
        {
            Debug.LogWarning($"[ModelSelector] Folder not found: {systemPath}. Keeping previous model.");
            return;
        }

        string[] files = Directory.GetFiles(systemPath, "*.onnx");
        if (files.Length == 0) return;

        string pattern = @"(\d+)[-_]S(\d+)[-_]W(\d+)\.onnx$";
        Regex regex = new Regex(pattern);

        string bestFilePath = "";
        float minDistance = float.MaxValue;
        string bestFileName = "";

        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            Match match = regex.Match(fileName);

            if (match.Success)
            {
                int fileSpeed = int.Parse(match.Groups[2].Value);
                int fileWeight = int.Parse(match.Groups[3].Value);

                float dist = Vector2.Distance(
                    new Vector2(targetSpeed, targetWeight),
                    new Vector2(fileSpeed, fileWeight)
                );

                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestFilePath = file;
                    bestFileName = fileName;
                }
            }
        }

        if (!string.IsNullOrEmpty(bestFilePath))
        {
            if (currentLoadedModel == bestFileName && behaviorParameters.Model != null) return;

            string relativePath = "Assets" + bestFilePath.Substring(Application.dataPath.Length);
            relativePath = relativePath.Replace("\\", "/");

            ModelAsset model = AssetDatabase.LoadAssetAtPath<ModelAsset>(relativePath);

            if (model != null)
            {
                behaviorParameters.Model = model;
                currentLoadedModel = bestFileName;
                SetModelRewardStats(model.name);
            }
            else
            {
                Debug.LogWarning($"[ModelSelector] Found file {relativePath} but failed to load as NNModel.");
            }
        }
#else
        Debug.LogWarning("[ModelSelector] This script currently relies on AssetDatabase and only works in the Unity Editor.");
#endif
    }

    private void SetModelRewardStats(string name)
    {
        int targetSpeed = int.Parse(name.Split("-")[1].Substring(1));
        int speedRewardPercent = int.Parse(name.Split("-")[2].Substring(1));

        agent.targetSpeed = targetSpeed;
        agent.DtCRewardPercent = speedRewardPercent;
    }
}