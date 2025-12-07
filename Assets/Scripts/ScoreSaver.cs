using Assets.Scripts.Components;
using Assets.Scripts.QLearningModules;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Burst.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using static Unity.Burst.Intrinsics.X86;
using static UnityEngine.GraphicsBuffer;

public class ScoreSaver : MonoBehaviour
{
    /*
     * Logged scores:
     * Distance to center, Speed, Acceleration, Jerk
     *
     * Other meaningful values:
     * Time for each Track-Segment, avg. Lap-Speed, slowest/fastest segment (with its avg. speed), which segment had min/max speed, deviation from avg. speed (constant speed) 
     *
     */

    [SerializeField]
    [Tooltip("This folder is saved within the assets directory and has to exist before the script is executed. It can also be a path.")]
    public string scoreFolder = string.Empty;

    private bool shouldSaveScores = false;
    private bool recordingRunning = false;
    private string filename = string.Empty;

    private string filePath = string.Empty;

    private RCC_CarControllerV4 carController;
    private SteeringController steeringController;

    [SerializeField] private GameObject setupWindow;
    [SerializeField] private TMP_InputField nameInputField;
    [SerializeField] private TextMeshProUGUI errorText;
    [SerializeField] private GameObject errorButton;
    [SerializeField] private GameObject scoreRecordingIndicator;
    [SerializeField] private GameObject scoreRecordingIndicatorManual;

    private int currentSegmentID = 0;
    private int currentSegmentIndex = 0;
    private float currentSpeed = 0.0f;
    private float currentAcceleration = 0.0f;
    private float currentJerk = 0.0f;
    private float currentDtC = 0.0f;
    private DateTime currentTime = DateTime.Now;

    private List<TrackSegment> segments = new List<TrackSegment>();
    private TrackSegment currentSegment;
    private TrackSegment currentLap = new TrackSegment(-1);
    private GameObject roadSegment;

    private TrackSegment total = new TrackSegment(-2);
    private struct TrackSegment
    {
        public int id;
        public List<float> speeds;
        public float minSpeed;
        public List<float> minSpeeds;
        public float maxSpeed;
        public List<float> maxSpeeds;
        public List<float> devSpeed;
        public DateTime timeEntered;
        public TimeSpan time;
        public List<long> times;
        public int minTime;
        public int maxTime;
        public List<float> distanceToCenter;
        public List<float> acceleration;
        public List<float> jerk;
        public TrackSegment(int trackId)
        {
            this.id = trackId;
            this.speeds = new List<float>();
            this.minSpeed = 0.0f;
            this.minSpeeds = new List<float>();
            this.maxSpeed = 0.0f;
            this.maxSpeeds = new List<float>();
            this.devSpeed = new List<float>();
            this.timeEntered = DateTime.Now;
            this.time = TimeSpan.Zero;
            this.times = new List<long>();
            this.minTime = 0;
            this.maxTime = 0;
            this.distanceToCenter = new List<float>();
            this.acceleration = new List<float>();
            this.jerk = new List<float>();
        }
    }

    void Start()
    {
        Time.timeScale = 0.0f;
        if (nameInputField)
        {
            nameInputField.onValueChanged.AddListener(ValidateInput);
        }
    }

    private void ValidateInput(string input)
    {
        input = input.ToLower();
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
        if (File.Exists(Application.dataPath + "/" + scoreFolder + "/" + input + ".txt"))
        {
            errorText.text = "File already exists";
            errorExists = true;
        }

        if (errorExists)
        {
            errorButton.SetActive(true);
        }
        else
        {
            errorText.text = string.Empty;
            errorButton.SetActive(false);
        }

    }

