using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class DriveYourselfAgent : Agent
{
    private RCC_CarControllerV4 carController;

    private float episodeProgressReward;
    private float episodeSpeedReward;
    private float episodeSpeedDeviation;
    private float episodeDtCReward;
    private float episodeDtCDeviation;

    [SerializeField] private int lookAheadSegments;

    [SerializeField] private TextMeshProUGUI agentAccText;
    [SerializeField] private TextMeshProUGUI agentBrkText;
    [SerializeField] private TextMeshProUGUI agentStrText;
    [SerializeField] private TextMeshProUGUI agentSpdText;
    [SerializeField] private TextMeshProUGUI agentRpmText;
    [SerializeField] private TextMeshProUGUI agentDtCText;

    [Header("Rewards")]
    [SerializeField, Min(0f)] public float targetSpeed;
    [SerializeField, Range(0.0f,100.0f)] private float speedRewardPercent;
    //[SerializeField, Min(0f)] private float maxAllowedSafeAcc;
    //[SerializeField, Min(0f)] private float maxAllowedRewardAcc;
    //[SerializeField, Range(0.0f,100.0f)] private float accRewardPercent;
    //[SerializeField, Min(0f)] private float maxAllowedSafeJerk;
    //[SerializeField, Min(0f)] private float maxAllowedRewardJerk;
    //[SerializeField, Range(0.0f,100.0f)] private float jerkRewardPercent;
    [SerializeField, Min(0f)] private float maxAllowedRewardDtc;
    [SerializeField, Range(0.0f,100.0f)] public float DtCRewardPercent;

    [Header("EndEpisodeConditions")]
    [SerializeField, Min(1)] private int endEpisodeAfterCompletedLaps = 1;
    [SerializeField] private int endEpisodeCarYPosition = -2;
    [SerializeField] private int endEpisodeCarStuckSeconds = 15;

    private GetVehicleData vehicleData;
    private Rigidbody carRb;
    private Vector3 startingPosition;
    private Vector3 startingPositionForEpisode;
    private Quaternion startingRotation;

    private float lastLapProgress = 0.0f;
    private int lastLap = 0;

    public bool startedFirstLap = false;

    [SerializeField, Min(0.0f)] private float startingPositionSidewaysOffset;
    [SerializeField, Range(0.0f, 180.0f)] private float startingRotationForEpisode;
    [SerializeField, Min(0.0f)] private float startingMaximumForwardSpeed;
    [SerializeField, Min(0.0f)] private float startingMaximumSidewaysSpeed;
    [SerializeField] private StartingAxis startingAxis;
    private enum StartingAxis
    {
        X,
        Z
    }

    void Start()
    {
        carController = this.transform.parent.GetComponent<RCC_CarControllerV4>();

        startingPosition = startingPositionForEpisode = carController.transform.position;
        startingRotation = carController.transform.rotation;
        carRb = carController.GetComponent<Rigidbody>();
        vehicleData = this.GetComponent<GetVehicleData>();
        carController.canGoReverseNow = false;
    }


    long fixedUpdateCounter = 0L;
    double ingameSecondsSinceStartup = 0.0d;
    private double timeAtLastSignificantMove = 0.0d;
    void FixedUpdate()
    {
        fixedUpdateCounter++;
        ingameSecondsSinceStartup = fixedUpdateCounter * Time.fixedDeltaTime;
    }

    public override void OnEpisodeBegin()
    {
        if (!this.GetComponent<AgentSelector>().enabled)
        {
            targetSpeed = Academy.Instance.EnvironmentParameters.GetWithDefault("target_speed", 50.0f);
            DtCRewardPercent = Academy.Instance.EnvironmentParameters.GetWithDefault("dtc_weight", 0.33f) * 100f;
        }

        speedRewardPercent = 100f - DtCRewardPercent;

        // Debug.Log($"[Agent Setup] Name: {transform.name} | Target Speed: {targetSpeed} | DtC %: {DtCRewardPercent}");

        episodeProgressReward = 0.0f;
        episodeSpeedReward = 0.0f;
        episodeSpeedDeviation = 0.0f;
        episodeDtCReward = 0.0f;
        episodeDtCDeviation = 0.0f;

        ForceDisableAllParticles();

        if (!carController)
        {
            return;
        }

        //this.transform.parent.Find("All Audio Sources").gameObject.SetActive(false);
        this.transform.parent.Find("All Contact Particles").gameObject.SetActive(false);

        startingPositionForEpisode = startingPosition;
        switch (startingAxis)
        {
            case StartingAxis.X:
                startingPositionForEpisode += new Vector3(0.0f, 0.0f, UnityEngine.Random.Range(-startingPositionSidewaysOffset, startingPositionSidewaysOffset));
                break;
            case StartingAxis.Z:
                startingPositionForEpisode += new Vector3(UnityEngine.Random.Range(-startingPositionSidewaysOffset, startingPositionSidewaysOffset), 0.0f, 0.0f);
                break;
        }
        carController.transform.SetPositionAndRotation(startingPositionForEpisode, startingRotation);
        carController.transform.Rotate(new Vector3(0.0f, UnityEngine.Random.Range(-startingRotationForEpisode, startingRotationForEpisode), 0.0f));

        //carRb.angularVelocity = Vector3.zero;
        carRb.linearVelocity = (carController.transform.forward * UnityEngine.Random.Range(0f, startingMaximumForwardSpeed / 3.6f)) + (carController.transform.right * UnityEngine.Random.Range(-startingMaximumSidewaysSpeed /3.6f, startingMaximumSidewaysSpeed / 3.6f));
        carController.externalController = true;
        carController.GetComponent<RCC_LogitechSteeringWheel>().overrideFFB = true;
        vehicleData.ResetVars();
        carController.canGoReverseNow = false;
        carController.currentGear = 1;
        //carController.GetComponent<Rigidbody>().isKinematic = true;
        //carController.engineRunning = false;
        //carController.engineRPMRaw = 0;

        lastLap = 0;
        lastLapProgress = 0.0f;

        startedFirstLap = false;

        fixedUpdateCounter = 0L;
        timeAtLastSignificantMove = 0.0d;

        //StartCoroutine(UnfreezeMovement());
    }

    private IEnumerator UnfreezeMovement()
    {
        while (ingameSecondsSinceStartup < 0.5)
        {
            yield return new WaitForFixedUpdate();
        }
        carController.GetComponent<Rigidbody>().isKinematic = false;
        carController.engineRunning = true;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var actions = actionsOut.ContinuousActions;
        actions[0] = Input.GetKey(KeyCode.UpArrow) ? 1 : Input.GetKey(KeyCode.DownArrow) ? -1 : 0;
        actions[1] = Input.GetKey(KeyCode.RightArrow) ? 1 : Input.GetKey(KeyCode.LeftArrow) ? -1 : 0;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (!vehicleData)
        {
            return;
        }

        sensor.AddObservation(targetSpeed / 150f); // 150 as the maximum Speed
        sensor.AddObservation(DtCRewardPercent / 100f);

        sensor.AddObservation(vehicleData.GetSpeed());
        sensor.AddObservation(vehicleData.GetAccelleration());
        //sensor.AddObservation(vehicleData.GetJerk());
        sensor.AddObservation(vehicleData.GetDtC());

        GameObject currentSegment = vehicleData.GetRoadSegment();
        switch (currentSegment.name.Split("_")[1])
        {
            case "left":
                sensor.AddObservation(-1);
                break;
            case "right":
                sensor.AddObservation(1);
                break;
            default:
                sensor.AddObservation(0);
                break;
        }

        List<float> nextRoadSegments = new List<float>();  
        for (int segment = 0; segment < lookAheadSegments; segment++)
        {
            currentSegment = vehicleData.GetNextRoadSegment(currentSegment);
            Vector3 relativePos = carController.transform.InverseTransformPoint(currentSegment.transform.position);

            sensor.AddObservation(relativePos.x / 100.0f);
            sensor.AddObservation(relativePos.z / 100.0f);
#if UNITY_EDITOR
            Debug.DrawLine(carController.transform.position, currentSegment.transform.position, Color.yellow);
#endif
        }
        sensor.AddObservation(nextRoadSegments);

        Transform nextRoadSegment = vehicleData.GetNextRoadSegment(vehicleData.GetRoadSegment()).transform;
        Vector3 toNextRoadSegment = (nextRoadSegment.position - carController.transform.position).normalized;
        float angleToNextRoadSegment = Vector3.SignedAngle(carController.transform.forward, toNextRoadSegment, Vector3.up);

        sensor.AddObservation((carController.transform.position - nextRoadSegment.position).normalized);
        sensor.AddObservation(angleToNextRoadSegment);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        //Debug.Log($"{GetHashCode()}: actions.ContinuousActions[0]: {actions.ContinuousActions[0]}; actions.ContinuousActions[1]: {actions.ContinuousActions[1]}; rpm: {carController.engineRPM}");

        if (!carController)
        {
            return;
        }

        float updateDiff = Time.fixedDeltaTime;
        float currentAccPerSecond = vehicleData.GetAccelleration() / updateDiff;
        //float currentAccOffset = Mathf.Abs(currentAccPerSecond) - maxAllowedSafeAcc;

        float acc = 0.0f;
        float brk = 0.0f;

        if (actions.ContinuousActions[0] >= 0)
        {
            acc = actions.ContinuousActions[0];
        }
        else
        {
            brk = Mathf.Abs(actions.ContinuousActions[0]);
        }

        // Time penalty
        AddReward(-0.005f);

        // Engine Inertia
        if (carController.engineRPM > 800.0f * 2.5f)
        {
            AddReward(0.006f);
        }

        // Move
        carController.throttleInput = acc;
        carController.brakeInput = brk;
        carController.steerInput = actions.ContinuousActions[1];


        // Input Text
        agentAccText.text = "Acc: " + acc.ToString("F4");
        agentBrkText.text = "Brk: " + brk.ToString("F4");
        agentStrText.text = "Str: " + actions.ContinuousActions[1].ToString("F4");
        agentSpdText.text = "Spd: " + carController.speed.ToString("F1") + " km/h";
        agentRpmText.text = "RPM: " + carController.engineRPM.ToString("F0") + " - G: " + carController.currentGear.ToString();
        agentDtCText.text = "DtC: " + vehicleData.ReturnLastDtC() + " m";

        // Rewards
        //Debug.Log("AGENT State: " + lastLap + ", Progress: " + lastLapProgress + "%");
        float currentProgress = vehicleData.GetProgress();
        float deltaProgress = Mathf.Max(0.0f, currentProgress - lastLapProgress);
        if (startedFirstLap && (deltaProgress > 0.0f || (vehicleData.GetLap() > lastLap && currentProgress < 50.0f)))
        {
            if (currentProgress < 50.0f)
            {
                lastLap = vehicleData.GetLap();
            }
            lastLapProgress = currentProgress;

            if (lastLap > endEpisodeAfterCompletedLaps)
            {
                InjectStats();
                EndEpisode();
            }
            else if (lastLap > 0)
            {
                //Debug.Log("AGENT Progress: " + lastLap + ", Progress: " + lastLapProgress + "%");
                timeAtLastSignificantMove = ingameSecondsSinceStartup;

                float currentSpeed = vehicleData.GetSpeed();
                float currentDtC = Mathf.Abs(vehicleData.ReturnLastDtC());
                float normalization = 150.0f / Mathf.Max(targetSpeed, 10.0f);
                float baseReward = deltaProgress * normalization;
                float speedMultiplier = 0.0f;

                // Speed
                if (currentSpeed > targetSpeed && currentSpeed > 0.1f)
                {
                    speedMultiplier = targetSpeed / currentSpeed;
                }
                else
                {
                    float ratio = currentSpeed / targetSpeed;
                    speedMultiplier = (ratio * ratio);
                    if (ratio > 0.95f) speedMultiplier *= 1.2f;
                }

                // Acceleration
                /*
                if (currentAccOffset <= 0)
                {
                    AddReward(1.0f * (accRewardPercent / 100.0f));
                }
                else
                {
                    float accRewardDegradeFactor = Mathf.InverseLerp(0.0f, maxAllowedRewardAcc, currentAccOffset);
                    float accReward = (1.0f - accRewardDegradeFactor) * (accRewardPercent / 100.0f);
                    if (!float.IsNaN(accReward))
                    {
                        AddReward(accReward);
                    }
                }
                */

                // Jerk
                /*
                float currentJerkPerSecond = vehicleData.GetJerk() / updateDiff;
                float currentJerkOffset = Mathf.Abs(currentJerkPerSecond) - maxAllowedSafeJerk;
                if (currentJerkOffset <= 0)
                {
                    AddReward(1.0f * (jerkRewardPercent / 100.0f));
                }
                else
                {
                    float jerkRewardDegradeFactor = Mathf.InverseLerp(0.0f, maxAllowedRewardJerk, currentJerkOffset);
                    float jerkReward = (1.0f - jerkRewardDegradeFactor) * (accRewardPercent / 100.0f);
                    if (!float.IsNaN(jerkReward))
                    {
                        AddReward(jerkReward);
                    }
                }
                */

                // Distance to Center
                float safeDist = Mathf.Max(maxAllowedRewardDtc, 1.0f);
                float dtcSigma = safeDist / 3.5f;
                float bellCurveDtC = Mathf.Exp(-(currentDtC * currentDtC) / (2 * dtcSigma * dtcSigma));

                float strictness = DtCRewardPercent / 100.0f;
                float safetyMultiplier = Mathf.Lerp(1.0f, bellCurveDtC, strictness);
                //Debug.Log("Reward DtC: " + DtCReward);


                // Final Reward
                float finalReward = baseReward * speedMultiplier * safetyMultiplier;
                AddReward(finalReward);

                episodeProgressReward += finalReward;
                episodeSpeedReward += speedMultiplier;
                episodeDtCReward += safetyMultiplier;
                episodeSpeedDeviation += Mathf.Abs(targetSpeed - currentSpeed);
                episodeDtCDeviation += currentDtC;
            }
        }
        else if (ingameSecondsSinceStartup - timeAtLastSignificantMove > endEpisodeCarStuckSeconds)
        {
            AddReward(-1.0f);
            //Debug.LogWarning($"Episode end: Car stuck (or agent didn't move)!");
            InjectStats();
            EndEpisode();
            return;
        }

        if (carController.transform.position.y < endEpisodeCarYPosition)
        {
            AddReward(-1.0f);
            //Debug.LogWarning("Episode end: Car out of Map!");
            InjectStats();
            EndEpisode();
            return;
        }
    }

    private void InjectStats()
    {
        if (float.IsNaN(episodeSpeedDeviation) || float.IsNaN(episodeDtCDeviation))
        {
            return;
        }

        long stepCount = fixedUpdateCounter > 0 ? fixedUpdateCounter : 1;
        float lapsCompleted = (vehicleData.GetLap() - 1) + (vehicleData.GetProgress() / 100f);
        //Debug.LogWarning("Completed Laps: " + lapsCompleted);
        if (!startedFirstLap)
        {
            lapsCompleted = 0;
        }

        var stats = Academy.Instance.StatsRecorder;
        //Debug.Log("Total Reward: " + episodeProgressReward);

        stats.Add("Custom/Laps Completed", lapsCompleted, StatAggregationMethod.Average);
        stats.Add("Custom/Total Progress Reward", episodeProgressReward, StatAggregationMethod.Average);
        stats.Add("Custom/Total Speed Reward", episodeSpeedReward, StatAggregationMethod.Average);
        stats.Add("Custom/Total DtC Reward", episodeDtCReward, StatAggregationMethod.Average);
        stats.Add("Custom/Avg Speed Deviation", episodeSpeedDeviation / stepCount, StatAggregationMethod.Average);
        stats.Add("Custom/Avg DtC Deviation", episodeDtCDeviation / stepCount, StatAggregationMethod.Average);
    }

    private void ForceDisableAllParticles()
    {
        ParticleSystem[] allParticles = carController.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particleSystem in allParticles)
        {
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystem.gameObject.SetActive(false);
        }

        foreach (var wheel in carController.AllWheelColliders)
        {
            ParticleSystem[] wheelParticles = wheel.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem wheelPs in wheelParticles)
            {
                wheelPs.gameObject.SetActive(false);
            }
        }
    }
}
