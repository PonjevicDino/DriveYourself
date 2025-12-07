using Assets.Scripts.Components;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.XR;

public class RoadLayout : MonoBehaviour
{
    public List<RoadSegment> roadSegments;
    public Transform autoAddRoadComponent;

    [Tooltip("Distance threshold (in meters) to switch to next waypoint.")]
    public float reachThreshold = 5f;

    private int currentSegmentIndex = 0;
    private Transform nextPoint;

    private RCC_CarControllerV4 carController;

    void Start()
    {
        carController = GameObject.FindFirstObjectByType<RCC_CarControllerV4>();
        if (autoAddRoadComponent != null)
        {
            for (int child = 0; child < autoAddRoadComponent.childCount; child++)
            {
                roadSegments.Add(autoAddRoadComponent.GetChild(child).GetComponent<RoadSegment>());
            }
        }
        nextPoint = roadSegments[currentSegmentIndex].BeginPoint;
    }

    public void CheckIfNextSegmentHasBeenReached()
    {
        Vector3 toTarget = nextPoint.position - carController.transform.position;
        float distance = toTarget.magnitude;
        float angleToTarget = Vector3.Angle(carController.transform.forward, toTarget);

        float threshHoldMult = Mathf.Clamp(carController.speed / 100, 1, 9);
        if (distance < reachThreshold * threshHoldMult)
        {
            currentSegmentIndex = (currentSegmentIndex + 1) % roadSegments.Count;
            nextPoint = roadSegments[currentSegmentIndex].BeginPoint;
        }
    }

    public int GetCurrentSegmentIndex()
    {
        int currentSegmentIndexAdapted = currentSegmentIndex - 1;
        if (currentSegmentIndexAdapted < 0)
        {
            currentSegmentIndexAdapted = roadSegments.Count - 1; 
        }
        return currentSegmentIndexAdapted;
    }

    public void ResetProgress()
    {
        currentSegmentIndex = 0;
        nextPoint = roadSegments[currentSegmentIndex].BeginPoint;
    }
}
