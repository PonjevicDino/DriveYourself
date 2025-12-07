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
	public struct FSMI_State
	{
		public const int NO_PAUSE  = (0 << 0);
		public const int PAUSE     = (1 << 0);
	}

	public enum FSMI_ParkMode
	{
		ToCenter     = 0,
		Normal       = 1,
		ForTransport = 2
	}

	public enum FSMI_Defines
	{
		SFX_MaxEffectsCount = 4,
		SBT_ChannelsCount   = 2,
		UserAuxCount = 8
	}
}