    public void SetupScoreLoggingPositive()
    {
        shouldSaveScores = true;
        carController = GameObject.FindFirstObjectByType<RCC_CarControllerV4>();
        steeringController = GetComponent<SteeringController>();
        currentSegment = new TrackSegment(currentSegmentID);
        filename = nameInputField.text.ToLower();
        filePath = Application.dataPath + "/" + scoreFolder + "/" + filename + ".txt";
        File.WriteAllText(filePath, "SIM-START | " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | SCENE STARTED" + "\n\n");
        setupWindow.SetActive(false);
        Time.timeScale = 1.0f;
        total.timeEntered = DateTime.Now;
        GetComponent<VehiclePlayback>().TogglePlaybackAllowance(true);
    }

    public void SetupScoreLoggingNegative()
    {
        setupWindow.SetActive(false);
        Time.timeScale = 1.0f;
        GetComponent<VehiclePlayback>().TogglePlaybackAllowance(true);
    }

    void Update()
    {
        if (shouldSaveScores)
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                File.AppendAllText(filePath,
                    "MANUALREC | " +
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " +
                    "DtC~" + currentDtC + ", Speed~" + currentSpeed + ", Acc~" + currentAcceleration + ", Jerk~" + currentJerk + " | " +
                    "Seg: " + currentSegment.id + " \n\t " +

                    "Seg_Curr: Time~" + currentSegment.time +
                    ", Avg_Speed~" + SafeAverage(currentSegment.speeds) +
                    ", Min_Speed~" + currentSegment.minSpeed +
                    ", Max_Speed~" + currentSegment.maxSpeed +
                    ", Dev_Speed~" + SafeAverage(currentSegment.devSpeed) +
                    ", Avg_Distance_To_Center~" + SafeAverage(currentSegment.distanceToCenter) +
                    ", Avg_Acceleration~" + SafeAverage(currentSegment.acceleration) +
                    ", Avg_Jerk~" + SafeAverage(currentSegment.jerk) + " \n\t " +

                    "Seg_Avg: Avg_Time~" + (segments[currentSegmentIndex].times.Any() ? segments[currentSegmentIndex].times.Average().ToString("F0") : "NONE") +
                    ", Min_Time~" + segments[currentSegmentIndex].minTime +
                    ", Max_Time~" + segments[currentSegmentIndex].maxTime +
                    ", Avg_Speed~" + SafeAverage(segments[currentSegmentIndex].speeds) +
                    ", Avg_Min_Speed~" + SafeAverage(segments[currentSegmentIndex].minSpeeds) +
                    ", Avg_Max_Speed~" + SafeAverage(segments[currentSegmentIndex].maxSpeeds) +
                    ", Avg_Dev_Speed~" + SafeAverage(segments[currentSegmentIndex].devSpeed) +
                    ", Avg_Distance_To_Center~" + SafeAverage(segments[currentSegmentIndex].distanceToCenter) +
                    ", Avg_Acceleration~" + SafeAverage(segments[currentSegmentIndex].acceleration) +
                    ", Avg_Jerk~" + SafeAverage(segments[currentSegmentIndex].jerk) + " \n\t " +

                    "Lap_Curr: Time~" + currentLap.time +
                    ", Avg_Speed~" + SafeAverage(currentLap.speeds) +
                    ", Min_Speed~" + currentLap.minSpeed +
                    ", Max_Speed~" + currentLap.maxSpeed +
                    ", Dev_Speed~" + SafeAverage(currentLap.devSpeed) +
                    ", Avg_Distance_To_Center~" + SafeAverage(currentLap.distanceToCenter) +
                    ", Avg_Acceleration~" + SafeAverage(currentLap.acceleration) +
                    ", Avg_Jerk~" + SafeAverage(currentLap.jerk) + " \n\t " +

                    "Lap_Avg: Avg_Time~" + (total.times.Any() ? total.times.Average().ToString("F0") : "NONE") +
                    ", Min_Time~" + total.minTime +
                    ", Max_Time~" + total.maxTime +
                    ", Avg_Speed~" + SafeAverage(total.speeds) +
                    ", Avg_Min_Speed~" + SafeAverage(total.minSpeeds) +
                    ", Avg_Max_Speed~" + SafeAverage(total.maxSpeeds) +
                    ", Avg_Dev_Speed~" + SafeAverage(total.devSpeed) +
                    ", Avg_Distance_To_Center~" + SafeAverage(total.distanceToCenter) +
                    ", Avg_Acceleration~" + SafeAverage(total.acceleration) +
                    ", Avg_Jerk~" + SafeAverage(total.jerk) +

                    "\n\n");
                StartCoroutine(IndicatorTimeout());
            }
            if (!recordingRunning && Input.GetKeyDown(KeyCode.RightShift))
            {
                recordingRunning = true;
                scoreRecordingIndicator.SetActive(recordingRunning);
                this.GetComponent<VehicleRecorder>().StartRecording(scoreFolder, filename);
                this.GetComponent<VehiclePlayback>().TogglePlaybackAllowance(false);
                File.AppendAllText(filePath,
                    "AUTOMATIC | " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | ========== RECORDING STARTED ==========" + "\n\n");
            }
            else if (Input.GetKeyDown(KeyCode.RightShift))
            {
                recordingRunning = false;
                scoreRecordingIndicator.SetActive(recordingRunning);
                this.GetComponent<VehicleRecorder>().StopRecording();
                this.GetComponent<VehiclePlayback>().TogglePlaybackAllowance(true);
                File.AppendAllText(filePath,
                    "AUTOMATIC | " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | ========== RECORDING STOPPED ==========" + "\n\n");
            }
        }
    }

    void FixedUpdate()
    {
        if (recordingRunning)
        {
            SaveCurrentValuesToSegments();

            File.AppendAllText(filePath,
                "AUTOMATIC | " +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " +
                "DtC~" + currentDtC + ", Speed~" + currentSpeed + ", Acc~" + currentAcceleration + ", Jerk~" + currentJerk + " | " +
                "Seg: " + currentSegment.id + " \n\t " +

                "Seg_Curr: Time~" + currentSegment.time +
                ", Avg_Speed~" + SafeAverage(currentSegment.speeds) +
                ", Min_Speed~" + currentSegment.minSpeed +
                ", Max_Speed~" + currentSegment.maxSpeed +
                ", Dev_Speed~" + SafeAverage(currentSegment.devSpeed) +
                ", Avg_Distance_To_Center~" + SafeAverage(currentSegment.distanceToCenter) +
                ", Avg_Acceleration~" + SafeAverage(currentSegment.acceleration) +
                ", Avg_Jerk~" + SafeAverage(currentSegment.jerk) + " \n\t " +

                "Seg_Avg: Avg_Time~" + (segments[currentSegmentIndex].times.Any() ? segments[currentSegmentIndex].times.Average().ToString("F0") : "NONE") +
                ", Min_Time~" + segments[currentSegmentIndex].minTime +
                ", Max_Time~" + segments[currentSegmentIndex].maxTime +
                ", Avg_Speed~" + SafeAverage(segments[currentSegmentIndex].speeds) +
                ", Avg_Min_Speed~" + SafeAverage(segments[currentSegmentIndex].minSpeeds) +
                ", Avg_Max_Speed~" + SafeAverage(segments[currentSegmentIndex].maxSpeeds) +
                ", Avg_Dev_Speed~" + SafeAverage(segments[currentSegmentIndex].devSpeed) +
                ", Avg_Distance_To_Center~" + SafeAverage(segments[currentSegmentIndex].distanceToCenter) +
                ", Avg_Acceleration~" + SafeAverage(segments[currentSegmentIndex].acceleration) +
                ", Avg_Jerk~" + SafeAverage(segments[currentSegmentIndex].jerk) + " \n\t " +

                "Lap_Curr: Time~" + currentLap.time +
                ", Avg_Speed~" + SafeAverage(currentLap.speeds) +
                ", Min_Speed~" + currentLap.minSpeed +
                ", Max_Speed~" + currentLap.maxSpeed +
                ", Dev_Speed~" + SafeAverage(currentLap.devSpeed) +
                ", Avg_Distance_To_Center~" + SafeAverage(currentLap.distanceToCenter) +
                ", Avg_Acceleration~" + SafeAverage(currentLap.acceleration) +
                ", Avg_Jerk~" + SafeAverage(currentLap.jerk) + " \n\t " +

                "Lap_Avg: Avg_Time~" + (total.times.Any() ? total.times.Average().ToString("F0") : "NONE") +
                ", Min_Time~" + total.minTime +
                ", Max_Time~" + total.maxTime +
                ", Avg_Speed~" + SafeAverage(total.speeds) +
                ", Avg_Min_Speed~" + SafeAverage(total.minSpeeds) +
                ", Avg_Max_Speed~" + SafeAverage(total.maxSpeeds) +
                ", Avg_Dev_Speed~" + SafeAverage(total.devSpeed) +
                ", Avg_Distance_To_Center~" + SafeAverage(total.distanceToCenter) +
                ", Avg_Acceleration~" + SafeAverage(total.acceleration) +
                ", Avg_Jerk~" + SafeAverage(total.jerk) +

                "\n\n");
        }
    }
    private void OnDestroy()
    {
        if (shouldSaveScores)
        {
            File.AppendAllText(filePath, "SIM-FINAL | " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | SCENE STOPPED" + "\n\n");
            foreach (TrackSegment segment in segments)
            {
                File.AppendAllText(filePath,
                    "SIM FINAL | " +
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " +
                    "Seg: " + segment.id + " \n\t " +
                    "Seg_Avg: Avg_Time~" + (segment.times.Any() ? segment.times.Average().ToString("F0") : "NONE") +
                    ", Min_Time~" + segment.minTime +
                    ", Max_Time~" + segment.maxTime +
                    ", Avg_Speed~" + SafeAverage(segment.speeds) +
                    ", Avg_Min_Speed~" + SafeAverage(segment.minSpeeds) +
                    ", Avg_Max_Speed~" + SafeAverage(segment.maxSpeeds) +
                    ", Avg_Dev_Speed~" + SafeAverage(segment.devSpeed) +
                    ", Avg_Distance_To_Center~" + SafeAverage(segment.distanceToCenter) +
                    ", Avg_Acceleration~" + SafeAverage(segment.acceleration) +
                    ", Avg_Jerk~" + SafeAverage(segment.jerk) +
                    "\n\n");
            }
            File.AppendAllText(filePath,
                "SIM FINAL | " +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " \n\t " +
                    "Total: Time~" + total.time + 
                    ", Avg_Time~" + (total.times.Any() ? total.times.Average().ToString("F0") : "NONE") + 
                    ", Avg_Speed~" + SafeAverage(total.speeds) + 
                    ", Avg_Min_Speed~" + SafeAverage(total.minSpeeds) + 
                    ", Avg_Max_Speed~" + SafeAverage(total.maxSpeeds) + 
                    ", Avg_Dev_Speed~" + SafeAverage(total.devSpeed) + 
                    ", Avg_Distance_To_Center~" + SafeAverage(total.distanceToCenter) +
                    ", Avg_Acceleration~" + SafeAverage(total.acceleration) +
                    ", Avg_Jerk~" + SafeAverage(total.jerk) +
                    "\n\n");
        }
    }

    private string SafeAverage(List<float> list)
    {
        if (list?.Count > 0)
        {
            return list.Average().ToString("F8");
        }
        else
        {
            return "NONE";
        }
    }

    private IEnumerator IndicatorTimeout()
    {
        scoreRecordingIndicatorManual.SetActive(true);
        yield return new WaitForSecondsRealtime(3.0f);
        scoreRecordingIndicatorManual.SetActive(false);
    }

    private int lastSegmentID = 0;
    private float lastSpeed = 0.0f;
    private float lastAcceleration = 0.0f;
    private void SaveCurrentValuesToSegments()
    {
        currentSegmentID = steeringController.GetCurrentSegmentIndex();
        currentSpeed = carController.speed;
        currentAcceleration = carController.speed - lastSpeed;
        lastSpeed = currentSpeed;
        currentJerk = currentAcceleration - lastAcceleration;
        lastAcceleration = currentAcceleration;
        currentTime = DateTime.Now;

        // Temporary Segment
        if (currentSegment.id != currentSegmentID)
        {
            currentSegment = new TrackSegment(currentSegmentID);
        }

        if (lastSegmentID != currentSegmentID)
        {
            currentSegment.timeEntered = currentTime;
            if (currentSegmentID == 0) {
                roadSegment = GetComponent<SteeringController>().roadSegments.Last().gameObject;
            }
            else
            {
                roadSegment = GetComponent<SteeringController>().roadSegments[currentSegmentID - 1].gameObject;
            }
        }

        currentDtC = CalculateDistanceToCenter();

        currentSegment.speeds.Add(currentSpeed);
        currentSegment.minSpeed = currentSpeed < currentSegment.minSpeed || currentSegment.minSpeed == 0 ? currentSpeed : currentSegment.minSpeed;
        currentSegment.maxSpeed = currentSpeed > currentSegment.maxSpeed ? currentSpeed : currentSegment.maxSpeed;
        currentSegment.devSpeed.Add(currentSpeed - currentSegment.speeds.Average());
        currentSegment.time = DateTime.Now - currentSegment.timeEntered;
        currentSegment.distanceToCenter.Add(currentDtC);
        currentSegment.acceleration.Add(currentAcceleration);
        currentSegment.jerk.Add(currentJerk);

        // Segment List
        if (!segments.Any(s => s.id == currentSegmentID))
        {
            segments.Add(new TrackSegment(currentSegmentID));
        }

        currentSegmentIndex = segments.FindIndex(s => s.id == currentSegmentID);
        int lastSegmentIndex = segments.FindIndex(s => s.id == lastSegmentID);
        TrackSegment currentSegmentFromList = segments[currentSegmentIndex];
        TrackSegment lastSegmentFromList = lastSegmentIndex < 0 ? new TrackSegment(0) : segments[lastSegmentIndex];

        if (lastSegmentID != currentSegmentID)
        {
            lastSegmentFromList.minTime = lastSegmentFromList.time.Milliseconds < lastSegmentFromList.minTime || lastSegmentFromList.minTime == 0 ? lastSegmentFromList.time.Milliseconds : lastSegmentFromList.minTime;
            lastSegmentFromList.maxTime = lastSegmentFromList.time.Milliseconds > lastSegmentFromList.maxTime ? lastSegmentFromList.time.Milliseconds : lastSegmentFromList.maxTime;
            lastSegmentFromList.times.Add(lastSegmentFromList.time.Milliseconds);
            lastSegmentFromList.minSpeeds.Add(lastSegmentFromList.minSpeed);
            lastSegmentFromList.maxSpeeds.Add(lastSegmentFromList.maxSpeed);
            if (lastSegmentIndex >= 0)
            {
                segments[lastSegmentIndex] = lastSegmentFromList;
            }
            currentSegmentFromList.timeEntered = currentTime;
        }

        currentSegmentFromList.speeds.Add(currentSpeed);
        currentSegmentFromList.minSpeed = currentSpeed < currentSegment.minSpeed || currentSegment.minSpeed == 0.0f ? currentSpeed : currentSegment.minSpeed;
        currentSegmentFromList.maxSpeed = currentSpeed > currentSegment.maxSpeed ? currentSpeed : currentSegment.maxSpeed;
        currentSegmentFromList.devSpeed.Add(currentSpeed - currentSegment.speeds.Average());
        currentSegmentFromList.time = DateTime.Now - currentSegment.timeEntered;
        currentSegmentFromList.distanceToCenter.Add(currentDtC);
        currentSegmentFromList.acceleration.Add(currentAcceleration);
        currentSegmentFromList.jerk.Add(currentJerk);
        segments[currentSegmentIndex] = currentSegmentFromList;

        // Current Lap
        bool lapChanged = currentSegmentID == 0 && currentSegmentID < lastSegmentID;
        if (lapChanged)
        {
            currentLap = new TrackSegment(-1);
        }

        if (lastSegmentID != currentSegmentID)
        {
            lastSegmentID = currentSegmentID;
        }

        currentLap.speeds.Add(currentSpeed);
        currentLap.minSpeed = currentSpeed < currentLap.minSpeed || currentLap.minSpeed == 0 ? currentSpeed : currentLap.minSpeed;
        currentLap.maxSpeed = currentSpeed > currentSegment.maxSpeed ? currentSpeed : currentSegment.maxSpeed;
        currentLap.devSpeed.Add(currentSpeed - currentLap.speeds.Average());
        currentLap.time = DateTime.Now - currentLap.timeEntered;
        currentLap.distanceToCenter.Add(currentDtC);
        currentLap.acceleration.Add(currentAcceleration);
        currentLap.jerk.Add(currentJerk);

        // Total
        total.time = DateTime.Now - total.timeEntered;
        total.speeds.Add(currentSpeed);
        total.devSpeed.Add(currentSpeed - total.speeds.Average());
        total.distanceToCenter.Add(currentDtC);
        total.acceleration.Add(currentAcceleration);
        total.jerk.Add(currentJerk);
        total.minSpeed = currentSpeed < total.minSpeed || total.minSpeed == 0.0f ? currentSpeed : total.minSpeed;
        total.maxSpeed = currentSpeed > total.maxSpeed ? currentSpeed : total.maxSpeed;
        if (lapChanged)
        {
            total.times.Add(total.time.Milliseconds);
            total.minTime = total.time.Milliseconds < total.minTime || total.minTime == 0 ? total.time.Milliseconds : total.minTime;
            total.maxTime = total.time.Milliseconds > total.maxTime ? total.time.Milliseconds : total.maxTime;
            total.minSpeeds.Add(total.minSpeed);
            total.minSpeed = 0.0f;
            total.maxSpeeds.Add(total.maxSpeed);
            total.maxSpeed = 0.0f;
        }
    }


    private float lastDtc = 0.0f;
    [SerializeField] private TextMeshProUGUI dtcText;
    private float CalculateDistanceToCenter()
    {
        if (!roadSegment)
        {
            return lastDtc;
        }

        float dtc = 0.0f;
        int nearestPair = 0;
        List<float> pairDistances = new List<float>();
        Vector3 vehiclePos = carController.transform.position;

        GameObject nextReadSegment = roadSegment.transform.GetSiblingIndex() + 1 < roadSegment.transform.parent.childCount - 1 ? roadSegment.transform.parent.GetChild(roadSegment.transform.GetSiblingIndex() + 1).gameObject : roadSegment.transform.parent.GetChild(0).gameObject;
        for (int pairIndex = 1; pairIndex <= 10; pairIndex++)
        {
            Vector3 leftPairPos = roadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "L").transform.position;
            Vector3 rightPairPos = roadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "R").transform.position;

            Debug.DrawLine(vehiclePos, leftPairPos, Color.white);
            Debug.DrawLine(vehiclePos, rightPairPos, Color.white);

            pairDistances.Add(Vector2.Distance(new Vector2(leftPairPos.x, leftPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)) + Vector2.Distance(new Vector2(rightPairPos.x, rightPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)));
        }
        for (int pairIndex = 1; pairIndex <= 10; pairIndex++)
        {
            Vector3 leftNextPairPos = nextReadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "L").transform.position;
            Vector3 rightNextPairPos = nextReadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "R").transform.position;

