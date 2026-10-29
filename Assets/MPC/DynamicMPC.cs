using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;

public class DynamicMPC : MonoBehaviour
{
    [Header("Manual Control")]
    [SerializeField] private bool manualControl;
    [SerializeField] private bool controlWithSteeringWheel;
    [SerializeField, Range(0.0f, 1.0f)] private float maxManualInputThrottle = 1.0f;
    [SerializeField, Range(0.0f, 1.0f)] private float maxManualInputBrake = 1.0f;
    [SerializeField, Range(0.0f, 1.0f)] private float maxManualInputSteer = 1.0f;
    private RCC_InputManager vehicleInputManager;
    
    [Header("Driving Style Boundaries")]
    [SerializeField, Range(30.0f, 150.0f)] private float targetSpeed = 100f;
    [SerializeField, Range(0.25f, 4.0f)] private float maxDtC = 2.0f;
    [SerializeField, Tooltip("Physical half-width of the asphalt road (meters). Hard off-track penalty triggers beyond this.")]
    [Range(1.5f, 5.0f)] private float physicalTrackHalfWidth = 2.5f;
    
    [SerializeField, Tooltip("Pull toward the center of the track on straightaways. Scales with DtC strictness.")]
    [Range(5.0f, 200.0f)] private float centerlinePullPenalty = 45.0f;

    [SerializeField, Tooltip("Heavy penalty applied if speed drops below this (prevents stopping out of bounds).")]
    [Range(5.0f, 50.0f)] private float minimumSpeed = 15f;

    [SerializeField, Tooltip("Desired 0-100 km/h acceleration time. The MPC tracks this exact acceleration ramp.")] 
    [Range(2.0f, 25.0f)] private float accelTime0to100 = 10.0f; 

    [SerializeField, Tooltip("Desired 100-0 km/h deceleration time in seconds.")]
    [Range(1.0f, 15.0f)] private float decelTime100to0 = 4.5f;

    [Header("Dynamic Tire Physics")]
    [SerializeField, Tooltip("Maximum friction coefficient before tires slide. Standard tires = 0.9 to 1.1")]
    [Range(0.5f, 2.0f)] private float tireFrictionCoeff = 1.15f; 
    
    [SerializeField, Tooltip("How much of the tire's grip the MPC is allowed to use in corners. 0.85 = Conservative, 0.95 = Sporty.")]
    [Range(0.7f, 1.0f)] private float maxGripThreshold = 0.92f; 

    [SerializeField, Tooltip("Front Tire Cornering Stiffness (N/rad). Higher = sharper turn-in, less understeer.")]
    private float corneringStiffnessFront = 65000f;
    [SerializeField, Tooltip("Rear Tire Cornering Stiffness (N/rad). Higher = less drifting/oversteer.")]
    private float corneringStiffnessRear = 65000f;

    [Header("MPC Tuning - Steering")]
    [SerializeField, Tooltip("Allowable steering change per tick.")] 
    private float steerSearchWindow = 0.06f; 
    [SerializeField, Tooltip("Penalty for twitchy steering.")] 
    private float steerSmoothnessPenalty = 180.0f; 

    [Header("MPC Tuning - Pedals")]
    [SerializeField, Tooltip("Allowable gas/brake change per tick.")] 
    private float pedalSearchWindow = 0.08f; 
    [SerializeField, Tooltip("Penalty for stomping pedals.")] 
    private float pedalSmoothnessPenalty = 80.0f; 

    [Header("MPC Settings")]
    [SerializeField, Range(1, 50)] private int mpcUpdatesPerSecond = 20;
    [SerializeField, Range(5, 50), Tooltip("Total steps to look ahead for braking.")] 
    private int horizonSteps = 30;
    [SerializeField, Range(1, 15), Tooltip("Steps to hold candidate steering. Keep at 2-3 for responsive corner exits.")] 
    private int steerControlSteps = 3;
    [SerializeField, Range(0.02f, 0.2f)] private float timeStep = 0.1f;
    [SerializeField, Range(1000, 250000), Tooltip("Uses 2D Stratified Grid sampling. 15,625 (125x125) is optimal.")] 
    private int numSamples = 15625;

    [Header("Diagnostics & Extended Logging")]
    [SerializeField] private bool enableConsoleLogging = true;
    [SerializeField, Range(0.05f, 2.0f)] private float logIntervalSeconds = 0.25f;
    [SerializeField] private bool showOnScreenTelemetry = true;

    private RCC_CarControllerV4 carController;
    private RCC_LogitechSteeringWheel steeringWheel; 
    private Rigidbody carRigidBody;
    private GetVehicleData vehicleData;
    
    private const int LUT_SIZE = 2048;
    private const int LUT_MASK = LUT_SIZE - 1;
    private NativeArray<float3> bakedPositions;
    private NativeArray<float3> bakedForwards;
    private NativeArray<float3> bakedRights;
    private NativeArray<float> bakedCurvatures;
    private NativeArray<float> bakedMaxSpeeds;
    private bool isSplineInitialized = false;
    private float trackLength;

    private NativeArray<float> sequenceCosts;
    private NativeArray<float3> sequenceInputs;
    private int allocatedSamples = 0;
    
    private float timeSinceLastMpc = 0f;
    private float timeSinceLastLog = 0f;
    
    private float bestSteer = 0f;
    private float bestThrottle = 0f;
    private float bestBrake = 0f;
    private float lastWinningCost = 0f;
    
    private float dynMass;
    private float dynYawInertia;
    private float theoreticalMaxDriveForce;
    private float calibratedDriveForce;
    private float dynMaxBrakeForce;
    private float dynLf;
    private float dynLr;

    private float cachedProfileTargetSpeed = -1f;
    private float cachedProfileMinSpeed = -1f;
    private float cachedProfileDecel = -1f;
    private float cachedProfileGrip = -1f;

    private string diagRegime = "INIT";
    private float diagProfileSpeedKmh;
    private float diagLocalCurveLimitKmh;
    private float diagImmediateRefSpeedKmh;
    private float diagHorizonEndSpeedKmh;
    private float diagHorizonMinProfileKmh;
    private float diagHorizonMaxDtC;
    private float diagCurrentRadiusM;
    private float diagMinAheadRadiusM;
    private float diagTargetHeadingDeg;
    private float diagActualHeadingDeg;
    private float diagTargetLatVelMs;
    private float diagActualLatVelMs;
    private string lastFormattedTelemetry = "";

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

        if (carController == null || carRigidBody == null || vehicleData == null ||
            vehicleData.roadLayout == null || vehicleData.roadLayout.trackSpline == null)
            return;

        carController.overrideInputs = false;
        carController.externalController = true;
        carController.canGoReverseNow = false;

        dynMass = carRigidBody.mass > 100f ? carRigidBody.mass : 1500f;
        dynYawInertia = carRigidBody.inertiaTensor.y > 100f ? carRigidBody.inertiaTensor.y : (dynMass * 5.0f);

