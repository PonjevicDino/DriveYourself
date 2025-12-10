using Assets.Scripts.Components;
using System.Collections.Generic;
using UnityEngine;

public class MultiAgentTraining : MonoBehaviour
{
    [SerializeField] private int agents = 1;

    void Start()
    {
        Vector3 origPosition = transform.GetChild(0).position;
        Quaternion origRotation = transform.GetChild(0).rotation;

        for (int agent = 1; agent < agents; agent++)
        {
            Transform newAgent = GameObject.Instantiate(transform.GetChild(0), origPosition, origRotation, transform);
            newAgent.gameObject.name = newAgent.gameObject.name + "_" + agent;
            newAgent.Find("Controller").GetComponent<RoadLayout>().roadSegments = new List<RoadSegment>();
        }
    }
}
