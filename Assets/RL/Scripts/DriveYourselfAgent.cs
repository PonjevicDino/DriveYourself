using System.Collections;
using TMPro;
using Unity.Mathematics;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.Splines;

public class DriveYourselfAgent : Agent
{
    private RCC_CarControllerV4 carController;
    private RCC_LogitechSteeringWheel steeringWheel; 

    private float episodeProgressReward;
    private float episodeSpeedPenalty;
    private float episodeSpeedDeviation;
    private float episodeMaxAcceleration;
    private float episodeSmoothnessPenalty;
    private float episodeAccelerationPenalty;
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

    [SerializeField] public int lookAheadSegments;
    [SerializeField] public float lookAheadDistanceMeters = 10f;

    [SerializeField] private TextMeshProUGUI agentAccText;
    [SerializeField] private TextMeshProUGUI agentBrkText;
    [SerializeField] private TextMeshProUGUI agentStrText;
    [SerializeField] private TextMeshProUGUI agentSpdText;
    [SerializeField] private TextMeshProUGUI agentRpmText;
    [SerializeField] private TextMeshProUGUI agentDtCText;

    [Header("Rewards")]
    [SerializeField, Range(50.0f, 112.5f)] public float targetSpeed;
    //[SerializeField, Min(0f)] private float maxAllowedSafeAcc;
    //[SerializeField, Min(0f)] private float maxAllowedRewardAcc;
    //[SerializeField, Range(0.0f,100.0f)] private float accRewardPercent;
    //[SerializeField, Min(0f)] private float maxAllowedSafeJerk;
    //[SerializeField, Min(0f)] private float maxAllowedRewardJerk;
    //[SerializeField, Range(0.0f,100.0f)] private float jerkRewardPercent;
    [SerializeField, Min(0f)] private float maxAllowedRewardDtc;
    [SerializeField, Range(0.0f, 100.0f)] public float DtCRewardPercent;
    [SerializeField, Range(5.0f, 15.0f)] public float accelTime0to100 = 10.0f;
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

    private float lastLapProgress;  
    private int lastLap;

    private float smoothMultiplier;
    private int decisionPeriod = 5;
    private int decisionStepCounter;

    private float cachedTargetSpeedMs = -1.0f;
    private float cachedWeightRatio;
    private float cachedCurrentEffectiveLimit;
    private float cachedCurvePower;
    private float cachedEffectiveSteerThreshold;
    private float cachedEffectivePedalThreshold;
    private float cachedCurrentAllowedPeak;
    
    [HideInInspector] public bool boMode = false;
    [SerializeField] private bool viewMode = false;
    
    private Vector3 startingPositionForEpisode;
    [SerializeField, Min(0.0f)] private float startingPositionSidewaysOffset;
    [SerializeField, Range(0.0f, 180.0f)] private float startingRotationForEpisode;
    [SerializeField, Min(0.0f)] private float startingMaximumForwardSpeed;
    [SerializeField, Min(0.0f)] private float startingMaximumSidewaysSpeed;

    public override void Initialize()
    {
        carController = this.transform.parent.GetComponent<RCC_CarControllerV4>();
        
        carRb = carController.GetComponent<Rigidbody>();
        vehicleData = this.GetComponent<GetVehicleData>();
        carController.canGoReverseNow = false;

        decisionPeriod = this.GetComponent<DecisionRequester>().DecisionPeriod;
        
#if UNITY_EDITOR
        GlobalSettings.showDebugRays = true;
#endif
    }

