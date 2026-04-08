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
    private float episodeMaxAcceleration;
    private float episodeSmoothnessPenalty;
    private float episodeDtCReward;
    private float episodeDtCDeviation;
    private float effectiveRatio = 0.0f;
    private float lastThrottleInput = 0.0f;
    private float lastBrakeInput = 0.0f;
    private float lastSteeringAction = 0.0f;

    private float smoothnessProgression = 1.0f;
    private float dtcProgression = 1.0f;

    [SerializeField] private int lookAheadSegments;

    [SerializeField] private TextMeshProUGUI agentAccText;
    [SerializeField] private TextMeshProUGUI agentBrkText;
    [SerializeField] private TextMeshProUGUI agentStrText;
    [SerializeField] private TextMeshProUGUI agentSpdText;
    [SerializeField] private TextMeshProUGUI agentRpmText;
    [SerializeField] private TextMeshProUGUI agentDtCText;

    [Header("Rewards")]
    [SerializeField, Range(20.0f, 100.0f)] public float targetSpeed;
    //[SerializeField, Min(0f)] private float maxAllowedSafeAcc;
    //[SerializeField, Min(0f)] private float maxAllowedRewardAcc;
    //[SerializeField, Range(0.0f,100.0f)] private float accRewardPercent;
    //[SerializeField, Min(0f)] private float maxAllowedSafeJerk;
    //[SerializeField, Min(0f)] private float maxAllowedRewardJerk;
    //[SerializeField, Range(0.0f,100.0f)] private float jerkRewardPercent;
    [SerializeField, Min(0f)] private float maxAllowedRewardDtc;
    [SerializeField, Range(0.0f, 100.0f)] public float DtCRewardPercent;
    [SerializeField, Range(5.0f, 20.0f)] public float accelTime0to100 = 10.0f;
    [SerializeField, Range(0.0f, 2.0f)] public float inputSmoothnessThreshold = 0.5f;

    [Header("Speeds")]
    [SerializeField] private float minCurriculumTrainingSpeed = 40.0f;
    [SerializeField] private float steeringSpeed = 0.75f; // time from full lock to center
    private float currentSteeringAngle = 0.0f;

    [Header("EndEpisodeConditions")]
    [SerializeField, Min(1)] private int endEpisodeAfterCompletedLaps = 1;
    [SerializeField] private int endEpisodeCarYPosition = -2;
    [SerializeField] public int endEpisodeCarStuckSeconds = 15;

    private GetVehicleData vehicleData;
    private Rigidbody carRb;
    private Vector3 startingPosition;
    private Vector3 startingPositionForEpisode;
    private Quaternion startingRotation;

    private float lastLapProgress = 0.0f;
    private int lastLap = 0;

    [HideInInspector] public bool startedFirstLap = false;

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
        float difficultyRatio = 1.0f;

        if (!this.GetComponent<AgentSelector>().enabled)
        {
            float rawTargetSpeed = Academy.Instance.EnvironmentParameters.GetWithDefault("target_speed", 50.0f);
            difficultyRatio = Academy.Instance.EnvironmentParameters.GetWithDefault("difficulty_ratio", 1.0f);
            DtCRewardPercent = Academy.Instance.EnvironmentParameters.GetWithDefault("dtc_weight", 0.33f) * 100f;
            accelTime0to100 = Academy.Instance.EnvironmentParameters.GetWithDefault("acc_time", 10.0f);
            inputSmoothnessThreshold = Academy.Instance.EnvironmentParameters.GetWithDefault("smooth_threshold", 0.5f);
            float minRatio = minCurriculumTrainingSpeed / Mathf.Max(rawTargetSpeed, 1.0f);
            effectiveRatio = Mathf.Clamp(Mathf.Max(difficultyRatio, minRatio), 0.0f, 1.0f);
            targetSpeed = Mathf.Max(rawTargetSpeed * effectiveRatio, 20.0f);
            smoothnessProgression = Academy.Instance.EnvironmentParameters.GetWithDefault("smoothness_progression", 1.0f);
            dtcProgression = Academy.Instance.EnvironmentParameters.GetWithDefault("dtc_progression", 1.0f);
        }

        lastThrottleInput = 0.0f;
        lastBrakeInput = 0.0f;
        lastSteeringAction = 0.0f;

        // Debug.Log($"[Agent Setup] Name: {transform.name} | Target Speed: {targetSpeed} | DtC %: {DtCRewardPercent}");

        episodeProgressReward = 0.0f;
        episodeSpeedReward = 0.0f;
        episodeSpeedDeviation = 0.0f;
        episodeDtCReward = 0.0f;
        episodeDtCDeviation = 0.0f;
        episodeMaxAcceleration = 0.0f;
        episodeSmoothnessPenalty = 0.0f;

        ForceDisableAllParticles();

        if (!carController)
        {
            return;
        }

        //this.transform.parent.Find("All Audio Sources").gameObject.SetActive(false);
        this.transform.parent.Find("All Contact Particles").gameObject.SetActive(false);

        if (!this.GetComponent<AgentSelector>().boActive || this.GetComponent<AgentSelector>().boStartCommandGiven)
        {
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
            carRb.linearVelocity = (carController.transform.forward * UnityEngine.Random.Range(0f, startingMaximumForwardSpeed / 3.6f)) + (carController.transform.right * UnityEngine.Random.Range(-startingMaximumSidewaysSpeed / 3.6f, startingMaximumSidewaysSpeed / 3.6f));
        }
        carController.externalController = true;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        if (!Application.isBatchMode)
        {
            var wheel = carController.GetComponent<RCC_LogitechSteeringWheel>();
            if (wheel) 
            { 
                wheel.overrideFFB = true;
            }
        }
