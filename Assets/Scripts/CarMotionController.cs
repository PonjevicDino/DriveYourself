/*
 * Copyright (C) 2012-2024 MotionSystems
 *
 * This file is part of ForceSeatMI SDK.
 *
 * www.motionsystems.eu
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using MotionSystems;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CarMotionController : MonoBehaviour
{
    // Vehicle body object
    private Rigidbody m_Rigidbody;
    [SerializeField] private float force;

    private RCC_CarControllerV4 carController;

    // ForceSeatMI API
    private ForceSeatMI_Unity m_Api;
    private ForceSeatMI_Vehicle m_vehicle;
    private ForceSeatMI_Unity.ExtraParameters m_extraParameters;

    private bool forceActive = false;

    private void Start()
    {
        m_Rigidbody = GetComponent<Rigidbody>();
        carController = GetComponent<RCC_CarControllerV4>();

        // ForceSeatMI - BEGIN
        m_Api             = new ForceSeatMI_Unity();
        m_vehicle         = new ForceSeatMI_Vehicle(m_Rigidbody);
        m_extraParameters = new ForceSeatMI_Unity.ExtraParameters();

        m_Api.SetAppID(""); // If you have dedicated app id, remove ActivateProfile calls from your code
        m_Api.ActivateProfile("SDK - Vehicle Telemetry ACE Modified");
        //m_Api.ActivateProfile("SDK - Vehicle Telemetry ACE - SIM");
        m_Api.SetTelemetryObject(m_vehicle);
        m_Api.Pause(false);
        m_Api.Begin();
        // ForceSeatMI - END

        m_vehicle.SetMaxRpm((uint)carController.maxEngineRPM);
    }

    private void OnDestroy()
    {
        // ForceSeatMI - BEGIN
        if (m_Api != null)
        {
            m_Api.End();
        }
        // ForceSeatMI - END
    }

    private IEnumerator DisableForce()
    {
        yield return new WaitForSeconds(5.0f);
        m_Rigidbody.mass /= 10.0f;
        forceActive = false;
    }

    private void FixedUpdate()
    {
        // ForceSeatMI - BEGIN
        if (m_vehicle != null && m_Api != null)
        {
            // Use extra parameters to generate custom effects, for exmp. vibrations. They will NOT be
            // filtered, smoothed or processed in any way.
            m_extraParameters.yaw = 0;
            m_extraParameters.pitch = (float)Math.Sin(Time.fixedTime * MathF.Floor(carController.engineRPM)) * 0.00015f * ((carController.maxEngineRPM - carController.engineRPM) / carController.maxEngineRPM);
            m_extraParameters.roll = (float)Math.Sin(Time.fixedTime * MathF.Floor(carController.engineRPM)) * 0.00015f * ((carController.maxEngineRPM - carController.engineRPM) / carController.maxEngineRPM);
            m_extraParameters.right = 0;
            m_extraParameters.up = 0;
            m_extraParameters.forward = 0;

            if (carController.ABSAct && carController.speed > 3.3f)
            {
                m_extraParameters.pitch -= UnityEngine.Random.Range(0.05f, 0.1f);
            }

            // Custom Values
            m_vehicle.SetRpm((uint)carController.engineRPM);
            m_vehicle.SetGearNumber(carController.currentGear);

            m_Api.AddExtra(m_extraParameters);
            m_Api.SetUserAux(0, 0.0f); // Demo of custom AUX data
            m_Api.FixedUpdate(Time.fixedDeltaTime);
        }
        // ForceSeatMI - END
    }
}
