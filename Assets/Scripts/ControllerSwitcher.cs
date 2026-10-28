using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

public class ControllerSwitcher : MonoBehaviour
{
    [SerializeField] private GameObject carControllers;
    private RCC_CarControllerV4 car;
    
    public enum ControllerMode
    {
        RL,
        MPC_SIMPLE,
        MPC_DYNAMIC
    }

    [SerializeField] private ControllerMode currentControllerMode = ControllerMode.RL;

    [SerializeField] private GameObject enabledRLButton;
    [SerializeField] private GameObject enabledMPCsButton;
    [SerializeField] private GameObject enabledMPCdButton;
    
    public ControllerMode CurrentMode => currentControllerMode;
    public bool IsRLMode => currentControllerMode == ControllerMode.RL;

    void Start()
    {
        car = carControllers.transform.parent.GetComponent<RCC_CarControllerV4>();
    }
    
    private void Awake()
    {
        if (car == null)
        {
            Start();
        }
        SwitchControllerMode();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            SwitchControllerMode();
        }
    }

    public void SwitchToSimpleMPC()
    {
        currentControllerMode = ControllerMode.MPC_SIMPLE;
        SwitchControllerMode();
    }

    public void SwitchToDynamicMPC()
    {
        currentControllerMode = ControllerMode.MPC_DYNAMIC;
        SwitchControllerMode();
    }

    public void SwitchToRL()
    {
        currentControllerMode = ControllerMode.RL;
        SwitchControllerMode();
    }

    private void SwitchControllerMode()
    {
        carControllers.GetComponent<SimpleMPC>().enabled = currentControllerMode == ControllerMode.MPC_SIMPLE;
        carControllers.GetComponent<DynamicMPC>().enabled = currentControllerMode == ControllerMode.MPC_DYNAMIC;
        
        carControllers.GetComponent<BehaviorParameters>().enabled = currentControllerMode == ControllerMode.RL;
        carControllers.GetComponent<DecisionRequester>().enabled = currentControllerMode == ControllerMode.RL;
        carControllers.GetComponent<DriveYourselfAgent>().enabled = currentControllerMode == ControllerMode.RL;

        if (currentControllerMode == ControllerMode.RL)
        {
            car.externalController = true;
        }
        else
        {
            car.externalController = false;
        }

        ApplyButtons();
    }

    public string GetActiveControllerLabel(string loadedRlModelName)
    {
        return currentControllerMode switch
        {
            ControllerMode.MPC_SIMPLE => "SimpleMPC",
            ControllerMode.MPC_DYNAMIC => "DynamicMPC",
            _ => loadedRlModelName
        };
    }

    private void ApplyButtons()
    {
        switch (currentControllerMode)
        {
            case ControllerMode.RL:
                enabledRLButton.SetActive(true);
                enabledMPCsButton.SetActive(false);
                enabledMPCdButton.SetActive(false);
                break;
            case ControllerMode.MPC_SIMPLE:
                enabledRLButton.SetActive(false);
                enabledMPCsButton.SetActive(true);
                enabledMPCdButton.SetActive(false);
                break;
            case ControllerMode.MPC_DYNAMIC:
                enabledRLButton.SetActive(false);
                enabledMPCsButton.SetActive(false);
                enabledMPCdButton.SetActive(true);
                break;
        }
    }
}