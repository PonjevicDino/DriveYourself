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
    
    [SerializeField, Tooltip("Gentle pull toward the center of the track ONLY on straightaways. Automatically turns off in curves.")]
    [Range(0.0f, 200.0f)] private float centerlinePullPenalty = 45.0f;

    [SerializeField, Tooltip("Heavy penalty applied if speed drops below this (prevents stopping out of bounds).")]
    [Range(5.0f, 50.0f)] private float minimumSpeed = 10f;

    [SerializeField, Tooltip("Desired 0-100 km/h acceleration time. The MPC tracks this exact acceleration ramp.")] 
    [Range(2.0f, 25.0f)] private float accelTime0to100 = 10.0f; 

    [SerializeField, Tooltip("Desired 100-0 km/h deceleration time in seconds. MPC will try to brake this smoothly, but will brake harder if the physical corner requires it.")]
    [Range(1.0f, 15.0f)] private float decelTime100to0 = 10.0f;

    [Header("Dynamic Tire Physics")]
    [SerializeField, Tooltip("Maximum friction coefficient before tires slide. Standard tires = 0.9 to 1.1")]
    [Range(0.5f, 2.0f)] private float tireFrictionCoeff = 1.15f; 
    
    [SerializeField, Tooltip("How much of the tire's grip the MPC is allowed to use in corners. 0.85 = Conservative, 0.95 = Sporty.")]
    [Range(0.7f, 1.0f)] private float maxGripThreshold = 0.90f; 

    [SerializeField, Tooltip("Front Tire Cornering Stiffness (N/rad). Higher = sharper turn-in, less understeer.")]
    private float corneringStiffnessFront = 65000f;
    [SerializeField, Tooltip("Rear Tire Cornering Stiffness (N/rad). Higher = less drifting/oversteer.")]
    private float corneringStiffnessRear = 65000f;

    [Header("MPC Tuning - Steering")]
    [SerializeField, Tooltip("Allowable steering change per tick.")] 
    private float steerSearchWindow = 0.05f; 
    [SerializeField, Tooltip("Penalty for twitchy steering.")] 
    private float steerSmoothnessPenalty = 250.0f; 

    [Header("MPC Tuning - Pedals")]
    [SerializeField, Tooltip("Allowable gas/brake change per tick.")] 
    private float pedalSearchWindow = 0.05f; 
    [SerializeField, Tooltip("Penalty for stomping pedals.")] 
    private float pedalSmoothnessPenalty = 100.0f; 

    [Header("MPC Settings")]
    [SerializeField, Range(1, 50)] private int mpcUpdatesPerSecond = 20;
    [SerializeField, Range(5, 50), Tooltip("Total steps to look ahead for braking.")] 
    private int horizonSteps = 30;
    [SerializeField, Range(1, 15), Tooltip("Steps to hold candidate steering. Keep at 2-3 for responsive corner exits.")] 
    private int steerControlSteps = 3;
    [SerializeField, Range(0.02f, 0.2f)] private float timeStep = 0.1f;
    [SerializeField, Range(1000, 250000), Tooltip("Uses 2D Stratified Grid sampling. 15,625 (125x125) is optimal.")] 
    private int numSamples = 15625;

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
    
    private float bestSteer = 0f;
    private float bestThrottle = 0f;
    private float bestBrake = 0f;
    
    private float dynMass;
    private float dynYawInertia;
    private float theoreticalMaxDriveForce;
    private float calibratedDriveForce;
    private float dynMaxBrakeForce;
    private float dynLf;
    private float dynLr;

    void Start()
    {
        carController = transform.parent.GetComponent<RCC_CarControllerV4>();
        carRigidBody = carController.GetComponent<Rigidbody>();
        vehicleData = GetComponent<GetVehicleData>();
        carController.overrideInputs = true;
        
        dynMass = carRigidBody.mass;
        dynYawInertia = carRigidBody.inertiaTensor.y > 0 ? carRigidBody.inertiaTensor.y : (dynMass * 5.0f);
        
        float wheelRadius = carController.FrontLeftWheelCollider.WheelCollider.radius; 
        float totalGearRatio = carController.gears[0].maxRatio * carController.finalRatio; 
        
        theoreticalMaxDriveForce = (carController.maxEngineTorque * totalGearRatio) / wheelRadius; 
        calibratedDriveForce = Mathf.Min(theoreticalMaxDriveForce * 0.55f, tireFrictionCoeff * 9.81f * dynMass);
        dynMaxBrakeForce = (carController.brakeTorque * 4.0f) / wheelRadius; 
        
        float wheelbase = 2.55f;
        dynLf = wheelbase * 0.5f; 
        dynLr = wheelbase * 0.5f;

        if (vehicleData.roadLayout.trackSpline.Spline != null)
        {
            BakeTrackLookupTable();
            UpdateBakedSpeedProfile();
            vehicleData.InitContinuousSplineState(); 
        }
        
        AllocateSampleArrays(numSamples);
        
        steeringWheel = carController.GetComponent<RCC_LogitechSteeringWheel>();
        if (steeringWheel != null) 
        { 
            steeringWheel.overrideFFB = true;
        }
    }
    
    public void ApplyParameters(float newTargetSpeed, float newMaxDtC, float dtcWeightRatio, float newAccelTime0to100, float smoothNormalized)
    {
        targetSpeed = Mathf.Clamp(newTargetSpeed, 30.0f, 150.0f);
        maxDtC = Mathf.Clamp(newMaxDtC, 0.25f, 4.0f);
        centerlinePullPenalty = Mathf.Lerp(10.0f, 120.0f, Mathf.Clamp01(dtcWeightRatio));

        accelTime0to100 = Mathf.Clamp(newAccelTime0to100, 2.0f, 25.0f);
        decelTime100to0 = Mathf.Clamp(newAccelTime0to100, 1.0f, 15.0f);
        
        steerSearchWindow = Mathf.Lerp(0.10f, 0.025f, smoothNormalized);
        pedalSearchWindow = Mathf.Lerp(0.10f, 0.025f, smoothNormalized);
        steerSmoothnessPenalty = Mathf.Lerp(80.0f, 500.0f, smoothNormalized);
        pedalSmoothnessPenalty = Mathf.Lerp(40.0f, 250.0f, smoothNormalized);

        if (isSplineInitialized)
        {
            UpdateBakedSpeedProfile();
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
            carController.OverrideInputs(new RCC_Inputs());
        }
    }

    void OnEnable()
    {
        if (carController == null && transform.parent != null)
            carController = transform.parent.GetComponent<RCC_CarControllerV4>();

        if (carController != null)
        {
            carController.overrideInputs = true;
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
    
    private void UpdateBakedSpeedProfile()
    {
        if (!isSplineInitialized) return;

        float targetSpeedMs = targetSpeed / 3.6f;
        float minSpeedMs = minimumSpeed / 3.6f;
        float stepDist = Mathf.Max(0.05f, trackLength / LUT_SIZE);

        float maxCornerLatAccel = maxGripThreshold * tireFrictionCoeff * 9.81f * 0.80f;

        for (int i = 0; i < LUT_SIZE; i++)
        {
            float k = bakedCurvatures[i];
            float vCorner = Mathf.Sqrt(maxCornerLatAccel / Mathf.Max(k, 0.0001f));
            bakedMaxSpeeds[i] = Mathf.Clamp(vCorner, minSpeedMs, targetSpeedMs);
        }

        int brakeBufferSteps = Mathf.Clamp(Mathf.RoundToInt(6.0f / stepDist), 1, 40);
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
        float physicalBrakeDecel = (dynMaxBrakeForce / dynMass) * 0.65f;
        float tireGripDecel = tireFrictionCoeff * 9.81f * 0.75f;
        float effectiveProfileDecel = Mathf.Max(1.0f, Mathf.Min(userTargetDecel, Mathf.Min(physicalBrakeDecel, tireGripDecel)));

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
        if (manualControl || !isSplineInitialized) return;

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
            dynMaxBrakeForce = (carController.brakeTorque * 4.0f) / wheelRadius;

            UpdateBakedSpeedProfile();
            
            int gridDim = Mathf.Max(11, Mathf.RoundToInt(Mathf.Sqrt(numSamples)));
            if ((gridDim & 1) == 0) gridDim--;

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

            DrawTrajectories(mpcInterval);
            LogMpcState(userTargetAccel, userTargetDecel);

            timeSinceLastMpc = 0f;
        }
        
        RCC_Inputs appliedInputs = new RCC_Inputs();
        appliedInputs.throttleInput = bestThrottle;
        appliedInputs.brakeInput = bestBrake;
        appliedInputs.steerInput = bestSteer;
        
        carController.OverrideInputs(appliedInputs);
        
        if (steeringWheel)
        {
            steeringWheel.steerInput = bestSteer;
        }
    }

    private void DrawTrajectories(float drawDuration)
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

        float currentStepSteer = bestSteer;
        float maxGrip = tireFrictionCoeff * 9.81f * dynMass;
        const int physicsSubSteps = 2;
        float subDt = timeStep / physicsSubSteps;

        for (int step = 0; step < horizonSteps; step++)
        {
            int curIdx = (int)(simT * LUT_SIZE) & LUT_MASK;
            float trackCurvature = bakedCurvatures[curIdx];
            float stepTargetSpeed = bakedMaxSpeeds[curIdx];
            float steerAngle = currentStepSteer * carController.steerAngle * Mathf.Deg2Rad;

            for (int sub = 0; sub < physicsSubSteps; sub++)
            {
                float safeVlong = Mathf.Max(Mathf.Abs(v_long), 1.0f) * Mathf.Sign(v_long);
                if (safeVlong == 0f) safeVlong = 1.0f;

                float alphaF = Mathf.Atan2(v_lat + dynLf * yaw_rate, safeVlong) - steerAngle;
                float alphaR = Mathf.Atan2(v_lat - dynLr * yaw_rate, safeVlong);

                float dragForce = (0.015f * 9.81f * dynMass) + (0.4f * v_long * v_long);
                
                float effectiveThrottle = bestThrottle;
                if (step >= 8 && v_long >= stepTargetSpeed && bestBrake == 0f)
                {
                    effectiveThrottle = Mathf.Min(bestThrottle, Mathf.Clamp01(dragForce / calibratedDriveForce));
                }

                float rawFx = (effectiveThrottle * calibratedDriveForce) - (bestBrake * dynMaxBrakeForce);
                float clampedFx = Mathf.Clamp(rawFx, -maxGrip, maxGrip);
                float longGripRatio = Mathf.Abs(clampedFx) / maxGrip;
                float latGripScale = Mathf.Sqrt(Mathf.Max(0.25f, 1.0f - longGripRatio * longGripRatio * 0.70f));

                float FyF = Mathf.Clamp(-corneringStiffnessFront * alphaF, -maxGrip * 0.5f * latGripScale, maxGrip * 0.5f * latGripScale);
                float FyR = Mathf.Clamp(-corneringStiffnessRear * alphaR, -maxGrip * 0.5f * latGripScale, maxGrip * 0.5f * latGripScale);

                float Fx = clampedFx - dragForce;
                if (v_long < 0.1f && bestBrake > 0f) Fx = 0f;

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

            Debug.DrawLine(lastGreenPoint, targetPos, Color.green, drawDuration);
            lastGreenPoint = targetPos;

            if (step + 1 >= steerControlSteps)
            {
                float signedDtC = math.dot(deltaPos, targetRight);
                float absDtC = math.abs(signedDtC);
                
                float straightness = Mathf.Clamp01(1.0f - (trackCurvature * 35.0f));
                float gentleCenterPull = -signedDtC * 0.08f * straightness;

                float interceptPull = gentleCenterPull;
                if (absDtC > maxDtC * 0.5f)
                {
                    float excess = (absDtC - maxDtC * 0.5f) / math.max(0.5f, maxDtC);
                    interceptPull = -math.sign(signedDtC) * math.clamp(excess, 0f, 1.0f);
                }

                float3 desiredForward = math.normalize(trackForward + targetRight * interceptPull);
                float3 carForward = new float3(math.sin(simYaw), 0f, math.cos(simYaw));
                float3 carRight = new float3(math.cos(simYaw), 0f, -math.sin(simYaw));

                float sinErr = math.dot(desiredForward, carRight);
                float cosErr = math.dot(desiredForward, carForward);
                float signedAngleErr = math.atan2(sinErr, cosErr);

                float speedSteerScale = 1.0f / (1.0f + 0.04f * Mathf.Max(0f, v_long));
                float yawDamping = yaw_rate * 0.18f;
                float desiredFutureSteer = math.clamp(((signedAngleErr / (carController.steerAngle * Mathf.Deg2Rad)) * speedSteerScale) - yawDamping, -1f, 1f);
                currentStepSteer = math.clamp(desiredFutureSteer, currentStepSteer - 0.12f, currentStepSteer + 0.12f);
            }
        }
    }

    private void LogMpcState(float userTargetAccel, float userTargetDecel)
    {
        float currentDtC = vehicleData.GetContinuousDtC();
        int curIdx = (int)(math.frac(vehicleData.GetCurrentSplineT()) * LUT_SIZE) & LUT_MASK;
        float profileSpeedKmh = bakedMaxSpeeds[curIdx] * 3.6f;

        Debug.Log($"<color=#00FF00><b>MPC OUTPUT</b></color> | Thr: {bestThrottle:F3}, Brk: {bestBrake:F3}, Str: {bestSteer:F3}\n" +
                  $"<color=#00FFFF><b>VEHICLE STATE</b></color> | Speed: {carController.speed:F1} km/h (Profile Limit: {profileSpeedKmh:F1} km/h), Signed DtC: {currentDtC:F2}m\n" +
                  $"<color=#FFA500><b>ACCEL TRACKING</b></color> | Target Accel: {userTargetAccel:F2} m/s² | Target Decel: {userTargetDecel:F2} m/s² | Physical Brake Limit: {dynMaxBrakeForce:F0}N");
    }

    private void ManualControl()
    {
        RCC_Inputs inputs = new RCC_Inputs();
        
        if (controlWithSteeringWheel)
        {
            if (vehicleInputManager == null)
            {
                vehicleInputManager = GameObject.FindFirstObjectByType<RCC_InputManager>()?.GetComponent<RCC_InputManager>();
            }

            if (vehicleInputManager != null)
            {
                inputs.throttleInput = vehicleInputManager.inputs.throttleInput;
                inputs.brakeInput = vehicleInputManager.inputs.brakeInput;
                inputs.steerInput = vehicleInputManager.inputs.steerInput;
                inputs.handbrakeInput = vehicleInputManager.inputs.handbrakeInput;
            }
        }
        else
        {
            if (Input.GetKey(KeyCode.W)) inputs.throttleInput = maxManualInputThrottle;
            else if (Input.GetKey(KeyCode.S)) inputs.brakeInput = maxManualInputBrake;
            if (Input.GetKey(KeyCode.A)) inputs.steerInput = -maxManualInputSteer;
            else if (Input.GetKey(KeyCode.D)) inputs.steerInput = maxManualInputSteer;
            if (Input.GetKey(KeyCode.Space)) inputs.handbrakeInput = 1.0f;
        }
        
        carController.OverrideInputs(inputs);
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
        
        public float targetSpeedMs, minimumSpeedMs, maxDtC, centerlinePullPenalty, userTargetAccel, userTargetDecel;
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
            bool isOutOfBounds = math.abs(currentSignedDtC) > maxDtC;

            float dynamicSteerWindow = steerSearchWindow + (math.abs(lastSteer) * 0.05f);

            float candidateSteer;
            float candidatePedal;

            if (index < totalGridCells && !isOutOfBounds)
            {
                int gx = index % gridDim;
                int gy = index / gridDim;
                float u = (float)gx / math.max(1, gridDim - 1);
                float v = (float)gy / math.max(1, gridDim - 1);

                candidateSteer = math.clamp(lastSteer + math.lerp(-dynamicSteerWindow, dynamicSteerWindow, u), -1f, 1f);
                candidatePedal = math.clamp(lastPedal + math.lerp(-pedalSearchWindow, pedalSearchWindow, v), -1f, 1f);
            }
            else
            {
                float u = rand.NextFloat();
                float v = rand.NextFloat();
                float wideSteerWindow = isOutOfBounds ? math.max(steerSearchWindow * 3.0f, 0.35f) : dynamicSteerWindow * 1.4f;
                float widePedalWindow = pedalSearchWindow * 1.5f;

                candidateSteer = math.clamp(lastSteer + math.lerp(-wideSteerWindow, wideSteerWindow, u), -1f, 1f);
                candidatePedal = math.clamp(lastPedal + math.lerp(-widePedalWindow, widePedalWindow, v), -1f, 1f);
            }
            
            if (currentVlong < minimumSpeedMs)
            {
                candidatePedal = math.max(0.25f, math.abs(candidatePedal));
            }
            else if (isOutOfBounds && currentVlong < minimumSpeedMs * 1.35f)
            {
                candidatePedal = math.max(0.15f, candidatePedal);
            }

            float candidateThrottle = math.select(0f, candidatePedal, candidatePedal > 0f);
            float candidateBrake = math.select(0f, -candidatePedal, candidatePedal < 0f);
            
            float steerJerk = math.abs(candidateSteer - lastSteer);
            float pedalJerk = math.abs(candidatePedal - lastPedal);
            
            float highSpeedSteerScale = 1.0f + (math.max(0f, currentVlong) * 0.06f) + (candidateBrake * 2.5f);
            sequenceCost += ((steerJerk + steerJerk * steerJerk * 10.0f) * steerSmoothnessPenalty * highSpeedSteerScale) + (pedalJerk * pedalSmoothnessPenalty);

            float currentStepSteer = candidateSteer;
            
            const int physicsSubSteps = 2;
            float subDt = timeStep / physicsSubSteps;
            float invTrackLength = 1.0f / trackLength;
            float wheelbase = Lf + Lr;

            float maxFyF = frictionLimit * (Lr / wheelbase);
            float maxFyR = frictionLimit * (Lf / wheelbase);
            float baseRollingDrag = 0.015f * 9.81f * mass;

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
                    if (step >= 8 && v_long >= stepMaxSpeed && candidateBrake == 0f)
                    {
                        float cruiseThrottle = math.clamp(dragForce / maxDriveForce, 0f, 1f);
                        effectiveThrottle = math.min(candidateThrottle, cruiseThrottle);
                    }

                    float rawFx = (effectiveThrottle * maxDriveForce) - (candidateBrake * maxBrakeForce);
                    float clampedFx = math.clamp(rawFx, -frictionLimit, frictionLimit);

                    float longGripUsage = math.abs(clampedFx) / frictionLimit;
                    float latGripScale = math.sqrt(math.max(0.25f, 1.0f - (longGripUsage * longGripUsage * 0.70f)));

                    float rawFyF = -Cf * alphaF;
                    float rawFyR = -Cr * alphaR;
                    
                    float gripUsageF = math.abs(rawFyF) / maxFyF;
                    float gripUsageR = math.abs(rawFyR) / maxFyR;
                    float maxGripUsage = math.max(gripUsageF, gripUsageR);
                    
                    if (maxGripUsage > maxGripThreshold && v_long > 6.0f)
                    {
                        sequenceCost += (maxGripUsage - maxGripThreshold) * 25000.0f;
                    }

                    float FyF = math.clamp(rawFyF, -maxFyF * latGripScale, maxFyF * latGripScale);
                    float FyR = math.clamp(rawFyR, -maxFyR * latGripScale, maxFyR * latGripScale);

                    float Fx = clampedFx - dragForce;
                    if (v_long < 0.1f && candidateBrake > 0f) Fx = 0f;

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
                float latVelAwayFromCenter = math.dot(worldVel, math.sign(signedDtC) * targetRight);
                
                sequenceCost += (signedDtC * signedDtC) * centerlinePullPenalty * straightnessFactor;
                
                if (step < steerControlSteps)
                {
                    sequenceCost += math.abs(yaw_rate) * straightnessFactor * (45.0f + candidateBrake * 90.0f);
                }

                if (absDtC > maxDtC * 0.65f)
                {
                    float marginExcess = absDtC - (maxDtC * 0.65f);
                    sequenceCost += (marginExcess * marginExcess) * 1500.0f;

                    if (latVelAwayFromCenter > 0f)
                    {
                        sequenceCost += latVelAwayFromCenter * marginExcess * 500.0f;
                    }
                }

                float gentleCenterPull = -signedDtC * 0.08f * straightnessFactor;
                float interceptPull = gentleCenterPull;

                if (absDtC > maxDtC)
                {
                    float excessDist = absDtC - maxDtC;
                    interceptPull = -math.sign(signedDtC) * math.clamp(excessDist * 0.5f, 0f, 1.0f);
                    
                    sequenceCost += 500000.0f + (excessDist * 50000.0f);
                    sequenceCost -= (-latVelAwayFromCenter) * 1500.0f;
                }
                else if (absDtC > maxDtC * 0.65f)
                {
                    float excess = (absDtC - maxDtC * 0.65f) / math.max(0.5f, maxDtC);
                    interceptPull = -math.sign(signedDtC) * math.clamp(excess * 0.6f, 0f, 0.6f);
                }

                float3 desiredForward = math.normalize(trackForward + targetRight * interceptPull);

                math.sincos(simYaw, out float carSinYaw, out float carCosYaw);
                float3 carForward = new float3(carSinYaw, 0f, carCosYaw);
                float3 carRight = new float3(carCosYaw, 0f, -carSinYaw);

                float headingError = 1.0f - math.dot(desiredForward, carForward); 
                float headingWeight = (step < steerControlSteps) ? 1000.0f : 350.0f;
                sequenceCost += headingError * headingWeight; 

                float effectiveTargetSpeed = (absDtC > maxDtC) 
                    ? math.min(stepMaxSpeed, math.max(minimumSpeedMs * 1.4f, 12.0f)) 
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
                sequenceCost += speedError * 40.0f;
                
                if (v_long > effectiveTargetSpeed)
                {
                    float overSpeed = v_long - effectiveTargetSpeed;
                    sequenceCost += (overSpeed + overSpeed * overSpeed) * 150.0f;
                }

                if (stepAlong > userTargetAccel)
                {
                    sequenceCost += (stepAlong - userTargetAccel) * 400.0f; 
                }
                if (stepAlong < -userTargetDecel)
                {
                    sequenceCost += (math.abs(stepAlong) - userTargetDecel) * 400.0f; 
                }
                
                if (v_long < minimumSpeedMs)
                {
                    float stallDiff = minimumSpeedMs - v_long;
                    sequenceCost += (stallDiff * stallDiff + stallDiff) * 25000.0f;
                }
                
                if (step + 1 >= steerControlSteps)
                {
                    float lookaheadPull = gentleCenterPull;
                    if (absDtC > maxDtC * 0.5f)
                    {
                        float excess = (absDtC - maxDtC * 0.5f) / math.max(0.5f, maxDtC);
                        lookaheadPull = -math.sign(signedDtC) * math.clamp(excess, 0f, 1.0f);
                    }

                    float3 lookaheadForward = math.normalize(trackForward + targetRight * lookaheadPull);
                    float sinErr = math.dot(lookaheadForward, carRight);
                    float cosErr = math.dot(lookaheadForward, carForward);
                    float signedAngleErr = math.atan2(sinErr, cosErr);

                    float speedSteerScale = 1.0f / (1.0f + 0.04f * math.max(0f, v_long));
                    float yawDamping = yaw_rate * 0.18f;
                    float desiredFutureSteer = math.clamp(((signedAngleErr / steerAngleLimit) * speedSteerScale) - yawDamping, -1f, 1f);
                    float nextSteer = math.clamp(desiredFutureSteer, currentStepSteer - 0.12f, currentStepSteer + 0.12f);
                    
                    sequenceCost += math.abs(nextSteer - currentStepSteer) * (steerSmoothnessPenalty * 0.25f);
                    currentStepSteer = nextSteer;
                }
            }

            sequenceCosts[index] = sequenceCost;
            sequenceInputs[index] = new float3(candidateSteer, candidateThrottle, candidateBrake);
        }
    }
}