using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StudyControllerConditionA : MonoBehaviour
{
    [SerializeField] private StudyController studyController;
    [SerializeField] private Slider feedbackSlider;
    
    [SerializeField] private GameObject page0;
    [SerializeField] private TextMeshProUGUI page0Title;
    [SerializeField] private GameObject page1;
    [SerializeField] private TextMeshProUGUI page1Title;
    
    public void OnStartRoundButtonClicked()
    {
        page0.SetActive(false);
        page1.SetActive(true);
        studyController.StartFirstRound();
    }

    public void OnSubmitButtonClicked()
    {
        StudyController.AgentFeedback feedback = new StudyController.AgentFeedback
        {
            likenessScore = feedbackSlider.value,
            speedAdjustment = StudyController.ParameterAdjustment.Ignore,
            dtcAdjustment = StudyController.ParameterAdjustment.Ignore,
            accelAdjustment = StudyController.ParameterAdjustment.Ignore,
            smoothAdjustment = StudyController.ParameterAdjustment.Ignore
        };

        studyController.SubmitFeedback(feedback);
        feedbackSlider.value = 0.5f;
    }
    
    public void Update()
    {
        if (page0.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                OnStartRoundButtonClicked();
            }
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                OnSubmitButtonClicked();
            }
        }
    }

    public void OnEnable()
    {
        page0Title.text = "DriveYourself - ID " + studyController.participantID + " - Condition A";
        page1Title.text = "Feedback for previous Driving Style: " + studyController.boManager.currentIteration + "/" + studyController.boManager.totalIterations;
    }
}