#endif
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

        sensor.AddObservation(targetSpeed / 100f);
        sensor.AddObservation(DtCRewardPercent / 100f);
        sensor.AddObservation(accelTime0to100 / 20.0f);
        sensor.AddObservation(inputSmoothnessThreshold);

        sensor.AddObservation(vehicleData.GetSpeed() / 100f);
        sensor.AddObservation(vehicleData.GetAccelleration());
        sensor.AddObservation(Mathf.Abs(vehicleData.GetDtC()) / maxAllowedRewardDtc);
        sensor.AddObservation(Mathf.Abs(vehicleData.ReturnLastDtC()) < 0.01f ? 0f : Mathf.Sign(vehicleData.ReturnLastDtC()));

        sensor.AddObservation(carController.throttleInput);
        sensor.AddObservation(carController.brakeInput);
        sensor.AddObservation(currentSteeringAngle / carController.steerAngle);

        Vector3 localVelocity = carController.transform.InverseTransformDirection(carRb.linearVelocity);
        sensor.AddObservation(localVelocity.x / 10.0f);

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

        //float updateDiff = Time.fixedDeltaTime;
        //float currentAccPerSecond = vehicleData.GetAccelleration() / updateDiff;
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
        //AddReward(-0.005f);

        // Engine Inertia
        /*
        if (carController.engineRPM > 800.0f * 2.0f)
        {
            AddReward(0.001f);
        }
        */

        // Steering check
        float targetSteer = actions.ContinuousActions[1];
        currentSteeringAngle = Mathf.MoveTowards(currentSteeringAngle, targetSteer, (1 / steeringSpeed) * Time.fixedDeltaTime);

        // Move
        float currentThrottle = 0.0f;
        float currentBrake = 0.0f;
        float currentSteer = 0.0f;
        if (!carRb.isKinematic)
        {
            currentThrottle = carController.throttleInput = acc;
            currentBrake = carController.brakeInput = brk;
            currentSteer = carController.steerInput = currentSteeringAngle;
        }

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
        float deltaProgress = Mathf.Max(0.001f, currentProgress - lastLapProgress);

        if (startedFirstLap && (deltaProgress > 0.001f || (vehicleData.GetLap() > lastLap && currentProgress < 50.0f)))
        {
            if (currentProgress < 50.0f)
            {
                lastLap = vehicleData.GetLap();
            }
            lastLapProgress = currentProgress;

            if (lastLap > 0)
            {
                timeAtLastSignificantMove = ingameSecondsSinceStartup;

                float currentDtC = Mathf.Abs(vehicleData.ReturnLastDtC());
                float weightRatio = DtCRewardPercent / 100.0f;


                // Speed
                float speedError = Mathf.Abs(vehicleData.GetSpeed() - targetSpeed);
                float speedMultiplier = CalculateCliffReward(speedError, 10.0f, 10.0f);


                // DtC
                float dnaTargetLimit = Mathf.Lerp(maxAllowedRewardDtc, 0.25f, weightRatio);
                float currentEffectiveLimit = Mathf.Lerp(maxAllowedRewardDtc, dnaTargetLimit, dtcProgression);

                float dtcMultiplier = 0.0f;
                if (currentDtC <= currentEffectiveLimit)
                {
                    dtcMultiplier = 1.0f - (currentDtC / currentEffectiveLimit);
                }


                // Acceleration
                Vector3 accelVec = vehicleData.GetAccellerationVector();
                float longitudinalAccel = Mathf.Abs(accelVec.z);
                float engineLimit = (100.0f / 3.6f) / Mathf.Max(accelTime0to100, 1.0f) * 2.5f;
                float accMultiplier = CalculateCliffReward(longitudinalAccel, engineLimit, 2.0f);

                if (longitudinalAccel > episodeMaxAcceleration)
                {
                    episodeMaxAcceleration = longitudinalAccel;
                }


                // Smoothness
                float deltaInput = Mathf.Abs(currentThrottle - lastThrottleInput) + Mathf.Abs(currentBrake - lastBrakeInput);
                float deltaSteer = Mathf.Abs(targetSteer - lastSteeringAction);
                float totalTwitch = deltaInput + deltaSteer;
                float effectiveThreshold = Mathf.Lerp(2.0f, inputSmoothnessThreshold, smoothnessProgression);
                float smoothMultiplier = CalculateCliffReward(totalTwitch, effectiveThreshold, 0.1f);


                // Final
                float combinedMultiplier = speedMultiplier * dtcMultiplier * accMultiplier * smoothMultiplier;

                float normalization = 100.0f / Mathf.Max(targetSpeed, 10.0f);
                float finalReward = deltaProgress * normalization * combinedMultiplier;

                AddReward(finalReward);


                // Logging
                episodeProgressReward += finalReward;
                episodeSpeedReward += speedMultiplier;
                episodeDtCReward += dtcMultiplier;
                episodeSpeedDeviation += speedError;
                episodeDtCDeviation += currentDtC;
                episodeSmoothnessPenalty += (1.0f - smoothMultiplier);

                lastThrottleInput = currentThrottle;
                lastBrakeInput = currentBrake;
                lastSteeringAction = currentSteer;

                if (lastLap > endEpisodeAfterCompletedLaps && currentProgress > 33.33f)
                {
                    InjectStats();
                    if (this.GetComponent<AgentSelector>().boActive)
                    {
                        this.GetComponent<AgentSelector>().IterationEnd();
                    }
                    else
                    {
                        EndEpisode();
                    }
                }
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

        stats.Add("Custom/Effective Difficulty", effectiveRatio, StatAggregationMethod.Average);
        stats.Add("Custom/Laps Completed", lapsCompleted, StatAggregationMethod.Average);
        stats.Add("Custom/Total Progress Reward", episodeProgressReward, StatAggregationMethod.Average);
        stats.Add("Custom/Total Speed Reward", episodeSpeedReward, StatAggregationMethod.Average);
        stats.Add("Custom/Total DtC Reward", episodeDtCReward, StatAggregationMethod.Average);
        stats.Add("Custom/Avg Speed Deviation", episodeSpeedDeviation / stepCount, StatAggregationMethod.Average);
        stats.Add("Custom/Avg DtC Deviation", episodeDtCDeviation / stepCount, StatAggregationMethod.Average);
        stats.Add("Custom/Max Acceleration", episodeMaxAcceleration, StatAggregationMethod.Average);
        stats.Add("Custom/Total Smoothness Penalty", episodeSmoothnessPenalty, StatAggregationMethod.Average);
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

    private float CalculateCliffReward(float val, float limit, float cliffWidth)
    {
        if (val <= limit)
        {
            return 1.0f; 
        }

        float violation = val - limit;
        float penalty = violation / cliffWidth;

        return Mathf.Clamp01(1.0f - penalty);
    }
}
