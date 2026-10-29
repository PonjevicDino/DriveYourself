using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;

public class SimpleMPC : MonoBehaviour
{
    [Header("Manual Control")]
    [SerializeField] private bool manualControl;
    [SerializeField, Range(0.0f, 1.0f)] private float maxManualInputThrottle = 1.0f;
    [SerializeField, Range(0.0f, 1.0f)] private float maxManualInputBrake = 1.0f;
    [SerializeField, Range(0.0f, 1.0f)] private float maxManualInputSteer = 1.0f;

    [Header("Driving Style Boundaries")]
    [SerializeField, Range(30.0f, 150.0f)] private float targetSpeed = 50f; 
    [SerializeField, Range(0.25f, 4.0f)] private float maxDtC = 2.0f;
    [SerializeField, Tooltip("0-100 km/h acceleration time in seconds.")] 
    [Range(2.0f, 25.0f)] private float accelTime0to100 = 10.0f; 
    
    [SerializeField, Tooltip("Maximum lateral G-force before the tires slide. Standard cars are ~0.8 to 1.2 Gs (1G = 9.81 m/s²).")]
    [Range(5.0f, 15.0f)] private float maxLateralGrip = 9.81f; 

    [Header("MPC Tuning - Steering")]
    [SerializeField, Tooltip("Allowable steering change per tick. (e.g. 0.2 = 20% turn per calculation)")] 
    private float steerSearchWindow = 0.2f; 
    [SerializeField, Tooltip("Penalty for twitchy steering.")] 
    private float steerSmoothnessPenalty = 10.0f; 

    [Header("MPC Tuning - Pedals")]
    [SerializeField, Tooltip("Allowable gas/brake change per tick. Keep lower than steering to simulate engine/brake delay.")] 
    private float pedalSearchWindow = 0.05f; 
    [SerializeField, Tooltip("Penalty for stomping pedals. High values stop gas/brake oscillation.")] 
    private float pedalSmoothnessPenalty = 100.0f; 

    [Header("MPC Settings")]
    [SerializeField, Range(1, 50)] private int mpcUpdatesPerSecond = 20;
    [SerializeField] private int horizonSteps = 15;
    [SerializeField] private float timeStep = 0.075f;
    [SerializeField] private int numSamples = 500000;

    private RCC_CarControllerV4 carController;
    private Rigidbody carRigidBody;
    private GetVehicleData vehicleData;
    
    private NativeSpline nativeTrackSpline;
    private bool isSplineInitialized = false;
    private float trackLength;

    private JobHandle mpcJobHandle;
    private NativeArray<float> sequenceCosts;
    private NativeArray<float3> sequenceInputs;
    
    private NativeArray<float3> targetPositions;
    private NativeArray<float3> targetRights;
    
    private bool isJobRunning = false;
    private float timeSinceLastMpc = 0f;
    
    private float bestSteer = 0f;
    private float bestThrottle = 0f;
    private float bestBrake = 0f;

    private bool isInitialized = false;

    void Start()
    {
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (isInitialized) return;

        if (carController == null && transform.parent != null)
            carController = transform.parent.GetComponent<RCC_CarControllerV4>();
        if (carRigidBody == null && carController != null)
            carRigidBody = carController.GetComponent<Rigidbody>();
        if (vehicleData == null)
            vehicleData = GetComponent<GetVehicleData>();

        if (carController == null || vehicleData == null || vehicleData.roadLayout == null || vehicleData.roadLayout.trackSpline == null)
            return;

        if (!isSplineInitialized && vehicleData.roadLayout.trackSpline.Spline != null)
        {
            nativeTrackSpline = new NativeSpline(vehicleData.roadLayout.trackSpline.Spline, Allocator.Persistent);
            trackLength = vehicleData.roadLayout.trackSpline.Spline.GetLength();
            isSplineInitialized = true;
            vehicleData.InitContinuousSplineState();
        }

        if (!sequenceCosts.IsCreated)
            sequenceCosts = new NativeArray<float>(numSamples, Allocator.Persistent);
        if (!sequenceInputs.IsCreated)
            sequenceInputs = new NativeArray<float3>(numSamples, Allocator.Persistent);
        if (!targetPositions.IsCreated)
            targetPositions = new NativeArray<float3>(horizonSteps, Allocator.Persistent);
        if (!targetRights.IsCreated)
            targetRights = new NativeArray<float3>(horizonSteps, Allocator.Persistent);

        isInitialized = true;
    }