            Debug.DrawLine(vehiclePos, leftNextPairPos, Color.blue);
            Debug.DrawLine(vehiclePos, rightNextPairPos, Color.blue);

            pairDistances.Add(Vector2.Distance(new Vector2(leftNextPairPos.x, leftNextPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)) + Vector2.Distance(new Vector2(rightNextPairPos.x, rightNextPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)));
        }

        nearestPair = pairDistances.IndexOf(pairDistances.Min());

        Vector3 pairLPos; Vector3 pairRPos;
        if (nearestPair >= 9)
        {
            nearestPair -= 9;
            pairLPos = nextReadSegment.transform.Find("DtC-Tracker").Find("P" + (nearestPair + 1) + "L").transform.position;
            pairRPos = nextReadSegment.transform.Find("DtC-Tracker").Find("P" + (nearestPair + 1) + "R").transform.position;
        }
        else
        {
            pairLPos = roadSegment.transform.Find("DtC-Tracker").Find("P" + (nearestPair + 1) + "L").transform.position;
            pairRPos = roadSegment.transform.Find("DtC-Tracker").Find("P" + (nearestPair + 1) + "R").transform.position;
        }

        Debug.DrawLine(vehiclePos, pairLPos, Color.red);
        Debug.DrawLine(vehiclePos, pairRPos, Color.red);
        float pairDist = Vector3.Distance(pairRPos, pairLPos);
        float distPercentage = pairDistances.Min() / pairDist;

        float pairLDist = Vector2.Distance(new Vector2(vehiclePos.x, vehiclePos.z), new Vector2(pairLPos.x, pairLPos.z));
        float pairRDist = Vector2.Distance(new Vector2(vehiclePos.x, vehiclePos.z), new Vector2(pairRPos.x, pairRPos.z));

        dtc = (pairLDist - pairRDist) / 2 * distPercentage;
        lastDtc = dtc;
        if (lastDtc == 0.0f)
        {
            dtcText.text = "X 0.0 X";
        }
        else if (lastDtc < 0.0f) {
            dtcText.text = "|·| < " + lastDtc.ToString("0.0000") + "m";
        }
        else
        {
            dtcText.text = lastDtc.ToString("0.0000") + "m > |·|";
        }

        return dtc;
    }
}
