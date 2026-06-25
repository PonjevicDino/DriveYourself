using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FeedbackButtonSelector : MonoBehaviour
{
    [SerializeField] private List<GameObject> speedButtons;
    [SerializeField] private List<GameObject> dtcButtons;
    [SerializeField] private List<GameObject> accButtons;
    [SerializeField] private List<GameObject> smoothButtons;

    private int selectedSpeed = 0;
    private int selectedDtC = 0;
    private int selectedAcc = 0;
    private int selectedSmoothness = 0;
    
    public int SelectedSpeed => selectedSpeed;
    public int SelectedDtC => selectedDtC;
    public int SelectedAcc => selectedAcc;
    public int SelectedSmoothness => selectedSmoothness;

    public void Reset()
    {
        selectedSpeed = 0;
        selectedDtC = 0;
        selectedAcc = 0;
        selectedSmoothness = 0;
        
        ClearButtonColors(speedButtons);
        ClearButtonColors(dtcButtons);
        ClearButtonColors(accButtons);
        ClearButtonColors(smoothButtons);
    }

    private void ClearButtonColors(List<GameObject> targetList)
    {
        if (targetList == null) return;
        foreach (GameObject button in targetList)
        {
            button.GetComponent<Image>().color = Color.white;
        }
    }

    public void SelectSpeedMuchLess() => RadioButtonSelector("speed", -2);
    public void SelectSpeedSlightlyLess() => RadioButtonSelector("speed", -1);
    public void SelectSpeedKeep() => RadioButtonSelector("speed", 0);
    public void SelectSpeedSlightlyMore() => RadioButtonSelector("speed", 1);
    public void SelectSpeedMuchMore() => RadioButtonSelector("speed", 2);
    
    public void SelectDtCMuchLess() => RadioButtonSelector("dtc", -2);
    public void SelectDtCSlightlyLess() => RadioButtonSelector("dtc", -1);
    public void SelectDtCKeep() => RadioButtonSelector("dtc", 0);
    public void SelectDtCSlightlyMore() => RadioButtonSelector("dtc", 1);
    public void SelectDtCMuchMore() => RadioButtonSelector("dtc", 2);
    
    public void SelectAccMuchLess() => RadioButtonSelector("acc", -2);
    public void SelectAccSlightlyLess() => RadioButtonSelector("acc", -1);
    public void SelectAccKeep() => RadioButtonSelector("acc", 0);
    public void SelectAccSlightlyMore() => RadioButtonSelector("acc", 1);
    public void SelectAccMuchMore() => RadioButtonSelector("acc", 2);
    
    public void SelectSmoothnessMuchLess() => RadioButtonSelector("smooth", -2);
    public void SelectSmoothnessSlightlyLess() => RadioButtonSelector("smooth", -1);
    public void SelectSmoothnessKeep() => RadioButtonSelector("smooth", 0);
    public void SelectSmoothnessSlightlyMore() => RadioButtonSelector("smooth", 1);
    public void SelectSmoothnessMuchMore() => RadioButtonSelector("smooth", 2);

    
    private void RadioButtonSelector(string type, int value)
    {
        List<GameObject> targetList = null;
        
        switch (type)
        {
            case "speed":
                targetList = speedButtons;
                selectedSpeed = value;
                break;
            case "dtc":
                targetList = dtcButtons;
                selectedDtC = value;
                break;
            case "acc":
                targetList = accButtons;
                selectedAcc = value;
                break;
            case "smooth":
                targetList = smoothButtons;
                selectedSmoothness = value;
                break;
        }
        
        if (targetList == null) return; 
        foreach (GameObject button in targetList)
        {
            button.GetComponent<Image>().color = Color.white;
        }

        int index = value + 2; 
        if (index >= 0 && index < targetList.Count) 
        {
            targetList[index].GetComponent<Image>().color = Color.blue;
        }
        else
        {
            Debug.LogWarning($"Button index {index} is missing in the {type} list. All 5 buttons assigned in the Inspector?");
        }
    }
}