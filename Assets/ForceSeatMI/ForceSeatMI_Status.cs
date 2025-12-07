/*
 * Copyright (C) 2012-2024 MotionSystems
 * 
 * This file is part of ForceSeatMI SDK.
 *
 * www.motionsystems.eu
 *
 */

using System;
using System.Runtime.InteropServices;

namespace MotionSystems
{
	///
	/// List of possible motion platform working states
	///
	public struct FSMI_PlatformCurrentState
	{
		public const int AnyPaused                = 1 << 0; // 0b00000001
		public const int ParkingCompleted         = 1 << 1; // 0b00000010
		public const int Offline                  = 1 << 2; // 0b00000100

		public const int RefRunCompleted          = 1 << 4; // 0b00010000

		// Only one park mode is valid at given time.
		// Greater parking mode value (number) = greater priority.
		// It means that transport parking wins over normal parking
		// and normal parking wins over parking to center.
		public const int ParkModeMask             = 0xE0;   // 0b11100000
		public const int NoParking                = 0x00;   // 0b00000000
		public const int SoftParkToCenter         = 0x20;   // 0b00100000
		public const int SoftParkNormal           = 0x40;   // 0b01000000
		public const int SoftParkForTransport     = 0x60;   // 0b01100000
		public const int OperatorParkToCenter     = 0x80;   // 0b10000000
		public const int OperatorParkNormal       = 0xA0;   // 0b10100000
		public const int OperatorParkForTransport = 0xC0;   // 0b11000000
		public const int Reserved                 = 0xE0;   // 0b11100000
	};

	///
	/// Actual platform status and motors position
	///
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct FSMI_PlatformInfo
	{
		public byte structSize; // check if this equals to sizeof(FSMI_PlatformInfo)
		public ulong timemark; // 64-bits

		public byte isConnected;
		public byte state;      // bits from FSMI_PlatformCurrentState

		public ushort actualMotor1_Position;
		public ushort actualMotor2_Position;
		public ushort actualMotor3_Position;
		public ushort actualMotor4_Position;
		public ushort actualMotor5_Position;
		public ushort actualMotor6_Position;
		public int actualMotor1_Speed;
		public int actualMotor2_Speed;
		public int actualMotor3_Speed;
		public int actualMotor4_Speed;
		public int actualMotor5_Speed;
		public int actualMotor6_Speed;

		public byte isThermalProtectionActivated; // global thermal protection status
		public byte worstModuleStatusCode;        // worst module (actuator or node) status - numerical code
		public byte worstModuleStatusIndex;       // index of module that above status applies to
		public byte coolingSystemMalfunction;     // global cooling system status

		public byte isKinematicsSupported;       // true if Inverse and Forward Kinematics are supported

		public float ikPrecision1;               // OBSOLETE: this field is always empty
		public float ikPrecision2;
		public float ikPrecision3;
		public float ikPrecision4;
		public float ikPrecision5;
		public float ikPrecision6;
		public byte  ikRecentStatus; // true if Inverse Kinematics was calculated correctly and given position is withing operating range

		public float fkRoll;         // roll  in rad from Fordward Kinematics
		public float fkPitch;        // pitch in rad from Fordward Kinematics
		public float fkYaw;          // yaw   in rad from Fordward Kinematics
		public float fkHeave;        // heave in mm  from Fordward Kinematics
		public float fkSway;         // sway  in mm  from Fordward Kinematics
		public float fkSurge;        // surge in mm  from Fordward Kinematics
		public byte  fkRecentStatus; // true if Fordward Kinematics was calculated correctly

		// New fields in 2.60
		public ushort requiredMotor1_Position;
		public ushort requiredMotor2_Position;
		public ushort requiredMotor3_Position;
		public ushort requiredMotor4_Position;
		public ushort requiredMotor5_Position;
		public ushort requiredMotor6_Position;

		public float fkRoll_deg;    // roll  in deg from Forward Kinematics
		public float fkPitch_deg;   // pitch in deg from Forward Kinematics
		public float fkYaw_deg;     // yaw   in deg from Forward Kinematics

		public float fkRollSpeed_deg;  // roll  in deg/s from Forward Kinematics
		public float fkPitchSpeed_deg; // pitch in deg/s from Forward Kinematics
		public float fkYawSpeed_deg;   // yaw   in deg/s from Forward Kinematics
		public float fkHeaveSpeed;     // heave in mm/s  from Forward Kinematics
		public float fkSwaySpeed;      // sway  in mm/s  from Forward Kinematics
		public float fkSurgeSpeed;     // surge in mm/s  from Forward Kinematics

		public float fkRollAcc_deg;  // roll  in deg/s2 from Forward Kinematics
		public float fkPitchAcc_deg; // pitch in deg/s2 from Forward Kinematics
		public float fkYawAcc_deg;   // yaw   in deg/s2 from Forward Kinematics
		public float fkHeaveAcc;     // heave in mm/s2  from Forward Kinematics
		public float fkSwayAcc;      // sway  in mm/s2  from Forward Kinematics
		public float fkSurgeAcc;     // surge in mm/s2  from Forward Kinematics
	}
}
