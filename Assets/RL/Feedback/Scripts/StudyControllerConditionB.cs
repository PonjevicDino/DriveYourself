using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class StudyControllerConditionB : MonoBehaviour
{
    [SerializeField] private StudyController studyController;
    [SerializeField] private Slider feedbackSlider;
    
    [SerializeField] private FeedbackButtonSelector ratingScreenButtonSelector;
    [SerializeField] private FeedbackButtonSelector inGameButtonSelector;
    
    [SerializeField] private GameObject page0;
    [SerializeField] private TextMeshProUGUI page0Title;
    [SerializeField] private GameObject page1;
    [SerializeField] private TextMeshProUGUI page1Title;

    public void OnSubmitButtonClicked()
    {
        StudyController.AgentFeedback feedback = new StudyController.AgentFeedback
        {
            likenessScore = feedbackSlider.value,
            speedAdjustment = (StudyController.ParameterAdjustment)ratingScreenButtonSelector.SelectedSpeed,
            dtcAdjustment = (StudyController.ParameterAdjustment)ratingScreenButtonSelector.SelectedDtC,
            accelAdjustment = (StudyController.ParameterAdjustment)ratingScreenButtonSelector.SelectedAcc,
            smoothAdjustment = (StudyController.ParameterAdjustment)ratingScreenButtonSelector.SelectedSmoothness
        };

        studyController.SubmitFeedback(feedback);
        feedbackSlider.value = 0.5f;
        ratingScreenButtonSelector.Reset();
        inGameButtonSelector.Reset();
        
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
        
        inGameButtonSelector.gameObject.SetActive(true);
    }
    
    public void OnClearButtonClicked()
    {
        ratingScreenButtonSelector.Reset();
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
        page1Title.text = "Feedback for previous Driving Style: " + studyController.boManager.currentIteration + "/" + studyController.boManager.totalIterations;

        if (inGameButtonSelector != null && ratingScreenButtonSelector != null && page1.activeSelf)
        {
            ratingScreenButtonSelector.SyncState(
                inGameButtonSelector.SelectedSpeed,
                inGameButtonSelector.SelectedDtC,
                inGameButtonSelector.SelectedAcc,
                inGameButtonSelector.SelectedSmoothness
            );
        }
        
        inGameButtonSelector.gameObject.SetActive(false);
    }

    void Start()
    {
        inGameButtonSelector.gameObject.SetActive(true);
    }
}