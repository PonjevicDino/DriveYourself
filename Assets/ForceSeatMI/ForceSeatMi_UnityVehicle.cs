/*
 * Copyright (C) 2012-2024 MotionSystems
 * 
 * This file is part of ForceSeatMI SDK.
 *
 * www.motionsystems.eu
 *
 */

using System.Runtime.InteropServices;
using UnityEngine;

namespace MotionSystems
{
	// !!! DEPRECATED !!!
	// Please use ForceSeatMI_Vehicle and ForceSeatMI_Unity
	public class ForceSeatMI_UnityVehicle
	{
		const float FSMI_VT_ACC_LOW_PASS_FACTOR = 0.5f;
		const float FSMI_VT_ANGLES_SPEED_LOW_PASS_FACTOR = 0.5f;

		private ForceSeatMI       m_api       = null;
		private FSMI_TelemetryACE m_telemetry = FSMI_TelemetryACE.Prepare();

		private bool    m_firstCall        = true;
		private float   m_prevForwardSpeed = 0;
		private float   m_prevRightSpeed   = 0;
		private float   m_prevUpSpeed      = 0;
		private Vector3 m_prevAngles       = new Vector3();

		public void Begin()
		{
			m_api = new ForceSeatMI();

			m_telemetry.state = FSMI_State.NO_PAUSE;

			// Change this if you are going to use dedicated game profile
			m_api.ActivateProfile("SDK - Vehicle Telemetry");
			m_api.BeginMotionControl();
		}

		public void End()
		{
			if (m_api.IsLoaded())
			{
				m_api.EndMotionControl();
				m_api.Dispose();
			}
		}

		public void Tick(Rigidbody body, float deltaTime, bool paused, float rpm, float maxRpm, int gearNumber)
		{
			var velocity      = body.transform.InverseTransformDirection(body.linearVelocity);

			var forwardSpeed = velocity.z;
			var rightSpeed   = velocity.x;
			var upSpeed      = velocity.y;

			m_telemetry.state                  = paused ? (byte)FSMI_State.PAUSE : (byte)FSMI_State.NO_PAUSE;
			m_telemetry.rpm                    = (uint)rpm;
			m_telemetry.maxRpm                 = (uint)maxRpm;
			m_telemetry.gearNumber             = (sbyte)gearNumber;
			m_telemetry.vehicleForwardSpeed    = velocity.magnitude; // m/s

			if (m_firstCall)
			{
				m_firstCall = false;

				m_telemetry.bodyLinearAcceleration[0].forward = 0;
				m_telemetry.bodyLinearAcceleration[0].right   = 0;
				m_telemetry.bodyLinearAcceleration[0].upward  = 0;
				m_telemetry.bodyAngularVelocity[0].roll       = 0;
				m_telemetry.bodyAngularVelocity[0].pitch      = 0;
				m_telemetry.bodyAngularVelocity[0].yaw        = 0;
			}
			else
			{
				LowPassFilter(ref m_telemetry.bodyLinearAcceleration[0].forward, (forwardSpeed - m_prevForwardSpeed) / deltaTime, FSMI_VT_ACC_LOW_PASS_FACTOR);
				LowPassFilter(ref m_telemetry.bodyLinearAcceleration[0].right,   (rightSpeed   - m_prevRightSpeed)   / deltaTime, FSMI_VT_ACC_LOW_PASS_FACTOR);
				LowPassFilter(ref m_telemetry.bodyLinearAcceleration[0].upward,  (upSpeed      - m_prevUpSpeed)      / deltaTime, FSMI_VT_ACC_LOW_PASS_FACTOR);

				var deltaAngles = new Vector3(
					Mathf.Deg2Rad * Mathf.DeltaAngle(body.transform.eulerAngles.x, m_prevAngles.x),
					Mathf.Deg2Rad * Mathf.DeltaAngle(body.transform.eulerAngles.y, m_prevAngles.y),
					Mathf.Deg2Rad * Mathf.DeltaAngle(body.transform.eulerAngles.z, m_prevAngles.z)
				);

				LowPassFilter(ref m_telemetry.bodyAngularVelocity[0].roll,  deltaAngles.z / deltaTime, FSMI_VT_ANGLES_SPEED_LOW_PASS_FACTOR);
				LowPassFilter(ref m_telemetry.bodyAngularVelocity[0].pitch, deltaAngles.x / deltaTime, FSMI_VT_ANGLES_SPEED_LOW_PASS_FACTOR);
				LowPassFilter(ref m_telemetry.bodyAngularVelocity[0].yaw,   deltaAngles.y / deltaTime, FSMI_VT_ANGLES_SPEED_LOW_PASS_FACTOR);
			}

			m_prevForwardSpeed = forwardSpeed;
			m_prevRightSpeed   = rightSpeed;
			m_prevUpSpeed      = upSpeed;
			m_prevAngles.x     = body.transform.eulerAngles.x;
			m_prevAngles.y     = body.transform.eulerAngles.y;
			m_prevAngles.z     = body.transform.eulerAngles.z;

			m_api.SendTelemetryACE(ref m_telemetry);
		}

		private void LowPassFilter(ref float stored, float newValue, float factor)
		{
			stored += (newValue - stored) * factor;
		}
	}
}
