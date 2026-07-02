using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using Debug = UnityEngine.Debug;

// This class manages the interaction with a Python script, which is responsible for System initialization
// and communication during a Unity application. It handles the launching of the Python process, monitors its
// status, and updates the user interface based on the System's state. Additionally, it facilitates scene
// transitions based on the application's configuration.
namespace BOforUnity.Scripts
{
    public class PythonStarter : MonoBehaviour
    {
        private string pythonExecutable;
        private Process pythonProcess;

        public bool isPythonProcessRunning;
        public bool isSystemStarted = false;

        private string outputFilePath;
        private StreamWriter outputFileWriter;

        private BoForUnityManager _bomanager;
        private bool _exitMessageShown = false;

        // ── Python dependency install status (shown in UI while running) ─────
        [Header("Python Install Status")]
        public string pythonInstallStatus = "Idle";
        public bool pythonInstallRunning = false;
        public bool pythonInstallSucceeded = false;

        private void Start()
        {
            _bomanager = gameObject.GetComponent<BoForUnityManager>();

            // Start the Python boot process
            StartCoroutine(SetupThenLaunchCoroutine());

#if UNITY_EDITOR
            // Subscribe to the play mode state change event
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        private IEnumerator SetupThenLaunchCoroutine()
        {
            // 1. HARDCODE TO THE BUNDLED PORTABLE PYTHON ENVIRONMENT
            // This safely resolves on any PC, in both the Editor and the final compiled .exe
            pythonExecutable = Path.Combine(Application.streamingAssetsPath, "PythonEnv", "python.exe");

            Debug.Log("Bundled Python Executable Path: " + pythonExecutable);
            Debug.Log("Bundled Python Executable Exists: " + File.Exists(pythonExecutable));

            // 2. SKIP PIP INSTALLATION
            // Because we baked the environment, we can instantly mark it as succeeded!
            pythonInstallStatus = "Bundled Python Environment Ready.";
            pythonInstallRunning = false;
            pythonInstallSucceeded = true;

            // Set an environment variable to allow for multiple instances of a dynamic link library.
            Environment.SetEnvironmentVariable("KMP_DUPLICATE_LIB_OK", "TRUE");

            // 3. DETERMINE WHICH SCRIPT TO RUN
            string moboScriptName = _bomanager.objectives.Count > 1 ? "mobo.py" : "bo.py";

            // Construct the full path to the Python script
            string fullPath = Path.Combine(Application.streamingAssetsPath, "BOData", "BayesianOptimization", moboScriptName);

            Debug.Log("Python BO Script Path: " + fullPath);
            Debug.Log("Python BO Script Exists: " + File.Exists(fullPath));

            outputFilePath = Path.Combine(Application.streamingAssetsPath, "BOData", "BayesianOptimization", "output.txt");
            outputFileWriter = new StreamWriter(outputFilePath);

            // 4. LAUNCH!
            CreateProcess(fullPath);
            yield return null;
        }

        private void Update()
        {
            // Live status during install
            if (pythonProcess != null && pythonProcess.HasExited && !_exitMessageShown)
            {
                _exitMessageShown = true;
                Debug.Log(">>>>> Python Process has EXITED!");
            }
        }

        private void CreateProcess(string fullPath)
        {
            StartCoroutine(RestartPythonProcessCoroutine(fullPath));
        }

        private IEnumerator RestartPythonProcessCoroutine(string fullPath)
        {
            yield return new WaitForSeconds(0.25f); // small delay

            pythonProcess = new Process();
            pythonProcess.StartInfo.FileName = pythonExecutable;
            pythonProcess.StartInfo.Arguments = $"\"{fullPath}\"";
            
            // Set working directory to the StreamingAssets/BOData folder
            pythonProcess.StartInfo.WorkingDirectory = Path.Combine(Application.streamingAssetsPath, "BOData");
            
            pythonProcess.StartInfo.UseShellExecute = false;
            pythonProcess.StartInfo.CreateNoWindow = true;
            pythonProcess.StartInfo.RedirectStandardOutput = true;
            pythonProcess.StartInfo.RedirectStandardError = true;
            pythonProcess.EnableRaisingEvents = true;

            pythonProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputFileWriter.WriteLine(e.Data);
                    outputFileWriter.Flush();
                    Debug.LogWarning("Python Output: " + e.Data);

                    if (e.Data.IndexOf("Server starts, waiting for connection...", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isSystemStarted = true;
                    }
                }
            };
            pythonProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Debug.LogError("Python Error: " + e.Data);
                }
            };
            pythonProcess.Exited += (sender, args) => Debug.LogWarning("Python process exited with code: " + pythonProcess.ExitCode);

            try
            {
                pythonProcess.Start();
                isPythonProcessRunning = true;
                pythonProcess.BeginOutputReadLine();
                pythonProcess.BeginErrorReadLine();
                Debug.Log("Python process started successfully.");
            }
            catch (Exception ex)
            {
                Debug.Log("Failed to start Python process: " + ex.Message);
                isPythonProcessRunning = false;
            }
        }

        public void StopPythonProcess()
        {
            if (pythonProcess != null)
            {
                try
                {
                    if (!pythonProcess.HasExited)
                    {
                        pythonProcess.Kill();
                        pythonProcess.WaitForExit();
                    }
                }
                catch { /* ignore */ }

                pythonProcess.Dispose();
                pythonProcess = null;
            }
        }

        private void OnDestroy()
        {
            StopPythonProcess();
            if (outputFileWriter != null)
            {
                outputFileWriter.Close();
            }

#if UNITY_EDITOR
            // Unsubscribe from the play mode state change event
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
        }

        private void OnApplicationQuit()
        {
            StopPythonProcess();
            if (outputFileWriter != null)
            {
                outputFileWriter.Close();
            }
        }

#if UNITY_EDITOR
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode)
            {
                StopPythonProcess();
            }
        }
#endif
    }
}