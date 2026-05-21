using System.Collections;
using TMPro;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class DriveYourselfAgent : Agent
{
    private RCC_CarControllerV4 carController;

    private float episodeProgressReward;
    private float episodeSpeedPenalty;
    private float episodeSpeedDeviation;
    private float episodeMaxAcceleration;
    private float episodeSmoothnessPenalty;
    private float episodeDtCReward;
    private float episodeDtCDeviation;
    private long episodeStepCount;
    private float effectiveRatio;
    private float lastThrottleInput;
    private float lastBrakeInput;
    private float lastSteeringAction;

    private float smoothnessProgression = 1.0f;
    private float dtcProgression = 1.0f;
    
    private float targetSteer;
    private float targetThrottle;
    private float targetBrake;

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
    [SerializeField] private float steeringSpeed = 0.25f; // time from full lock to center
    [SerializeField] private float pedalSpeed = 0.25f;
    private float currentSteeringAngle;
    private float currentThrottle;
    private float currentBrake;

    [Header("EndEpisodeConditions")]
    [SerializeField, Min(1)] private int endEpisodeAfterCompletedLaps = 1;
    [SerializeField] private int endEpisodeCarYPosition = -2;
    [SerializeField] public int endEpisodeCarStuckSeconds = 15;

    private GetVehicleData vehicleData;
    private Rigidbody carRb;
    private Vector3 startingPosition;
    private Vector3 startingPositionForEpisode;
    private Quaternion startingRotation;

    private float lastLapProgress;
    private int lastLap;

    [HideInInspector] public bool startedFirstLap;

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


    long fixedUpdateCounter;
    double ingameSecondsSinceStartup;
    private double timeAtLastSignificantMove;
    void FixedUpdate()
    {
        fixedUpdateCounter++;
        ingameSecondsSinceStartup = fixedUpdateCounter * Time.fixedDeltaTime;
        
        currentThrottle = Mathf.MoveTowards(currentThrottle, targetThrottle, (1.0f / pedalSpeed) * Time.fixedDeltaTime);
        currentBrake = Mathf.MoveTowards(currentBrake, targetBrake, (1.0f / pedalSpeed) * Time.fixedDeltaTime);
        currentSteeringAngle = Mathf.MoveTowards(currentSteeringAngle, targetSteer, (1.0f / steeringSpeed) * Time.fixedDeltaTime);

        // Apply it to the physical car
        if (!carRb.isKinematic)
        {
            carController.throttleInput = currentThrottle;
            carController.brakeInput = currentBrake;
            carController.steerInput = currentSteeringAngle;
        }
    }
    
    private void Update()
    {
        // Should be in Update as FixedUpdate returns always 0.2 secs
        if (Academy.Instance.IsCommunicatorOn)
        {
            Academy.Instance.StatsRecorder.Add("Performance/Delta_Time", Time.deltaTime, StatAggregationMethod.Average);
            Academy.Instance.StatsRecorder.Add("Performance/Effective_Time_Scale", Time.timeScale, StatAggregationMethod.Average);
            if (Time.deltaTime >= Time.maximumDeltaTime - 0.01f)
            {
                Academy.Instance.StatsRecorder.Add("Performance/Lag_Spikes", 1.0f, StatAggregationMethod.Sum);
            }
        }
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
        episodeSpeedPenalty = 0.0f;
        episodeSpeedDeviation = 0.0f;
        episodeDtCReward = 0.0f;
        episodeDtCDeviation = 0.0f;
        episodeStepCount = 0L;
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
                    startingPositionForEpisode += new Vector3(0.0f, 0.0f, Random.Range(-startingPositionSidewaysOffset, startingPositionSidewaysOffset));
                    break;
                case StartingAxis.Z:
                    startingPositionForEpisode += new Vector3(Random.Range(-startingPositionSidewaysOffset, startingPositionSidewaysOffset), 0.0f, 0.0f);
                    break;
            }
            carController.transform.SetPositionAndRotation(startingPositionForEpisode, startingRotation);
            carController.transform.Rotate(new Vector3(0.0f, Random.Range(-startingRotationForEpisode, startingRotationForEpisode), 0.0f));

            carRb.angularVelocity = Vector3.zero;
            carRb.linearVelocity = (carController.transform.forward * Random.Range(0f, startingMaximumForwardSpeed / 3.6f)) + (carController.transform.right * Random.Range(-startingMaximumSidewaysSpeed / 3.6f, startingMaximumSidewaysSpeed / 3.6f));
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

        sensor.AddObservation(targetSpeed / 100.0f);
        sensor.AddObservation(DtCRewardPercent / 100.0f);
        sensor.AddObservation(accelTime0to100 / 20.0f);
        sensor.AddObservation(inputSmoothnessThreshold / 2.0f);

        sensor.AddObservation(vehicleData.GetSpeed() / 100f);
        sensor.AddObservation(Mathf.Clamp(vehicleData.GetAccelleration(), 0f, 20f) / 20f);
        sensor.AddObservation(Mathf.Clamp(vehicleData.GetDtC() / maxAllowedRewardDtc, -1f, 1f));
        
        Debug.Log("DTC: (old) " + vehicleData.ReturnLastDtC() + "m + - (new) " + vehicleData.GetContinuousDtC() + "m + - (diff) " + (vehicleData.ReturnLastDtC() - vehicleData.GetContinuousDtC() + "m"));
        Debug.Log("LAP: (old) " + vehicleData.GetLap() + "m + - (new) " + vehicleData.GetContinuousLap() + "m");
        Debug.Log("PRG: (old) " + vehicleData.GetProgress() + "% + - (new) " + vehicleData.GetContinuousProgress() + "%");

        sensor.AddObservation(carController.throttleInput);
        sensor.AddObservation(carController.brakeInput);
        sensor.AddObservation(currentSteeringAngle / carController.steerAngle);
        sensor.AddObservation(carController.currentGear / 6.0f);

        Vector3 localVelocity = carController.transform.InverseTransformDirection(carRb.linearVelocity);
        sensor.AddObservation(localVelocity.x / 10.0f);
        sensor.AddObservation(localVelocity.z / 27.7f); // 27,7 m/s = 100 km/h

        GameObject currentSegment = vehicleData.GetRoadSegment();
        switch (currentSegment.name.Split("_")[1])
        {
            case "Left":
                sensor.AddObservation(-1);
                break;
            case "Right":
                sensor.AddObservation(1);
                break;
            default:
                sensor.AddObservation(0);
                break;
        }
        
        for (int segment = 0; segment < lookAheadSegments; segment++)
        {
            currentSegment = vehicleData.GetNextRoadSegment(currentSegment);
            Vector3 relativePos = carController.transform.InverseTransformPoint(currentSegment.transform.position);
            sensor.AddObservation(new Vector2(relativePos.x / 100.0f, relativePos.z / 100.0f));
#if UNITY_EDITOR
            Debug.DrawLine(carController.transform.position, currentSegment.transform.position, Color.yellow);
#endif
        }

        Transform nextRoadSegment = vehicleData.GetNextRoadSegment(vehicleData.GetRoadSegment()).transform;
        Vector3 toNextRoadSegment = (nextRoadSegment.position - carController.transform.position).normalized;
        float angleToNextRoadSegment = Vector3.SignedAngle(carController.transform.forward, toNextRoadSegment, Vector3.up);

        Vector2 carPos2D = new Vector2(carController.transform.position.x, carController.transform.position.z);
        Vector2 roadSegmentPos2D = new Vector2(nextRoadSegment.position.x, nextRoadSegment.position.z);
        
        sensor.AddObservation((carPos2D - roadSegmentPos2D).normalized);
        sensor.AddObservation(angleToNextRoadSegment / 180.0f);
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

        // Move and Steer
        float rawForwardAction = actions.ContinuousActions[0];
        if (rawForwardAction > 0.01f)
        {
            if (carController.currentGear == 0 || carController.direction == -1) 
            {
                targetThrottle = Mathf.Lerp(0.15f, 1.0f, rawForwardAction);
            }
            else 
            {
                targetThrottle = rawForwardAction; 
            }
            
            targetBrake = 0.0f;
        }
        else if (rawForwardAction < -0.01f)
        {
            targetBrake = Mathf.Abs(rawForwardAction);
            targetThrottle = 0.0f;
        }
        else
        {
            targetThrottle = 0.0f;
            targetBrake = 0.0f;
        }
        
        targetSteer = actions.ContinuousActions[1];

        // Time penalty
        AddReward(-0.01f);

        // Engine Inertia
        /*
        if (carController.engineRPM > 800.0f * 2.0f)
        {
            AddReward(0.001f);
        }
        */

        // Input Text
        agentAccText.text = "Acc: " + currentThrottle.ToString("F4");
        agentBrkText.text = "Brk: " + currentBrake.ToString("F4");
        agentStrText.text = "Str: " + actions.ContinuousActions[1].ToString("F4");
        agentSpdText.text = "Spd: " + vehicleData.GetSpeed().ToString("F1") + " km/h";
        agentRpmText.text = "RPM: " + carController.engineRPM.ToString("F0") + " - G: " + carController.currentGear.ToString();
        agentDtCText.text = "DtC: " + vehicleData.GetContinuousDtC().ToString("F4") + " m";

        // Rewards
        //Debug.Log("AGENT State: " + lastLap + ", Progress: " + lastLapProgress + "%");
        float currentProgress = vehicleData.GetProgress();
        float deltaProgress = 0.0f;

        if (startedFirstLap)
        {
            if (lastLap == 0) lastLap = vehicleData.GetLap();
            if (vehicleData.GetLap() > lastLap && currentProgress < 50.0f)
            {
                deltaProgress = (100.0f - lastLapProgress) + currentProgress;
                lastLap = vehicleData.GetLap();
                lastLapProgress = currentProgress;
            }
            else if (currentProgress > lastLapProgress)
            {
                deltaProgress = currentProgress - lastLapProgress;
                lastLapProgress = currentProgress;
            }

            if (lastLap > 0 && deltaProgress > 0.001f)
            {
                timeAtLastSignificantMove = ingameSecondsSinceStartup;
                
                // Progress (Always rewarded)
                //float normalization = 100.0f / Mathf.Max(targetSpeed, 10.0f);
                float progressReward = deltaProgress;

                
                // Speed
                float speedScore = 0.0f;
                float currentSpeed = vehicleData.GetSpeed();

                if (currentSpeed <= targetSpeed)
                {
                    speedScore = Mathf.Clamp01(currentSpeed / Mathf.Max(targetSpeed, 1.0f)); 
                }
                else
                {
                    float overSpeedPenalty = Mathf.Clamp01((currentSpeed - targetSpeed) / 20.0f);
                    speedScore = 1.0f - overSpeedPenalty;
                }

                    // Reward only for Progress + Speed
                    // float finalReward = progressReward * speedScore;
                    // finalReward= Mathf.Max(progressReward * 0.10f, finalRewardSpeed);
                    // AddReward(finalReward);
                    
                    
                // DtC
                float currentDtC = Mathf.Abs(vehicleData.ReturnLastDtC());
                float weightRatio = DtCRewardPercent / 100.0f;
                
                float dtcTargetLimit = Mathf.Lerp(maxAllowedRewardDtc, 0.25f, weightRatio);
                float currentEffectiveLimit = Mathf.Lerp(maxAllowedRewardDtc, dtcTargetLimit, dtcProgression);

                float dtcScore = Mathf.Clamp01(1.0f - (currentDtC / currentEffectiveLimit));
                
                    // Reward for Progress + Speed + DtC
                    float universalFloor = 0.40f;
                    float dtcMultiplier = Mathf.Lerp(universalFloor, 1.0f, dtcScore);
                    float combinedMultiplier = speedScore * dtcMultiplier;
                    float finalReward = progressReward * combinedMultiplier;
                    
                    float speedViolation = Mathf.Clamp01((currentSpeed - (targetSpeed + 5.0f)) / 15.0f);
                    float dtcViolation = Mathf.Clamp01((currentDtC - (currentEffectiveLimit + 1.0f)) / 2.0f);
                    float deltaViolation = Mathf.Max(speedViolation, dtcViolation);
                    float adaptiveFloor = Mathf.Lerp(universalFloor, 0.0f, deltaViolation);
                    
                    finalReward = Mathf.Max(progressReward * adaptiveFloor, finalReward);
                    AddReward(finalReward);


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
                //float combinedMultiplier = speedPenaltyMultiplier * dtcMultiplier * accMultiplier * smoothMultiplier;
                //float finalReward = deltaProgress * normalization * combinedMultiplier;
                //AddReward(finalReward);


                // Logging
                episodeProgressReward += finalReward;
                episodeStepCount++;
                
                episodeSpeedPenalty += (1.0f - speedScore);
                episodeDtCReward += dtcScore;
                episodeSpeedDeviation += Mathf.Abs(currentSpeed - targetSpeed);
                episodeDtCDeviation += currentDtC;
                episodeSmoothnessPenalty += (1.0f - smoothMultiplier);

                lastThrottleInput = currentThrottle;
                lastBrakeInput = currentBrake;
                lastSteeringAction = targetSteer;

                if (lastLap > endEpisodeAfterCompletedLaps && currentProgress > 33.33f && currentProgress < 80.0f)
                {
                    AddReward(10.0f);
                    InjectStats();
                    if (this.GetComponent<AgentSelector>().boActive)
                    {
                        this.GetComponent<AgentSelector>().IterationEnd();
                    }
                    else
                    {
                        Academy.Instance.StatsRecorder.Add("Custom/Episodes_Completed", 1.0f, StatAggregationMethod.Sum);
                        EndEpisode();
                        return;
                    }
                }
            }
        }
        if (ingameSecondsSinceStartup - timeAtLastSignificantMove > endEpisodeCarStuckSeconds)
        {
            AddReward(-10.0f);
            //Debug.LogWarning($"Episode end: Car stuck (or agent didn't move)!");
            Academy.Instance.StatsRecorder.Add("Custom/Episodes_Stuck", 1.0f, StatAggregationMethod.Sum);
            InjectStats();
            EndEpisode();
            return;
        }

        if (carController.transform.position.y < endEpisodeCarYPosition)
        {
            AddReward(-20.0f);
            //Debug.LogWarning("Episode end: Car out of Map!");
            Academy.Instance.StatsRecorder.Add("Custom/Episodes_Fell_Off", 1.0f, StatAggregationMethod.Sum);
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
        
        long statsStepCount = episodeStepCount > 0 ? episodeStepCount : 1;
        
        float lapsCompleted = 0f;
        //Debug.LogWarning("Completed Laps: " + lapsCompleted);
        if (startedFirstLap)
        {
            if (ingameSecondsSinceStartup < 10.0d)
            {
                lapsCompleted = lastLapProgress / 100f;
            }
            else
            {
                int safeLap = lastLap > 0 ? lastLap : 1;
                lapsCompleted = (safeLap - 1) + (lastLapProgress / 100f);
            }
        }

        var stats = Academy.Instance.StatsRecorder;
        //Debug.Log("Total Reward: " + episodeProgressReward);

        stats.Add("Custom/Effective Difficulty", effectiveRatio, StatAggregationMethod.Average);
        stats.Add("Custom/Laps Completed", lapsCompleted, StatAggregationMethod.Average);
        stats.Add("Custom/Total Progress Reward", episodeProgressReward, StatAggregationMethod.Average);
        stats.Add("Custom/Total Speed Penalty", episodeSpeedPenalty, StatAggregationMethod.Average);
        stats.Add("Custom/Total DtC Reward", episodeDtCReward, StatAggregationMethod.Average);
        stats.Add("Custom/Avg Speed Deviation", episodeSpeedDeviation / statsStepCount, StatAggregationMethod.Average);
        stats.Add("Custom/Avg DtC Deviation", episodeDtCDeviation / statsStepCount, StatAggregationMethod.Average);
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
