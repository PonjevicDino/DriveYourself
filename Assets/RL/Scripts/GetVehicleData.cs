using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

public class GetVehicleData : MonoBehaviour
{
    private RCC_CarControllerV4 carController;
    private Rigidbody carRb;
    private GameObject roadSegment;
    public RoadLayout roadLayout;
    
    [Tooltip("Distance threshold (in meters) to switch to next waypoint.")]
    public float reachThreshold = 5f;
    private int currentSegmentIndex = 0;
    private int chunkLap = 1;
    private Transform nextPoint;
    private float segmentProgress;
    
    private Vector3 lastVelocity;
    private Vector3 currentAccelerationVector;
    private float currentAccelerationMagnitude;
    private float currentJerk;
    private float lastAccelerationMag;
    
    private float currentContinuousProgressPercent;
    private float currentContinuousDtC;
    private float previousContinuousDtC;
    private float continuousDtCVelocity;
    private int currentContinuousLap = 1;
    private float lastT = 0f;
    private float accumulatedT = 0f;
    
    private Vector3 cachedForwardVector = Vector3.forward;

    void Start()
    {
        carController = this.transform.parent.GetComponent<RCC_CarControllerV4>();
        carRb = carController.GetComponent<Rigidbody>();

        if (carRb) lastVelocity = carRb.linearVelocity;

        if (roadLayout != null && roadLayout.roadSegments.Count > 0)
        {
            nextPoint = roadLayout.roadSegments[currentSegmentIndex].BeginPoint;
        }
        else
        {
            Debug.LogError($"{gameObject.name}: Missing Shared Road Layout!");
        }
    }

    public void UpdateVehicleData(float accumulatedDeltaTime)
    {
        if (!carController || !carRb) return;
        if (accumulatedDeltaTime <= 0f) return;

        Vector3 currentVelocity = carRb.linearVelocity;
        Vector3 accelerationWorld = (currentVelocity - lastVelocity) / accumulatedDeltaTime;

        currentAccelerationVector = carController.transform.InverseTransformDirection(accelerationWorld);
        currentAccelerationMagnitude = currentAccelerationVector.magnitude;
        currentJerk = (currentAccelerationMagnitude - lastAccelerationMag) / accumulatedDeltaTime;
        lastVelocity = currentVelocity;
        lastAccelerationMag = currentAccelerationMagnitude;
        
        EvaluateSplineData(accumulatedDeltaTime);
    }
    
    public void InitContinuousSplineState()
    {
        if (roadLayout == null || roadLayout.trackSpline == null) return;
        
        Spline spline = roadLayout.trackSpline.Spline;
        Vector3 carWorldPos = carController.transform.position;
        float3 carLocalPos = roadLayout.trackSpline.transform.InverseTransformPoint(carWorldPos);
        SplineUtility.GetNearestPoint(spline, carLocalPos, out float3 nearestLocalPos, out float t);

        lastT = t;
        accumulatedT = 0f;
        currentContinuousLap = 1;
        currentContinuousProgressPercent = 0f;
        previousContinuousDtC = 0f;
        continuousDtCVelocity = 0f;
    }
    
    public void CheckIfNextSegmentHasBeenReached()
    {
        if (roadLayout == null || nextPoint == null) return;

        Vector2 carPos2D = new Vector2(carController.transform.position.x, carController.transform.position.z);
        Vector2 targetPos2D = new Vector2(nextPoint.position.x, nextPoint.position.z);
        float distance = Vector2.Distance(carPos2D, targetPos2D);

        float threshHoldMult = Mathf.Clamp(carController.speed / 50f, 1f, 2f); 
        float effectiveThreshold = reachThreshold * threshHoldMult;
        
        int nextNextIndex = (currentSegmentIndex + 1) % roadLayout.roadSegments.Count;
        Vector2 nextNextPos2D = new Vector2(roadLayout.roadSegments[nextNextIndex].BeginPoint.position.x, roadLayout.roadSegments[nextNextIndex].BeginPoint.position.z);
        float distanceToNextNext = Vector2.Distance(carPos2D, nextNextPos2D);
        
        if (distance < effectiveThreshold || distanceToNextNext < distance)
        {
            if (currentSegmentIndex == roadLayout.roadSegments.Count - 1)
            {
                chunkLap += 1;
            }
            currentSegmentIndex = (currentSegmentIndex + 1) % roadLayout.roadSegments.Count;
            nextPoint = roadLayout.roadSegments[currentSegmentIndex].BeginPoint;
        }
    }

