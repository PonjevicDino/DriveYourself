using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

public class ControllerSwitcher : MonoBehaviour
{
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
    
    private ControllerMode appliedControllerMode;
    private bool pendingModeSwitch = false;

    public ControllerMode CurrentMode => currentControllerMode;
    public bool IsRLMode => currentControllerMode == ControllerMode.RL;

    [SerializeField] private GameObject controllers;
    private DriveYourselfAgent agent;
    private DecisionRequester agentDecisionRequester;
    private SimpleMPC simpleMpc;
    private DynamicMPC dynamicMpc;
    private RCC_CarControllerV4 carController;

    private void Awake()
    {
        CacheComponents();
        SwitchControllerMode();
    }

    private void CacheComponents()
    {
        if (agent == null)
        {
            agent = controllers.GetComponent<DriveYourselfAgent>();
        }
        if (agentDecisionRequester == null)
        {
            agentDecisionRequester = controllers.GetComponent<DecisionRequester>();
        }
        if (simpleMpc == null)
        {
            simpleMpc = controllers.GetComponent<SimpleMPC>();
        }
        if (dynamicMpc == null)
        {
            dynamicMpc = controllers.GetComponent<DynamicMPC>();
        }
        if (carController == null && transform.parent != null)
        {
            carController = controllers.transform.parent.GetComponent<RCC_CarControllerV4>();
        }
    }

    private void OnValidate()
    {
        if (Application.isPlaying && currentControllerMode != appliedControllerMode)
        {
            pendingModeSwitch = true;
        }
    }

    private void Update()
    {
        if (pendingModeSwitch)
        {
            pendingModeSwitch = false;
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
        CacheComponents();
        appliedControllerMode = currentControllerMode;
        pendingModeSwitch = false;

        bool isRL = currentControllerMode == ControllerMode.RL;
        bool isSimple = currentControllerMode == ControllerMode.MPC_SIMPLE;
        bool isDynamic = currentControllerMode == ControllerMode.MPC_DYNAMIC;

        if (simpleMpc != null && !isSimple)
        {
            simpleMpc.enabled = false;
        }
        if (dynamicMpc != null && !isDynamic)
        {
            dynamicMpc.enabled = false;
        }

        if (agentDecisionRequester != null)
        {
            agentDecisionRequester.enabled = isRL;
        }
        if (agent != null)
        {
            if (!agent.enabled) agent.enabled = true;
            agent.SetControlActive(isRL);
        }
        
        if (simpleMpc != null && isSimple) simpleMpc.enabled = true;
        if (dynamicMpc != null && isDynamic) dynamicMpc.enabled = true;
        
        if (carController != null)
        {
            carController.overrideInputs = !isRL;
            carController.externalController = true;
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