        float wheelRadius = (carController.FrontLeftWheelCollider != null && carController.FrontLeftWheelCollider.WheelCollider != null)
            ? carController.FrontLeftWheelCollider.WheelCollider.radius
            : 0.34f;
        float gear0Ratio = (carController.gears != null && carController.gears.Length > 0)
            ? carController.gears[0].maxRatio
            : 3.5f;
        float totalGearRatio = gear0Ratio * carController.finalRatio;

        theoreticalMaxDriveForce = (carController.maxEngineTorque * totalGearRatio) / Mathf.Max(0.1f, wheelRadius);
        calibratedDriveForce = Mathf.Min(theoreticalMaxDriveForce * 0.55f, tireFrictionCoeff * 9.81f * dynMass);
        dynMaxBrakeForce = (carController.brakeTorque * 4.0f) / Mathf.Max(0.1f, wheelRadius);

        float wheelbase = 2.55f;
        dynLf = wheelbase * 0.5f;
        dynLr = wheelbase * 0.5f;

        if (!isSplineInitialized && vehicleData.roadLayout.trackSpline.Spline != null)
        {
            BakeTrackLookupTable();
            UpdateBakedSpeedProfile(true);
            vehicleData.InitContinuousSplineState();
        }

        if (allocatedSamples != numSamples || !sequenceCosts.IsCreated)
        {
            AllocateSampleArrays(numSamples);
        }

        steeringWheel = carController.GetComponent<RCC_LogitechSteeringWheel>();
        if (steeringWheel != null)
        {
            steeringWheel.overrideFFB = true;
        }

