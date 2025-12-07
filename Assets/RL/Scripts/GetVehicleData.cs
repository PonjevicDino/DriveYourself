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
    private GameObject roadSegment;
    private RoadLayout roadLayout;

    private float segmentProgress;

    void Start()
    {
        carController = this.transform.parent.GetComponent<RCC_CarControllerV4>();
        roadLayout = this.GetComponent<RoadLayout>();
    }

    public float GetSpeed()
    {
        return carController.speed;
    }

    private float lastSpeed = 0.0f;
    public float GetAccelleration()
    {
        return carController.speed - lastSpeed;
    }

    private float lastAcceleration = 0.0f;
    public float GetJerk()
    {
        return GetAccelleration() - lastAcceleration;
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

            Debug.DrawLine(vehiclePos, leftPairPos, Color.white);
            Debug.DrawLine(vehiclePos, rightPairPos, Color.white);

            pairDistances.Add(Vector2.Distance(new Vector2(leftPairPos.x, leftPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)) + Vector2.Distance(new Vector2(rightPairPos.x, rightPairPos.z), new Vector2(vehiclePos.x, vehiclePos.z)));
        }
        for (int pairIndex = 1; pairIndex <= 10; pairIndex++)
        {
            Vector3 leftNextPairPos = nextReadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "L").transform.position;
            Vector3 rightNextPairPos = nextReadSegment.transform.Find("DtC-Tracker").Find("P" + pairIndex + "R").transform.position;

            Debug.DrawLine(vehiclePos, leftNextPairPos, Color.blue);
            Debug.DrawLine(vehiclePos, rightNextPairPos, Color.blue);

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

        Debug.DrawLine(vehiclePos, pairLPos, Color.red);
        Debug.DrawLine(vehiclePos, pairRPos, Color.red);
        float pairDist = Vector3.Distance(pairRPos, pairLPos);
        float distPercentage = pairDistances.Min() / pairDist;

        float pairLDist = Vector2.Distance(new Vector2(vehiclePos.x, vehiclePos.z), new Vector2(pairLPos.x, pairLPos.z));
        float pairRDist = Vector2.Distance(new Vector2(vehiclePos.x, vehiclePos.z), new Vector2(pairRPos.x, pairRPos.z));

        dtc = (pairLDist - pairRDist) / 2 * distPercentage;
        lastDtc = dtc;

        return dtc;
    }

    public GameObject GetRoadSegment()
    {
        roadLayout.CheckIfNextSegmentHasBeenReached();
        roadSegment = roadLayout.roadSegments[roadLayout.GetCurrentSegmentIndex()].gameObject;
        return roadSegment; 
    }

    public GameObject GetNextRoadSegment(GameObject roadSegment)
    {
        return roadSegment.transform.GetSiblingIndex() + 1 < roadSegment.transform.parent.childCount - 1 ? roadSegment.transform.parent.GetChild(roadSegment.transform.GetSiblingIndex() + 1).gameObject : roadSegment.transform.parent.GetChild(0).gameObject; ;
    }

    public void ResetVars()
    {
        roadLayout.ResetProgress();
        roadSegment = roadLayout.roadSegments[0].gameObject;
        lastSpeed = GetSpeed();
        lastAcceleration = GetAccelleration();
    }

    public float GetProgress()
    {
        float roadSegmentPercent = (float) roadSegment.transform.GetSiblingIndex() / (float) roadLayout.roadSegments.Count() * 100.0f;
        float accurateSegmentPercent = 1.0f / (float) roadLayout.roadSegments.Count() * segmentProgress / 10.0f * 100.0f;
        return roadSegmentPercent + accurateSegmentPercent;
    }
}
