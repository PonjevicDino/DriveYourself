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

    private int selectedSpeed = -999;
    private int selectedDtC = -999;
    private int selectedAcc = -999;
    private int selectedSmoothness = -999;
    
    public int SelectedSpeed => selectedSpeed;
    public int SelectedDtC => selectedDtC;
    public int SelectedAcc => selectedAcc;
    public int SelectedSmoothness => selectedSmoothness;
    
    public void SyncState(int speed, int dtc, int acc, int smooth)
    {
        RadioButtonSelector("speed", speed);
        RadioButtonSelector("dtc", dtc);
        RadioButtonSelector("acc", acc);
        RadioButtonSelector("smooth", smooth);
    }

    public void Reset()
    {
        selectedSpeed = -999;
        selectedDtC = -999;
        selectedAcc = -999;
        selectedSmoothness = -999;
        
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
        
        if (value == -999) return;

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