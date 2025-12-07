//----------------------------------------------
//            Realistic Car Controller
//
// Copyright © 2014 - 2020 BoneCracker Games
// http://www.bonecrackergames.com
// Buğra Özdoğanlar
//
//----------------------------------------------

#if RCC_LOGITECH
//using DirectInputManager;
using Logitech;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

#region --> Original SDK-Code
/*
public class RCC_LogitechSteeringWheel : MonoBehaviour {

    #region singleton
    private static RCC_LogitechSteeringWheel instance;
    public static RCC_LogitechSteeringWheel Instance {

        get {

            if (instance == null) {

                instance = FindObjectOfType<RCC_LogitechSteeringWheel>();

                if (instance == null) {

                    GameObject sceneManager = new GameObject("_RCCLogitechSteeringWheelManager");
                    instance = sceneManager.AddComponent<RCC_LogitechSteeringWheel>();

                }

            }

            return instance;

        }

    }
    #endregion

    public bool useForceFeedback = true;
    public float roughness = 70f;
    public float collisionForce = 40f;

    void Start() {

        LogitechGSDK.LogiSteeringInitialize(false);

    }

    void OnEnable() {

        RCC_CarControllerV4.OnRCCPlayerCollision += RCC_CarControllerV4_OnRCCPlayerCollision;
        RCC_InputManager.Instance.logitechSteeringUsed = true;

    }

    void RCC_CarControllerV4_OnRCCPlayerCollision(RCC_CarControllerV4 RCC, Collision collision) {

        if (RCC == RCC_SceneManager.Instance.activePlayerVehicle)
            LogitechGSDK.LogiPlayFrontalCollisionForce(0, Mathf.CeilToInt(collision.impulse.magnitude / 10000f * collisionForce));

    }

    void Update() {

        if (LogitechGSDK.LogiUpdate() && LogitechGSDK.LogiIsConnected(0)) {

            if (useForceFeedback)
                ForceFeedback();

        }

    }

    void ForceFeedback() {

        RCC_CarControllerV3 playerVehicle = RCC_SceneManager.Instance.activePlayerVehicle;

        if (!playerVehicle)
            return;

        float sidewaysForce = playerVehicle.FrontLeftWheelCollider.wheelSlipAmountSideways + playerVehicle.FrontRightWheelCollider.wheelSlipAmountSideways;
        sidewaysForce *= Mathf.Abs(sidewaysForce);
        sidewaysForce *= -roughness;

        LogitechGSDK.LogiStopConstantForce(0);
        LogitechGSDK.LogiPlayConstantForce(0, (int)(sidewaysForce));

        bool isGrounded = playerVehicle.isGrounded;

        if (!isGrounded)
            LogitechGSDK.LogiPlayCarAirborne(0);
        else
            LogitechGSDK.LogiStopCarAirborne(0);

    }

    void OnDisable() {

        RCC_CarControllerV4.OnRCCPlayerCollision -= RCC_CarControllerV3_OnRCCPlayerCollision;
        RCC_InputManager.Instance.logitechSteeringUsed = false;

    }

    void OnApplicationQuit() {

        LogitechGSDK.LogiSteeringShutdown();

    }

}*/
#endregion

