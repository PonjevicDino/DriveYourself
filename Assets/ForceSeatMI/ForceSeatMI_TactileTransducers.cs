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
	/// This structure defines effects for tactile transducers. Signals of given frequency and amplitude are generates on associated audio outputs.
	///
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct FSMI_TactileAudioBasedFeedbackEffects
	{
		public byte structSize; // put here sizeof(FSMI_TactileAudioBasedFeedbackEffects)

		public uint frequencyCenter; // frequency in Hz
		public uint frequencyEngine;
		public uint frequencyFL;
		public uint frequencyFR;
		public uint frequencyRL;
		public uint frequencyRR;
		public uint frequencyCH;

		public float amplitudeCenter; // signal amplitude from 0 to 1
		public float amplitudeEngine;
		public float amplitudeFL;
		public float amplitudeFR;
		public float amplitudeRL;
		public float amplitudeRR;
		public float amplitudeCH;

		// Helper method to create a structure with preallocated space for nested structures
		public static FSMI_TactileAudioBasedFeedbackEffects Prepare()
		{
			var tabfEffects        = new FSMI_TactileAudioBasedFeedbackEffects();
			tabfEffects.structSize = (byte)Marshal.SizeOf(tabfEffects);
			return tabfEffects;
		}
	}
}