        isInitialized = true;
    }
    
    public void ApplyParameters(float newTargetSpeed, float newMaxDtC, float dtcWeightRatio, float newAccelTime0to100, float smoothNormalized)
    {
        targetSpeed = Mathf.Clamp(newTargetSpeed, 30.0f, 150.0f);
        maxDtC = Mathf.Clamp(newMaxDtC, 0.25f, 4.0f);
        centerlinePullPenalty = Mathf.Lerp(15.0f, 110.0f, Mathf.Clamp01(dtcWeightRatio));

        accelTime0to100 = Mathf.Clamp(newAccelTime0to100, 2.0f, 25.0f);
        decelTime100to0 = Mathf.Clamp(newAccelTime0to100 * 0.45f, 3.0f, 5.5f);
        
        steerSearchWindow = Mathf.Lerp(0.10f, 0.035f, smoothNormalized);
        pedalSearchWindow = Mathf.Lerp(0.14f, 0.06f, smoothNormalized);
        steerSmoothnessPenalty = Mathf.Lerp(80.0f, 320.0f, smoothNormalized);
        pedalSmoothnessPenalty = Mathf.Lerp(30.0f, 140.0f, smoothNormalized);

        if (isSplineInitialized)
        {
            UpdateBakedSpeedProfile(true);
        }
    }
    
    public void ResetState()
    {
        bestSteer = 0f;
        bestThrottle = 0f;
        bestBrake = 0f;
        timeSinceLastMpc = 0f;

        if (carController != null)
        {
            carController.overrideInputs = false;
            carController.externalController = true;
            carController.canGoReverseNow = false;
            carController.currentGear = 0;
            carController.throttleInput = 0f;
            carController.brakeInput = 0f;
            carController.steerInput = 0f;
        }
    }

    void OnEnable()
    {
        if (carController == null && transform.parent != null)
            carController = transform.parent.GetComponent<RCC_CarControllerV4>();

        if (carController != null)
        {
            carController.overrideInputs = false;
            carController.externalController = true;
            carController.canGoReverseNow = false;
            bestSteer = carController.steerInput;
            bestThrottle = carController.throttleInput;
            bestBrake = carController.brakeInput;
        }
    }

    private void BakeTrackLookupTable()
    {
        Spline spline = vehicleData.roadLayout.trackSpline.Spline;
        Transform splineTransform = vehicleData.roadLayout.trackSpline.transform;
        trackLength = spline.GetLength();

        bakedPositions = new NativeArray<float3>(LUT_SIZE, Allocator.Persistent);
        bakedForwards = new NativeArray<float3>(LUT_SIZE, Allocator.Persistent);
        bakedRights = new NativeArray<float3>(LUT_SIZE, Allocator.Persistent);
        bakedCurvatures = new NativeArray<float>(LUT_SIZE, Allocator.Persistent);
        bakedMaxSpeeds = new NativeArray<float>(LUT_SIZE, Allocator.Persistent);

        float4x4 localToWorld = splineTransform.localToWorldMatrix;
        quaternion rot = splineTransform.rotation;

        for (int i = 0; i < LUT_SIZE; i++)
        {
            float t = (float)i / LUT_SIZE;
            float3 localPos = SplineUtility.EvaluatePosition(spline, t);
            float3 localTan = SplineUtility.EvaluateTangent(spline, t);

            float3 worldPos = math.transform(localToWorld, localPos);
            float3 worldFwd = math.normalize(math.rotate(rot, localTan));
            float3 worldRight = math.normalize(math.cross(new float3(0, 1, 0), worldFwd));

            bakedPositions[i] = worldPos;
            bakedForwards[i] = worldFwd;
            bakedRights[i] = worldRight;
        }

        float stepDist = Mathf.Max(0.05f, trackLength / LUT_SIZE);
        int windowShort = Mathf.Clamp(Mathf.RoundToInt(4.0f / stepDist), 2, 32);
        int windowLong = Mathf.Clamp(Mathf.RoundToInt(10.0f / stepDist), 4, 64);

        for (int i = 0; i < LUT_SIZE; i++)
        {
            float3 fPrevS = bakedForwards[(i - windowShort + LUT_SIZE) & LUT_MASK];
            float3 fNextS = bakedForwards[(i + windowShort) & LUT_MASK];
            float curvShort = math.length(fNextS - fPrevS) / (2.0f * windowShort * stepDist);

            float3 fPrevL = bakedForwards[(i - windowLong + LUT_SIZE) & LUT_MASK];
            float3 fNextL = bakedForwards[(i + windowLong) & LUT_MASK];
            float curvLong = math.length(fNextL - fPrevL) / (2.0f * windowLong * stepDist);

            bakedCurvatures[i] = Mathf.Max(curvShort, curvLong);
        }

        isSplineInitialized = true;
    }
    
    private void UpdateBakedSpeedProfile(bool forceRebuild = false)
    {
        if (!isSplineInitialized) return;

        if (!forceRebuild &&
            Mathf.Approximately(cachedProfileTargetSpeed, targetSpeed) &&
            Mathf.Approximately(cachedProfileMinSpeed, minimumSpeed) &&
            Mathf.Approximately(cachedProfileDecel, decelTime100to0) &&
            Mathf.Approximately(cachedProfileGrip, maxGripThreshold * tireFrictionCoeff))
        {
            return;
        }

        cachedProfileTargetSpeed = targetSpeed;
        cachedProfileMinSpeed = minimumSpeed;
        cachedProfileDecel = decelTime100to0;
        cachedProfileGrip = maxGripThreshold * tireFrictionCoeff;

        float targetSpeedMs = targetSpeed / 3.6f;
        float minSpeedMs = minimumSpeed / 3.6f;
        float stepDist = Mathf.Max(0.05f, trackLength / LUT_SIZE);

        float maxCornerLatAccel = maxGripThreshold * tireFrictionCoeff * 9.81f * 0.85f;

        for (int i = 0; i < LUT_SIZE; i++)
        {
            float k = bakedCurvatures[i];
            float vCorner = Mathf.Sqrt(maxCornerLatAccel / Mathf.Max(k, 0.0001f));
            bakedMaxSpeeds[i] = Mathf.Clamp(vCorner, minSpeedMs, targetSpeedMs);
        }

        int brakeBufferSteps = Mathf.Clamp(Mathf.RoundToInt(4.5f / stepDist), 1, 32);
        for (int step = 0; step < brakeBufferSteps; step++)
        {
            for (int i = 0; i < LUT_SIZE; i++)
            {
                int next = (i + 1) & LUT_MASK;
                if (bakedMaxSpeeds[next] < bakedMaxSpeeds[i])
                {
                    bakedMaxSpeeds[i] = bakedMaxSpeeds[next];
                }
            }
        }

        float userTargetDecel = (100f / 3.6f) / decelTime100to0;
        float physicalBrakeDecel = (dynMaxBrakeForce / Mathf.Max(1f, dynMass)) * 0.70f;
        float tireGripDecel = tireFrictionCoeff * 9.81f * 0.80f;
        float effectiveProfileDecel = Mathf.Max(2.5f, Mathf.Min(userTargetDecel, Mathf.Min(physicalBrakeDecel, tireGripDecel)));

        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = LUT_SIZE - 1; i >= 0; i--)
            {
                int next = (i + 1) & LUT_MASK;
                float vNext = bakedMaxSpeeds[next];
                float vMaxEntry = Mathf.Sqrt(vNext * vNext + 2.0f * effectiveProfileDecel * stepDist);
                if (vMaxEntry < bakedMaxSpeeds[i])
                {
                    bakedMaxSpeeds[i] = vMaxEntry;
                }
            }
        }
    }

    private void AllocateSampleArrays(int count)
    {
        if (sequenceCosts.IsCreated) sequenceCosts.Dispose();
        if (sequenceInputs.IsCreated) sequenceInputs.Dispose();

        sequenceCosts = new NativeArray<float>(count, Allocator.Persistent);
        sequenceInputs = new NativeArray<float3>(count, Allocator.Persistent);
        allocatedSamples = count;
    }

    void OnDestroy()
    {
        if (isSplineInitialized)
        {
            if (bakedPositions.IsCreated) bakedPositions.Dispose();
            if (bakedForwards.IsCreated) bakedForwards.Dispose();
            if (bakedRights.IsCreated) bakedRights.Dispose();
            if (bakedCurvatures.IsCreated) bakedCurvatures.Dispose();
            if (bakedMaxSpeeds.IsCreated) bakedMaxSpeeds.Dispose();
        }
        if (sequenceCosts.IsCreated) sequenceCosts.Dispose();
        if (sequenceInputs.IsCreated) sequenceInputs.Dispose();
    }

    void Update()
    {
        if (manualControl)
        {
            ManualControl();
            if (steeringWheel && steeringWheel.overrideFFB)
            {
                steeringWheel.overrideFFB = false;
            }
        }
        else if (steeringWheel && !steeringWheel.overrideFFB)
        {
            steeringWheel.overrideFFB = true;
        }
    }

    void FixedUpdate()
    {
        EnsureInitialized();
        if (manualControl || !isInitialized || !isSplineInitialized)
        {
            return;
        }

        if (carRigidBody.isKinematic)
        {
            return;
        }

        if (carRigidBody.inertiaTensor.y > 100f)
        {
            dynYawInertia = carRigidBody.inertiaTensor.y;
        }

        if (carController.overrideInputs) carController.overrideInputs = false;
        if (!carController.externalController) carController.externalController = true;

        if (numSamples != allocatedSamples)
        {
            AllocateSampleArrays(numSamples);
        }

        vehicleData.UpdateVehicleData(Time.fixedDeltaTime);
        
        float realLongAccel = vehicleData.GetAccellerationVector().z;
        float currentSpeedMs = carController.speed / 3.6f;
        if (bestThrottle > 0.15f && bestBrake < 0.01f && Mathf.Abs(bestSteer) < 0.25f && currentSpeedMs > 1.5f && realLongAccel > 0.2f)
        {
            float estimatedDrag = (0.015f * 9.81f * dynMass) + (0.4f * currentSpeedMs * currentSpeedMs);
            float measuredForcePerThrottle = ((realLongAccel * dynMass) + estimatedDrag) / bestThrottle;
            float maxPossibleGripForce = tireFrictionCoeff * 9.81f * dynMass * 1.2f;
            float clampedMeasured = Mathf.Clamp(measuredForcePerThrottle, dynMass * 1.5f, Mathf.Min(theoreticalMaxDriveForce, maxPossibleGripForce));
            calibratedDriveForce = Mathf.Lerp(calibratedDriveForce, clampedMeasured, Time.fixedDeltaTime * 2.0f);
        }

        timeSinceLastMpc += Time.fixedDeltaTime;
        timeSinceLastLog += Time.fixedDeltaTime;
        float mpcInterval = 1.0f / mpcUpdatesPerSecond;

        if (timeSinceLastMpc >= mpcInterval)
        {
            Vector3 localVel = carRigidBody.transform.InverseTransformDirection(carRigidBody.linearVelocity);
            float currentVlong = localVel.z;
            float currentVlat = localVel.x;
            float currentYawRate = carRigidBody.angularVelocity.y;

            float userTargetAccel = (100f / 3.6f) / accelTime0to100;
            float userTargetDecel = (100f / 3.6f) / decelTime100to0;
            
            float wheelRadius = carController.FrontLeftWheelCollider.WheelCollider.radius;
            dynMaxBrakeForce = (carController.brakeTorque * 4.0f) / Mathf.Max(0.1f, wheelRadius);

            UpdateBakedSpeedProfile(false);
            
            int gridDim = Mathf.Max(11, Mathf.RoundToInt(Mathf.Sqrt(numSamples)));
            if ((gridDim & 1) == 0) gridDim--;

            float hardTrackBoundary = Mathf.Max(maxDtC + 0.35f, physicalTrackHalfWidth);

            MpcEvaluationJob mpcJob = new MpcEvaluationJob
            {
                bakedPositions = bakedPositions,
                bakedForwards = bakedForwards,
                bakedRights = bakedRights,
                bakedCurvatures = bakedCurvatures,
                bakedMaxSpeeds = bakedMaxSpeeds,
                startT = vehicleData.GetCurrentSplineT(),
                trackLength = trackLength,

                currentX = transform.position.x,
                currentY = transform.position.y,
                currentZ = transform.position.z,
                currentYaw = transform.eulerAngles.y * Mathf.Deg2Rad,
                
                currentVlong = currentVlong,
                currentVlat = currentVlat,
                currentYawRate = currentYawRate,
                currentSignedDtC = vehicleData.GetContinuousDtC(),
                
                mass = dynMass,
                yawInertia = dynYawInertia,
                Lf = dynLf,
                Lr = dynLr,
                maxDriveForce = calibratedDriveForce,
                maxBrakeForce = dynMaxBrakeForce,
                Cf = corneringStiffnessFront,
                Cr = corneringStiffnessRear,
                frictionLimit = tireFrictionCoeff * 9.81f * dynMass,
                maxGripThreshold = maxGripThreshold,
                
                targetSpeedMs = targetSpeed / 3.6f,
                minimumSpeedMs = minimumSpeed / 3.6f,
                maxDtC = maxDtC,
                hardTrackBoundary = hardTrackBoundary,
                centerlinePullPenalty = centerlinePullPenalty,
                userTargetAccel = userTargetAccel,
                userTargetDecel = userTargetDecel,
                
                steerSearchWindow = steerSearchWindow,
                pedalSearchWindow = pedalSearchWindow,
                steerSmoothnessPenalty = steerSmoothnessPenalty,
                pedalSmoothnessPenalty = pedalSmoothnessPenalty,
                
                horizonSteps = horizonSteps,
                steerControlSteps = steerControlSteps,
                gridDim = gridDim,
                timeStep = timeStep,
                steerAngleLimit = carController.steerAngle * Mathf.Deg2Rad,
                
                lastSteer = bestSteer,
                lastThrottle = bestThrottle,
                lastBrake = bestBrake,
                
                seed = (uint)(Time.frameCount * 747796405u + 2891336453u), 
                
                sequenceCosts = sequenceCosts,
                sequenceInputs = sequenceInputs
            };
            
            int batchSize = math.max(64, numSamples / 64);
            JobHandle handle = mpcJob.Schedule(numSamples, batchSize);
            handle.Complete();

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
            lastWinningCost = lowestCost;

            SimulateAndDrawWinningTrajectory(mpcInterval, userTargetAccel, userTargetDecel);

            if (timeSinceLastLog >= logIntervalSeconds)
            {
                LogMpcDiagnostics(userTargetAccel, userTargetDecel);
                timeSinceLastLog = 0f;
            }

            timeSinceLastMpc = 0f;
        }
        
        if (!carRigidBody.isKinematic)
        {
            carController.throttleInput = bestThrottle;
            carController.brakeInput = bestBrake;
            carController.steerInput = bestSteer;
        }
        
        if (steeringWheel)
        {
            steeringWheel.steerInput = bestSteer;
        }
    }

    private void SimulateAndDrawWinningTrajectory(float drawDuration, float userTargetAccel, float userTargetDecel)
    {
        float simX = transform.position.x;
        float simZ = transform.position.z;
        float simYaw = transform.eulerAngles.y * Mathf.Deg2Rad;
        
        Vector3 localVel = carRigidBody.transform.InverseTransformDirection(carRigidBody.linearVelocity);
        float v_long = localVel.z;
        float v_lat = localVel.x;
        float yaw_rate = carRigidBody.angularVelocity.y;
        Vector3 lastPoint = transform.position;

        float simT = vehicleData.GetCurrentSplineT();
        int startIdx = (int)(math.frac(simT) * LUT_SIZE) & LUT_MASK;
        Vector3 lastGreenPoint = bakedPositions[startIdx];

        float startCurvature = bakedCurvatures[startIdx];
        diagCurrentRadiusM = startCurvature > 0.0005f ? (1.0f / startCurvature) : 9999f;
        float maxCornerLatAccel = maxGripThreshold * tireFrictionCoeff * 9.81f * 0.85f;
        diagLocalCurveLimitKmh = Mathf.Min(targetSpeed, Mathf.Sqrt(maxCornerLatAccel / Mathf.Max(startCurvature, 0.0001f)) * 3.6f);
        diagProfileSpeedKmh = bakedMaxSpeeds[startIdx] * 3.6f;

        float currentDtC = vehicleData.GetContinuousDtC();
        float absStartDtC = Mathf.Abs(currentDtC);
        float3 startFwd = bakedForwards[startIdx];
        float3 startRight = bakedRights[startIdx];
        Vector3 worldVelNow = carRigidBody.linearVelocity;
        diagActualLatVelMs = Vector3.Dot(worldVelNow, startRight);

        float startStraightness = Mathf.Clamp01(1.0f - (startCurvature * 35.0f));
        float startBoundaryExcess = Mathf.Max(0f, absStartDtC - maxDtC);
        float innerLatVel = -currentDtC * 0.22f * startStraightness;
        float outerLatVel = -Mathf.Sign(currentDtC) * (startBoundaryExcess * 0.45f);
        float maxLatSpeed = Mathf.Max(0.5f, Mathf.Abs(v_long) * 0.10f);
        diagTargetLatVelMs = Mathf.Clamp(innerLatVel + outerLatVel, -maxLatSpeed, maxLatSpeed);

        float innerPull = currentDtC * 0.04f * startStraightness;
        float outerPull = Mathf.Sign(currentDtC) * (startBoundaryExcess * 0.12f);
        float startInterceptPull = Mathf.Clamp(-(innerPull + outerPull), -0.22f, 0.22f);
        diagTargetHeadingDeg = Mathf.Atan(startInterceptPull) * Mathf.Rad2Deg;

        Vector3 carFwdNow = transform.forward;
        diagActualHeadingDeg = Vector3.SignedAngle(new Vector3(startFwd.x, 0f, startFwd.z), new Vector3(carFwdNow.x, 0f, carFwdNow.z), Vector3.up);

        float refSpeed = Mathf.Max(minimumSpeed / 3.6f, v_long);
        float step0TargetSpeed = bakedMaxSpeeds[startIdx];
        if (refSpeed < step0TargetSpeed)
            diagImmediateRefSpeedKmh = Mathf.Min(step0TargetSpeed, refSpeed + userTargetAccel * timeStep) * 3.6f;
        else
            diagImmediateRefSpeedKmh = Mathf.Max(step0TargetSpeed, refSpeed - userTargetDecel * timeStep) * 3.6f;

        float currentStepSteer = bestSteer;
        float maxGrip = tireFrictionCoeff * 9.81f * dynMass;
        const int physicsSubSteps = 2;
        float subDt = timeStep / physicsSubSteps;

        diagHorizonMaxDtC = absStartDtC;
        diagHorizonMinProfileKmh = diagProfileSpeedKmh;
        float maxCurvatureAhead = startCurvature;

        for (int step = 0; step < horizonSteps; step++)
        {
            int curIdx = (int)(simT * LUT_SIZE) & LUT_MASK;
            float trackCurvature = bakedCurvatures[curIdx];
            float stepTargetSpeed = bakedMaxSpeeds[curIdx];
            maxCurvatureAhead = Mathf.Max(maxCurvatureAhead, trackCurvature);
            diagHorizonMinProfileKmh = Mathf.Min(diagHorizonMinProfileKmh, stepTargetSpeed * 3.6f);

            float steerAngle = currentStepSteer * carController.steerAngle * Mathf.Deg2Rad;

            for (int sub = 0; sub < physicsSubSteps; sub++)
            {
                float safeVlong = Mathf.Max(Mathf.Abs(v_long), 1.0f) * Mathf.Sign(v_long);
                if (safeVlong == 0f) safeVlong = 1.0f;

                float alphaF = Mathf.Atan2(v_lat + dynLf * yaw_rate, safeVlong) - steerAngle;
                float alphaR = Mathf.Atan2(v_lat - dynLr * yaw_rate, safeVlong);

                float dragForce = (0.015f * 9.81f * dynMass) + (0.4f * v_long * v_long);
                
                float effectiveThrottle = bestThrottle;
                float effectiveBrake = bestBrake;
                if (step >= steerControlSteps && v_long >= stepTargetSpeed && bestBrake == 0f)
                {
                    effectiveThrottle = Mathf.Min(bestThrottle, Mathf.Clamp01(dragForce / calibratedDriveForce));
                    float speedExcess = v_long - stepTargetSpeed;
                    if (speedExcess > 0.5f)
                    {
                        effectiveThrottle = 0f;
                        effectiveBrake = Mathf.Clamp((speedExcess * 2.5f * dynMass) / dynMaxBrakeForce, 0f, 0.85f);
                    }
                }

                float rawFx = (effectiveThrottle * calibratedDriveForce) - (effectiveBrake * dynMaxBrakeForce);
                float clampedFx = Mathf.Clamp(rawFx, -maxGrip, maxGrip);
                float longGripRatio = Mathf.Abs(clampedFx) / maxGrip;
                float latGripScale = Mathf.Sqrt(Mathf.Max(0.35f, 1.0f - longGripRatio * longGripRatio * 0.60f));

                float FyF = Mathf.Clamp(-corneringStiffnessFront * alphaF, -maxGrip * 0.5f * latGripScale, maxGrip * 0.5f * latGripScale);
                float FyR = Mathf.Clamp(-corneringStiffnessRear * alphaR, -maxGrip * 0.5f * latGripScale, maxGrip * 0.5f * latGripScale);

                float Fx = clampedFx - dragForce;
                if (v_long < 0.1f && effectiveBrake > 0f) Fx = 0f;

                float a_long = (Fx - FyF * Mathf.Sin(steerAngle)) / dynMass + v_lat * yaw_rate;
                float a_lat = (FyF * Mathf.Cos(steerAngle) + FyR) / dynMass - v_long * yaw_rate;
                float yaw_accel = (FyF * Mathf.Cos(steerAngle) * dynLf - FyR * dynLr) / dynYawInertia;

                v_long += a_long * subDt;
                v_lat += a_lat * subDt;
                yaw_rate += yaw_accel * subDt;

                if (v_long < 0f && effectiveThrottle == 0f) { v_long = 0f; v_lat = 0f; yaw_rate = 0f; }

                simYaw += yaw_rate * subDt;
                
                float globalVx = v_long * Mathf.Sin(simYaw) + v_lat * Mathf.Cos(simYaw);
                float globalVz = v_long * Mathf.Cos(simYaw) - v_lat * Mathf.Sin(simYaw);

                simX += globalVx * subDt;
                simZ += globalVz * subDt;
            }

            Vector3 nextPoint = new Vector3(simX, transform.position.y, simZ);
            Debug.DrawLine(lastPoint, nextPoint, Color.red, drawDuration);
            lastPoint = nextPoint;

            simT = math.frac(simT + (v_long * timeStep) / trackLength);
            int lutIdx = (int)(simT * LUT_SIZE) & LUT_MASK;
            float3 targetPos = bakedPositions[lutIdx];
            float3 trackForward = bakedForwards[lutIdx];
            float3 targetRight = bakedRights[lutIdx];

            float3 deltaPos = (float3)nextPoint - targetPos;
            float lonError = math.dot(deltaPos, trackForward);
            simT = math.frac(simT + (lonError * 0.5f) / trackLength);

            float signedDtC = math.dot(deltaPos, targetRight);
            diagHorizonMaxDtC = Mathf.Max(diagHorizonMaxDtC, Mathf.Abs(signedDtC));

            Debug.DrawLine(lastGreenPoint, targetPos, Color.green, drawDuration);
            lastGreenPoint = targetPos;

            if (step + 1 >= steerControlSteps)
            {
                float absDtC = math.abs(signedDtC);
                float straightness = Mathf.Clamp01(1.0f - (trackCurvature * 35.0f));
                float boundaryExcess = math.max(0f, absDtC - maxDtC);
                float stepInnerPull = signedDtC * 0.04f * straightness;
                float stepOuterPull = math.sign(signedDtC) * (boundaryExcess * 0.12f);
                float interceptPull = math.clamp(-(stepInnerPull + stepOuterPull), -0.22f, 0.22f);

                float3 desiredForward = math.normalize(trackForward + targetRight * interceptPull);
                float3 carForward = new float3(math.sin(simYaw), 0f, math.cos(simYaw));
                float3 carRight = new float3(math.cos(simYaw), 0f, -math.sin(simYaw));

                float sinErr = math.dot(desiredForward, carRight);
                float cosErr = math.dot(desiredForward, carForward);
                float signedAngleErr = math.atan2(sinErr, cosErr);

                float speedSteerScale = 1.0f / (1.0f + 0.035f * Mathf.Max(0f, v_long));
                float yawDamping = yaw_rate * 0.18f;
                float desiredFutureSteer = math.clamp(((signedAngleErr / (carController.steerAngle * Mathf.Deg2Rad)) * speedSteerScale) - yawDamping, -1f, 1f);
                currentStepSteer = math.clamp(desiredFutureSteer, currentStepSteer - 0.12f, currentStepSteer + 0.12f);
            }
        }

        diagHorizonEndSpeedKmh = v_long * 3.6f;
        diagMinAheadRadiusM = maxCurvatureAhead > 0.0005f ? (1.0f / maxCurvatureAhead) : 9999f;

        float actualSpeedKmh = carController.speed;
        if (absStartDtC > maxDtC)
            diagRegime = "CORRIDOR RECOVERY (OUTSIDE DtC)";
        else if (diagProfileSpeedKmh < diagLocalCurveLimitKmh - 3.0f && actualSpeedKmh > diagProfileSpeedKmh + 2.0f)
            diagRegime = "BRAKING FOR UPCOMING CURVE";
        else if (diagLocalCurveLimitKmh < targetSpeed - 2.0f && Mathf.Abs(actualSpeedKmh - diagLocalCurveLimitKmh) < 8.0f)
            diagRegime = "CORNERING (CURVATURE SPEED LIMIT)";
        else if (actualSpeedKmh < diagProfileSpeedKmh - 3.0f)
            diagRegime = "ACCELERATING ON RAMP";
        else
            diagRegime = "CRUISING AT TARGET SPEED";
    }

    private void LogMpcDiagnostics(float userTargetAccel, float userTargetDecel)
    {
        float actualSpeedKmh = carController.speed;
        float currentDtC = vehicleData.GetContinuousDtC();
        Vector3 accelVec = vehicleData.GetAccellerationVector();
        float actualLongAccel = accelVec.z;
        float actualLatAccel = accelVec.x;

        float desiredLongAccel = (actualSpeedKmh < diagProfileSpeedKmh - 1.0f)
            ? userTargetAccel
            : (actualSpeedKmh > diagProfileSpeedKmh + 1.0f ? -userTargetDecel : 0.0f);

        lastFormattedTelemetry =
            $"<b>[MPC REGIME: {diagRegime}]</b> (Cost: {lastWinningCost:F0})\n" +
            $"<b>1. SPEED (km/h)</b>    | Actual: <b>{actualSpeedKmh:F1}</b> | Step-1 Ref: <b>{diagImmediateRefSpeedKmh:F1}</b> | Profile Limit: <b>{diagProfileSpeedKmh:F1}</b> | Local Curve Max: <b>{diagLocalCurveLimitKmh:F1}</b> | User Target: <b>{targetSpeed:F0}</b>\n" +
            $"<b>2. HORIZON (3.0s)</b>  | Pred End Speed: <b>{diagHorizonEndSpeedKmh:F1} km/h</b> | Min Profile Ahead: <b>{diagHorizonMinProfileKmh:F1} km/h</b> | Curve Radius Now/Ahead: <b>{diagCurrentRadiusM:F0}m / {diagMinAheadRadiusM:F0}m</b>\n" +
            $"<b>3. LATERAL & YAW</b>   | Signed DtC: <b>{currentDtC:+0.00;-0.00;0.00}m</b> (Max: ±{maxDtC:F2}m, Pred Peak: {diagHorizonMaxDtC:F2}m) | LatVel: <b>{diagActualLatVelMs:+0.00;-0.00;0.00} m/s</b> (Target: {diagTargetLatVelMs:+0.00;-0.00;0.00}) | Heading: <b>{diagActualHeadingDeg:+0.0;-0.0;0.0}°</b> (Target: {diagTargetHeadingDeg:+0.0;-0.0;0.0}°)\n" +
            $"<b>4. ACCEL & INPUTS</b> | Long Accel: <b>{actualLongAccel:+0.00;-0.00;0.00} m/s²</b> (Target: {desiredLongAccel:+0.00;-0.00;0.00}) | Lat Accel: <b>{actualLatAccel:F2} m/s²</b> | Thr: <b>{bestThrottle:F3}</b>, Brk: <b>{bestBrake:F3}</b>, Str: <b>{bestSteer:+0.000;-0.000;0.000}</b>";

        if (enableConsoleLogging)
        {
            Debug.Log($"<color=#00FF88>[DynamicMPC Diagnostics]</color> {lastFormattedTelemetry}");
        }
    }

    void OnGUI()
    {
        if (!showOnScreenTelemetry || manualControl || string.IsNullOrEmpty(lastFormattedTelemetry)) return;

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.alignment = TextAnchor.UpperLeft;
        boxStyle.fontSize = 12;
        boxStyle.richText = true;
        boxStyle.normal.textColor = Color.white;

        GUI.Box(new Rect(15, 15, 900, 80), lastFormattedTelemetry, boxStyle);
    }

    private void ManualControl()
    {
        if (carController == null) return;
        carController.overrideInputs = false;
        carController.externalController = true;

        float thr = 0f, brk = 0f, str = 0f, hbrk = 0f;
        
        if (controlWithSteeringWheel)
        {
            if (vehicleInputManager == null)
            {
                vehicleInputManager = GameObject.FindFirstObjectByType<RCC_InputManager>()?.GetComponent<RCC_InputManager>();
            }

            if (vehicleInputManager != null)
            {
                thr = vehicleInputManager.inputs.throttleInput;
                brk = vehicleInputManager.inputs.brakeInput;
                str = vehicleInputManager.inputs.steerInput;
                hbrk = vehicleInputManager.inputs.handbrakeInput;
            }
        }
        else
        {
            if (Input.GetKey(KeyCode.W)) thr = maxManualInputThrottle;
            else if (Input.GetKey(KeyCode.S)) brk = maxManualInputBrake;
            if (Input.GetKey(KeyCode.A)) str = -maxManualInputSteer;
            else if (Input.GetKey(KeyCode.D)) str = maxManualInputSteer;
            if (Input.GetKey(KeyCode.Space)) hbrk = 1.0f;
        }
        
        carController.throttleInput = thr;
        carController.brakeInput = brk;
        carController.steerInput = str;
        carController.handbrakeInput = hbrk;
    }

    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
    public struct MpcEvaluationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> bakedPositions;
        [ReadOnly] public NativeArray<float3> bakedForwards;
        [ReadOnly] public NativeArray<float3> bakedRights;
        [ReadOnly] public NativeArray<float> bakedCurvatures;
        [ReadOnly] public NativeArray<float> bakedMaxSpeeds;
        public float startT;
        public float trackLength;

        public float currentX, currentY, currentZ, currentYaw;
        public float currentVlong, currentVlat, currentYawRate, currentSignedDtC;
        
        public float mass, yawInertia, Lf, Lr, maxDriveForce, maxBrakeForce, Cf, Cr, frictionLimit, maxGripThreshold;
        
        public float targetSpeedMs, minimumSpeedMs, maxDtC, hardTrackBoundary, centerlinePullPenalty, userTargetAccel, userTargetDecel;
        public float steerSearchWindow, pedalSearchWindow;
        public float steerSmoothnessPenalty, pedalSmoothnessPenalty;
        
        public float timeStep, steerAngleLimit;
        public int horizonSteps;
        public int steerControlSteps;
        public int gridDim;
        public uint seed;

        public float lastSteer, lastThrottle, lastBrake;

        [WriteOnly] public NativeArray<float> sequenceCosts;
        [WriteOnly] public NativeArray<float3> sequenceInputs; 

        public void Execute(int index)
        {
            uint h = seed ^ ((uint)index * 747796405u + 2891336453u);
            h = ((h >> ((int)(h >> 28) + 4)) ^ h) * 277803737u;
            h = (h >> 22) ^ h;
            Unity.Mathematics.Random rand = new Unity.Mathematics.Random(math.max(1u, h));

            float simX = currentX;
            float simZ = currentZ;
            float simYaw = currentYaw;
            
            float v_long = currentVlong;
            float v_lat = currentVlat;
            float yaw_rate = currentYawRate;
            float simT = startT;

            float sequenceCost = 0f;
            float lastPedal = lastThrottle - lastBrake;
            
            int totalGridCells = gridDim * gridDim;
            bool isSeverelyOffTrack = math.abs(currentSignedDtC) > hardTrackBoundary;

            float minPedalDelta = -pedalSearchWindow;
            float maxPedalDelta = (lastPedal < 0f) ? math.max(pedalSearchWindow * 2.0f, 0.15f) : pedalSearchWindow;

            float candidateSteer;
            float candidatePedal;

            if (index < totalGridCells && !isSeverelyOffTrack)
            {
                int gx = index % gridDim;
                int gy = index / gridDim;
                float u = (float)gx / math.max(1, gridDim - 1);
                float v = (float)gy / math.max(1, gridDim - 1);

                candidateSteer = math.clamp(lastSteer + math.lerp(-steerSearchWindow, steerSearchWindow, u), -1f, 1f);
                candidatePedal = math.clamp(lastPedal + math.lerp(minPedalDelta, maxPedalDelta, v), -1f, 1f);
            }
            else
            {
                float u = rand.NextFloat();
                float v = rand.NextFloat();
                float wideSteerWindow = isSeverelyOffTrack ? math.max(steerSearchWindow * 2.0f, 0.20f) : steerSearchWindow;
                float widePedalWindow = pedalSearchWindow * 1.8f;

                candidateSteer = math.clamp(lastSteer + math.lerp(-wideSteerWindow, wideSteerWindow, u), -1f, 1f);
                candidatePedal = math.clamp(lastPedal + math.lerp(-widePedalWindow, widePedalWindow, v), -1f, 1f);
            }
            
            if (currentVlong < minimumSpeedMs)
            {
                candidatePedal = math.max(0.25f, math.abs(candidatePedal));
            }
            else if (isSeverelyOffTrack && currentVlong < minimumSpeedMs * 1.35f)
            {
                candidatePedal = math.max(0.15f, candidatePedal);
            }

            float candidateThrottle = math.select(0f, candidatePedal, candidatePedal > 0f);
            float candidateBrake = math.select(0f, -candidatePedal, candidatePedal < 0f);
            
            float steerJerk = math.abs(candidateSteer - lastSteer);
            float pedalJerk = math.abs(candidatePedal - lastPedal);
            
            float highSpeedSteerScale = 1.0f + (math.max(0f, currentVlong) * 0.05f) + (candidateBrake * 1.5f);
            sequenceCost += ((steerJerk + steerJerk * steerJerk * 12.0f) * steerSmoothnessPenalty * highSpeedSteerScale) + (pedalJerk * pedalSmoothnessPenalty);

            float currentStepSteer = candidateSteer;
            
            const int physicsSubSteps = 2;
            float subDt = timeStep / physicsSubSteps;
            float invTrackLength = 1.0f / trackLength;
            float wheelbase = Lf + Lr;

            float maxFyF = frictionLimit * (Lr / wheelbase);
            float maxFyR = frictionLimit * (Lf / wheelbase);
            float baseRollingDrag = 0.015f * 9.81f * mass;
            float maxAllowedLatAccel = (frictionLimit / mass) * maxGripThreshold;

            float refSpeed = math.max(minimumSpeedMs, currentVlong);

            for (int step = 0; step < horizonSteps; step++)
            {
                float exactStartIdx = simT * LUT_SIZE;
                int sIdx0 = (int)exactStartIdx & LUT_MASK;
                int sIdx1 = (sIdx0 + 1) & LUT_MASK;
                float sFrac = exactStartIdx - (int)exactStartIdx;

                float trackCurvature = math.lerp(bakedCurvatures[sIdx0], bakedCurvatures[sIdx1], sFrac);
                float stepMaxSpeed = math.lerp(bakedMaxSpeeds[sIdx0], bakedMaxSpeeds[sIdx1], sFrac);
                
                float straightnessFactor = math.saturate(1.0f - (trackCurvature * 35.0f));

                float steerAngle = currentStepSteer * steerAngleLimit;
                float stepAlong = 0f;
                float stepAlat = 0f;
                float stepYawAccel = 0f;
                float globalVx = 0f;
                float globalVz = 0f;
                math.sincos(steerAngle, out float sinSteer, out float cosSteer);

                for (int sub = 0; sub < physicsSubSteps; sub++)
                {
                    float safeVlong = math.max(math.abs(v_long), 1.0f) * math.select(1.0f, -1.0f, v_long < 0f);

                    float alphaF = math.atan2(v_lat + Lf * yaw_rate, safeVlong) - steerAngle;
                    float alphaR = math.atan2(v_lat - Lr * yaw_rate, safeVlong);

                    float dragForce = baseRollingDrag + (0.4f * v_long * v_long);
                    
                    float effectiveThrottle = candidateThrottle;
                    float effectiveBrake = candidateBrake;

                    if (step >= steerControlSteps && v_long >= stepMaxSpeed && candidateBrake == 0f)
                    {
                        float cruiseThrottle = math.clamp(dragForce / maxDriveForce, 0f, 1f);
                        effectiveThrottle = math.min(candidateThrottle, cruiseThrottle);

                        float speedExcess = v_long - stepMaxSpeed;
                        if (speedExcess > 0.5f)
                        {
                            effectiveThrottle = 0f;
                            effectiveBrake = math.clamp((speedExcess * 2.5f * mass) / maxBrakeForce, 0f, 0.85f);
                        }
                    }

                    float rawFx = (effectiveThrottle * maxDriveForce) - (effectiveBrake * maxBrakeForce);
                    float clampedFx = math.clamp(rawFx, -frictionLimit, frictionLimit);

                    float longGripUsage = math.abs(clampedFx) / frictionLimit;
                    float latGripScale = math.sqrt(math.max(0.35f, 1.0f - (longGripUsage * longGripUsage * 0.60f)));

                    float rawFyF = -Cf * alphaF;
                    float rawFyR = -Cr * alphaR;

                    float FyF = math.clamp(rawFyF, -maxFyF * latGripScale, maxFyF * latGripScale);
                    float FyR = math.clamp(rawFyR, -maxFyR * latGripScale, maxFyR * latGripScale);

                    float Fx = clampedFx - dragForce;
                    if (v_long < 0.1f && effectiveBrake > 0f) Fx = 0f;

                    float a_long = (Fx - FyF * sinSteer) / mass + v_lat * yaw_rate;
                    float a_lat = (FyF * cosSteer + FyR) / mass - v_long * yaw_rate;
                    float yaw_accel = (FyF * cosSteer * Lf - FyR * Lr) / yawInertia;

                    v_long += a_long * subDt;
                    v_lat += a_lat * subDt;
                    yaw_rate += yaw_accel * subDt;

                    if (v_long < 0f && effectiveThrottle == 0f) 
                    { 
                        v_long = 0f; 
                        v_lat = 0f; 
                        yaw_rate = 0f; 
                    }

                    simYaw += yaw_rate * subDt;
                    math.sincos(simYaw, out float sinYaw, out float cosYaw);
                    
                    globalVx = v_long * sinYaw + v_lat * cosYaw;
                    globalVz = v_long * cosYaw - v_lat * sinYaw;

                    simX += globalVx * subDt;
                    simZ += globalVz * subDt;
                    stepAlong = a_long;
                    stepAlat = (FyF * cosSteer + FyR) / mass;
                    stepYawAccel = yaw_accel;
                }

                float absLatG = math.abs(stepAlat);
                if (absLatG > maxAllowedLatAccel)
                {
                    float overGrip = (absLatG - maxAllowedLatAccel) / math.max(1.0f, maxAllowedLatAccel);
                    sequenceCost += overGrip * overGrip * 12000.0f;
                }
                
                simT = math.frac(simT + (v_long * timeStep) * invTrackLength);
                float exactIdx = simT * LUT_SIZE;
                int idx0 = (int)exactIdx & LUT_MASK;
                int idx1 = (idx0 + 1) & LUT_MASK;
                float frac = exactIdx - (int)exactIdx;

                float3 targetPos = math.lerp(bakedPositions[idx0], bakedPositions[idx1], frac);
                float3 trackForward = math.lerp(bakedForwards[idx0], bakedForwards[idx1], frac);
                float3 targetRight = math.lerp(bakedRights[idx0], bakedRights[idx1], frac);

                float3 simWorldPos = new float3(simX, currentY, simZ);
                float3 deltaPos = simWorldPos - targetPos;

                float lonError = math.dot(deltaPos, trackForward);
                simT = math.frac(simT + (lonError * 0.5f) * invTrackLength);

                float signedDtC = math.dot(deltaPos, targetRight);
                float absDtC = math.abs(signedDtC);
                
                float3 worldVel = new float3(globalVx, 0f, globalVz);
                float trackLatVel = math.dot(worldVel, targetRight);

                sequenceCost += (signedDtC * signedDtC) * centerlinePullPenalty * straightnessFactor;

                float boundaryExcess = math.max(0f, absDtC - maxDtC);
                float innerLatVel = -signedDtC * 0.22f * straightnessFactor;
                float outerLatVel = -math.sign(signedDtC) * (boundaryExcess * 0.45f);
                float maxConvergenceLatSpeed = math.max(0.5f, v_long * 0.10f);
                float desiredLatVel = math.clamp(innerLatVel + outerLatVel, -maxConvergenceLatSpeed, maxConvergenceLatSpeed);
                float latVelError = trackLatVel - desiredLatVel;
                sequenceCost += (latVelError * latVelError) * 35.0f;

                if (boundaryExcess > 0f)
                {
                    sequenceCost += (boundaryExcess * boundaryExcess) * 2800.0f;
                }

                if (absDtC > hardTrackBoundary)
                {
                    float offTrackExcess = absDtC - hardTrackBoundary;
                    float horizonDiscount = (step < 12) ? 1.0f : 0.25f;
                    sequenceCost += (200000.0f + offTrackExcess * 30000.0f) * horizonDiscount;
                }

                float innerPull = signedDtC * 0.04f * straightnessFactor;
                float outerPull = math.sign(signedDtC) * (boundaryExcess * 0.12f);
                float interceptPull = math.clamp(-(innerPull + outerPull), -0.22f, 0.22f);
                float3 desiredForward = math.normalize(trackForward + targetRight * interceptPull);

                math.sincos(simYaw, out float carSinYaw, out float carCosYaw);
                float3 carForward = new float3(carSinYaw, 0f, carCosYaw);
                float3 carRight = new float3(carCosYaw, 0f, -carSinYaw);

                float headingError = 1.0f - math.dot(desiredForward, carForward); 
                float headingWeight = (step < steerControlSteps) ? 950.0f : 380.0f;
                sequenceCost += headingError * headingWeight; 

                if (step < steerControlSteps)
                {
                    sequenceCost += math.abs(yaw_rate) * straightnessFactor * (40.0f + candidateBrake * 80.0f);
                    sequenceCost += (stepYawAccel * stepYawAccel) * 4.0f;
                }

                float effectiveTargetSpeed = (absDtC > hardTrackBoundary) 
                    ? math.min(stepMaxSpeed, math.max(minimumSpeedMs * 1.4f, 14.0f)) 
                    : stepMaxSpeed;

                if (refSpeed < effectiveTargetSpeed)
                {
                    refSpeed = math.min(effectiveTargetSpeed, refSpeed + userTargetAccel * timeStep);
                }
                else
                {
                    refSpeed = math.max(effectiveTargetSpeed, refSpeed - userTargetDecel * timeStep);
                }
                
                refSpeed = math.min(refSpeed, effectiveTargetSpeed);

                float speedError = math.abs(refSpeed - v_long);
                sequenceCost += speedError * 65.0f;
                
                if (v_long > effectiveTargetSpeed)
                {
                    float overSpeed = v_long - effectiveTargetSpeed;
                    sequenceCost += (overSpeed + overSpeed * overSpeed) * 180.0f;
                }

                if (stepAlong > userTargetAccel)
                {
                    sequenceCost += (stepAlong - userTargetAccel) * 350.0f; 
                }
                
                if (v_long < minimumSpeedMs)
                {
                    float stallDiff = minimumSpeedMs - v_long;
                    sequenceCost += (stallDiff * stallDiff + stallDiff) * 25000.0f;
                }
                
                if (step + 1 >= steerControlSteps)
                {
                    float sinErr = math.dot(desiredForward, carRight);
                    float cosErr = math.dot(desiredForward, carForward);
                    float signedAngleErr = math.atan2(sinErr, cosErr);

                    float speedSteerScale = 1.0f / (1.0f + 0.035f * math.max(0f, v_long));
                    float yawDamping = yaw_rate * 0.18f;
                    float desiredFutureSteer = math.clamp(((signedAngleErr / steerAngleLimit) * speedSteerScale) - yawDamping, -1f, 1f);
                    currentStepSteer = math.clamp(desiredFutureSteer, currentStepSteer - 0.12f, currentStepSteer + 0.12f);
                }
            }

            sequenceCosts[index] = sequenceCost;
            sequenceInputs[index] = new float3(candidateSteer, candidateThrottle, candidateBrake);
        }
    }
}