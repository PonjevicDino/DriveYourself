using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem.XR;

public class VehicleRecorder : MonoBehaviour
{
    private bool recRunning = false;
    private string filePath = String.Empty;
    private RCC_CarControllerV4 carCont;
    private DateTime lastRecordedTime;
    [SerializeField][Range(1, 60)] private int precisionTime = 5;

    private Rigidbody vehicleRigidbody;

    public void StartRecording(string scoreFolder, string fileName)
    {
        if (!recRunning)
        {
            Application.targetFrameRate = 120;
            carCont = GameObject.FindFirstObjectByType<RCC_CarControllerV4>();
            vehicleRigidbody = carCont.GetComponent<Rigidbody>();

            int recCount = 1;
            while (File.Exists(Application.dataPath + "/" + scoreFolder + "/" + fileName + "_Recording_" + recCount + ".txt"))
            {
                recCount++;
            }
            filePath = Application.dataPath + "/" + scoreFolder + "/" + fileName + "_Recording_" + recCount + ".txt";
            File.WriteAllText(filePath, "REC-START | " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "\n\n");
            File.AppendAllText(filePath, "INIT-VARS | " +
                                         "POS:[" + carCont.transform.position.x + "," + carCont.transform.position.y + "," + carCont.transform.position.z + "] | " +
                                         "ROT:[" + carCont.transform.rotation.eulerAngles.x + "," + carCont.transform.rotation.eulerAngles.y + "," + carCont.transform.rotation.eulerAngles.z + "] | " +
                                         "VEL:[" + vehicleRigidbody.linearVelocity.x + "," + vehicleRigidbody.linearVelocity.y + "," + vehicleRigidbody.linearVelocity.z + "] | " +
                                         "AVL:[" + vehicleRigidbody.angularVelocity.x + "," + vehicleRigidbody.angularVelocity.y + "," + vehicleRigidbody.angularVelocity.z + "]\n\n");

            lastRecordedTime = DateTime.Now;
            recRunning = true;
            StartCoroutine(EnsurePrecision());
        }
    }

    void FixedUpdate()
    {
        if (recRunning)
        {
            float accInput, brakeInput, steerInput;

            accInput = carCont.throttleInput;
            brakeInput = carCont.brakeInput;
            steerInput = carCont.steerInput;

            File.AppendAllText(filePath, "CURR-VARS | " +
                                         "DUR:[" + (DateTime.Now - lastRecordedTime).Milliseconds + "]ms | " +
                                         "ACC:[" + accInput.ToString("0.00000") + "] | " +
                                         "BRK:[" + brakeInput.ToString("0.00000") + "] | " +
                                         "STR:[" + steerInput.ToString("0.00000") + "]\n");

            lastRecordedTime = DateTime.Now;
        }
    }

    public void StopRecording()
    {
        if (recRunning)
        {
            recRunning = false;
            Application.targetFrameRate = 0;
            StopCoroutine(EnsurePrecision());
        }
    }

    private void OnDestroy()
    {
        StopRecording();
    }

    private IEnumerator EnsurePrecision()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(precisionTime);
            File.AppendAllText(filePath, "UPD-VPREC | " +
                                         "POS:[" + carCont.transform.position.x + "," + carCont.transform.position.y + "," + carCont.transform.position.z + "] | " +
                                         "ROT:[" + carCont.transform.rotation.eulerAngles.x + "," + carCont.transform.rotation.eulerAngles.y + "," + carCont.transform.rotation.eulerAngles.z + "] | " +
                                         "VEL:[" + vehicleRigidbody.linearVelocity.x + "," + vehicleRigidbody.linearVelocity.y + "," + vehicleRigidbody.linearVelocity.z + "] | " +
                                         "AVL:[" + vehicleRigidbody.angularVelocity.x + "," + vehicleRigidbody.angularVelocity.y + "," + vehicleRigidbody.angularVelocity.z + "]\n");
        }
    }
}