#region --> DI-Code
/*
public class RCC_LogitechSteeringWheel : MonoBehaviour
{

    #region singleton
    private static RCC_LogitechSteeringWheel instance;
    public static RCC_LogitechSteeringWheel Instance
    {

        get
        {

            if (instance == null)
            {

                instance = FindObjectOfType<RCC_LogitechSteeringWheel>();

                if (instance == null)
                {

                    GameObject sceneManager = new GameObject("_RCCLogitechSteeringWheelManager");
                    instance = sceneManager.AddComponent<RCC_LogitechSteeringWheel>();

                }

            }

            return instance;

        }

    }
    #endregion

    public bool useForceFeedback = true;
    public float roughness = 70f;
    public float collisionForce = 40f;

    DirectInputDevice ISDevice;
    InputActionMap Actions;
    [SerializeField] private InputActionAsset ControlScheme;

    void Start()
    {
        Actions = ControlScheme.FindActionMap("DirectInputDemo");
        Actions.Enable();
    }

    void OnEnable()
    {
        RCC_CarControllerV4.OnRCCPlayerCollision += RCC_CarControllerV4_OnRCCPlayerCollision;
        RCC_InputManager.Instance.logitechSteeringUsed = true;
    }

    void RCC_CarControllerV4_OnRCCPlayerCollision(RCC_CarControllerV4 RCC, Collision collision)
    {
        if (RCC == RCC_SceneManager.Instance.activePlayerVehicle)
            DIManager.UpdateConstantForceSimple(ISDevice.description.serial, Mathf.CeilToInt(collision.impulse.magnitude * 20.0f));
    }

    void FixedUpdate()
    {
        CheckPnP();

        if (ISDevice != null)
        {
            if (useForceFeedback)
                ForceFeedback();
        }
    }

    private void CheckPnP()
    {
        if (ISDevice == null)
        {
            ISDevice = Actions.FindAction("FFBAxis").controls                                     // Select the control intended to have FFB
              .Select(x => x.device)                                                              // Select the "device" child element
              .OfType<DirectInputDevice>()                                                        // Filter to our DirectInput Type
              .Where(d => d.description.capabilities.Contains("\"FFBCapable\":true"))             // Ensure the Device is FFBCapable
              .Where(d => DIManager.Attach(d.description.serial))                                 // Attempt to attach to device
              .FirstOrDefault();                                                                  // Return the first successful or null if none found
            if (ISDevice == null)
            { 
                return;
            }
            Debug.Log($"FFB Device: {ISDevice.description.serial}, Acquired: {DIManager.Attach(ISDevice.description.serial)}");
            DIManager.EnableFFBEffect(ISDevice.description.serial, FFBEffects.ConstantForce);
            DIManager.EnableFFBEffect(ISDevice.description.serial, FFBEffects.Damper);
            DIManager.EnableFFBEffect(ISDevice.description.serial, FFBEffects.Friction);
            DIManager.EnableFFBEffect(ISDevice.description.serial, FFBEffects.Inertia);
            DIManager.EnableFFBEffect(ISDevice.description.serial, FFBEffects.Spring);
        }
    }

    void ForceFeedback()
    {
        int maxFrictionCarSpeed = 50;

        RCC_CarControllerV4 playerVehicle = RCC_SceneManager.Instance.activePlayerVehicle;

        if (!playerVehicle)
            return;

        float sidewaysForce = playerVehicle.FrontLeftWheelCollider.wheelSlipAmountSideways + playerVehicle.FrontRightWheelCollider.wheelSlipAmountSideways;
        sidewaysForce *= Mathf.Abs(sidewaysForce);
        sidewaysForce *= -roughness;

        DIManager.UpdateConstantForceSimple(ISDevice.description.serial, Mathf.CeilToInt(sidewaysForce * 200.0f));

        if (playerVehicle.engineRPM >= 500.0f)
        {
            DIManager.UpdateFrictionSimple(ISDevice.description.serial, 3000 - Mathf.CeilToInt(Mathf.Abs(playerVehicle.speed) * (3000 / maxFrictionCarSpeed)));
        }
        else
        {
            DIManager.UpdateFrictionSimple(ISDevice.description.serial, 10000 - Mathf.CeilToInt(Mathf.Abs(playerVehicle.speed) * (10000 / maxFrictionCarSpeed)));
        }
        DIManager.UpdateSpringSimple(ISDevice.description.serial, (uint)Mathf.CeilToInt(Mathf.Max(10000 - (Mathf.Abs(playerVehicle.speed) * 200), 0)) , 0, Mathf.CeilToInt(5000 * (Mathf.Abs(playerVehicle.speed) / 100)), Mathf.CeilToInt(5000 * (Mathf.Abs(playerVehicle.speed) / 100)), 0, 0);

        bool isGrounded = playerVehicle.isGrounded;

        SimulateABSandESP(sidewaysForce);

        // TODO: Check FFB if car is Airborne
        /*
        if (!isGrounded)
            LogitechGSDK.LogiPlayCarAirborne(0);
        else
            LogitechGSDK.LogiStopCarAirborne(0);
        
    }

    private void SimulateABSandESP(float sidewaysForce)
    {
        RCC_CarControllerV4 playerVehicle = RCC_SceneManager.Instance.activePlayerVehicle;
        int absFFB = Mathf.CeilToInt(Random.Range(-1.0f, 1.0f) * 10000.0f);
        int espFFB = Mathf.CeilToInt(sidewaysForce * 70.0f);

        if (playerVehicle.ABSAct)
        {
            DIManager.UpdateConstantForceSimple(ISDevice.description.serial, absFFB);
            Debug.Log("ABS FFB: " + absFFB);
        }
        if (playerVehicle.ESPAct)
        {
            DIManager.UpdateConstantForceSimple(ISDevice.description.serial, espFFB);
            DIManager.UpdateSpringSimple(ISDevice.description.serial, 10000, 0, Mathf.CeilToInt(5000 * (Mathf.Abs(playerVehicle.speed) / 100)), Mathf.CeilToInt(5000 * (Mathf.Abs(playerVehicle.speed) / 100)), 0, 0);

            Debug.Log("ESP FFB: " + espFFB);
        }
    }

    void OnDisable()
    {

        RCC_CarControllerV4.OnRCCPlayerCollision -= RCC_CarControllerV4_OnRCCPlayerCollision;
        RCC_InputManager.Instance.logitechSteeringUsed = false;
        if (ISDevice != null)
        {
            DIManager.StopAllFFBEffects(ISDevice.description.serial);
        }
    }

    void OnApplicationQuit()
    {
        if (ISDevice != null)
        {
            DIManager.StopAllFFBEffects(ISDevice.description.serial);
        }
    }
}
*/
#endregion

