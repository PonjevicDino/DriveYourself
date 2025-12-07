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
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct FSMI_SbtChannelData
	{
		public float force;        // 0..1
		public float sfxAmplitude; // 0..1
		public byte  sfxFrequency;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct FSMI_SbtData
	{
		public byte structSize; // put here sizeof(FSMI_SbtData).

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)FSMI_Defines.SBT_ChannelsCount)]
		public FSMI_SbtChannelData[] channels;

		// Helper method to create a structure with preallocated space for nested structures
		public static FSMI_SbtData Prepare()
		{
			var tmp        = new FSMI_SbtData();
			tmp.structSize = (byte)Marshal.SizeOf(tmp);
			tmp.channels   = new FSMI_SbtChannelData[(int)FSMI_Defines.SBT_ChannelsCount];
			return tmp;
		}
	}
}
