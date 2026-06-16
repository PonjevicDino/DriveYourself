using System;
using System.Collections;
using BOforUnity;
using UnityEngine;

public class StudyController : MonoBehaviour
{
    [SerializeField] private GameObject startupWindow;
    [SerializeField] private GameObject conditionAWindow;
    [SerializeField] private GameObject conditionBWindow;
    [SerializeField] private GameObject conditionCWindow;
    [SerializeField] private GameObject conditionDWindow;
    [SerializeField] private GameObject progressWindow;
    private GameObject introWindow;
    private GameObject activeConditionWindow;
    
    [Header("Car Parameters")]
    [SerializeField] private GameObject car;
    [SerializeField] private Transform startingPosition;
    
    [Header("Bayesian Optimization")]
    [SerializeField] private BoForUnityManager boManager;

    private float currentSpeed;
    private float currentDistanceToCenter;
    private float currentAcceleration;
    private float currentSmoothness;
    
    private void Start()
    {
        RespawnCar();
        car.GetComponent<Rigidbody>().isKinematic = true;
        startupWindow.SetActive(true);
        conditionAWindow.SetActive(false);
        conditionBWindow.SetActive(false);
        conditionCWindow.SetActive(false);
        conditionDWindow.SetActive(false);
        startingPosition.GetChild(0).gameObject.SetActive(false);
    }

    public void SelectCondition(String condition)
    {
        startupWindow.SetActive(false);
        switch (condition)
        {
            case "A":
                conditionAWindow.SetActive(true);
                activeConditionWindow = conditionAWindow;
                break;
            case "B":
                conditionBWindow.SetActive(true);
                activeConditionWindow = conditionBWindow;
                break;
            case "C":
                conditionCWindow.SetActive(true);
                activeConditionWindow = conditionCWindow;
                break;
            case "D":
                conditionDWindow.SetActive(true);
                activeConditionWindow = conditionDWindow;
                break;
            default:
                startupWindow.SetActive(true);
                return;
        }
    }

    public void StartFirstRound()
    {
        StartCoroutine(WaitForBOInitialization());
    }

    private IEnumerator WaitForBOInitialization()
    {
        yield return new WaitUntil(() => boManager.initialized);
        ExtractAgentParameters();
        RespawnCar();
        StartCoroutine(CheckForFinishedLap());
    }
    
    private void ExtractAgentParameters()
    {
        currentSpeed = boManager.optimizer.GetParameterValue("VehicleSpeed");
        currentDistanceToCenter = boManager.optimizer.GetParameterValue("VehicleDistanceToCenter");
        currentAcceleration = boManager.optimizer.GetParameterValue("VehicleMaxAcceleration");
        currentSmoothness = boManager.optimizer.GetParameterValue("VehicleSmoothness");

        // TODO: Apply these 4 variables directly to your car controller or ML-Agent script here
        Debug.Log($"New Agent Loaded -> Speed: {currentSpeed}, Dist: {currentDistanceToCenter}, Accel: {currentAcceleration}, Smooth: {currentSmoothness}");
    }
    
    private IEnumerator CheckForFinishedLap()
    {
        while (true) // TODO: Replace with actual lap completion logic
        {
            yield return new WaitForEndOfFrame();
            // TODO: Update Lap position in progress window
            
            // Temporary break condition for testing
            if (Input.GetKeyDown(KeyCode.Return)) break; 
        }
        StopRound();
    }
    
    public void StopRound()
    {
        activeConditionWindow.SetActive(true);
        // TODO: Enter current Round in Title
    }

    private void RespawnCar()
    {
        car.GetComponent<Rigidbody>().isKinematic = true;
        car.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        car.GetComponent<Rigidbody>().angularVelocity = Vector3.zero;
        car.transform.SetPositionAndRotation(startingPosition.position, startingPosition.rotation);
        car.GetComponent<RCC_CarControllerV4>().engineRPMRaw = 0f;
        car.GetComponent<RCC_CarControllerV4>().engineRPM = 0f;
        car.GetComponent<Rigidbody>().isKinematic = false;
    }
    
    public void SubmitFeedbackAndStartNextRound(float siderValue)
    {
        activeConditionWindow.SetActive(false);
        StartCoroutine(ProcessFeedbackSequence(siderValue));
    }

    private IEnumerator ProcessFeedbackSequence(float sliderValue)
    {
        boManager.optimizer.AddObjectiveValue("Comfort", sliderValue);
        boManager.OptimizationStart();
        yield return new WaitUntil(() => boManager.hasNewDesignParameterValues);
        boManager.hasNewDesignParameterValues = false;
        
        ExtractAgentParameters();
        RespawnCar();
        StartCoroutine(CheckForFinishedLap());
    }
}
