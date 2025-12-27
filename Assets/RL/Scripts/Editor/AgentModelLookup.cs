using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;

public class AgentModelLookup : EditorWindow
{
    private string sourceFolder = "";
    private string targetFolder = "";
    private string runPrefix = "Run002";
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
        modelFolderName = EditorGUILayout.TextField("Inner Model Folder", modelFolderName);
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
        string cleanModelFolder = modelFolderName.Trim();

        Log($"Starting... Looking for Prefix: '{cleanPrefix}' inside '{sourceFolder}'");

        string pattern = $@"^{Regex.Escape(cleanPrefix)}-Agent_(\d+)[-_](S\d+)-(W\d+)$";

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
                string agentID = match.Groups[1].Value;
                string speed = match.Groups[2].Value;
                string weight = match.Groups[3].Value;

                string innerPath = Path.Combine(dirPath, cleanModelFolder);
                if (Directory.Exists(innerPath))
                {
                    FileInfo bestFile = GetHighestVersionOnnx(innerPath);
                    if (bestFile != null)
                    {
                        // Create Target Path
                        string finalTargetDir = Path.Combine(targetFolder, cleanPrefix);
                        if (!Directory.Exists(finalTargetDir)) Directory.CreateDirectory(finalTargetDir);

                        string newName = $"{agentID}-{speed}-{weight}.onnx";
                        string destPath = Path.Combine(finalTargetDir, newName);

                        File.Copy(bestFile.FullName, destPath, true);
                        Log($"SUCCESS: {dirName} -> {newName}");
                        successCount++;
                    }
                    else
                    {
                        Log($"FAIL: No .onnx files found in {innerPath}");
                    }
                }
                else
                {
                    Log($"FAIL: Could not find folder '{cleanModelFolder}' inside '{dirName}'");
                }
            }

            else if (dirName.StartsWith(cleanPrefix))
            {
                Log($"FAIL Regex: '{dirName}' still mismatch. Pattern expects: {cleanPrefix}-Agent_[Num]-[Speed]-[Weight]");
            }
        }

        Log($"Process Finished. Successfully processed {successCount} agents.");
        if (successCount > 0)
        {
            EditorUtility.DisplayDialog("Success", $"Processed {successCount} Agents.", "OK");
        }
        AssetDatabase.Refresh();
    }

    private FileInfo GetHighestVersionOnnx(string folderPath)
    {
        DirectoryInfo dir = new DirectoryInfo(folderPath);
        FileInfo[] files = dir.GetFiles("*.onnx");

        if (files.Length == 0) return null;

        var sorted = files
            .Select(f => new { File = f, Step = ExtractStepNumber(f.Name) })
            .OrderByDescending(x => x.Step)
            .ToList();

        return sorted.First().File;
    }

    private long ExtractStepNumber(string fileName)
    {
        Match m = Regex.Match(fileName, @"(\d+)(?=\.onnx$)");
        if (m.Success && long.TryParse(m.Value, out long result))
        {
            return result;
        }
        return -1;
    }
}