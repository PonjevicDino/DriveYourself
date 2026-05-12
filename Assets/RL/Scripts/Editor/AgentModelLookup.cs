using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;

public class AgentModelLookup : EditorWindow
{
    private string sourceFolder = "";
    private string targetFolder = "";
    private string runPrefix = "SpeedTest";
    private string modelFolderName = "TestModel";
    private bool debugMode = true;

    private Vector2 scrollPos;
    private string logs = "";

    [MenuItem("Tools/ML-Agents Batch Extractor (Final)")]
    public static void ShowWindow()
    {
        GetWindow<AgentModelLookup>("Batch Extractor");
    }

    private void OnGUI()
    {
        GUILayout.Label("ML-Agents Batch Extractor", EditorStyles.boldLabel);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.LabelField("Paths", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        sourceFolder = EditorGUILayout.TextField("Source Folder", sourceFolder);
        if (GUILayout.Button("Select", GUILayout.Width(60)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Source Folder", sourceFolder, "");
            if (!string.IsNullOrEmpty(path)) sourceFolder = path;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        targetFolder = EditorGUILayout.TextField("Target Folder", targetFolder);
        if (GUILayout.Button("Select", GUILayout.Width(60)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Target Output", targetFolder, "");
            if (!string.IsNullOrEmpty(path)) targetFolder = path;
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
        runPrefix = EditorGUILayout.TextField("Run Prefix", runPrefix);
        modelFolderName = EditorGUILayout.TextField("Inner Model Name (.onnx)", modelFolderName);
        debugMode = EditorGUILayout.Toggle("Enable Debug Logs", debugMode);

        GUILayout.Space(10);

        if (GUILayout.Button("Run Extraction", GUILayout.Height(40)))
        {
            ExtractModels();
        }

        if (!string.IsNullOrEmpty(logs))
        {
            GUILayout.Space(10);
            GUILayout.Label("Logs:", EditorStyles.boldLabel);
            GUIStyle logStyle = new GUIStyle(EditorStyles.textArea);
            logStyle.wordWrap = true;
            EditorGUILayout.TextArea(logs, logStyle, GUILayout.Height(200));
        }

        EditorGUILayout.EndScrollView();
    }

    private void Log(string message)
    {
        if (debugMode)
        {
            Debug.Log($"[Extractor] {message}");
            logs += message + "\n";
        }
    }

    private void ExtractModels()
    {
        logs = "";
        if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            Log("ERROR: Source folder is invalid or empty.");
            return;
        }

        string cleanPrefix = runPrefix.Trim();
        string cleanModelName = modelFolderName.Trim();

        Log($"Starting... Looking for Prefix: '{cleanPrefix}' inside '{sourceFolder}'");
        
        string pattern = $@"^{Regex.Escape(cleanPrefix)}-Agent_(\d+)[-_](S\d+)(?:-(W\d+))?(?:-(A\d+))?(?:-(Sm\d+))?$";

        Log($"Using Flexible Regex: {pattern}");
        Regex nameRegex = new Regex(pattern);

        string[] directories = Directory.GetDirectories(sourceFolder);
        Log($"Found {directories.Length} sub-folders in source directory.");

        int successCount = 0;

        foreach (string dirPath in directories)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(dirPath);
            string dirName = dirInfo.Name;

            Match match = nameRegex.Match(dirName);
            if (match.Success)
            {
                // Mandatory groups
                string agentID = match.Groups[1].Value;
                string speed = match.Groups[2].Value;

                // Optional groups (Fallback to 00 if they weren't in the folder name)
                string weight = match.Groups[3].Success ? match.Groups[3].Value : "W00";
                string acc = match.Groups[4].Success ? match.Groups[4].Value : "A00";
                string smooth = match.Groups[5].Success ? match.Groups[5].Value : "Sm00";

                string expectedOnnxPath = Path.Combine(dirPath, cleanModelName + ".onnx");

                if (File.Exists(expectedOnnxPath))
                {
                    string finalTargetDir = Path.Combine(targetFolder, cleanPrefix);
                    if (!Directory.Exists(finalTargetDir)) Directory.CreateDirectory(finalTargetDir);

                    string newName = $"{cleanPrefix}-Agent_{agentID}-{speed}-{weight}-{acc}-{smooth}.onnx";
                    string destPath = Path.Combine(finalTargetDir, newName);

                    File.Copy(expectedOnnxPath, destPath, true);
                    Log($"SUCCESS: {dirName} -> {newName}");
                    successCount++;
                }
                else
                {
                    Log($"FAIL: Found folder '{dirName}' but missing '{cleanModelName}.onnx' inside it.");
                }
            }
            else if (dirName.StartsWith(cleanPrefix))
            {
                Log($"FAIL Regex: '{dirName}' skipped. Pattern mismatch.");
            }
        }

        Log($"Process Finished. Successfully processed {successCount} agents.");
        if (successCount > 0)
        {
            EditorUtility.DisplayDialog("Success", $"Processed {successCount} Agents.", "OK");
        }
        AssetDatabase.Refresh();
    }
}