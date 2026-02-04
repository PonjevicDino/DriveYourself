using Assets.Scripts.Components;
using Assets.Scripts.QLearningModules;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.XR;

public class GetVehicleData : MonoBehaviour
{
    private RCC_CarControllerV4 carController;
    private Rigidbody carRb;
    private GameObject roadSegment;
    private RoadLayout roadLayout;

    private float segmentProgress;
    private int lap = 0;

    // Calculation variables
    private Vector3 lastVelocity;
    private Vector3 currentAccelerationVector;
    private float currentAccelerationMagnitude;
    private float currentJerk;
    private float lastAccelerationMag;

    void Start()
    {
        carController = this.transform.parent.GetComponent<RCC_CarControllerV4>();
        carRb = carController.GetComponent<Rigidbody>();
        roadLayout = this.GetComponent<RoadLayout>();

        if (carRb)
        {
            lastVelocity = carRb.linearVelocity;
        }
    }

    void FixedUpdate()
    {
        if (!carController || !carRb) return;

        Vector3 currentVelocity = carRb.linearVelocity;
        Vector3 accelerationWorld = (currentVelocity - lastVelocity) / Time.fixedDeltaTime;

        currentAccelerationVector = carController.transform.InverseTransformDirection(accelerationWorld);
        currentAccelerationMagnitude = currentAccelerationVector.magnitude;
        currentJerk = (currentAccelerationMagnitude - lastAccelerationMag) / Time.fixedDeltaTime;
        lastVelocity = currentVelocity;
        lastAccelerationMag = currentAccelerationMagnitude;
    }

    public float GetSpeed()
    {
        if (!carController)
        {
            return 0.0f;
        }
        return carController.speed;
    }

    public float GetAccelleration()
    {
        return currentAccelerationMagnitude;
    }

    public Vector3 GetAccellerationVector()
    {
        return currentAccelerationVector;
    }

    public float GetJerk()
    {
        return currentJerk;
    }

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

        GameObject nextReadSegment = roadSegment.transform.GetSiblingIndex() + 1 < roadSegment.transform.parent.childCount - 1 ? roadSegment.transform.parent.GetChild(roadSegment.transform.GetSiblingIndex() + 1).gameObject : roadSegment.transform.parent.GetChild(0).gameObject;
        for (int pairIndex = 1; pairIndex <= 10; pairIndex++)
        {
            Vector3 leftPairPos = roadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "L").transform.position;
            Vector3 rightPairPos = roadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "R").transform.position;

#if UNITY_EDITOR
            Debug.DrawLine(vehiclePos, leftPairPos, Color.white);
            Debug.DrawLine(vehiclePos, rightPairPos, Color.white);
#endif

            pairDistances.Add(Vector2.Distance(new Vector2(leftPairPos.x, leftPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)) + Vector2.Distance(new Vector2(rightPairPos.x, rightPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)));
        }
        for (int pairIndex = 1; pairIndex <= 10; pairIndex++)
        {
            Vector3 leftNextPairPos = nextReadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "L").transform.position;
            Vector3 rightNextPairPos = nextReadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "R").transform.position;

#if UNITY_EDITOR
            Debug.DrawLine(vehiclePos, leftNextPairPos, Color.blue);
            Debug.DrawLine(vehiclePos, rightNextPairPos, Color.blue);
#endif

            pairDistances.Add(Vector2.Distance(new Vector2(leftNextPairPos.x, leftNextPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)) + Vector2.Distance(new Vector2(rightNextPairPos.x, rightNextPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)));
        }

        segmentProgress = nearestPair = pairDistances.IndexOf(pairDistances.Min());

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
    public float ReturnLastDtC()
    {
        return lastDtc;
    }

    public GameObject GetRoadSegment()
    {
        roadLayout.CheckIfNextSegmentHasBeenReached();
        roadSegment = roadLayout.roadSegments[roadLayout.GetCurrentSegmentIndex()].gameObject;
        lap = roadLayout.GetCurrentLap();
        return roadSegment; 
    }

    public GameObject GetNextRoadSegment(GameObject roadSegment)
    {
        return roadSegment.transform.GetSiblingIndex() + 1 < roadSegment.transform.parent.childCount - 1 ? roadSegment.transform.parent.GetChild(roadSegment.transform.GetSiblingIndex() + 1).gameObject : roadSegment.transform.parent.GetChild(0).gameObject; ;
    }

    public void ResetVars()
    {
        if (!roadLayout)
        {
            return;
        }
        roadLayout.ResetProgress();
        roadSegment = roadLayout.roadSegments[0].gameObject;

        if (carRb) lastVelocity = carRb.linearVelocity;
        currentAccelerationVector = Vector3.zero;
        currentAccelerationMagnitude = 0f;
        lastAccelerationMag = 0f;

        lap = 1;
    }

    public float GetProgress()
    {
        float roadSegmentPercent = (float) roadSegment.transform.GetSiblingIndex() / (float) roadLayout.roadSegments.Count() * 100.0f;
        float accurateSegmentPercent = 1.0f / (float) roadLayout.roadSegments.Count() * segmentProgress / 10.0f * 100.0f;
        return Mathf.Clamp(roadSegmentPercent + accurateSegmentPercent, 0.0f, 100.0f);
    }

    public int GetLap()
    {
        return lap;
    }
}