    public GameObject GetRoadSegment()
    {
        CheckIfNextSegmentHasBeenReached();
        int adaptedIndex = currentSegmentIndex - 1;
        if (adaptedIndex < 0) adaptedIndex = roadLayout.roadSegments.Count - 1;
        roadSegment = roadLayout.roadSegments[adaptedIndex].gameObject;
        return roadSegment; 
    }

    public GameObject GetNextRoadSegment(GameObject roadSegment)
    {
        return roadSegment.transform.GetSiblingIndex() + 1 < roadSegment.transform.parent.childCount ? roadSegment.transform.parent.GetChild(roadSegment.transform.GetSiblingIndex() + 1).gameObject : roadSegment.transform.parent.GetChild(0).gameObject;
    }

    public void ResetVars()
    {
        if (carRb) lastVelocity = carRb.linearVelocity;
        currentAccelerationVector = Vector3.zero;
        currentAccelerationMagnitude = 0f;
        lastAccelerationMag = 0f;
        
        currentSegmentIndex = 0;
        chunkLap = 1;
        if (roadLayout != null && roadLayout.roadSegments.Count > 0)
        {
            nextPoint = roadLayout.roadSegments[currentSegmentIndex].BeginPoint;
            roadSegment = roadLayout.roadSegments[0].gameObject;
        }
        
        previousContinuousDtC = 0f;
        continuousDtCVelocity = 0f;
        
        // currentContinuousLap = 1;
        // lastT = 0f;
        // currentContinuousProgressPercent = 0f;
    }
    
    private void EvaluateSplineData(float fixedDeltaTime)
    {
        if (!roadLayout || roadLayout.trackSpline == null) return;
        
        Spline spline = roadLayout.trackSpline.Spline;
        if (spline == null || spline.Count == 0) return;

        Vector3 carWorldPos = carController.transform.position;
        float3 carLocalPos = roadLayout.trackSpline.transform.InverseTransformPoint(carWorldPos);

        float t = GetLocalizedNearestT(spline, carLocalPos, lastT);
        float3 nearestLocalPos = SplineUtility.EvaluatePosition(spline, t);
        
        float deltaT = t - lastT;
        if (deltaT < -0.5f)
        {
            deltaT += 1.0f; 
        }
        else if (deltaT > 0.5f)
        {
            deltaT -= 1.0f;
        }

        accumulatedT += deltaT;
        lastT = t;
        currentContinuousProgressPercent = accumulatedT * 100.0f;
        currentContinuousLap = Mathf.FloorToInt(accumulatedT) + 1;

        float3 trackForwardLocal = SplineUtility.EvaluateTangent(spline, t);
        Vector3 trackForwardWorld = roadLayout.trackSpline.transform.TransformDirection(trackForwardLocal);
        Vector3 nearestWorldPos = roadLayout.trackSpline.transform.TransformPoint(nearestLocalPos);
        Vector3 flatCarPos = new Vector3(carWorldPos.x, 0f, carWorldPos.z);
        Vector3 flatNearestPos = new Vector3(nearestWorldPos.x, 0f, nearestWorldPos.z);
        Vector3 flatTrackToCar = flatCarPos - flatNearestPos;

        float distance = flatTrackToCar.magnitude;
        Vector3 flatTrackForward = new Vector3(trackForwardWorld.x, 0f, trackForwardWorld.z).normalized;
        float side = Mathf.Sign(Vector3.Dot(Vector3.Cross(Vector3.up, flatTrackForward), flatTrackToCar));
        
        cachedForwardVector = trackForwardWorld.normalized;
        currentContinuousDtC = distance * side;
        
        continuousDtCVelocity = (currentContinuousDtC - previousContinuousDtC) / fixedDeltaTime;
        previousContinuousDtC = currentContinuousDtC;
    }
    