    long fixedUpdateCounter;
    double ingameSecondsSinceStartup;
    private double timeAtLastSignificantMove;
    private float accumulatedPhysicsTime = 0f;
    void FixedUpdate()
    {
        fixedUpdateCounter++;
        accumulatedPhysicsTime += Time.fixedDeltaTime;
        ingameSecondsSinceStartup = fixedUpdateCounter * Time.fixedDeltaTime;
        
        currentThrottle = Mathf.MoveTowards(currentThrottle, targetThrottle, (1.0f / pedalSpeed) * Time.fixedDeltaTime);
        currentBrake = Mathf.MoveTowards(currentBrake, targetBrake, (1.0f / pedalSpeed) * Time.fixedDeltaTime);
        currentSteeringAngle = Mathf.MoveTowards(currentSteeringAngle, targetSteer, (1.0f / steeringSpeed) * Time.fixedDeltaTime);
        
        if (!carRb.isKinematic)
        {
            carController.throttleInput = currentThrottle;
            carController.brakeInput = currentBrake;
            carController.steerInput = currentSteeringAngle;
        }

        if (steeringWheel)
        {
            steeringWheel.steerInput = currentSteeringAngle;
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
            dtcProgression = Academy.Instance.EnvironmentParameters.GetWithDefault("dtc_progression", 1.0f);
            
            float maxPythonSteps = Academy.Instance.EnvironmentParameters.GetWithDefault("max_python_steps", 1000000f);
            float totalGlobalAgents = Academy.Instance.EnvironmentParameters.GetWithDefault("total_global_agents", 256f);
            float maxAcademySteps = maxPythonSteps / totalGlobalAgents;
            float curriculumPacingSteps = maxAcademySteps * 0.6f; 
            float currentProgress = Mathf.Clamp01((float)Academy.Instance.StepCount / curriculumPacingSteps);
            smoothnessProgression = currentProgress;
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
        episodeAccelerationPenalty = 0.0f;
        
        if (!carController)
        {
            return;
        }
        
        ForceDisableAllParticles();

        //this.transform.parent.Find("All Audio Sources").gameObject.SetActive(false);
        this.transform.parent.Find("All Contact Particles").gameObject.SetActive(false);

        if (!boMode)
        {
            Spline spline = vehicleData.roadLayout.trackSpline.Spline;
            float randomT = UnityEngine.Random.Range(0f, 1f);

            float3 localPos = SplineUtility.EvaluatePosition(spline, randomT);
            float3 localTangent = SplineUtility.EvaluateTangent(spline, randomT);

            Transform trackTransform = vehicleData.roadLayout.trackSpline.transform;
            Vector3 worldPos = trackTransform.TransformPoint(localPos);
            Vector3 worldForward = trackTransform.TransformDirection(localTangent).normalized;
            Vector3 worldRight = Vector3.Cross(Vector3.up, worldForward).normalized;

            worldPos += worldRight * UnityEngine.Random.Range(-startingPositionSidewaysOffset, startingPositionSidewaysOffset);
            worldPos += Vector3.up * 1.0f;

            Quaternion rotation = Quaternion.LookRotation(worldForward, Vector3.up);
            rotation *= Quaternion.Euler(0, UnityEngine.Random.Range(-startingRotationForEpisode, startingRotationForEpisode), 0);

            carController.transform.SetPositionAndRotation(worldPos, rotation);

            carRb.angularVelocity = Vector3.zero;
            carRb.linearVelocity =
                (carController.transform.forward * UnityEngine.Random.Range(0f, startingMaximumForwardSpeed / 3.6f)) +
                (carController.transform.right * UnityEngine.Random.Range(-startingMaximumSidewaysSpeed / 3.6f,
                    startingMaximumSidewaysSpeed / 3.6f));
        }

        carController.externalController = true;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        if (!Application.isBatchMode)
        {
            steeringWheel = carController.GetComponent<RCC_LogitechSteeringWheel>();
            if (steeringWheel != null) 
            { 
                steeringWheel.overrideFFB = true;
            }
        }
#endif
        vehicleData.ResetVars();
        carController.canGoReverseNow = false;
        carController.currentGear = 0;

        lastLap = 1;
        lastLapProgress = 0.0f;

        fixedUpdateCounter = 0L;
        timeAtLastSignificantMove = 0.0d;
        decisionStepCounter = 0;
        accumulatedPhysicsTime = 0f;

        smoothMultiplier = 1.0f;
        
        cachedTargetSpeedMs = targetSpeed / 3.6f;
        cachedWeightRatio = DtCRewardPercent / 100.0f;
        
        float dtcTargetLimit = Mathf.Lerp(maxAllowedRewardDtc, 0.25f, cachedWeightRatio);
        cachedCurrentEffectiveLimit = Mathf.Lerp(maxAllowedRewardDtc, dtcTargetLimit, dtcProgression);
        cachedCurvePower = Mathf.Lerp(8.0f, 1.0f, cachedWeightRatio);
        
        cachedEffectiveSteerThreshold = Mathf.Lerp(2.0f, inputSmoothnessThreshold * decisionPeriod, smoothnessProgression);
        cachedEffectivePedalThreshold = Mathf.Lerp(2.0f, 0.04f * decisionPeriod, smoothnessProgression);
        
        float linearAvgAccel = 27.78f / Mathf.Max(accelTime0to100, 1.0f);
        float strictEnginePeak = linearAvgAccel * 1.5f; 
        cachedCurrentAllowedPeak = Mathf.Lerp(20.0f, strictEnginePeak, smoothnessProgression);
        
        vehicleData.SyncDiscreteSegmentToSpline();
        vehicleData.InitContinuousSplineState();
    }
    
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var actions = actionsOut.ContinuousActions;
        actions[0] = Input.GetKey(KeyCode.UpArrow) ? 1.0f : Input.GetKey(KeyCode.DownArrow) ? -1f : 0;
        actions[1] = Input.GetKey(KeyCode.RightArrow) ? 1.0f : Input.GetKey(KeyCode.LeftArrow) ? -1 : 0;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (!vehicleData)
        {
            return;
        }
        
        if (accumulatedPhysicsTime > 0f)
        {
            vehicleData.UpdateVehicleData(accumulatedPhysicsTime);
            accumulatedPhysicsTime = 0f;
        }

        sensor.AddObservation(targetSpeed / 100.0f);
        sensor.AddObservation(DtCRewardPercent / 100.0f);
        sensor.AddObservation(accelTime0to100 / 20.0f);
        sensor.AddObservation(inputSmoothnessThreshold / 2.0f);

        sensor.AddObservation(vehicleData.GetSpeed() / 100f);
        Vector3 currentAccelVec = vehicleData.GetAccellerationVector();
        sensor.AddObservation(Mathf.Clamp(currentAccelVec.z, -20f, 20f) / 20f);
        sensor.AddObservation(Mathf.Clamp(currentAccelVec.x, -10f, 10f) / 10f);
        sensor.AddObservation(Mathf.Clamp(vehicleData.GetContinuousDtC() / maxAllowedRewardDtc, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(vehicleData.GetContinuousDtCVelocity() / 10f, -1f, 1f));

        sensor.AddObservation(carController.throttleInput);
        sensor.AddObservation(carController.brakeInput);
        sensor.AddObservation(currentSteeringAngle / carController.steerAngle);
        
        sensor.AddObservation(targetThrottle);
        sensor.AddObservation(targetBrake);
        sensor.AddObservation(targetSteer);

        Vector3 localVelocity = carController.transform.InverseTransformDirection(carRb.linearVelocity);
        sensor.AddObservation(localVelocity.x / 10.0f);
        sensor.AddObservation(localVelocity.z / 27.78f); // 27.78 m/s = 100 km/h

        float currentT = vehicleData.GetCurrentSplineT();
        Spline spline = vehicleData.roadLayout.trackSpline.Spline;
        float trackLength = spline.GetLength();
        float speedFactor = targetSpeed / 20.0f; 
        float dynamicLookAheadDistance = lookAheadDistanceMeters * speedFactor;
        float tStep = dynamicLookAheadDistance / trackLength;

        Vector3 carPos = carController.transform.position;
        Vector3 carForward = carController.transform.forward;

        for (int i = 1; i <= lookAheadSegments; i++)
        {
            float lookAheadT = (currentT + (i * tStep)) % 1.0f;

            float3 localPos = SplineUtility.EvaluatePosition(spline, lookAheadT);
            Vector3 worldPos = vehicleData.roadLayout.trackSpline.transform.TransformPoint(localPos);
            
            Vector3 relativePos = carController.transform.InverseTransformPoint(worldPos);
            sensor.AddObservation(new Vector2(relativePos.x / 100.0f, relativePos.z / 100.0f));
            
            Vector3 toTarget = (worldPos - carPos).normalized;
            float angleToTarget = Vector3.SignedAngle(carForward, toTarget, Vector3.up);
            sensor.AddObservation(angleToTarget / 180.0f);

            if (GlobalSettings.showDebugRays)
            {
                Debug.DrawLine(carPos, worldPos, Color.yellow);
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        //Debug.Log($"{GetHashCode()}: actions.ContinuousActions[0]: {actions.ContinuousActions[0]}; actions.ContinuousActions[1]: {actions.ContinuousActions[1]}; rpm: {carController.engineRPM}");

        if (!carController)
        {
            return;
        }

        // Move and Steer
        float rawForwardAction = actions.ContinuousActions[0];
        if (rawForwardAction > 0.01f)
        {
            targetThrottle = rawForwardAction; 
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
        //AddReward(-0.01f);

        // Input Text
        if (viewMode)
        {
            agentAccText.text = "Acc: " + targetThrottle.ToString("F4");
            agentBrkText.text = "Brk: " + targetBrake.ToString("F4");
            agentStrText.text = "Str: " + targetSteer.ToString("F4");
            agentSpdText.text = "Spd: " + vehicleData.GetSpeed().ToString("F1") + " km/h";
            agentRpmText.text = "RPM: " + carController.engineRPM.ToString("F0") + " - G: " + carController.currentGear;
            agentDtCText.text = "DtC: " + vehicleData.GetContinuousDtC().ToString("F4") + " m";
        }

        // Rewards
        //Debug.Log("AGENT State: " + lastLap + ", Progress: " + lastLapProgress + "%");
        
        float currentContinuousProgress = vehicleData.GetContinuousProgress();
        float currentDtC = Mathf.Abs(vehicleData.GetContinuousDtC());
        int currentLap = vehicleData.GetContinuousLap();
        
        Vector3 trackForward = vehicleData.GetContinuousForwardVector();
        Vector3 carVelocity = carRb.linearVelocity;
            
        // Progress (Always rewarded)
        float velocityAlongPath = Vector3.Dot(carVelocity, trackForward);
        float baseAlignmentReward = Mathf.Clamp(velocityAlongPath / cachedTargetSpeedMs, -1.0f, 1.0f);

        if (velocityAlongPath > 1.0f) 
        {
            timeAtLastSignificantMove = ingameSecondsSinceStartup;
        }

            
        // Speed
        float speedScore;
        float currentSpeed = vehicleData.GetSpeed();

        if (currentSpeed <= targetSpeed)
        {
            speedScore = Mathf.Clamp01(currentSpeed / Mathf.Max(targetSpeed, 1.0f)); 
        }
        else
        {
            float overSpeedPenalty = Mathf.Clamp01((currentSpeed - targetSpeed) / 10.0f);
            speedScore = 1.0f - overSpeedPenalty;
        }

            // Reward only for Progress + Speed
            // float finalReward = progressReward * speedScore;
            // finalReward= Mathf.Max(progressReward * 0.10f, finalRewardSpeed);
            // AddReward(finalReward);
                
                
        // DtC
        float normalizedDtC = Mathf.Clamp01(currentDtC / cachedCurrentEffectiveLimit);
        float dtcScore = 1.0f - Mathf.Pow(normalizedDtC, cachedCurvePower);
        
            // Reward for Progress + Speed + DtC
            // float universalFloor = 0.40f;
            // float dtcMultiplier = Mathf.Lerp(universalFloor, 1.0f, dtcScore);
            // float combinedMultiplier = speedScore * dtcMultiplier;
            // float stepReward = 0f;
            // if (baseAlignmentReward > 0f) {
            //     stepReward = baseAlignmentReward * combinedMultiplier * 0.02f;
            // } else {
            //     stepReward = baseAlignmentReward * 0.02f; 
            // }
            // AddReward(stepReward);

            
        // Smoothness
        // float deltaInput = Mathf.Abs(targetThrottle - lastThrottleInput) + Mathf.Abs(targetBrake - lastBrakeInput);
        // float deltaSteer = Mathf.Abs(targetSteer - lastSteeringAction);
        // float totalTwitch = deltaInput + deltaSteer;
        // float effectiveThreshold = Mathf.Lerp(2.0f, inputSmoothnessThreshold * decisionPeriod, smoothnessProgression);
        // float smoothMultiplier = CalculateCliffReward(totalTwitch, effectiveThreshold, 0.1f);
        
            // Reward for Progress + Speed + DtC + Smoothness
            // decisionStepCounter++;
            // if (decisionStepCounter >= decisionPeriod)
            // {
            //     float deltaInput = Mathf.Abs(targetThrottle - lastThrottleInput) + Mathf.Abs(targetBrake - lastBrakeInput);
            //     float deltaSteer = Mathf.Abs(targetSteer - lastSteeringAction);
            //     float totalTwitch = deltaInput + deltaSteer;
            //     float effectiveThreshold = Mathf.Lerp(2.0f, inputSmoothnessThreshold * decisionPeriod, smoothnessProgression);
            //     smoothMultiplier = CalculateCliffReward(totalTwitch, effectiveThreshold, 0.1f);
            //     
            //     lastThrottleInput = targetThrottle;
            //     lastBrakeInput = targetBrake;
            //     lastSteeringAction = targetSteer;
            //     
            //     episodeSmoothnessPenalty += (1.0f - smoothMultiplier);
            //
            //     decisionStepCounter = 0;
            // }
            //
            // float universalFloor = 0.25f;
            // float dtcMultiplier = Mathf.Lerp(universalFloor, 1.0f, dtcScore);
            // float combinedMultiplier = speedScore * dtcMultiplier * smoothMultiplier;
            // float stepReward = 0f;
            // if (baseAlignmentReward > 0f) {
            //     stepReward = baseAlignmentReward * combinedMultiplier * 0.02f;
            // } else {
            //     stepReward = baseAlignmentReward * 0.02f; 
            // }
            //
            // AddReward(stepReward);
            
            
        // Acceleration
        Vector3 accelVec = vehicleData.GetAccellerationVector();
        float longitudinalAccel = accelVec.z;
        float accMultiplier = 1.0f;

        if (longitudinalAccel > 0.0f) 
        {
            accMultiplier = CalculateCliffReward(longitudinalAccel, cachedCurrentAllowedPeak, 5.0f);
        }
        else 
        {
            float brakePhysicalLimit = 20.0f; 
            accMultiplier = CalculateCliffReward(Mathf.Abs(longitudinalAccel), brakePhysicalLimit, 2.0f);
        }
        
        if (Mathf.Abs(longitudinalAccel) > episodeMaxAcceleration)
        {
            episodeMaxAcceleration = Mathf.Abs(longitudinalAccel);
        }
        episodeAccelerationPenalty += (1.0f - accMultiplier);
        

        // Final
        decisionStepCounter++;
        if (decisionStepCounter >= decisionPeriod)
        {
            float deltaPedals = Mathf.Abs(targetThrottle - lastThrottleInput) + Mathf.Abs(targetBrake - lastBrakeInput);
            float deltaSteer = Mathf.Abs(targetSteer - lastSteeringAction);
            
            float steerMultiplier = CalculateCliffReward(deltaSteer, cachedEffectiveSteerThreshold, 2.0f);
            float pedalMultiplier = CalculateCliffReward(deltaPedals, cachedEffectivePedalThreshold, 2.0f);
            smoothMultiplier = steerMultiplier * pedalMultiplier;
            
            lastThrottleInput = targetThrottle;
            lastBrakeInput = targetBrake;
            lastSteeringAction = targetSteer;
            
            episodeSmoothnessPenalty += (1.0f - smoothMultiplier);
            decisionStepCounter = 0;
        }
            
        float dynamicFloor = Mathf.Lerp(0.25f, 0.0f, cachedWeightRatio);
        float dtcMultiplier = Mathf.Lerp(dynamicFloor, 1.0f, dtcScore);
        float combinedMultiplier = speedScore * dtcMultiplier * smoothMultiplier * accMultiplier;
        float stepReward;
        if (baseAlignmentReward > 0f) {
            stepReward = baseAlignmentReward * combinedMultiplier * 0.02f;
        } else {
            stepReward = baseAlignmentReward * 0.02f; 
        }
        
        float targetedAccPenalty = (1.0f - accMultiplier) * 0.05f;
        stepReward -= targetedAccPenalty;
            
        AddReward(stepReward);


        // Logging
        episodeProgressReward += stepReward;
        episodeStepCount++;
        
        episodeSpeedPenalty += (1.0f - speedScore);
        episodeDtCReward += dtcScore;
        episodeSpeedDeviation += Mathf.Abs(currentSpeed - targetSpeed);
        episodeDtCDeviation += currentDtC;
        
        lastLapProgress = currentContinuousProgress;
        lastLap = currentLap;

        if (lastLap > endEpisodeAfterCompletedLaps)
        {
            //AddReward(10.0f);
            InjectStats();
            Academy.Instance.StatsRecorder.Add("Custom/Episodes_Completed", 1.0f, StatAggregationMethod.Sum);
            EndEpisode();
            return;
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
        
        float lapsCompleted = lastLapProgress / 100.0f;
        
        var stats = Academy.Instance.StatsRecorder;
        //Debug.Log("Total Reward: " + episodeProgressReward);

        stats.Add("Custom/Effective Difficulty", effectiveRatio, StatAggregationMethod.Average);
        stats.Add("Custom/Laps Completed", lapsCompleted, StatAggregationMethod.Average);
        stats.Add("Custom/Total Progress Reward", episodeProgressReward, StatAggregationMethod.Average);
        stats.Add("Custom/Total Speed Penalty", episodeSpeedPenalty, StatAggregationMethod.Average);
        stats.Add("Custom/Total DtC Reward", episodeDtCReward, StatAggregationMethod.Average);
        stats.Add("Custom/Total Smoothness Penalty", episodeSmoothnessPenalty, StatAggregationMethod.Average);
        stats.Add("Custom/Total Acceleration Penalty", episodeAccelerationPenalty, StatAggregationMethod.Average);
        stats.Add("Custom/Avg Speed Deviation", episodeSpeedDeviation / statsStepCount, StatAggregationMethod.Average);
        stats.Add("Custom/Avg DtC Deviation", episodeDtCDeviation / statsStepCount, StatAggregationMethod.Average);
        stats.Add("Custom/Avg Smoothness Penalty", (episodeSmoothnessPenalty * decisionPeriod) / statsStepCount, StatAggregationMethod.Average);
        stats.Add("Custom/Avg Acceleration Penalty", episodeAccelerationPenalty / statsStepCount, StatAggregationMethod.Average);
        stats.Add("Custom/Max Acceleration", episodeMaxAcceleration, StatAggregationMethod.Average);
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
