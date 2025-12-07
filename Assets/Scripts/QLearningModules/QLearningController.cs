using Assets.Scripts.Components;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting.FullSerializer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Assets.Scripts.QLearningModules
{
    public class QLearningController : MonoBehaviour
    {
        public SteeringController SteeringController;
        public UIUpdater UIUpdater;

        public Transform RoadCollectionParent;
        [HideInInspector]
        public List<RoadSegment> RoadCollection;
        [HideInInspector]
        public int currentSegment = 0;

        // Q-learning parameters
        public float maxSpeed = 90;
        private const int speedBins = 10;
        private const int layoutTypes = 3;
        private const int actions = 3;
        private float[,] Q = new float[speedBins * layoutTypes * layoutTypes * layoutTypes, actions];
        [SerializeField]
        private float speedTarget = 5f;

        [Header("Learning Parameters")]
        public float learningRate = 0.8f;
        public float discountFactor = 0.95f;
        private float epsilon = 1.0f;
        public float epsilonMin = 0.05f;
        public float epsilonDecay = 0.9f;
        public float targetSpeed = 50f;

        [Header("Car Properties")]
        public GameObject Car;
        public RCC_CarControllerV4 carCont = null;
        public GameObject carFront = null;
        private Rigidbody carRigidbody;
        private int timeWheelsOffGround;
        public int roadSegmentIndex;

        public float actionInterval = 0.1f; // seconds per action step
        private int currentDeltaSpeed = 0;
        private int lastSegmentIndex = 0;
        private int lastStateIndex = -1;
        private int lastAction = -1;
        private bool fallOff = false;

        private LinkedList<(int stateIndex, int action)> recentStates = new();

        private void Awake()
        {
            if (SteeringController == null)
            {
                SteeringController = GetComponent<SteeringController>();
            }
            if (UIUpdater == null)
            {
                UIUpdater = gameObject.GetComponent<UIUpdater>();
            }
            if (RoadCollectionParent != null)
            {
                foreach (Transform item in RoadCollectionParent.transform)
                {
                    RoadCollection.Add(item.gameObject.GetComponent<RoadSegment>());
                }
                RoadCollection = RoadCollection
                    .OrderBy(go => go.RoadSegmentNumber)
                    .ToList();
            }
            else
            {
                Debug.Log("Sphere Parent containing the list of Sphere's has not been assigned to the script.");
            }
        }
        void Start()
        {
            Application.runInBackground = true; // Keep the app running even when unfocused
            carRigidbody = GetComponent<Rigidbody>();
            carCont.externalController = true;
        }
        void FixedUpdate()
        {
            bool allWheelsOnGround = CheckForOutOfBounds();
            //actionTimer += Time.deltaTime;
            if (allWheelsOnGround)
            {
                timeWheelsOffGround = 0;
                StepQLearning(0);
                epsilon = Mathf.Max(epsilonMin, epsilon * epsilonDecay);
            }
            else
            {
                DetermineCarReset();
            }
            ApplySpeedChange(currentDeltaSpeed);
            ApplySteeringChange();
        }
        private bool CheckForOutOfBounds()
        {
            //Check if wheels are on the ground
            foreach (var wheel in carCont.AllWheelColliders)
            {
                if (!wheel.isGrounded)
                {
                    return false;
                }
            }
            return true;
        }
        public void DetermineCarReset()
        {
            //Debug.Log("Some wheels are off the ground!");
            timeWheelsOffGround++;


            //Once the car has dropped off the road, reset it
            if (timeWheelsOffGround > 100)
            {

                carRigidbody = Car.GetComponent<Rigidbody>();

                // Disable the car temporarily
                carCont.KillEngine();
                carCont.canControl = false;

                // Reset velocity 
                carRigidbody.linearVelocity = UnityEngine.Vector3.zero;
                carRigidbody.angularVelocity = UnityEngine.Vector3.zero;

                // Reset position and rotation
                Car.transform.position = new UnityEngine.Vector3(0.0f, 0.7f, -13f);
                Car.transform.rotation = new UnityEngine.Quaternion(0, 0, 0, 1);
                carRigidbody.MovePosition(new UnityEngine.Vector3(0.0f, 0.7f, -13f));
                carRigidbody.MoveRotation(new UnityEngine.Quaternion(0, 0, 0, 1));

                if (carCont != null)
                {
                    carCont.Repair();  // Repair in case of damage
                }

                carCont.throttleInput = 0f;
                carCont.brakeInput = 1f;
                carCont.steerInput = 0f;


                //Reset the direction/reset the sphere that the car is driving towards
                //currentRoad = RoadCollection[0].gameObject;
                //currentSphere = RoadCollection[0];
                roadSegmentIndex = 0;

                #region Reset Steering for Controller
                SteeringController.ResetSteering();
                SteeringController.CheckTargetPoint();
                SteeringController.SteerTowardsNextTarget();
                #endregion
                carCont.canControl = true;
                carCont.StartEngine();

                //Give a penalty for this messup of driving off the road
                //float reward = -20f;
                //float punishment = -2.0f - (carCont.speed / 20f);
                /*foreach (var (state, action) in recentStates)
                {
                    DecisionMatrix.UnpackSpeedAction(action, out int delta);
                    float punishment = 2f;
                    Q[state, 2] -= punishment;
                    Q[state, action] -= punishment;
                }
                recentStates.Clear();*/
            }
        }
        float GetMaxQ(int stateIndex)
        {
            float maxQ = float.MinValue;
            for (int i = 0; i < actions; i++)
            {
                maxQ = Mathf.Max(maxQ, Q[stateIndex, i]);
            }
            return maxQ;
        }
        void StepQLearning(float reward)
        {
            currentSegment = GetCurrentSegmentIndex();
            DecisionMatrix.GetLookaheadIndices(currentSegment, RoadCollection.Count, out int t, out int t1, out int t2);
            int layout0 = GetRoadLayout(t);
            int layout1 = GetRoadLayout(t1);
            int layout2 = GetRoadLayout(t2);

            int currentSpeedIndex = Mathf.Clamp(Mathf.FloorToInt(carCont.speed / 10f), 0, 9);
            int stateIndex = DecisionMatrix.GetStateIndex(currentSpeedIndex, layout0, layout1, layout2);

            int action = ChooseAction(stateIndex);
            lastStateIndex = stateIndex;
            lastAction = action;

            if (!recentStates.Any(x => x.stateIndex == stateIndex))
            {
                recentStates.AddLast((stateIndex, action));
                if (recentStates.Count > 4)
                    recentStates.RemoveFirst();
            }

            DecisionMatrix.UnpackSpeedAction(action, out int deltaSpeed);
            currentDeltaSpeed = deltaSpeed;

            // Predict speed bin after action (before applying it)
            int newSpeedIndex = Mathf.Clamp(currentSpeedIndex + deltaSpeed, 0, 9);
            int newSegment = GetCurrentSegmentIndex() + 1;
            DecisionMatrix.GetLookaheadIndices(newSegment, RoadCollection.Count, out int nt, out int nt1, out int nt2);
            int nlayout0 = GetRoadLayout(nt);
            int nlayout1 = GetRoadLayout(nt1);
            int nlayout2 = GetRoadLayout(nt2);

            int newStateIndex = DecisionMatrix.GetStateIndex(newSpeedIndex, nlayout0, nlayout1, nlayout2);


            if (reward == 0)
                reward = ComputeReward(stateIndex,action);
                //reward = ComputeReward(newSegment, newSpeedIndex, deltaSpeed);

            float maxQ = float.MinValue;
            for (int i = 0; i < actions; i++)
            {
                maxQ = Mathf.Max(maxQ, Q[newStateIndex, i]);
            }
            float learningValue = learningRate * (reward + discountFactor * maxQ - Q[stateIndex, action]);
            //Debug.Log($"Current State: {stateIndex}, Current action: {action}, Learning Value: {learningValue}");
            Q[stateIndex, action] += learningValue;
            float updateSpeed = carCont.speed;
            if (carCont.direction == -1)
            {
                updateSpeed *= -1;
            }
            UIUpdater?.UpdateSpeed(updateSpeed);
            UIUpdater?.UpdateSegment(currentSegment);
            UIUpdater?.UpdateReward(reward);
            UIUpdater?.UpdateAction(deltaSpeed);
            UIUpdater?.UpdateSpeedBracket(currentSpeedIndex);
            UIUpdater?.UpdateQTableInfo(Q, stateIndex);
        }


        int ChooseAction(int stateIndex)
        {
            float rand = UnityEngine.Random.value; // value in [0,1)

            if (rand < epsilon)
            {
                // Exploration: choose a random action [0, 1, 2]
                int randomAction = UnityEngine.Random.Range(0, actions);
                return randomAction;
            }

            float maxQ = float.MinValue;
            int bestAction = 0;
            for (int i = 0; i < actions; i++)
            {
                if (Q[stateIndex, i] >= maxQ)
                {
                    maxQ = Q[stateIndex, i];
                    bestAction = i;
                }
            }
            return bestAction;
        }
        void ApplySpeedChange(int deltaSpeed)
        {
            float currentKmh = carCont.speed;
            if (carCont.direction == -1)
            {
                currentKmh *= -1;
            }
            int speedBin = Mathf.Clamp(Mathf.FloorToInt(currentKmh / 10f), 0, 9);
            //Debug.Log("SpeedBin: " + speedBin);
            int targetSpeedBin = Mathf.Clamp(speedBin + deltaSpeed, 0, 9);
            float targetKmh = targetSpeedBin * 10f;
            //Debug.Log("TargetSpeedBin: " + targetSpeedBin);
            float speedDiff = targetKmh - speedBin * 10f;

            float throttle = 0f;
            float brake = 0f;
            //Debug.Log("Speed diff: " + speedDiff);
            if (deltaSpeed == 1)
            {
                brake = 0f;
                throttle = 0f;
                if (carCont.direction != -1)
                {
                    throttle = 1f;
                }
            }
            else if (deltaSpeed == -1)
            {
                throttle = 0f;
                brake = 1f;
            }
            else
            {
                if (carCont.direction != -1)
                {
                    throttle = 0.25f;
                }
                brake = 0f;
            }
            //Debug.Log("Throttle: " + throttle);
            carCont.throttleInput = throttle;
            carCont.brakeInput = brake;
        }

        void ApplySteeringChange()
        {
            SteeringController.CheckTargetPoint();
            //SteeringController.SteerWithDelta(currentDeltaSteer);
            SteeringController.SteerTowardsNextTarget();
        }

        /*float ComputeReward(int currentSegmentIdx, int speedIdx, int deltaSpeed)
        {
            float reward = 0f;

            // Penalize being stuck at low speeds
            if (speedIdx <= 3 && deltaSpeed != 1)
            {
                reward -= 0.03f;
            }
            if (lastSegmentIndex != currentSegmentIdx && deltaSpeed == 0)
            {
                reward -= 0.02f;
            }
            if (lastSegmentIndex != currentSegmentIdx && deltaSpeed == 1)
            {
                reward += 0.01f;
            }
            return reward;
        }*/
        float ComputeReward(int stateIndex, int action)
        {
            float reward = 0f;

            int speedBucket = Mathf.Clamp(Mathf.FloorToInt(carCont.speed / 10f), 0, 9);
            int targetBucket = Mathf.Clamp(Mathf.FloorToInt(targetSpeed / 10f), 0, 9);

            if (speedBucket < targetBucket && action == 2)
            {
                reward += 2f;
            }
            else if (speedBucket == targetBucket && action == 1)
            {
                reward += 2f;
            }
            else if(speedBucket > targetBucket && action == 0)
            {
                reward += 2f;
            }

            return reward;
        }

        public int GetCurrentSegmentIndex()
        {
            //Debug.Log("RoadSegment Index: " + roadSegmentIndex);
            return roadSegmentIndex; // <- Currently controlled by SteeringController
        }

        int GetRoadLayout(int segmentIndex)
        {
            RoadSegment rs = RoadCollection[segmentIndex];

            switch (rs.Type)
            {
                case RoadType.Left:
                    return 0;
                case RoadType.Straight:
                    return 1;
                case RoadType.Right:
                    return 2;
                default:
                    return 1;
            }
        }
    }
}
