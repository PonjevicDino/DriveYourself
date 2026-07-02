using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class StudyControllerConditionB : MonoBehaviour
{
    [SerializeField] private StudyController studyController;
    [SerializeField] private Slider feedbackSlider;
    [SerializeField] private FeedbackButtonSelector buttonSelector;
    
    [SerializeField] private GameObject page0;
    [SerializeField] private TextMeshProUGUI page0Title;
    [SerializeField] private GameObject page1;
    [SerializeField] private TextMeshProUGUI page1Title;

    public void OnSubmitButtonClicked()
    {
        StudyController.AgentFeedback feedback = new StudyController.AgentFeedback
        {
            likenessScore = feedbackSlider.value,
            speedAdjustment = (StudyController.ParameterAdjustment)buttonSelector.SelectedSpeed,
            dtcAdjustment = (StudyController.ParameterAdjustment)buttonSelector.SelectedDtC,
            accelAdjustment = (StudyController.ParameterAdjustment)buttonSelector.SelectedAcc,
            smoothAdjustment = (StudyController.ParameterAdjustment)buttonSelector.SelectedSmoothness
        };

        studyController.SubmitFeedback(feedback);
        feedbackSlider.value = 0.5f;
        buttonSelector.Reset();
        
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }
    
    public void Update()
    {
        if (page0.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                page0.SetActive(false);
                page1.SetActive(true);
                studyController.StartFirstRound();
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
        page0Title.text = "DriveYourself - ID " + studyController.participantID + " - Condition B";
        //page1Title.text = "Feedback for previous Driving Style: " + studyController.demoBoManager.ReturnIterations()[0] + "/" + studyController.demoBoManager.ReturnIterations()[1];
        page1Title.text = "Feedback for previous Driving Style: " + studyController.boManager.currentIteration + "/" + studyController.boManager.totalIterations;
    }
}