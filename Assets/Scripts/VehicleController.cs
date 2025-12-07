using Assets.Scripts.Components;
using Assets.Scripts.QLearningModules;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Assets.Scripts
{
    public class VehicleController : MonoBehaviour
    {
        #region Variables
        [Header("Attached Scripts")]
        [SerializeField]
        private DisplayUpdates DisplayUpdates;
        [SerializeField]
        private SteeringController SteeringController;

        //Waypoints: For now the car drives/turns by the spheres placed on the track
        //Though the steering is not smooth yet, it will be improved later on.
        [Header("Road Segments")]
        [SerializeField]
        public GameObject RoadCollectionParent;
        public List<RoadSegment> RoadCollection = new List<RoadSegment>();

        public GameObject Car;
        public RCC_CarControllerV4 carCont = null;
        public GameObject currentRoad = null;
        //public GameObject currentSphere = null;
        public GameObject carFront = null;
        public int roadSegmentIndex = 0;


        public bool EligibilityTraces = false;
        public float desiredSpeed = 50f;
        public float speedStep = 10f;
        public float maxSpeed = 90f;
        public float targetSpeed = 0f; // Set your desired speed (km/h)
        public float accelerationRate = 1.5f; // Speed adjustment rate
        public float brakingPower = 3f; // Strength of braking if speed is too high
        public bool enableBraking = true; // Enable braking if the car exceeds speed
                                          // Start is called once before the first execution of Update after the MonoBehaviour is created

        // Q Learning
        public float learningRate = 0.1f; // Alpha
        public float discountFactor = 0.9f; // Gamma
        public float explorationRate = 1.0f; // 0.2f; // Epsilon  //HF Start at 1

        public float lambda_ = 0.9f; //used for Eligibility traces. 0 < lambda_ < 1. If 0, regular update, only most recent experience taken into account. If 1, Monte Carlo, use all experience
                                     //λ = 0.3: Faster but less stable
                                     // λ = 0.9: Slower but better credit assignment

        public bool Watkins = true; //If true, set Eligibility traces to 0 when an explorative action is taken
        public bool scriptControl = true;




        protected int numUpdates; //Number of update cycles




        public int state; // Current speed state
        public int action; // Current action (0 = decrease, 1 = maintain, 2 = increase)

        protected int rewardstate;
        protected int rewardaction;

        public int numstates = 8;
        public int numactions = 3;

        public float[,] qTable; // Q-table
        public float[,] eTable; // Eligibility traces Table


        protected float decisionTimer = 0f; // Timer to control decision intervals
        protected float decisionInterval = 1f; // Make a decision once per 1 seconds
        protected bool waitingForReward = false; // Prevent multiple reward calculations

        protected bool rewardbool = false;
        protected bool punishment = false;
        protected bool increaseSpeedAction = false;
        protected bool decreaseSpeedAction = false;
        protected bool noAction = false;
        protected bool processUserAction = false;

        protected int timeWheelsOffGround = 0;


        protected float RTimePressed;
        protected float RTimeUp;

        protected bool timedReward;
        protected float amountOfReward;

        public enum RewardMode
        {
            SimpleController,
            User,
            Combined,
            DriveYourself,
            SpeedBasedPotential
        }

        [SerializeField]
        public RewardMode rewardMode; // This appears as a dropdown in the Inspector

        public RewardMode SelectedRewardMode => rewardMode; // Public getter

        #endregion

        private void Awake()
        {
            if(SteeringController == null)
            {
                SteeringController = GetComponent<SteeringController>();
            }
            if (DisplayUpdates == null)
            {
                DisplayUpdates = gameObject.GetComponent<DisplayUpdates>();
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

            rewardMode = RewardMode.DriveYourself;
            explorationRate = 1.0f;
            qTable = new float[numstates, numactions];
            eTable = new float[numstates, numactions];

            if (Car != null)
            {
                carCont = Car.GetComponent<RCC_CarControllerV4>();
            }
            if (RoadCollection != null && RoadCollection.Count > 0) 
            {
                currentRoad = RoadCollection[0].gameObject;
                //currentSphere = RoadCollection[0];
            }

        }
        // Update is called once per frame
        void Update()
        {

            bool allWheelsOnGround = true;

            //Check if wheels are on the ground
            foreach (var wheel in carCont.AllWheelColliders)
            {

                if (!wheel.isGrounded)
                {
                    allWheelsOnGround = false;
                    break;
                }
            }


            //I only want to update state when the wheels are on the road
            if (allWheelsOnGround)
            {

                timeWheelsOffGround = 0;
                // ⏳ Update decision timer
                decisionTimer += Time.deltaTime;

                if (decisionTimer >= decisionInterval && !waitingForReward)
                {
                    decisionTimer = 0f; // Reset timer
                    StartCoroutine(DelayedRewardCalculation()); // Start reward delay process 
                                                                //RewardCalculation();
                }

                GetUserInput();

                #region Simulate Steering
                SteeringController.CheckTargetPoint();
                SteeringController.SteerTowardsNextTarget();
                #endregion

                if (SelectedRewardMode != RewardMode.DriveYourself) ControlSpeed();
                DisplayUpdates.UpdateQTableDisplay();
            }
            else
            {
                DetermineCarReset();
            }
        }
        public void DetermineCarReset()
        {
            //Debug.Log("Some wheels are off the ground!");
            timeWheelsOffGround++;


            //Once the car has dropped off the road, reset it
            if (timeWheelsOffGround > 100)
            {
                targetSpeed = 0;
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
                currentRoad = RoadCollection[0].gameObject;
                //currentSphere = RoadCollection[0];
                roadSegmentIndex = 0;

                #region Reset Steering for Controller
                SteeringController.ResetSteering();
                SteeringController.CheckTargetPoint();
                SteeringController.SteerTowardsNextTarget();
                #endregion

                carCont.StartEngine();

                //Give a penalty for this messup of driving off the road
                float reward = -10;

                if (EligibilityTraces) UpdateEliQTable(state, action, reward);
                else UpdateQTable(state, action, reward);
            }
        }

        // 🔥 Coroutine: Delayed Reward Calculation
        IEnumerator DelayedRewardCalculation()
        {
            waitingForReward = true; // Prevent multiple reward calculations


            // 🎯 Determine the current state (0 to 7 based on targetSpeed) 
            state = Mathf.Clamp(Mathf.FloorToInt(targetSpeed / 10f), 0, 9);                    //         
            //state=Mathf.Clamp(Mathf.FloorToInt(carCont.speed  / 10f), 0, numstates-1);            //Change to actual speed?

            if (rewardMode == RewardMode.DriveYourself)
            {
                state = Mathf.Clamp(Mathf.FloorToInt(carCont.speed / 10f), 0, numstates - 1);
            }


            // 🔀 Choose an action (Explore or Exploit)
            if (increaseSpeedAction || decreaseSpeedAction || rewardMode == RewardMode.DriveYourself)
            {
                processUserAction = true;
                if (increaseSpeedAction)
                {
                    action = 2;
                    //Debug.Log("Increase");
                }
                if (decreaseSpeedAction)
                {
                    action = 0;
                    //Debug.Log("Decrease");
                }
                if (noAction) //Only used in RewardMode.DriveYourself
                {
                    action = 1;
                    //Debug.Log("Maintain");
                }
            }
            else
            {
                if (UnityEngine.Random.value < explorationRate)
                {
                    action = UnityEngine.Random.Range(0, numactions); // Explore (random action)

                    if (Watkins && EligibilityTraces) Array.Clear(eTable, 0, eTable.Length); //reset the eligibility traces table if a nongreedy action is taken, and we are using Watkins' method
                }
                else
                {
                    action = GetBestAction(state); // Exploit (use learned Q-values)
                }
            }

            bool waitTillStateChange = false;

            if (rewardMode != RewardMode.DriveYourself)
            {
                // 🚀 Perform the chosen action
                if (action == 0) // Decrease Target Speed
                {
                    targetSpeed = Mathf.Max(0, targetSpeed - speedStep);  //tried before: change targetSpeed to state*10
                    if (state > 0) waitTillStateChange = true;
                }
                else if (action == 1) // Maintain Target Speed
                {

                    targetSpeed = state * 10f; //added
                                               // Do nothing, keep current speed
                                               //waitTillStateChange=false;
                }
                else if (action == 2) // Increase Target Speed
                {
                    targetSpeed = Mathf.Min(maxSpeed, targetSpeed + speedStep);  //tried before: change targetSpeed to state*10
                    if (state < 7) waitTillStateChange = true;
                }

                //For Potential Based Reward Shaping, calculate reward before taking action
                if (rewardMode == RewardMode.SpeedBasedPotential)
                {
                    float reward = CalculateRewardSBP(targetSpeed);
                    Debug.Log("Speed is: " + carCont.speed + ", State is: " + state + ", action is: " + action + ", next state is: " + targetSpeed + ", and the reward is: " + reward);
                    UpdateQTable(state, action, reward);
                    //Debug.Log("Q update: state "+state+", action "+action+" reward: "+reward);
                }



                // ⏳ Wait 1 second before calculating reward
                //yield return new WaitForSeconds(.5f);     //Moved this into the if else statement above, I only want to wait //On second thought, why do I want to wait.. 

                Debug.Log("Target speed is " + targetSpeed);
                //We take the action to transition to the next state
                while (waitTillStateChange)
                {
                    int currentState = Mathf.Clamp(Mathf.FloorToInt(carCont.speed / 10f), 0, 7);//
                                                                                                //Debug.Log("WaitTillStateChange Routine. State: "+state+" CurrentState: "+currentState+ " Speed: "+ carCont.speed + " Action: "+action);


                    if (state == currentState)
                    {
                        yield return new WaitForSeconds(.5f);
                    }
                    else
                    {
                        //waitTillStateChange=false;
                        //Debug.Log("Break! State is: "+state+" Current state is:"+currentState); 
                        break;
                    }


                }
            }
            //need to make sure to update the state if in Rewardmode DriveYourself


            // After! taking the action, we observe the reward 🏆 unless we are in the SpeedBasedPotential reward type
            if (rewardMode != RewardMode.SpeedBasedPotential)
            {
                float reward = CalculateReward(targetSpeed, action);

                //int updatedState = Mathf.Clamp(Mathf.FloorToInt(carCont.speed/ 10f), 0, 7);//

                //Debug.Log("The reward is: "+reward);

                //and the new state, which is s' (new state after taking the action)



                if (EligibilityTraces == false)
                {
                    //Debug.Log("Regular Q Update");

                    UpdateQTable(state, action, reward);
                }
                else
                {
                    //p. 188
                    //Debug.Log("EliqUpdate");

                    UpdateEliQTable(state, action, reward);
                }

            }
            // ⏳ Wait 1 second AFTER calculating reward
            //yield return new WaitForSeconds(.5f);     //Moved this into the if else statement above, I only want to wait //On second thought, why do I want to wait.. 


            if (processUserAction)
            {

                //Reset human actions
                if (increaseSpeedAction && action == 2)
                    increaseSpeedAction = false;
                if (decreaseSpeedAction && action == 0)
                    decreaseSpeedAction = false;
                if (noAction && action == 1)
                    noAction = false;
                processUserAction = false;
            }
            if (scriptControl)
            {
                if (explorationRate > 0f) explorationRate = explorationRate - 0.003f; //UPDATE EPSILON.. so the exploration rate is going to decrease slowly over time
            }
            numUpdates++;

            waitingForReward = false; // Ready for next decision
            //Debug.Log("Ready");
        }

        float SpeedPotential(float speed)
        {
            float error = Mathf.Abs(speed - desiredSpeed);

            return -error;

        }
        // 🏆 Reward function (Now Based on Target Speed)

        float CalculateRewardSBP(float nextSpeed)
        {

            // Regular environment reward
            float normalReward = 0f; //envReward;

            //Debug.Log("Actual speed is: "+carCont.speed+", nextspeed is: "+nextSpeed+" while desired speed is: "+ desiredSpeed);
            // Compute potentials
            float phi_s = SpeedPotential(carCont.speed);
            float phi_sPrime = SpeedPotential(nextSpeed);

            // Shaping reward
            float shapingReward = discountFactor * phi_sPrime - phi_s;


            //I am adding this, which is not in the Ng paper I think, to add some environmental reward
            //If desired speed is >0 and the current speed is 0, I add a penalty.
            //If the desired speed is reached, I give environmental reward
            if (desiredSpeed > 0 && nextSpeed == 0)
            {
                Debug.Log("Nextspeed is 0");
                normalReward = -10f;
            }
            if (state == Mathf.Clamp(Mathf.FloorToInt(desiredSpeed / 10f), 0, 7))
            {
                normalReward += 25f;
                Debug.Log("We are driving the desired speed");
            }



            //Optional? 
            //Debug.Log("Shaping reward is: "+shapingReward);

            // Final reward
            return normalReward + shapingReward;
        }
        float CalculateReward(float targetSpeed, int action)
        {



            if (SelectedRewardMode == RewardMode.DriveYourself)
            {
                return 1.0f; //Just reward whatever the user does
            }

            if (SelectedRewardMode == RewardMode.SimpleController)
            {

                if (targetSpeed == desiredSpeed) return 1;

                return -(Mathf.Abs(desiredSpeed - targetSpeed) / 10);
            }

            if (SelectedRewardMode == RewardMode.User)
            {
                if (increaseSpeedAction && processUserAction)
                {
                    //  increaseSpeedAction = false;
                    return 1;
                }
                if (decreaseSpeedAction && processUserAction)
                {
                    //   decreaseSpeedAction = false;
                    return 1;
                }

                if (rewardbool)
                {
                    rewardbool = false;
                    return 1;
                }
                if (punishment)
                {
                    punishment = false;
                    return -1;
                }
            }

            if (SelectedRewardMode == RewardMode.Combined)
            {


                // Gaussian around targetspeed; mu is the targetspeed, x is the actual speed, sigma is chosen to be 5
                double gaussian = (1.0 / (5 * (System.Math.Sqrt(2 * System.Math.PI)))) * System.Math.Exp((-1.0 / 2.0) * (System.Math.Pow((double)carCont.speed - (double)targetSpeed, 2) / System.Math.Pow(5, 2)));


                float combinedReward = (float)gaussian;


                if (increaseSpeedAction && processUserAction)
                {
                    //  increaseSpeedAction = false;
                    combinedReward += 1;
                    //specialEvents[(state,action)]+=1;
                }
                else if (decreaseSpeedAction && processUserAction)
                {
                    //   decreaseSpeedAction = false;
                    combinedReward += 1;
                    //specialEvents[(state,action)]+=1;
                }

                if (rewardbool)
                {
                    rewardbool = false;
                    combinedReward += 1;
                    //specialEvents[(state,action)]+=1;
                }
                else if (timedReward)
                {
                    timedReward = false;

                    //DIRECT Q TABLE UPDATE!
                    if (rewardaction > 0 && rewardstate > 0) qTable[rewardstate, rewardaction] += amountOfReward;
                    rewardstate = -1;
                    rewardaction = -1;

                    //combinedReward+=1+amountOfReward;
                    //Debug.Log("Combined Reward:"+ combinedReward);
                }
                else if (punishment)
                {
                    punishment = false;
                    combinedReward -= 1;

                }


                //I want to discourage switching constantly between braking and accelerating
                if (action == 0 | action == 2)
                    combinedReward -= 0.2f;


                return combinedReward;

            }

            return 0;


        }

        // 📖 Q-learning update rule
        void UpdateQTable(int state, int action, float reward)
        {


            //Debug.Log("Start of UpdateQtable routine. State is: "+ state+ ", Action is: "+action);
            //int nextState = Mathf.Clamp(Mathf.FloorToInt(targetSpeed / 10f), 0, 7); //This is not actually the next state... 
            //I'd say the next state is the state given the current action?
            int nextState = getNextState(state, action);

            float maxNextQ = 0f;
            // Debug.Log("nextState"+ nextState);

            //Only calculate if the action yields a state that can be visited...
            if (nextState != -1 && nextState != numstates)
            {
                maxNextQ = Mathf.Max(GetRow(qTable, nextState));
            }

            //maxNextQ = Mathf.Max(qTable[nextState,qTable[nextState, 0], qTable[nextState, 1], qTable[nextState, 2]);
            //Debug.Log("MaxNextQ: "+ maxNextQ);

            //Debug.Log("Updating Q table. State is: "+state+ " and action is: "+ action);

            qTable[state, action] = (1 - learningRate) * qTable[state, action] + learningRate * (reward + discountFactor * maxNextQ);
            //Debug.Log("End of Qtable update routine. State is: "+ state+ ", Action is: "+action+", Reward is: "+ reward+" Update is: "+qTable[state,action]);

        }

        int getNextState(int istate, int iaction)
        {

            int inextState = -1;
            if (iaction == 0 && istate > 0) inextState = istate - 1;
            else if (iaction == 1) inextState = istate;
            else if (iaction == numactions - 1 && istate < numstates - 1) inextState = istate + 1;
            else if (istate >= numstates - 1) inextState = numstates;

            return inextState;

        }

        float[] GetRow(float[,] completeTable, int row)
        {
            float[] rowData = new float[numactions];
            for (int i = 0; i < numactions; i++)
                rowData[i] = completeTable[row, i];
            return rowData;
        }

        void UpdateEliQTable(int state, int action, float reward)
        {


            //Debug.Log("EligibilityTracesRoutine");

            //Set terminal nodes Q table to be 0

            int nextState = getNextState(state, action);
            float maxNextQ = 0f;
            //Debug.Log("nextState"+ nextState);


            //Only calculate if the action yields a state that can be visited...
            if (nextState != -1 && nextState != numstates)
            {
                maxNextQ = Mathf.Max(GetRow(qTable, nextState));
            }

            //Debug.Log("Updating Q table and Eligibility Traces. State is: "+state+ " and action is: "+ action);


            float delta_ = reward + discountFactor * maxNextQ - qTable[state, action];

            // Debug.Log("Reward is: "+reward+", delta is: "+delta_+", update is: "+ learningRate*delta_+" , discountFactor is "+ discountFactor+" maxNextQ is "+maxNextQ+" q table: "+ qTable[state,action]);

            eTable[state, action] = 1.0f; //Replacing traces //can also be +=1 for accumulating traces
            DisplayUpdates.UpdateEligibilityTableDisplay();

            //Update the entire q table and eligibility traces table
            for (int i = 0; i < numstates; i++)
            {
                for (int k = 0; k < numactions; k++)
                {

                    //if (qTable[i,k]+learningRate * delta_ * eTable[i,k] > 1)
                    //  Debug.Log("FLAG!!! Q table is "+qTable[i,k] +" Learning rate is "+learningRate+ " Delta is "+ delta_ + " and etable is "+ eTable[i,k]);
                    qTable[i, k] += learningRate * delta_ * eTable[i, k];
                    eTable[i, k] *= discountFactor * lambda_;
                }
            }





            //Debug.Log("End of Qtable update routine. State is: "+ state+ ", Action is: "+action);

        }

        // 🤖 Get the best action for a given state
        int GetBestAction(int state)
        {
            float maxQ = float.MinValue;
            int bestAction = 1; // Default to maintaining speed
            bool allEqual = true; // Flag to check if all Q-values are the same
            float firstQValue = qTable[state, 0]; // Store first Q-value for comparison

            for (int a = 0; a < numactions; a++)
            {
                if (qTable[state, a] > maxQ)
                {
                    maxQ = qTable[state, a];
                    bestAction = a;
                }

                // Check if all Q-values are equal
                if (qTable[state, a] != firstQValue)
                {
                    allEqual = false;
                }
            }

            // If all Q-values are equal, return 1 (maintain speed)
            return allEqual ? 1 : bestAction;
        }

        public void GetUserInput()
        {
            //NB this uses the legacy input manager
            //should be reworked so that it uses Input System
            if (rewardMode == RewardMode.DriveYourself)
            {

                carCont.externalController = true;
                carCont.throttleInput = 0f;
                carCont.brakeInput = 0f;
                //Check if key is being held
                if (Input.GetKey(KeyCode.UpArrow))
                {
                    //Debug.Log("Uparrow");
                    carCont.throttleInput = 1.0f;//Mathf.Clamp(carCont.throttleInput + (accelerationRate * Time.deltaTime), 0f, 1f);
                    carCont.brakeInput = 0f; // Ensure braking is off while accelerating
                    increaseSpeedAction = true;
                    decreaseSpeedAction = false;
                    noAction = false;

                }
                if (Input.GetKey(KeyCode.DownArrow))
                {
                    //Debug.Log("Downarrow");
                    carCont.throttleInput = 0f; // Stop accelerating
                    carCont.brakeInput = 1.0f; //Mathf.Clamp((carCont.speed - targetSpeed) / brakingPower, 0f, 1f); // Apply brakes smoothly
                    decreaseSpeedAction = true;
                    increaseSpeedAction = false;
                    noAction = false;

                }

                if (!Input.GetKey(KeyCode.UpArrow) && !Input.GetKey(KeyCode.DownArrow))
                {
                    increaseSpeedAction = false;
                    decreaseSpeedAction = false;
                    noAction = true;
                }


            }
            if (rewardMode == RewardMode.User | rewardMode == RewardMode.Combined)
            {



                // Increase Target Speed when "Y" is pressed 
                if (Input.GetKeyDown(KeyCode.Q))
                {
                    //  targetSpeed = Mathf.Min(targetSpeed + speedStep, maxSpeed);
                    increaseSpeedAction = true;


                    Debug.Log("Key Q up.");
                }

                // Decrease Target Speed when "A" is releapressedsed
                if (Input.GetKeyDown(KeyCode.A))
                {

                    decreaseSpeedAction = true;
                    Debug.Log("Key A");

                }

                if (Input.GetKeyDown(KeyCode.W))
                {
                    rewardbool = true;


                    Debug.Log("Reward");
                    Debug.Log("Key W");
                }

                if (Input.GetKeyDown(KeyCode.S))
                {
                    punishment = true;
                    Debug.Log("Punish");
                    Debug.Log("Key S");
                }


                //Experiment with reward mode, where the duration of the key press is a factor
                if (Input.GetKeyDown(KeyCode.R))
                {
                    RTimePressed = Time.time;
                    Debug.Log("R down, state is " + state + " action is " + action);
                    rewardstate = state;
                    rewardaction = action;

                    //Would need to somehow pass the state and action when pressing down

                    //Maybe direct update of a separate table? 
                }

                // Increase Target Speed when "Y" is released (but not beyond maxSpeed)
                if (Input.GetKeyUp(KeyCode.R))
                {

                    timedReward = true;

                    RTimeUp = Time.time;
                    amountOfReward = RTimeUp - RTimePressed;
                    Debug.Log("Key R up. Press Duration: " + amountOfReward + ". State is: " + state + ", action is: " + action + ", To be rewarded state:" + rewardstate + " To be rewarded action: " + rewardaction);




                    //Duration of time press will be somewhere between 0.06 - 2 sec, where 0.06 is normal, 2 sec would be quite long, anything beyond that may not make sense to take into account
                }



            }

            if (rewardMode == RewardMode.SpeedBasedPotential)
            {
                if (Input.GetKeyUp(KeyCode.Q))
                {
                    desiredSpeed += 10;
                }

                if (Input.GetKeyUp(KeyCode.A))
                {
                    desiredSpeed -= 10;
                }
            }

            if (rewardMode == RewardMode.SimpleController)
            {
                if (Input.GetKeyUp(KeyCode.E))
                {
                    desiredSpeed += 10;
                }

                if (Input.GetKeyUp(KeyCode.D))
                {
                    desiredSpeed -= 10;
                }
            }

        }


        // Store the last target direction
        private UnityEngine.Vector3 lastToOther;
        private bool isTransitioning = false; // Track if transitioning between spheres
        private Rigidbody carRigidbody;

        /*void ControlSteering()
        {
            if (currentSphere != null)
            {
                // Compute the new direction to the target sphere
                UnityEngine.Vector3 newToOther = currentSphere.transform.position - carFront.transform.position;

                // If transitioning, smoothly interpolate between the old and new target directions
                if (isTransitioning)
                {
                    lastToOther = UnityEngine.Vector3.Lerp(lastToOther, newToOther, Time.deltaTime * 2f);

                    // Stop transitioning once the change is small enough
                    if (UnityEngine.Vector3.Distance(lastToOther, newToOther) < 0.1f)
                    {
                        lastToOther = newToOther;
                        isTransitioning = false;
                    }
                }
                else
                {
                    // Update directly when not transitioning
                    lastToOther = newToOther;
                }

                // Calculate the signed angle between forward and the (lerped) direction to the sphere
                float angle = UnityEngine.Vector3.SignedAngle(carFront.transform.forward, lastToOther, UnityEngine.Vector3.up);

                // Map the angle to [-1, 1] range for steering input
                float mappedValue = Mathf.Clamp(angle / 90f, -1f, 1f);
                carCont.steerInput = mappedValue;

                // Check if the car is close enough to the sphere to switch to the next one
                if (newToOther.magnitude < 2.5)
                {
                    if (index < 43)
                        index++;
                    else
                        index = 0;

                    currentSphere = RoadCollection[index];
                    isTransitioning = true; // Enable smooth transition to the next sphere
                }
            }
        }*/



        public void ControlSpeed()
        {


            float currentSpeed = carCont.speed; // Get the car's current speed
            carCont.externalController = true;
            // Adjust throttle to reach target speed
            if (currentSpeed < targetSpeed)
            {

                carCont.throttleInput = Mathf.Clamp(carCont.throttleInput + (accelerationRate * Time.deltaTime), 0f, 1f);
                carCont.brakeInput = 0f; // Ensure braking is off while accelerating
                                         //Debug.Log("Acceleration: "+carCont.throttleInput);

            }
            else if (enableBraking && currentSpeed > targetSpeed)
            {
                //Debug.Log("Brake");
                carCont.throttleInput = 0f; // Stop accelerating
                carCont.brakeInput = Mathf.Clamp((currentSpeed - targetSpeed) / brakingPower, 0f, 1f); // Apply brakes smoothly
                                                                                                       //Debug.Log("Braking: "+carCont.brakeInput);
            }
            else
            {
                //Debug.Log("Maintain");
                carCont.throttleInput = 0f; // Maintain speed without accelerating
                carCont.brakeInput = 0f; // Ensure no braking when at target speed
            }



        }



    }
}