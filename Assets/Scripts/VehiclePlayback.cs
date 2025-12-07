using Assets.Scripts.QLearningModules;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

public class VehiclePlayback : MonoBehaviour
{
    private RCC_CarControllerV4 carCont;

    private bool allowForPlayback = false;
    private string fileName;
    private int recNumber;
    private int totalRecNumber;

    private bool playbackRunning;
    private bool playbackPaused;

    [SerializeField] private GameObject playbackSetupWindow;
    [SerializeField] private TMP_InputField nameInputField;
    [SerializeField] private TextMeshProUGUI errorText;
    [SerializeField] private TextMeshProUGUI successText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI currentVars;
    [SerializeField] private GameObject errorButton;
    private string scoreFolder;

    void Start()
    {
        scoreFolder = this.GetComponent<ScoreSaver>().scoreFolder;
        carCont = GameObject.FindFirstObjectByType<RCC_CarControllerV4>();
        if (nameInputField)
        {
            nameInputField.onValueChanged.AddListener(ValidateInput);
        }
    }

    private void ValidateInput(string input)
    {
        input = input.ToLower();
        var fileInfo = new DirectoryInfo(Application.dataPath + "/" + scoreFolder + "/").GetFiles();
        totalRecNumber = 0;
        string constructedFileName = input + "_Recording_";
        foreach (var file in fileInfo)
        {
            if (file.Name.Contains(constructedFileName) && file.Name.EndsWith(".txt"))
            {
                totalRecNumber++;
            }
        }

        bool errorExists = false;
        if (input.Length < 3)
        {
            errorText.text = "Filename too short!";
            errorExists = true;
        }
        if (input == string.Empty)
        {
            errorText.text = "Filename cannot be empty!";
            errorExists = true;
        }
        else if (input.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            errorText.text = "Filename contains one or multiple illegal characters! '" + input[input.IndexOfAny(Path.GetInvalidFileNameChars())] + "'";
            errorExists = true;
        }
        if (totalRecNumber == 0)
        {
            errorText.text = "No recordings available for this file!";
            errorExists = true;
        }

        if (errorExists)
        {
            errorButton.SetActive(true);
            successText.text = string.Empty;
        }
        else
        {
            errorText.text = string.Empty;
            successText.text = totalRecNumber + " Recodings available";
            fileName = input;
            errorButton.SetActive(false);
        }
    }

