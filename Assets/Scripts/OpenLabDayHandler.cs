using TMPro;
using Unity.Mathematics;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.UI;

public static class GlobalSettings 
{
    public static bool showDebugRays = false;
}

public class OpenLabDayHandler : MonoBehaviour
{
    [SerializeField] private bool simPcSetting = false;
    [SerializeField] private Slider speedSlider;
    [SerializeField] private Slider dtcSlider;
    [SerializeField] private Slider accelerationSlider;
    [SerializeField] private Slider smoothnessSlider;

    private AgentSelector agentSelector;
    private DriveYourselfAgent driveYourselfAgent;
    private GetVehicleData vehicleData;
    
    private RoadLayout roadLayout;
    private LineRenderer trackLineRenderer;
    
    private LineRenderer[] rayPool;
    private int activeRayCount = 0;
    private const int MAX_RAYS = 64;

    private void Start()
    {
        if (GameObject.FindFirstObjectByType<MultiAgentTraining>().transform.childCount != 1)
        {
            Debug.LogError("Only one Agent should be in the ViewScene so that the Sliders can work");
            speedSlider.transform.parent.gameObject.SetActive(false);
            dtcSlider.transform.parent.gameObject.SetActive(false);
            accelerationSlider.transform.parent.gameObject.SetActive(false);
            smoothnessSlider.transform.parent.gameObject.SetActive(false);
            this.enabled = false;
            return;
        }
        
        agentSelector = FindFirstObjectByType<AgentSelector>();
        driveYourselfAgent = agentSelector.GetComponent<DriveYourselfAgent>();
        vehicleData = driveYourselfAgent.GetComponentInChildren<GetVehicleData>();
        if (vehicleData == null) vehicleData = FindFirstObjectByType<GetVehicleData>();
        
        roadLayout = GameObject.FindFirstObjectByType<RoadLayout>();
        
        SetupTrackSplineRenderer();
        InitializeRayPool();
        
        speedSlider.onValueChanged.AddListener(delegate {UpdateAgentSpeed();});
        dtcSlider.onValueChanged.AddListener(delegate {UpdateAgentDtC();});
        accelerationSlider.onValueChanged.AddListener(delegate {UpdateAgentAcceleration();});
        smoothnessSlider.onValueChanged.AddListener(delegate {UpdateAgentSmoothness();});

        LaunchMultiDisplay();
    }

    private void Update()
    {
        CheckForReset();
        CheckForDebugGizmos();
        
        if (GlobalSettings.showDebugRays)
        {
            UpdateDtCVisuals();
        }
        else
        {
            HideUnusedRays();
        }
    }
    
    private void SetupTrackSplineRenderer()
    {
        if (roadLayout != null)
        {
            trackLineRenderer = roadLayout.GetComponent<LineRenderer>();
            if (trackLineRenderer == null)
            {
                trackLineRenderer = roadLayout.gameObject.AddComponent<LineRenderer>();
            }
            
            trackLineRenderer.enabled = false;
            
            if (roadLayout.trackSpline == null) return;

            var spline = roadLayout.trackSpline;
            int resolution = 4096; 
            trackLineRenderer.positionCount = resolution;
            trackLineRenderer.useWorldSpace = true;
            trackLineRenderer.startWidth = 0.33f; 
            trackLineRenderer.endWidth = 0.33f;
            trackLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            trackLineRenderer.startColor = Color.green;
            trackLineRenderer.endColor = Color.green;

            for (int i = 0; i < resolution; i++)
            {
                float t = i / (float)(resolution - 1);
                Vector3 point = spline.EvaluatePosition(t);
                trackLineRenderer.SetPosition(i, point);
            }
        }
    }
    
    private void InitializeRayPool()
    {
        GameObject poolContainer = new GameObject("DtC_RayPool_Presentation");
        poolContainer.transform.SetParent(this.transform);

        rayPool = new LineRenderer[MAX_RAYS];
        Material lineMat = new Material(Shader.Find("Sprites/Default"));

        for (int i = 0; i < MAX_RAYS; i++)
        {
            GameObject rayObj = new GameObject($"Ray_{i}");
            rayObj.transform.SetParent(poolContainer.transform);
            
            LineRenderer lr = rayObj.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.005f; 
            lr.endWidth = 0.005f;
            lr.material = lineMat;
            lr.useWorldSpace = true;
            lr.enabled = false;
            
            rayPool[i] = lr;
        }
    }