    void OnEnable()
    {
        EnsureInitialized();
        if (carController != null)
        {
            carController.overrideInputs = false;
            carController.externalController = true;
            if (!carRigidBody.isKinematic)
            {
                carController.throttleInput = bestThrottle;
                carController.brakeInput = bestBrake;
                carController.steerInput = bestSteer;
            }
        }
        timeSinceLastMpc = 0f;
    }

    void OnDisable()
    {
        if (isJobRunning)
        {
            mpcJobHandle.Complete();
            isJobRunning = false;
        }
    }

    void OnDestroy()
    {
        mpcJobHandle.Complete();
        
        if (isSplineInitialized) nativeTrackSpline.Dispose();
        if (sequenceCosts.IsCreated) sequenceCosts.Dispose();
        if (sequenceInputs.IsCreated) sequenceInputs.Dispose();
        if (targetPositions.IsCreated) targetPositions.Dispose();
        if (targetRights.IsCreated) targetRights.Dispose();
    }

    void Update()
    {
        if (manualControl) ManualControl();
    }

    void FixedUpdate()
    {
        EnsureInitialized();
        if (manualControl || !isInitialized || !isSplineInitialized)
        {
            return;
        }

        vehicleData.UpdateVehicleData(Time.fixedDeltaTime);
        timeSinceLastMpc += Time.fixedDeltaTime;
        float mpcInterval = 1.0f / mpcUpdatesPerSecond;

        if (isJobRunning && mpcJobHandle.IsCompleted && timeSinceLastMpc >= mpcInterval)
        {
            mpcJobHandle.Complete();
            
            float lowestCost = float.MaxValue;
            
            for (int i = 0; i < numSamples; i++)
            {
                if (sequenceCosts[i] < lowestCost)
                {
                    lowestCost = sequenceCosts[i];
                    bestSteer = sequenceInputs[i].x;
                    bestThrottle = sequenceInputs[i].y;
                    bestBrake = sequenceInputs[i].z;
                }
            }

            float calculatedMaxAccel = (100f / 3.6f) / accelTime0to100;

            DrawTrajectories(mpcInterval, calculatedMaxAccel);
            LogMpcState(calculatedMaxAccel);

            timeSinceLastMpc = 0f;
            isJobRunning = false;
        }

        if (!isJobRunning)
        {
            Transform splineTransform = vehicleData.roadLayout.trackSpline.transform;
            float currentT = vehicleData.GetCurrentSplineT();
            float targetSpeedMs = targetSpeed / 3.6f;
            
            float lookAheadDistance = targetSpeedMs * (horizonSteps * timeStep);
            float stepT = lookAheadDistance / trackLength / horizonSteps;

            for (int i = 0; i < horizonSteps; i++)
            {
                float t = math.frac(currentT + ((i + 1) * stepT));
                float3 localPos = SplineUtility.EvaluatePosition(nativeTrackSpline, t);
                float3 localTan = SplineUtility.EvaluateTangent(nativeTrackSpline, t);
                
                targetPositions[i] = math.transform(splineTransform.localToWorldMatrix, localPos);
                
                float3 worldTan = math.normalize(math.rotate(splineTransform.rotation, localTan));
                targetRights[i] = math.normalize(math.cross(new float3(0, 1, 0), worldTan));
            }
            
            float calculatedMaxAccel = (100f / 3.6f) / accelTime0to100;
            float trueForwardSpeed = carRigidBody.transform.InverseTransformDirection(carRigidBody.linearVelocity).z;

            MpcEvaluationJob mpcJob = new MpcEvaluationJob
            {
                currentX = transform.position.x,
                currentY = transform.position.y,
                currentZ = transform.position.z,
                currentYaw = transform.eulerAngles.y * Mathf.Deg2Rad,
                currentVel = trueForwardSpeed,
                
                targetSpeedMs = targetSpeedMs,
                maxDtC = maxDtC,
                maxAcceleration = calculatedMaxAccel,
                maxLateralGrip = maxLateralGrip, 
                
                steerSearchWindow = steerSearchWindow,
                pedalSearchWindow = pedalSearchWindow,
                steerSmoothnessPenalty = steerSmoothnessPenalty,
                pedalSmoothnessPenalty = pedalSmoothnessPenalty,

                horizonSteps = horizonSteps,
                timeStep = timeStep,
                steerAngleLimit = carController.steerAngle * Mathf.Deg2Rad,
                wheelbase = 2.55f, 
                
                lastSteer = bestSteer,
                lastThrottle = bestThrottle,
                lastBrake = bestBrake,
                
                seed = (uint)(Time.frameCount * 1000 + 1), 
                
                targetPositions = targetPositions,
                targetRights = targetRights,
                sequenceCosts = sequenceCosts,
                sequenceInputs = sequenceInputs
            };
            
            mpcJobHandle = mpcJob.Schedule(numSamples, 64);
            isJobRunning = true;
        }
        
        RCC_Inputs appliedInputs = new RCC_Inputs();
        appliedInputs.throttleInput = bestThrottle;
        appliedInputs.brakeInput = bestBrake;
        appliedInputs.steerInput = bestSteer;
        
        carController.overrideInputs = false;
        carController.externalController = true;
        if (!carRigidBody.isKinematic)
        {
            carController.throttleInput = bestThrottle;
            carController.brakeInput = bestBrake;
            carController.steerInput = bestSteer;
        }
    }
    