    private float GetLocalizedNearestT(Spline spline, float3 localPos, float previousT)
    {
        float tCenter = previousT;
        float stepSize = 0.005f; 
        
        float3 posCenter = SplineUtility.EvaluatePosition(spline, tCenter);
        float distCenter = math.distancesq(localPos, posCenter);
        
        for (int i = 0; i < 10; i++) 
        {
            float tLeft = tCenter - stepSize;
            float tRight = tCenter + stepSize;
            
            if (tLeft < 0f) tLeft += 1f;
            if (tRight >= 1f) tRight -= 1f;

            float3 posLeft = SplineUtility.EvaluatePosition(spline, tLeft);
            float distLeft = math.distancesq(localPos, posLeft);
            
            float3 posRight = SplineUtility.EvaluatePosition(spline, tRight);
            float distRight = math.distancesq(localPos, posRight);
            
            if (distLeft < distCenter && distLeft < distRight)
            {
                tCenter = tLeft;
                distCenter = distLeft;
            }
            else if (distRight < distCenter && distRight < distLeft)
            {
                tCenter = tRight;
                distCenter = distRight;
            }
            else
            {
                stepSize *= 0.5f;
            }
        }
        
        return tCenter;
    }
    
    public void SyncDiscreteSegmentToSpline()
    {
        if (roadLayout == null || roadLayout.roadSegments.Count == 0) return;

        Vector3 carPos = carController.transform.position;
        Vector3 carForward = carController.transform.forward;

        float closestDist = float.MaxValue;
        int targetIdx = 0;

        for (int i = 0; i < roadLayout.roadSegments.Count; i++)
        {
            Vector3 toPoint = roadLayout.roadSegments[i].BeginPoint.position - carPos;
            float dist = toPoint.magnitude;
            
            if (Vector3.Dot(carForward, toPoint.normalized) > 0f)
            {
                if (dist < closestDist)
                {
                    closestDist = dist;
                    targetIdx = i;
                }
            }
        }

        currentSegmentIndex = targetIdx;
        nextPoint = roadLayout.roadSegments[currentSegmentIndex].BeginPoint;
        chunkLap = 1;
        
        int adaptedIndex = currentSegmentIndex - 1;
        if (adaptedIndex < 0) adaptedIndex = roadLayout.roadSegments.Count - 1;
        roadSegment = roadLayout.roadSegments[adaptedIndex].gameObject;
    }
    
    public float GetProgress()
    {
        if (roadLayout == null || roadSegment == null) return 0f;
        float roadSegmentPercent = (float) roadSegment.transform.GetSiblingIndex() / (float) roadLayout.roadSegments.Count * 100.0f;
        float accurateSegmentPercent = 1.0f / (float) roadLayout.roadSegments.Count * segmentProgress / 10.0f * 100.0f;
        return Mathf.Clamp(roadSegmentPercent + accurateSegmentPercent, 0.0f, 100.0f);
    }

    public int GetLap() => chunkLap;
    
    public float GetContinuousDtC() => currentContinuousDtC;
    public float GetContinuousDtCVelocity() => continuousDtCVelocity;
    public float GetContinuousProgress() => currentContinuousProgressPercent;
    public int GetContinuousLap() => currentContinuousLap;
    public float GetCurrentSplineT() => lastT;
    
    public Vector3 GetContinuousForwardVector() 
    {
        return cachedForwardVector;
    }

    public float GetSpeed()
    {
        if (!carController)
        {
            return 0.0f;
        }
        return carController.speed;
    }

