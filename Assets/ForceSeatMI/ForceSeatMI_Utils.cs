/*
 * Copyright (C) 2012-2024 MotionSystems
 *
 * This file is part of ForceSeatMI SDK.
 *
 * www.motionsystems.eu
 *
 */

using System.Runtime.InteropServices;

namespace MotionSystems
{
	abstract class ForceSeatMI_Utils
	{
		static public void LowPassFilter(ref float stored, float newValue, float factor)
		{
			stored += (newValue - stored) * factor;
		}

		static public float MotionFriendlyPRY_rad(float x)
		{
			float c = x;

			while (c > UnityEngine.Mathf.PI)
			{
				c -= 2 * UnityEngine.Mathf.PI;
			}
			while (c < -UnityEngine.Mathf.PI)
			{
				c += 2 * UnityEngine.Mathf.PI;
			}

			if (c > UnityEngine.Mathf.PI / 2)
			{
				c = UnityEngine.Mathf.PI / 2 - (c - UnityEngine.Mathf.PI / 2);
			}
			else if (c < -UnityEngine.Mathf.PI / 2)
			{
				c = -UnityEngine.Mathf.PI / 2 - (c + UnityEngine.Mathf.PI / 2);
			}

			return c;
		}

	}
}
