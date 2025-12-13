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

    [Header("Rewards")]
    [SerializeField, Min(0f)] private float targetSpeed;
    [SerializeField, Range(0.0f,100.0f)] private float speedRewardPercent;
    [SerializeField, Min(0f)] private float maxAllowedSafeAcc;
    [SerializeField, Min(0f)] private float maxAllowedRewardAcc;
    [SerializeField, Range(0.0f,100.0f)] private float accRewardPercent;
    //[SerializeField, Min(0f)] private float maxAllowedSafeJerk;
    //[SerializeField, Min(0f)] private float maxAllowedRewardJerk;
    //[SerializeField, Range(0.0f,100.0f)] private float jerkRewardPercent;
    [SerializeField, Min(0f)] private float maxAllowedRewardDtc;
    [SerializeField, Range(0.0f,100.0f)] private float DtCRewardPercent;

    [Header("EndEpisodeConditions")]
    [SerializeField] private int endEpisodeCarYPosition;
    [SerializeField] private int endEpisodeCarStuckSeconds = 15;
    [SerializeField] private float startingThreshold = 1.0f;

    private GetVehicleData vehicleData;
    private Rigidbody carRb;
    private Vector3 startingPosition;
    private Quaternion startingRotation;

    private float lastLapProgress = 0.0f;
    private int lastLap = 0;
    private bool movedFromInit = false;

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

        startingPosition = carController.transform.position;
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

        Vector3 startingPositionForEpisode = startingPosition;
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

        movedFromInit = false;
        lastLap = 0;
        lastLapProgress = 0.0f;

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
            switch (currentSegment.name.Split("_")[1])
            {
                case "left":
                    nextRoadSegments.Add(-1);
                    break;
                case "right":
                    nextRoadSegments.Add(1);
                    break;
                default:
                    nextRoadSegments.Add(0);
                    break;
            }
            currentSegment = vehicleData.GetNextRoadSegment(currentSegment);
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
        float currentAccOffset = Mathf.Abs(currentAccPerSecond) - maxAllowedSafeAcc;

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

        // Engine Inertia
        if (carController.engineRPM > 800.0f * 2.5f)
        {
            AddReward(0.001f);
        }

        // Move
        carController.throttleInput = acc;
        carController.brakeInput = brk;
        carController.steerInput = actions.ContinuousActions[1];


        // Input Text
        agentAccText.text = "Acc: " + acc.ToString("F4");
        agentBrkText.text = "Brk: " + brk.ToString("F4");
        agentStrText.text = "Str: " + actions.ContinuousActions[1].ToString("F4");

        // Rewards
        //Debug.Log("AGENT State: " + lastLap + ", Progress: " + lastLapProgress + "%");
        if (!movedFromInit && Vector3.Distance(startingPosition, carController.transform.position) > startingThreshold)
        {
            movedFromInit = true;
        }

        float deltaProgress = vehicleData.GetProgress() - lastLapProgress;
        if (movedFromInit && (deltaProgress > 0.0f || vehicleData.GetLap() > lastLap))
        {
            if (vehicleData.GetProgress() < 50.0f)
            {
                lastLap = vehicleData.GetLap();
            }
            lastLapProgress = vehicleData.GetProgress() < 99.0f ? vehicleData.GetProgress() : 0.0f;

            if (lastLap > 0)
            {
                //Debug.Log("AGENT Progress: " + lastLap + ", Progress: " + lastLapProgress + "%");
                timeAtLastSignificantMove = ingameSecondsSinceStartup;

                // Progress
                AddReward(deltaProgress);
                episodeProgressReward += deltaProgress;
                //Debug.Log("Reward Progress: " + deltaProgress);

                // Speed
                float maxRewardSpeed = targetSpeed * 2.0f;
                float currentSpeedFactor = Mathf.InverseLerp(0.0f, maxRewardSpeed, vehicleData.GetSpeed()) * 2.0f;
                float currentSpeedOffset = 1.0f - Mathf.Abs(currentSpeedFactor - 1);
                if (carController.currentGear != -1)
                {
                    float speedReward = currentSpeedOffset * (speedRewardPercent / 100.0f);
                    AddReward(speedReward);
                    episodeSpeedReward += speedReward;
                    //Debug.Log("Reward Speed: " + (speedReward));
                }
                episodeSpeedDeviation += Mathf.Abs(targetSpeed - vehicleData.GetSpeed());

                // Acceleration
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
                float DtCOffsetFactor = Mathf.Max(0.0f, (maxAllowedRewardDtc - Mathf.Abs(vehicleData.GetDtC()))) / maxAllowedRewardDtc;
                float DtCReward = DtCOffsetFactor * (DtCRewardPercent / 100.0f);
                AddReward(DtCReward);
                episodeDtCReward += DtCReward;
                episodeDtCDeviation += Mathf.Abs(vehicleData.GetDtC());
                //Debug.Log("Reward DtC: " + DtCReward);
            }
        }
        else if (ingameSecondsSinceStartup - timeAtLastSignificantMove > endEpisodeCarStuckSeconds)
        {
            float stuckPunishment = -10.0f + Mathf.Clamp(Vector3.Distance(carController.transform.position, startingPosition), 0.0f, startingThreshold) * 10.0f / startingThreshold;
            AddReward(stuckPunishment);
            //Debug.LogWarning($"Episode end: Car stuck (or agent didn't move)! Moved for: " + Vector3.Distance(carController.transform.position, startingPosition) + "m");
            InjectStats();
            EndEpisode();
        }

        if (carController.transform.position.y < endEpisodeCarYPosition)
        {
            AddReward(-50.0f);
            //Debug.LogWarning("Episode end: Car out of Map!");
            InjectStats();
            EndEpisode();
        }
    }

    private void InjectStats()
    {
        long stepCount = fixedUpdateCounter > 0 ? fixedUpdateCounter : 1;
        float lapsCompleted = (vehicleData.GetLap() - 1) + (vehicleData.GetProgress() / 100f);
        var stats = Academy.Instance.StatsRecorder;

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
