using Assets.Scripts.Components;
using System.Collections.Generic;
using UnityEngine;

public class RoadLayout : MonoBehaviour
{
    public List<RoadSegment> roadSegments;
    public Transform autoAddRoadComponent;

    [Tooltip("Distance threshold (in meters) to switch to next waypoint.")]
    public float reachThreshold = 5f;

    private int currentSegmentIndex = 0;
    private int currentLap = 1;
    private Transform nextPoint;

    private RCC_CarControllerV4 carController;

    void Start()
    {
        carController = this.transform.parent.GetComponent<RCC_CarControllerV4>();
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
        Vector2 carPos2D = new Vector2(carController.transform.position.x, carController.transform.position.z);
        Vector2 targetPos2D = new Vector2(nextPoint.position.x, nextPoint.position.z);
        float distance = Vector2.Distance(carPos2D, targetPos2D);

        float threshHoldMult = Mathf.Clamp(carController.speed / 50f, 1f, 2f); 
        float effectiveThreshold = reachThreshold * threshHoldMult;
        
        // Fallback
        int nextNextIndex = (currentSegmentIndex + 1) % roadSegments.Count;
        Vector2 nextNextPos2D = new Vector2(roadSegments[nextNextIndex].BeginPoint.position.x, roadSegments[nextNextIndex].BeginPoint.position.z);
        float distanceToNextNext = Vector2.Distance(carPos2D, nextNextPos2D);
        
        if (distance < effectiveThreshold || distanceToNextNext < distance)
        {
            if (currentSegmentIndex == roadSegments.Count - 1)
            {
                currentLap += 1;
            }
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

    public int GetCurrentLap()
    {
        return currentLap; 
    }

    public void ResetProgress()
    {
        currentSegmentIndex = 0;
        currentLap = 1;
        nextPoint = roadSegments[currentSegmentIndex].BeginPoint;
    }
}