    public void ApplyParameters(float newTargetSpeed, float newMaxDtC, float newAccelTime0to100, float smoothNormalized)
    {
        targetSpeed = Mathf.Clamp(newTargetSpeed, 30.0f, 150.0f);
        maxDtC = Mathf.Clamp(newMaxDtC, 0.25f, 4.0f);
        accelTime0to100 = Mathf.Clamp(newAccelTime0to100, 2.0f, 25.0f);
        
        steerSearchWindow = Mathf.Lerp(0.30f, 0.05f, smoothNormalized);
        pedalSearchWindow = Mathf.Lerp(0.10f, 0.02f, smoothNormalized);
        steerSmoothnessPenalty = Mathf.Lerp(5.0f, 80.0f, smoothNormalized);
        pedalSmoothnessPenalty = Mathf.Lerp(50.0f, 250.0f, smoothNormalized);
    }

    private void DrawTrajectories(float drawDuration, float calculatedMaxAccel)
    {
        for (int i = 0; i < horizonSteps - 1; i++)
        {
            Debug.DrawLine(targetPositions[i], targetPositions[i+1], Color.green, drawDuration);
        }

        float simX = transform.position.x;
        float simZ = transform.position.z;
        float simYaw = transform.eulerAngles.y * Mathf.Deg2Rad;
        float simVel = carRigidBody.transform.InverseTransformDirection(carRigidBody.linearVelocity).z;
        Vector3 lastPoint = transform.position;

        for (int step = 0; step < horizonSteps; step++)
        {
            float actualAccel = (bestThrottle * calculatedMaxAccel) - (bestBrake * calculatedMaxAccel);
            simVel += actualAccel * timeStep;
            simVel = Mathf.Max(0f, simVel);

            float steerAngle = bestSteer * carController.steerAngle * Mathf.Deg2Rad;
            simYaw += (simVel / 2.55f) * Mathf.Tan(steerAngle) * timeStep;
            
            simX += simVel * Mathf.Sin(simYaw) * timeStep;
            simZ += simVel * Mathf.Cos(simYaw) * timeStep;

            Vector3 nextPoint = new Vector3(simX, transform.position.y, simZ);
            
            Debug.DrawLine(lastPoint, nextPoint, Color.red, drawDuration);
            lastPoint = nextPoint;
        }
    }

    private void LogMpcState(float calculatedMaxAccel)
    {
        float currentDtC = vehicleData.GetContinuousDtC();
        float inputChange = Mathf.Abs(bestSteer - carController.steerInput) + 
                            Mathf.Abs(bestThrottle - carController.throttleInput) + 
                            Mathf.Abs(bestBrake - carController.brakeInput);
        float requestedAccel = (bestThrottle * calculatedMaxAccel) - (bestBrake * calculatedMaxAccel);

        Debug.Log($"<color=#00FF00><b>MPC OUTPUT</b></color> | Thr: {bestThrottle:F3}, Brk: {bestBrake:F3}, Str: {bestSteer:F3}\n" +
                  $"<color=#00FFFF><b>VEHICLE STATE</b></color> | Speed: {carController.speed:F1} km/h, Signed DtC: {currentDtC:F2}m\n" +
                  $"<color=#FFA500><b>EVALUATION</b></color> | Target Accel: {requestedAccel:F2} m/s² (0-100 limit: {accelTime0to100}s), Input Change: {inputChange:F3}");
    }

    private void ManualControl()
    {
        RCC_Inputs inputs = new RCC_Inputs();
        if (Input.GetKey(KeyCode.W)) inputs.throttleInput = maxManualInputThrottle;
        else if (Input.GetKey(KeyCode.S)) inputs.brakeInput = maxManualInputBrake;
        if (Input.GetKey(KeyCode.A)) inputs.steerInput = -maxManualInputSteer;
        else if (Input.GetKey(KeyCode.D)) inputs.steerInput = maxManualInputSteer;
        carController.OverrideInputs(inputs);
    }
    