    public float GetAccelleration() => currentAccelerationMagnitude;
    public Vector3 GetAccellerationVector() => currentAccelerationVector;
    public float GetJerk() => currentJerk;

    private float lastDtc = 0.0f;
    public float GetDtC()
    {
        if (!roadSegment)
        {
            return lastDtc;
        }

        float dtc = 0.0f;
        int nearestPair = 0;
        List<float> pairDistances = new List<float>();
        Vector3 vehiclePos = carController.transform.position;

        GameObject nextReadSegment = roadSegment.transform.GetSiblingIndex() + 1 < roadSegment.transform.parent.childCount ? roadSegment.transform.parent.GetChild(roadSegment.transform.GetSiblingIndex() + 1).gameObject : roadSegment.transform.parent.GetChild(0).gameObject;
        for (int pairIndex = 1; pairIndex <= 10; pairIndex++)
        {
            Vector3 leftPairPos = roadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "L").transform.position;
            Vector3 rightPairPos = roadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "R").transform.position;

            if (GlobalSettings.showDebugRays) {
                Debug.DrawLine(vehiclePos, leftPairPos, Color.white);
                Debug.DrawLine(vehiclePos, rightPairPos, Color.white);
            }

            pairDistances.Add(Vector2.Distance(new Vector2(leftPairPos.x, leftPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)) + Vector2.Distance(new Vector2(rightPairPos.x, rightPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)));
        }
        for (int pairIndex = 1; pairIndex <= 10; pairIndex++)
        {
            Vector3 leftNextPairPos = nextReadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "L").transform.position;
            Vector3 rightNextPairPos = nextReadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "R").transform.position;

            if (GlobalSettings.showDebugRays) {
                Debug.DrawLine(vehiclePos, leftNextPairPos, Color.blue);
                Debug.DrawLine(vehiclePos, rightNextPairPos, Color.blue);
            }

            pairDistances.Add(Vector2.Distance(new Vector2(leftNextPairPos.x, leftNextPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)) + Vector2.Distance(new Vector2(rightNextPairPos.x, rightNextPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)));
        }

        int minIndex = 0;
        float minValue = pairDistances[0];

        for (int i = 1; i < pairDistances.Count; i++)
        {
            if (pairDistances[i] < minValue)
            {
                minValue = pairDistances[i];
                minIndex = i;
            }
        }

        segmentProgress = nearestPair = minIndex;

        Vector3 pairLPos; Vector3 pairRPos;
        if (nearestPair >= 10)
        {
            nearestPair -= 10;
            pairLPos = nextReadSegment.transform.Find("DtC-Tracker").Find("P" + (nearestPair + 1) + "L").transform.position;
            pairRPos = nextReadSegment.transform.Find("DtC-Tracker").Find("P" + (nearestPair + 1) + "R").transform.position;
        }
        else
        {
            pairLPos = roadSegment.transform.Find("DtC-Tracker").Find("P" + (nearestPair + 1) + "L").transform.position;
            pairRPos = roadSegment.transform.Find("DtC-Tracker").Find("P" + (nearestPair + 1) + "R").transform.position;
        }

#if UNITY_EDITOR
        Debug.DrawLine(vehiclePos, pairLPos, Color.red);
        Debug.DrawLine(vehiclePos, pairRPos, Color.red);
#endif
        float pairDist = Vector3.Distance(pairRPos, pairLPos);
        float distPercentage = pairDistances.Min() / pairDist;

        float pairLDist = Vector2.Distance(new Vector2(vehiclePos.x, vehiclePos.z), new Vector2(pairLPos.x, pairLPos.z));
        float pairRDist = Vector2.Distance(new Vector2(vehiclePos.x, vehiclePos.z), new Vector2(pairRPos.x, pairRPos.z));

        dtc = (pairLDist - pairRDist) / 2 * distPercentage;
        lastDtc = dtc;

        return dtc;
    }

    public float ReturnLastDtC() => lastDtc;
}
