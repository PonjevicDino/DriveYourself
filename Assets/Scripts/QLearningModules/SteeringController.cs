using Assets.Scripts.Components;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.QLearningModules
{
    public class SteeringController : MonoBehaviour
    {
        [Header("Car References")]
        public QLearningController carController;
        //public VehicleController carController;
        public Transform carFront;

        [Header("Road Segments")]
        public List<RoadSegment> roadSegments;
        private int currentSegmentIndex = 0;
        private Transform nextPoint;

        [Header("Steering Settings")]
        [Tooltip("Steering interpolation rate at zero speed.")]
        [Range(0f, 1f)] public float lowSpeedLerp = 0.1f;
        [Tooltip("Steering interpolation rate at max speed.")]
        [Range(0f, 1f)] public float highSpeedLerp = 0.5f;
        [Tooltip("Maximum angle (in degrees) that corresponds to full steering input.")]
        public float maxSteerAngle = 45f;
        [Tooltip("Distance threshold (in meters) to switch to next waypoint.")]
        public float reachThreshold = 3f;
        [Tooltip("Distance (m) at which steering becomes maximal as you approach the point.")]
        public float maxDistance = 20f;
        [Tooltip("Threshold (m) for snapping small steering inputs to zero.")]
        public float zeroThreshold = 0.1f;
        //private fields
        private float maxSpeed = 90f;
        private Transform carTransform;
        private float lastTargetSteer = 0f;

        private void Awake()
        {
            if (carController == null)
            {
                carController = GetComponent<QLearningController>();
                //carController = GetComponent<VehicleController>();
            }
        }
        private void Start()
        {
            carTransform = carController.carCont.transform;
            maxSpeed = carController.maxSpeed;
            carFront = carController.carFront.transform;
            roadSegments = carController.RoadCollection;

            currentSegmentIndex = 0;
            nextPoint = roadSegments[currentSegmentIndex].BeginPoint;
        }
        public void SteerWithDelta(int deltaSteer)
        {
            float currentSteer = carController.carCont.steerInput;

            // Define discrete steering targets
            float[] steerTargets = { -0.5f, 0f, 0.5f };  // Corresponds to deltaSteer -1, 0, +1

            float targetSteer = steerTargets[deltaSteer + 1]; // Shift -1..1 to 0..2

            float speedFactor = Mathf.Clamp01(carController.carCont.speed / maxSpeed);
            float lerpRate = Mathf.Lerp(lowSpeedLerp, highSpeedLerp, speedFactor) / 5;

            float newSteer = Mathf.MoveTowards(currentSteer, targetSteer, lerpRate * Time.deltaTime);
            carController.carCont.steerInput = newSteer;

            lastTargetSteer = newSteer;
        }
        public void CheckTargetPoint()
        {
            Vector3 toTarget = nextPoint.position - carTransform.position;
            float distance = toTarget.magnitude;
            float angleToTarget = Vector3.Angle(carTransform.forward, toTarget);

            //Debug.DrawRay(carTransform.position, toTarget, Color.red);
            // If within reach threshold or the target is behind (angle > 90°), advance
            float threshHoldMult = Mathf.Clamp(carController.carCont.speed / 100, 1, 9);
            if (distance < reachThreshold * threshHoldMult)
            {
                currentSegmentIndex = (currentSegmentIndex + 1) % roadSegments.Count;
                nextPoint = roadSegments[currentSegmentIndex].BeginPoint;
                carController.roadSegmentIndex = currentSegmentIndex;
            }
        }
        public void SteerTowardsNextTarget()
        {
            // 1. Heading error angle
            Vector3 toTarget = (nextPoint.position - carTransform.position).normalized;
            float signedAngle = Vector3.SignedAngle(carTransform.forward, toTarget, Vector3.up);

            // 2. Desired steer proportional to angle
            float targetSteer = Mathf.Clamp(signedAngle / maxSteerAngle, -0.5f, 0.5f);

            if (Mathf.Abs(targetSteer) < 0.2f)
            {
                targetSteer = 0;
            }
            else if (lastTargetSteer == targetSteer)
            {
                return;
            }
            // 3. Read current steer
            float currentSteer = carController.carCont.steerInput;


            //4. Compute responsiveness based on speed
            float speedFactor = Mathf.Clamp01(carController.carCont.speed / maxSpeed);
            float lerpRate = Mathf.Lerp(lowSpeedLerp, highSpeedLerp, speedFactor) / 5;
           
            float newSteer = Mathf.MoveTowards(currentSteer, targetSteer, lerpRate * Time.deltaTime);
            carController.carCont.steerInput = targetSteer;// newSteer;
            lastTargetSteer = newSteer;
            
        }


        public void ResetSteering()
        {
            currentSegmentIndex = 0;
            nextPoint = roadSegments[currentSegmentIndex+1].BeginPoint;
            carController.roadSegmentIndex = currentSegmentIndex;
        }

        public int GetCurrentSegmentIndex()
        {
            return currentSegmentIndex;
        }
    }
}