    public void ResetState()
    {
        if (isJobRunning)
        {
            mpcJobHandle.Complete();
            isJobRunning = false;
        }

        bestSteer = 0f;
        bestThrottle = 0f;
        bestBrake = 0f;
        timeSinceLastMpc = 0f;

        if (carController != null)
        {
            carController.OverrideInputs(new RCC_Inputs());
        }
    }

    [BurstCompile]
    public struct MpcEvaluationJob : IJobParallelFor
    {
        public float currentX, currentY, currentZ, currentYaw, currentVel;
        public float targetSpeedMs, maxDtC, maxAcceleration, maxLateralGrip;
        
        public float steerSearchWindow, pedalSearchWindow;
        public float steerSmoothnessPenalty, pedalSmoothnessPenalty;
        
        public float timeStep, steerAngleLimit, wheelbase;
        public int horizonSteps;
        public uint seed;

        public float lastSteer, lastThrottle, lastBrake;

        [ReadOnly] public NativeArray<float3> targetPositions;
        [ReadOnly] public NativeArray<float3> targetRights;
        public NativeArray<float> sequenceCosts;
        public NativeArray<float3> sequenceInputs; 

        public void Execute(int index)
        {
            Unity.Mathematics.Random rand = new Unity.Mathematics.Random(seed + (uint)index);

            float simX = currentX;
            float simZ = currentZ;
            float simYaw = currentYaw;
            float simVel = currentVel;

            float sequenceCost = 0f;
            
            float candidateSteer = math.clamp(lastSteer + rand.NextFloat(-steerSearchWindow, steerSearchWindow), -1f, 1f);
            float candidateThrottle = 0f;
            float candidateBrake = 0f;

            float accelRequest = rand.NextFloat(-1f, 1f);
            if (accelRequest > 0)
            {
                candidateThrottle = math.clamp(lastThrottle + rand.NextFloat(-pedalSearchWindow, pedalSearchWindow), 0f, 1f);
            }
            else
            {
                candidateBrake = math.clamp(lastBrake + rand.NextFloat(-pedalSearchWindow, pedalSearchWindow), 0f, 1f);
            }
            
            float steerJerk = math.abs(candidateSteer - lastSteer);
            float accelJerk = math.abs(candidateThrottle - lastThrottle) + math.abs(candidateBrake - lastBrake);
            sequenceCost += (steerJerk * steerSmoothnessPenalty) + (accelJerk * pedalSmoothnessPenalty);
            
            sequenceCost += candidateThrottle * 2.0f;

            for (int step = 0; step < horizonSteps; step++)
            {
                float actualAccel = (candidateThrottle * maxAcceleration) - (candidateBrake * maxAcceleration);
                simVel += actualAccel * timeStep;
                simVel = math.max(0f, simVel); 

                float steerAngle = candidateSteer * steerAngleLimit;
                
                float lateralAccel = (simVel * simVel) / wheelbase * math.abs(math.tan(steerAngle));
                
                if (lateralAccel > maxLateralGrip)
                {
                    sequenceCost += (lateralAccel - maxLateralGrip) * 1000.0f;
                }

                simYaw += (simVel / wheelbase) * math.tan(steerAngle) * timeStep;
                simX += simVel * math.sin(simYaw) * timeStep;
                simZ += simVel * math.cos(simYaw) * timeStep;

                float3 simWorldPos = new float3(simX, currentY, simZ);
                float3 deltaPos = simWorldPos - targetPositions[step];

                float signedDtC = math.dot(deltaPos, targetRights[step]);
                float absDtC = math.abs(signedDtC);

                if (absDtC > maxDtC)
                {
                    sequenceCost += (absDtC - maxDtC) * 5000.0f; 
                }

                float3 trackForward = math.normalize(math.cross(targetRights[step], new float3(0, 1, 0)));
                float3 carForward = new float3(math.sin(simYaw), 0f, math.cos(simYaw));
                
                float headingError = 1.0f - math.dot(trackForward, carForward); 
                sequenceCost += headingError * 500.0f; 
                
                float speedError = math.abs(targetSpeedMs - simVel);
                sequenceCost += speedError * 5.0f;
            }

            sequenceCosts[index] = sequenceCost;
            sequenceInputs[index] = new float3(candidateSteer, candidateThrottle, candidateBrake);
        }
    }
}