    void Update()
    {
        if (allowForPlayback && Input.GetKeyDown(KeyCode.Backspace) && !playbackSetupWindow.activeSelf)
        {
            StopAllCoroutines();
            RunSetup();
        }

        if (playbackRunning && !playbackSetupWindow.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                if (recNumber == 1)
                {
                    recNumber = totalRecNumber;
                }
                else
                {
                    recNumber--;
                }
                StopAllCoroutines();
                statusText.text = "Playing: " + fileName + "-" + recNumber + "/" + totalRecNumber;
                RunPlayback(recNumber);
            }
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                if (recNumber == totalRecNumber)
                {
                    recNumber = 1;
                }
                else
                {
                    recNumber++;
                }
                StopAllCoroutines();
                statusText.text = "Playing: " + fileName + "-" + recNumber + "/" + totalRecNumber;
                RunPlayback(recNumber);
            }
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                StopPlayback();
            }
            if (Input.GetKeyDown(KeyCode.Space))
            {
                playbackPaused = !playbackPaused;
                if (playbackPaused)
                {
                    Time.timeScale = 0;
                }
                else
                {
                    Time.timeScale = 1;
                }
            }
        }
    }

    public void TogglePlaybackAllowance(bool state)
    {
        allowForPlayback = state;
    }

    public void RunSetup()
    {
        Time.timeScale = 0;
        playbackSetupWindow.SetActive(true);
    }

    public void CancelSetup()
    {
        playbackSetupWindow.SetActive(false);
        Time.timeScale = 1;
    }

    public void RunPlayback(int playbackId)
    {
        playbackRunning = true;
        playbackSetupWindow.SetActive(false);
        statusText.text = "Playing: " + fileName + "-" + recNumber + "/" + totalRecNumber;
        StartCoroutine(ProcessPlayback(playbackId));
    }

    public void StartPlaybackSession()
    {
        recNumber = 1;
        RunPlayback(recNumber);
        Time.timeScale = 1;
    }

    public void StopPlayback()
    {
        StopAllCoroutines();
        Time.timeScale = 1;
    }

    private IEnumerator ProcessPlayback(int playbackId)
    {
        int lineCount = File.ReadLines(Application.dataPath + "/" + scoreFolder + "/" + fileName + "_Recording_" + playbackId + ".txt").Count();
        int currentLine = 1;
        StreamReader reader = new StreamReader(Application.dataPath + "/" + scoreFolder + "/" + fileName + "_Recording_" + playbackId + ".txt");
        string line;

        string originalStatusText = statusText.text;
        float progress = 0.0f;

        float lineStartTime = 0.0f;
        int inputTime = 0;
        float accInput = 0.0f;
        float brkInput = 0.0f;
        float strInput = 0.0f;

        carCont.externalController = true;
        RCC_LogitechSteeringWheel logitechInput = carCont.GetComponent< RCC_LogitechSteeringWheel>();
        logitechInput.overrideFFB = true;

        while (true)
        {
            Application.targetFrameRate = 120;
            line = reader.ReadLine();
            while (line == string.Empty)
            {
                line = reader.ReadLine();
            }
            if (line == null)
            {
                statusText.text = originalStatusText + " | Progress: " + lineCount + "/" + lineCount + " (100.0%)";
                break;
            }

            if (line.StartsWith("CURR-VARS"))
            {
                inputTime = int.Parse(line.Split(" | ")[1].Split("[")[1].Split("]")[0]);
                accInput = float.Parse(line.Split(" | ")[2].Split("[")[1].Split("]")[0]);
                brkInput = float.Parse(line.Split(" | ")[3].Split("[")[1].Split("]")[0]);
                strInput = float.Parse(line.Split(" | ")[4].Split("[")[1].Split("]")[0]);
                logitechInput.steerInput = strInput;
                yield return new WaitForFixedUpdate();
            }

            else if (line.StartsWith("UPD-VPREC") || line.StartsWith("INIT-VARS")) {
                Vector3 pos = new Vector3(float.Parse(line.Split(" | ")[1].Split("[")[1].Split("]")[0].Split(",")[0]),
                                          float.Parse(line.Split(" | ")[1].Split("[")[1].Split("]")[0].Split(",")[1]),
                                          float.Parse(line.Split(" | ")[1].Split("[")[1].Split("]")[0].Split(",")[2]));
                Quaternion rot = Quaternion.Euler(float.Parse(line.Split(" | ")[2].Split("[")[1].Split("]")[0].Split(",")[0]),
                                                  float.Parse(line.Split(" | ")[2].Split("[")[1].Split("]")[0].Split(",")[1]),
                                                  float.Parse(line.Split(" | ")[2].Split("[")[1].Split("]")[0].Split(",")[2]));
                Vector3 vel = new Vector3(float.Parse(line.Split(" | ")[3].Split("[")[1].Split("]")[0].Split(",")[0]),
                                          float.Parse(line.Split(" | ")[3].Split("[")[1].Split("]")[0].Split(",")[1]),
                                          float.Parse(line.Split(" | ")[3].Split("[")[1].Split("]")[0].Split(",")[2]));
                Vector3 avl = new Vector3(float.Parse(line.Split(" | ")[4].Split("[")[1].Split("]")[0].Split(",")[0]),
                                          float.Parse(line.Split(" | ")[4].Split("[")[1].Split("]")[0].Split(",")[1]),
                                          float.Parse(line.Split(" | ")[4].Split("[")[1].Split("]")[0].Split(",")[2]));
                carCont.transform.SetPositionAndRotation(pos, rot);
                carCont.GetComponent<Rigidbody>().linearVelocity = vel;
                carCont.GetComponent<Rigidbody>().angularVelocity = avl;
            }

            carCont.throttleInput = accInput;
            carCont.brakeInput = brkInput;
            carCont.steerInput = strInput;

            currentVars.text = "ACC: " + accInput.ToString("0.0000") + " | BRK: " + brkInput.ToString("0.0000") + " | STR: " + strInput.ToString("0.0000");
            progress = currentLine * 100.0f / lineCount;
            statusText.text = originalStatusText + " | Progress: " + currentLine + "/" + lineCount + " (" + progress.ToString("00.0") + "%) | " + inputTime + "ms - Exec: " + ((Time.realtimeSinceStartup - Time.deltaTime - lineStartTime) * 1000.0f).ToString(".000") + "ms";
            currentLine++;

            lineStartTime = Time.realtimeSinceStartup;
        }

        currentVars.text = string.Empty;
        carCont.externalController = false;
        logitechInput.overrideFFB = false;
        playbackRunning = false;
        Application.targetFrameRate = 0;
    }

}
