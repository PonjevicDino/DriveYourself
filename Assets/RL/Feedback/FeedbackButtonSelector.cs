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

    public void SelectSpeedMuchLess()
    {
        RadioButtonSelector("speed", -2);
    }

    public void SelectSpeedSlightlyLess()
    {
        RadioButtonSelector("speed", -1);
    }

    public void SelectSpeedSlightlyMore()
    {
        RadioButtonSelector("speed", +1);
    }

    public void SelectSpeedMuchMore()
    {
        RadioButtonSelector("speed", +2);
    }

    public void SelectDtCMuchLess()
    {
        RadioButtonSelector("dtc", -2);
    }

    public void SelectDtCSlightlyLess()
    {
        RadioButtonSelector("dtc", -1);
    }

    public void SelectDtCSlightlyMore()
    {
        RadioButtonSelector("dtc", +1);
    }

    public void SelectDtCMuchMore()
    {
        RadioButtonSelector("dtc", +2);
    }

    public void SelectAccMuchLess()
    {
        RadioButtonSelector("acc", -2);
    }

    public void SelectAccSlightlyLess()
    {
        RadioButtonSelector("acc", -1);
    }

    public void SelectAccSlightlyMore()
    {
        RadioButtonSelector("acc", +1);
    }

    public void SelectAccMuchMore()
    {
        RadioButtonSelector("acc", +2);
    }

    public void SelectSmoothnessMuchLess()
    {
        RadioButtonSelector("smooth", -2);
    }

    public void SelectSmoothnessSlightlyLess()
    {
        RadioButtonSelector("smooth", -1);
    }

    public void SelectSmoothnessSlightlyMore()
    {
        RadioButtonSelector("smooth", +1);
    }

    public void SelectSmoothnessMuchMore()
    {
        RadioButtonSelector("smooth", +2);
    }

    private void RadioButtonSelector(string type, int value)
    {
        switch (type)
        {
            case "speed":
                foreach (GameObject button in speedButtons)
                {
                    button.GetComponent<Image>().color = Color.white;
                }
                selectedSpeed = value;
                switch (value)
                {
                    case -2:
                        speedButtons[0].GetComponent<Image>().color = Color.blue;
                        break;
                    case -1:
                        speedButtons[1].GetComponent<Image>().color = Color.blue;
                        break;
                    case +1:
                        speedButtons[2].GetComponent<Image>().color = Color.blue;
                        break;
                    case +2:
                        speedButtons[3].GetComponent<Image>().color = Color.blue;
                        break;
                }
                break;
            case "dtc":
                foreach (GameObject button in dtcButtons)
                {
                    button.GetComponent<Image>().color = Color.white;
                }
                selectedDtC = value;
                switch (value)
                {
                    case -2:
                        dtcButtons[0].GetComponent<Image>().color = Color.blue;
                        break;
                    case -1:
                        dtcButtons[1].GetComponent<Image>().color = Color.blue;
                        break;
                    case +1:
                        dtcButtons[2].GetComponent<Image>().color = Color.blue;
                        break;
                    case +2:
                        dtcButtons[3].GetComponent<Image>().color = Color.blue;
                        break;
                }
                break;
            case "acc":
                foreach (GameObject button in accButtons)
                {
                    button.GetComponent<Image>().color = Color.white;
                }
                selectedAcc = value;
                switch (value)
                {
                    case -2:
                        accButtons[0].GetComponent<Image>().color = Color.blue;
                        break;
                    case -1:
                        accButtons[1].GetComponent<Image>().color = Color.blue;
                        break;
                    case +1:
                        accButtons[2].GetComponent<Image>().color = Color.blue;
                        break;
                    case +2:
                        accButtons[3].GetComponent<Image>().color = Color.blue;
                        break;
                }
                break;
            case "smooth":
                foreach (GameObject button in smoothButtons)
                {
                    button.GetComponent<Image>().color = Color.white;
                }
                selectedSmoothness = value;
                switch (value)
                {
                    case -2:
                        smoothButtons[0].GetComponent<Image>().color = Color.blue;
                        break;
                    case -1:
                        smoothButtons[1].GetComponent<Image>().color = Color.blue;
                        break;
                    case +1:
                        smoothButtons[2].GetComponent<Image>().color = Color.blue;
                        break;
                    case +2:
                        smoothButtons[3].GetComponent<Image>().color = Color.blue;
                        break;
                }
                break;
        }
    }
}
