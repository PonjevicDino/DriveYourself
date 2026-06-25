using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

public class DemoBO : MonoBehaviour
{
    [SerializeField] private uint stepOneIterations;
    [SerializeField] private uint stepTwoIterations;
    private uint currentIteration = 0;

    private uint minSpeed = 50;
    private uint maxSpeed = 100;
    private uint minDtC = 0;
    private uint maxDtC = 100;
    private uint minAcc = 5;
    private uint maxAcc = 15;
    private uint minSmooth = 1;
    private uint maxSmooth = 9;
    
    [HideInInspector] public bool initialized = true;
    [HideInInspector] public bool hasNextParameterValue = false;

    public int4 ReturnNextAgent()
    {
        int nextTargetSpeed = Mathf.RoundToInt(Random.Range(minSpeed, maxSpeed));
        int nextTargetDtC = Mathf.RoundToInt(Random.Range(minDtC, maxDtC));
        int nextTargetAcc = Mathf.RoundToInt(Random.Range(minAcc, maxAcc));
        int nextTargetSmooth = Mathf.RoundToInt(Random.Range(minSmooth, maxSmooth));
        currentIteration++;
        
        Debug.Log(currentIteration + "/" + stepOneIterations + "/" + (stepOneIterations + stepTwoIterations) + ": " +
                  "DummyBO returned [" + nextTargetSpeed + "," + nextTargetDtC + "," + nextTargetAcc + "," + nextTargetSmooth + "]");
        
        return new int4(nextTargetSpeed, nextTargetDtC, nextTargetAcc, nextTargetSmooth);
    }

    public void GetUserResponse(StudyController.AgentFeedback feedback)
    {
        Debug.Log("========================================");
        Debug.Log($"Iteration {currentIteration}: DummyBO received Likeness: {feedback.likenessScore}");
        Debug.Log($"- Adjustments -> Speed: {feedback.speedAdjustment}, DtC: {feedback.dtcAdjustment}, Accel: {feedback.accelAdjustment}, Smooth: {feedback.smoothAdjustment}");
        
        hasNextParameterValue = true;
    }

    public uint2 ReturnIterations()
    {
        return new uint2(currentIteration, stepOneIterations + stepTwoIterations);
    } 
}