    private void UpdateDtCVisuals()
    {
        if (vehicleData == null) return;
        
        GameObject currentSegment = vehicleData.GetRoadSegment();
        if (currentSegment == null) return;
        GameObject nextSegment = vehicleData.GetNextRoadSegment(currentSegment);

        Vector3 vehiclePos = vehicleData.transform.parent.position; 
        
        activeRayCount = 0;
        
        float minVal = float.MaxValue;
        Vector3 bestL = Vector3.zero;
        Vector3 bestR = Vector3.zero;
        
        for (int i = 1; i <= 10; i++)
        {
            Transform pL = currentSegment.transform.Find("DtC-Tracker/P" + i + "L");
            Transform pR = currentSegment.transform.Find("DtC-Tracker/P" + i + "R");
            if (pL == null || pR == null) continue;

            DrawRay(vehiclePos, pL.position, Color.white);
            DrawRay(vehiclePos, pR.position, Color.white);

            float dist = Vector2.Distance(new Vector2(pL.position.x, pL.position.z), new Vector2(vehiclePos.x, vehiclePos.z)) + 
                         Vector2.Distance(new Vector2(pR.position.x, pR.position.z), new Vector2(vehiclePos.x, vehiclePos.z));
                         
            if (dist < minVal) { minVal = dist; bestL = pL.position; bestR = pR.position; }
        }
        
        for (int i = 1; i <= 10; i++)
        {
            Transform pL = nextSegment.transform.Find("DtC-Tracker/P" + i + "L");
            Transform pR = nextSegment.transform.Find("DtC-Tracker/P" + i + "R");
            if (pL == null || pR == null) continue;

            DrawRay(vehiclePos, pL.position, Color.blue);
            DrawRay(vehiclePos, pR.position, Color.blue);

            float dist = Vector2.Distance(new Vector2(pL.position.x, pL.position.z), new Vector2(vehiclePos.x, vehiclePos.z)) + 
                         Vector2.Distance(new Vector2(pR.position.x, pR.position.z), new Vector2(vehiclePos.x, vehiclePos.z));
                         
            if (dist < minVal) { minVal = dist; bestL = pL.position; bestR = pR.position; }
        }
        
        if (minVal != float.MaxValue)
        {
            DrawRay(vehiclePos, bestL, Color.red);
            DrawRay(vehiclePos, bestR, Color.red);
        }
        
        if (driveYourselfAgent != null && roadLayout != null && roadLayout.trackSpline != null)
        {
            float currentT = vehicleData.GetCurrentSplineT();
            Spline spline = roadLayout.trackSpline.Spline;
            
            float trackLength = spline.GetLength();
            float speedFactor = driveYourselfAgent.targetSpeed / 20.0f; 
            float dynamicLookAheadDistance = driveYourselfAgent.lookAheadDistanceMeters * speedFactor;
            float tStep = dynamicLookAheadDistance / trackLength;

            for (int i = 1; i <= driveYourselfAgent.lookAheadSegments; i++)
            {
                float lookAheadT = (currentT + (i * tStep)) % 1.0f;

                float3 localPos = SplineUtility.EvaluatePosition(spline, lookAheadT);
                Vector3 worldPos = roadLayout.trackSpline.transform.TransformPoint(localPos);
                
                DrawRay(vehiclePos, worldPos, Color.yellow);
            }
        }

        HideUnusedRays();
    }

    private void DrawRay(Vector3 start, Vector3 end, Color color)
    {
        if (activeRayCount >= MAX_RAYS) return;

        LineRenderer lr = rayPool[activeRayCount];
        lr.enabled = true;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);

        activeRayCount++;
    }

    private void HideUnusedRays()
    {
        for (int i = activeRayCount; i < MAX_RAYS; i++)
        {
            if (rayPool[i].enabled) rayPool[i].enabled = false;
        }
        activeRayCount = 0; 
    }
    
    private void CheckForDebugGizmos()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            GlobalSettings.showDebugRays = !GlobalSettings.showDebugRays;
            
            if (trackLineRenderer != null)
            {
                trackLineRenderer.enabled = GlobalSettings.showDebugRays;
            }
        }
    }

    private void CheckForReset()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            driveYourselfAgent.EndEpisode();
        }
    }

    private void UpdateAgentSpeed()
    {
        int sliderTargetSpeed = Mathf.RoundToInt(Mathf.Lerp(50.0f, 100.0f, speedSlider.value));
        agentSelector.targetSpeed = sliderTargetSpeed;
        agentSelector.FindAndAssignModel();
    }
    
    private void UpdateAgentDtC()
    {
        int sliderTargetDtC = Mathf.RoundToInt(Mathf.Lerp(0.0f, 100.0f, dtcSlider.value));
        agentSelector.targetDtC = sliderTargetDtC;
        agentSelector.FindAndAssignModel();
    }
    
    private void UpdateAgentAcceleration()
    {
        int sliderTargetAcceleration = Mathf.RoundToInt(Mathf.Lerp(5.0f, 15.0f, accelerationSlider.value));
        agentSelector.targetAccelTime = sliderTargetAcceleration;
        agentSelector.FindAndAssignModel();
    }
    
    private void UpdateAgentSmoothness()
    {
        int sliderTargetSmoothness = Mathf.RoundToInt(Mathf.Lerp(0.0f, 9.0f, smoothnessSlider.value));
        agentSelector.targetSmoothness = sliderTargetSmoothness;
        agentSelector.FindAndAssignModel();
    }

    private void LaunchMultiDisplay()
    {
        QualitySettings.vSyncCount = 1;
        Display.displays[0].SetRenderingResolution(Display.displays[0].systemWidth, Display.displays[0].systemHeight);

        if (simPcSetting)
        {
            if (Display.displays.Length > 2)
            {
                Display.displays[2].Activate();
                Display.displays[2].SetRenderingResolution(Display.displays[2].systemWidth, Display.displays[2].systemHeight);
                Debug.Log("Bypassed Display 1. Successfully activated Display 2 on Monitor 2.");
            }
            else if (Display.displays.Length > 1)
            {
                Display.displays[1].Activate();
                Display.displays[1].SetRenderingResolution(Display.displays[1].systemWidth, Display.displays[1].systemHeight);
                Debug.LogWarning("Only two monitors detected. Activating standard Display 1.");
            }
        }
        else
        {
            if (Display.displays.Length > 1)
            {
                Display.displays[1].Activate();
                Display.displays[1].SetRenderingResolution(Display.displays[1].systemWidth, Display.displays[1].systemHeight);
                Debug.LogWarning("Only two monitors detected. Activating standard Display 1.");
            }
        }
    }
}