public class RCC_LogitechSteeringWheel : MonoBehaviour
{
    private LogitechGSDK.LogiControllerPropertiesData properties;
    private RCC_CarControllerV4 playerVehicle;
    private LogitechGSDK.DIJOYSTATE2ENGINES rec;
    private RCC_InputManager vehicleInputManager;

    #region singleton
    private static RCC_LogitechSteeringWheel instance;
    public static RCC_LogitechSteeringWheel Instance
    {

        get
        {

            if (instance == null)
            {

                instance = FindObjectOfType<RCC_LogitechSteeringWheel>();

                if (instance == null)
                {

                    GameObject sceneManager = new GameObject("_RCCLogitechSteeringWheelManager");
                    instance = sceneManager.AddComponent<RCC_LogitechSteeringWheel>();

                }

            }

            return instance;

        }

    }
    #endregion

    public float roughness = 70f;

    [HideInInspector]
    public bool overrideFFB = false;
    [HideInInspector]
    public float steerInput = 0.0f;

    void Start()
    {
        vehicleInputManager = GameObject.FindFirstObjectByType<RCC_InputManager>().GetComponent<RCC_InputManager>();
        Debug.Log("SteeringInit:" + LogitechGSDK.LogiSteeringInitialize(false));
    }

    void OnEnable()
    {
        RCC_CarControllerV4.OnRCCPlayerCollision += RCC_CarControllerV4_OnRCCPlayerCollision;
        RCC_InputManager.Instance.logitechSteeringUsed = true;
        playerVehicle = RCC_SceneManager.Instance.activePlayerVehicle;
    }

    void RCC_CarControllerV4_OnRCCPlayerCollision(RCC_CarControllerV4 RCC, Collision collision)
    {
        if (RCC == RCC_SceneManager.Instance.activePlayerVehicle)
        {
            float direction = Mathf.Sign(RCC.steerInput);
            if (Mathf.Abs(RCC.steerInput) < 0.025f)
            {
                LogitechGSDK.LogiPlayFrontalCollisionForce(0, Mathf.CeilToInt((collision.impulse.magnitude) * RCC.speed / 3.6f / 100.0f));
            }
            else
            {
                LogitechGSDK.LogiPlaySideCollisionForce(0, Mathf.CeilToInt(((collision.impulse.magnitude * (direction * Mathf.Abs(RCC.steerInput)) * (RCC.speed / 3.6f / 100.0f)))));
            }
        }
    }

    void FixedUpdate()
    {
        if (!playerVehicle)
        {
            try
            {
                playerVehicle = RCC_SceneManager.Instance.activePlayerVehicle;
            }
            catch { }
            return;
        }

        if (LogitechGSDK.LogiUpdate() && LogitechGSDK.LogiIsConnected(0))
        {
            vehicleInputManager.RCC_SteeringWheel(LogitechGSDK.LogiGetStateUnity(0));
            ForceFeedback();
            if (overrideFFB)
            {
                LogitechGSDK.LogiPlayConstantForce(0, Mathf.CeilToInt(-steerInput * 100.0f));
            }
        }
        else if (!LogitechGSDK.LogiIsConnected(0))
        {
            Debug.LogWarning("PLEASE PLUG IN A STEERING WHEEL OR A FORCE FEEDBACK CONTROLLER");
        }
    }

