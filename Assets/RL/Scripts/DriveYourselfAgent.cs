using System;
using System.Collections.Generic;
using TMPro;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class DriveYourselfAgent : Agent
{
    private RCC_CarControllerV4 carController;

    [SerializeField] private int lookAheadSegments;

    [SerializeField] private TextMeshProUGUI agentAccText;
    [SerializeField] private TextMeshProUGUI agentBrkText;
    [SerializeField] private TextMeshProUGUI agentStrText;

    [Header("Rewards")]
    [SerializeField, Min(0f)] private float targetSpeed;
    [SerializeField, Range(0.0f,100.0f)] private float speedRewardPercent;
    [SerializeField, Min(0f)] private float maxAllowedSafeAcc;
    [SerializeField, Min(0f)] private float maxAllowedRewardAcc;
    [SerializeField, Range(0.0f,100.0f)] private float accRewardPercent;
    //[SerializeField, Min(0f)] private float maxAllowedSafeJerk;
    //[SerializeField, Min(0f)] private float maxAllowedRewardJerk;
    //[SerializeField, Range(0.0f,100.0f)] private float jerkRewardPercent;
    [SerializeField, Min(0f)] private float maxAllowedRewardDtc;
    [SerializeField, Range(0.0f,100.0f)] private float DtCRewardPercent;

    [Header("EndEpisodeConditions")]
    [SerializeField] private int endEpisodeCarYPosition;
    [SerializeField] private int endEpisodeCarStuckSeconds = 15;

    private GetVehicleData vehicleData;
    private Rigidbody carRb;
    private Vector3 startingPosition;
    private Quaternion startingRotation;
    private Vector2 posUpdate;

    private DateTime lastAgentUpdate;
    private float updateDiff = 0.0f;

    private DateTime timeAtLastSignificantMove = DateTime.Now;

    private float lastLapProgress = 0.0f;
    private float nextLapTolerancePercent = 10.0f;

    void Start()
    {
        carController = this.transform.parent.GetComponent<RCC_CarControllerV4>();

        startingPosition = carController.transform.position;
        startingRotation = carController.transform.rotation;
        posUpdate = new Vector2(startingPosition.x, startingPosition.y);
        carRb = carController.GetComponent<Rigidbody>();
        vehicleData = this.GetComponent<GetVehicleData>();
        lastAgentUpdate = DateTime.Now;
    }

    public override void OnEpisodeBegin()
    {
        if(!carController)
        {
            return;
        }

        carController.transform.SetPositionAndRotation(startingPosition, startingRotation);
        carRb.angularVelocity = Vector3.zero;
        carRb.linearVelocity = Vector3.zero;
        carController.externalController = true;
        carController.GetComponent<RCC_LogitechSteeringWheel>().overrideFFB = true;
        vehicleData.ResetVars();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var actions = actionsOut.ContinuousActions;
        actions[0] = Input.GetKey(KeyCode.UpArrow) ? 1 : Input.GetKey(KeyCode.DownArrow) ? -1 : 0;
        actions[1] = Input.GetKey(KeyCode.RightArrow) ? 1 : Input.GetKey(KeyCode.LeftArrow) ? -1 : 0;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(vehicleData.GetSpeed());
        sensor.AddObservation(vehicleData.GetAccelleration());
        //sensor.AddObservation(vehicleData.GetJerk());
        sensor.AddObservation(vehicleData.GetDtC());
        updateDiff = (lastAgentUpdate - DateTime.Now).Milliseconds / 1000.0f;
        lastAgentUpdate = DateTime.Now;

        GameObject currentSegment = vehicleData.GetRoadSegment();
        switch (currentSegment.name.Split("_")[1])
        {
            case "left":
                sensor.AddObservation(-1);
                break;
            case "right":
                sensor.AddObservation(1);
                break;
            default:
                sensor.AddObservation(0);
                break;
        }

        List<float> nextRoadSegments = new List<float>();  
        for (int segment = 0; segment < lookAheadSegments; segment++)
        {
            switch (currentSegment.name.Split("_")[1])
            {
                case "left":
                    nextRoadSegments.Add(-1);
                    break;
                case "right":
                    nextRoadSegments.Add(1);
                    break;
                default:
                    nextRoadSegments.Add(0);
                    break;
            }
            currentSegment = vehicleData.GetNextRoadSegment(currentSegment);
        }
        sensor.AddObservation(nextRoadSegments);

        Transform nextRoadSegment = vehicleData.GetNextRoadSegment(vehicleData.GetRoadSegment()).transform;
        float angleToNextRoadSegment = Vector3.Angle(carController.transform.position, nextRoadSegment.transform.position);

        sensor.AddObservation((carController.transform.position - nextRoadSegment.position).normalized);
        sensor.AddObservation(angleToNextRoadSegment);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float acc = 0.0f;
        float brk = 0.0f;

        if (actions.ContinuousActions[0] >= 0)
        {
            acc = actions.ContinuousActions[0];
        }
        else
        {
            brk = Mathf.Abs(actions.ContinuousActions[0]);
        }

        // Move Vehicle
        carController.throttleInput = acc;
        carController.brakeInput = brk;
        carController.steerInput = actions.ContinuousActions[1];


        // Input Text
        agentAccText.text = "Acc: " + acc.ToString("F4");
        agentBrkText.text = "Brk: " + brk.ToString("F4");
        agentStrText.text = "Str: " + actions.ContinuousActions[1].ToString("F4");

        // Rewards
        Vector2 carPos2D = new Vector2(carController.transform.position.x, carController.transform.position.z);
        //bool significantMovement = Vector2.Distance(carPos2D, posUpdate) >= significantMovementThreshold ? true : false;
        bool significantMovement = vehicleData.GetProgress() > lastLapProgress || (vehicleData.GetProgress() < lastLapProgress && lastLapProgress > (100.0f - nextLapTolerancePercent) && vehicleData.GetProgress() < nextLapTolerancePercent);

        if (significantMovement)
        {
            lastLapProgress = vehicleData.GetProgress();

            posUpdate = carPos2D;
            timeAtLastSignificantMove = DateTime.Now;

            // Speed
            float maxRewardSpeed = targetSpeed * 2.0f;
            float currentSpeedFactor = Mathf.InverseLerp(0.0f, maxRewardSpeed, vehicleData.GetSpeed()) * 2.0f;
            float currentSpeedOffset = Mathf.Abs(currentSpeedFactor - 1);
            if (carController.currentGear != -1)
            {
                AddReward(currentSpeedOffset * (speedRewardPercent / 100.0f));
            }

            // Acceleration
            float currentAccPerSecond = vehicleData.GetAccelleration() / updateDiff;
            float currentAccOffset = Mathf.Abs(currentAccPerSecond) - maxAllowedSafeAcc;
            if (currentAccOffset <= 0)
            {
                AddReward(1.0f * (accRewardPercent / 100.0f));
            }
            else
            {
                float accRewardDegradeFactor = Mathf.InverseLerp(0.0f, maxAllowedRewardAcc, currentAccOffset);
                float accReward = (1.0f - accRewardDegradeFactor) * (accRewardPercent / 100.0f);
                if (!float.IsNaN(accReward))
                {
                    AddReward(accReward);
                }
            }

            // Jerk
            /*
            float currentJerkPerSecond = vehicleData.GetJerk() / updateDiff;
            float currentJerkOffset = Mathf.Abs(currentJerkPerSecond) - maxAllowedSafeJerk;
            if (currentJerkOffset <= 0)
            {
                AddReward(1.0f * (jerkRewardPercent / 100.0f));
            }
            else
            {
                float jerkRewardDegradeFactor = Mathf.InverseLerp(0.0f, maxAllowedRewardJerk, currentJerkOffset);
                float jerkReward = (1.0f - jerkRewardDegradeFactor) * (accRewardPercent / 100.0f);
                if (!float.IsNaN(jerkReward))
                {
                    AddReward(jerkReward);
                }
            }
            */

            // Distance to Center
            float DtCOffsetFactor = Mathf.Max(0.0f, (maxAllowedRewardDtc - Mathf.Abs(vehicleData.GetDtC()))) / maxAllowedRewardDtc;
            AddReward(DtCOffsetFactor * (DtCRewardPercent / 100.0f));
        }
        else if ((DateTime.Now - timeAtLastSignificantMove).TotalSeconds > endEpisodeCarStuckSeconds)
        {
            timeAtLastSignificantMove = DateTime.Now;
            EndEpisode();
            //Debug.LogWarning("Episode end: Car stuck (or agent didn't move)!");
        }

        if (carController.transform.position.y < endEpisodeCarYPosition)
        {
            EndEpisode();
            //Debug.LogWarning("Episode end: Car out of Map!");
        }
    }
}