    void ForceFeedback()
    {
        int maxFrictionCarSpeed = 25;

        float sidewaysForce = playerVehicle.FrontLeftWheelCollider.wheelSlipAmountSideways + playerVehicle.FrontRightWheelCollider.wheelSlipAmountSideways;
        sidewaysForce *= Mathf.Abs(sidewaysForce);
        sidewaysForce *= -roughness;

        LogitechGSDK.LogiPlayConstantForce(0, Mathf.CeilToInt(sidewaysForce * 3.0f));
        //DIManager.UpdateConstantForceSimple(ISDevice.description.serial, Mathf.CeilToInt(sidewaysForce * 200.0f));

        if (playerVehicle.engineRPM >= 500.0f)
        {
            LogitechGSDK.LogiPlayDamperForce(0, -100 + Mathf.CeilToInt(Mathf.Min(100, Mathf.Abs(playerVehicle.speed))));
            //DIManager.UpdateFrictionSimple(ISDevice.description.serial, 3000 - Mathf.CeilToInt(Mathf.Abs(playerVehicle.speed) * (3000 / maxFrictionCarSpeed)));
        }
        else
        {
            LogitechGSDK.LogiPlayDamperForce(0, 100 - Mathf.CeilToInt(Mathf.Abs(playerVehicle.speed) * (100 / maxFrictionCarSpeed)));
            //DIManager.UpdateFrictionSimple(ISDevice.description.serial, 10000 - Mathf.CeilToInt(Mathf.Abs(playerVehicle.speed) * (10000 / maxFrictionCarSpeed)));
        }
        LogitechGSDK.LogiPlaySpringForce(0, 0, Mathf.CeilToInt(Mathf.Min(30, Mathf.Abs(playerVehicle.speed))), Mathf.CeilToInt(Mathf.Min(30, Mathf.Abs(playerVehicle.speed))));
        //DIManager.UpdateSpringSimple(ISDevice.description.serial, (uint)Mathf.CeilToInt(Mathf.Max(10000 - (Mathf.Abs(playerVehicle.speed) * 200), 0)) , 0, Mathf.CeilToInt(5000 * (Mathf.Abs(playerVehicle.speed) / 100)), Mathf.CeilToInt(5000 * (Mathf.Abs(playerVehicle.speed) / 100)), 0, 0);

        SimulateABSandESP(sidewaysForce);

        if (!playerVehicle.isGrounded)
            LogitechGSDK.LogiPlayCarAirborne(0);
        else
            LogitechGSDK.LogiStopCarAirborne(0);
    }

    private void SimulateABSandESP(float sidewaysForce)
    {
        RCC_CarControllerV4 playerVehicle = RCC_SceneManager.Instance.activePlayerVehicle;
        int absFFB = Random.Range(50, 100);
        int espFFB = Mathf.CeilToInt(sidewaysForce * 0.2f);

        if (playerVehicle.ABSAct)
        {
            LogitechGSDK.LogiStopSpringForce(0);
            LogitechGSDK.LogiStopDamperForce(0);
            LogitechGSDK.LogiStopConstantForce(0);
            LogitechGSDK.LogiPlaySurfaceEffect(0, LogitechGSDK.LOGI_PERIODICTYPE_SQUARE, absFFB, 100);
            LogitechGSDK.LogiPlayConstantForce(0, (int) (100 * Random.Range(-1.0f, 1.0f)));
            //DIManager.UpdateConstantForceSimple(ISDevice.description.serial, absFFB);
            Debug.Log("ABS FFB: " + absFFB);
        }
        else
        {
            LogitechGSDK.LogiStopSurfaceEffect(0);
        }

        if (playerVehicle.ESPAct)
        {
            LogitechGSDK.LogiStopSpringForce(0);
            LogitechGSDK.LogiPlayConstantForce(0, espFFB);
            //DIManager.UpdateConstantForceSimple(ISDevice.description.serial, espFFB);

            LogitechGSDK.LogiPlayDirtRoadEffect(0, Mathf.CeilToInt(Mathf.Abs(playerVehicle.speed)));
            LogitechGSDK.LogiPlaySurfaceEffect(0, LogitechGSDK.LOGI_PERIODICTYPE_TRIANGLE, Mathf.CeilToInt(Mathf.Abs(playerVehicle.speed)), 20);
            //DIManager.UpdateSpringSimple(ISDevice.description.serial, 10000, 0, Mathf.CeilToInt(5000 * (Mathf.Abs(playerVehicle.speed) / 100)), Mathf.CeilToInt(5000 * (Mathf.Abs(playerVehicle.speed) / 100)), 0, 0);

            //Debug.Log("ESP FFB: " + espFFB);
        }
        else
        {
            LogitechGSDK.LogiStopDirtRoadEffect(0);
            LogitechGSDK.LogiStopSurfaceEffect(0);
        }
    }

    void OnDisable()
    {
        RCC_CarControllerV4.OnRCCPlayerCollision -= RCC_CarControllerV4_OnRCCPlayerCollision;
        RCC_InputManager.Instance.logitechSteeringUsed = false;
    }

    void OnApplicationQuit()
    {
        Debug.Log("SteeringShutdown:" + LogitechGSDK.LogiSteeringShutdown());
    }
}

#endif