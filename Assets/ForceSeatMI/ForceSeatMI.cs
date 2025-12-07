/*
 * Copyright (C) 2012-2024 MotionSystems
 * 
 * This file is part of ForceSeatMI SDK.
 *
 * www.motionsystems.eu
 *
 */

// If for some reason Microsoft.Win32.Registry is not available, use hardcoded path:
// C:\Program Files (x86)\MotionSystems\ForceSeatPM
// or change in Unity player Api Compatibility Level to .NET 4.x
using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

namespace MotionSystems
{
	///
	/// Wrapper for ForceSeatMI native DLL
	///
	public class ForceSeatMI: IDisposable
	{
		public ForceSeatMI()
		{
			LoadAndCreate();
		}

		public void Dispose()
		{
			Close();
		}

		public bool IsLoaded()
		{
			return m_api != IntPtr.Zero;
		}

		///
		/// Call this function when the SIM is ready for sending data to the motion platform.
		///
		public bool BeginMotionControl()
		{
			if (m_api == IntPtr.Zero || m_fsmiBeginMotionControl == null) return false;
			return m_fsmiBeginMotionControl(m_api) != 0;
		}

		///
		/// Call this function to when the SIM does not want to send any more data to the motion platform.
		///
		public bool EndMotionControl()
		{
			if (m_api == IntPtr.Zero || m_fsmiEndMotionControl == null) return false;
			return m_fsmiEndMotionControl(m_api) != 0;
		}

		///
		/// It gets current status of the motion platform. This function can be called at any time.
		///
		public bool GetPlatformInfoEx(ref FSMI_PlatformInfo info, uint platformInfoStructSize, uint timeout)
		{
			if (m_api == IntPtr.Zero || m_fsmiGetPlatformInfoEx == null) return false;
			return m_fsmiGetPlatformInfoEx(m_api, ref info, platformInfoStructSize, timeout) != 0;
		}

		///
		/// It sends updated telemetry information to ForceSeatPM to ACE processor. 
		/// Make sure that 'state' field  is set correctly.
		/// Make sure to call ForceSeatMI_BeginMotionControl before this function is called.
		///
		public bool SendTelemetryACE(ref FSMI_TelemetryACE telemetry)
		{
			if (m_api == IntPtr.Zero || m_fsmiSendTelemetryACE == null) return false;
			return m_fsmiSendTelemetryACE(m_api, ref telemetry) != 0;
		}

		///
		/// It sends updated telemetry information to ForceSeatPM to ACE processor. 
		/// Make sure that 'state' field  is set correctly.
		/// Make sure to call ForceSeatMI_BeginMotionControl before this function is called.
		///
		/// NOTE: sfx, audioEffects and sbt (seat belt tensioner) are optional.
		///
		public bool SendTelemetryACE3(ref FSMI_TelemetryACE telemetry,
		                              ref FSMI_SFX sfx,
		                              ref FSMI_TactileAudioBasedFeedbackEffects audioEffects,
		                              ref FSMI_SbtData sbt)
		{
			if (m_api == IntPtr.Zero || m_fsmiSendTelemetryACE3 == null) return false;
			return m_fsmiSendTelemetryACE3(m_api, ref telemetry, ref sfx, ref audioEffects, ref sbt) != 0;
		}

		///
		/// Use this function if you want to specify position of the top table (top frame) in physical units (radians and milimeters).
		/// Make sure to call ForceSeatMI_BeginMotionControl before this function is called.
		///
		public bool SendTopTablePosPhy(ref FSMI_TopTablePositionPhysical position)
		{
			if (m_api == IntPtr.Zero || m_fsmiSendTopTablePosPhy == null) return false;
			return m_fsmiSendTopTablePosPhy(m_api, ref position) != 0;
		}

		///
		/// Use this function if you want to specify position of the top table (top frame) in physical units (radians and milimeters).
		/// Make sure to call ForceSeatMI_BeginMotionControl before this function is called.
		///
		/// NOTE: sfx, audioEffects and sbt (seat belt tensioner) are optional.
		///
		public bool SendTopTablePosPhy3(ref FSMI_TopTablePositionPhysical position,
		                                ref FSMI_SFX sfx, 
		                                ref FSMI_TactileAudioBasedFeedbackEffects audioEffects,
		                                ref FSMI_SbtData sbt)
		{
			if (m_api == IntPtr.Zero || m_fsmiSendTopTablePosPhy3 == null) return false;
			return m_fsmiSendTopTablePosPhy3(m_api, ref position, ref sfx, ref audioEffects, ref sbt) != 0;
		}

		///
		/// Use this function if you want to specify transformation matrix for the top table (top frame). 
		/// It is recommended only for 6DoF in cases when rotation center is not in default point (0, 0, 0).
		/// Make sure to call ForceSeatMI_BeginMotionControl before this function is called.
		///
		public bool SendTopTableMatrixPhy(ref FSMI_TopTableMatrixPhysical matrix)
		{
			if (m_api == IntPtr.Zero || m_fsmiSendTopTableMatrixPhy == null) return false;
			return m_fsmiSendTopTableMatrixPhy(m_api, ref matrix) != 0;
		}

		///
		/// Use this function if you want to specify transformation matrix for the top table (top frame). 
		/// It is recommended only for 6DoF in cases when rotation center is not in default point (0, 0, 0).
		/// Make sure to call ForceSeatMI_BeginMotionControl before this function is called.
		///
		/// NOTE: sfx, audioEffects and sbt (seat belt tensioner) are optional.
		///
		public bool SendTopTableMatrixPhy3(ref FSMI_TopTableMatrixPhysical matrix, 
		                                   ref FSMI_SFX sfx, 
		                                   ref FSMI_TactileAudioBasedFeedbackEffects audioEffects,
		                                   ref FSMI_SbtData sbt)
		{
			if (m_api == IntPtr.Zero || m_fsmiSendTopTableMatrixPhy3 == null) return false;
			return m_fsmiSendTopTableMatrixPhy3(m_api, ref matrix, ref sfx, ref audioEffects, ref sbt) != 0;
		}

		///
		/// Call this function to set required profile in ForceSeatPM application.
		///
		public bool ActivateProfile(string profileName)
		{
			if (m_api == IntPtr.Zero || m_fsmiActivateProfile == null) return false;
			return m_fsmiActivateProfile(m_api, profileName) != 0;
		}

		///
		/// Call this function to set application ID.
		///
		public bool SetAppID(string appId)
		{
			if (m_api == IntPtr.Zero || m_fsmiSetAppID == null) return false;
			return m_fsmiSetAppID(m_api, appId) != 0;
		}

		///
		/// Call this function to park the motion platform.
		///
		public bool Park(FSMI_ParkMode parkMode)
		{
			if (m_api == IntPtr.Zero || m_fsmiPark == null) return false;
			return m_fsmiPark(m_api, (byte)parkMode) != 0;
		}

		#region Internals
		private IntPtr m_api = IntPtr.Zero;
		private IntPtr m_apiDll = IntPtr.Zero;

		~ForceSeatMI()
		{
			// Just in case it is not deleted
			Close();
		}

		private Delegate LoadFunction<T>(string functionName)
		{
			var addr = GetProcAddress(m_apiDll, functionName);
			if (addr == IntPtr.Zero) 
			{
				return null;
			}
			return Marshal.GetDelegateForFunctionPointer(addr, typeof(T));
		}

		private void LoadAndCreate()
		{
			bool is64Bits = IntPtr.Size > 4;
			
			string registryPath = is64Bits 
				? "HKEY_LOCAL_MACHINE\\SOFTWARE\\Wow6432Node\\MotionSystems\\ForceSeatPM" 
				: "HKEY_LOCAL_MACHINE\\SOFTWARE\\MotionSystems\\ForceSeatPM";
			
			string dllName = is64Bits 
				? "ForceSeatMI64.dll" 
				: "ForceSeatMI32.dll";

			// If for some reason Microsoft.Win32.Registry is not available, use hardcoded path:
			// C:\Program Files (x86)\MotionSystems\ForceSeatPM
			// or change in Unity player Api Compatibility Level to .NET 4.x

			// Let's check if there is ForceSeatPM installed, if yes there is ForceSeatMIxx.dll that can be used
			string installationPath = (string)Registry.GetValue(registryPath, "InstallationPath", null);
			if (installationPath != null)
			{
				m_apiDll = LoadLibrary(installationPath + "\\" + dllName);
			}

			// If there is still not ForceSeatMIxx.dll found, then let's try in standard search path
			if (m_apiDll == IntPtr.Zero)
			{
				m_apiDll = LoadLibrary(dllName);
			}

			if (m_apiDll != IntPtr.Zero) 
			{
				m_fsmiCreate                     = (ForceSeatMI_Create_Delegate)                    LoadFunction<ForceSeatMI_Create_Delegate>                    ("ForceSeatMI_Create");
				m_fsmiDelete                     = (ForceSeatMI_Delete_Delegate)                    LoadFunction<ForceSeatMI_Delete_Delegate>                    ("ForceSeatMI_Delete");
				m_fsmiBeginMotionControl         = (ForceSeatMI_BeginMotionControl_Delegate)        LoadFunction<ForceSeatMI_BeginMotionControl_Delegate>        ("ForceSeatMI_BeginMotionControl");
				m_fsmiEndMotionControl           = (ForceSeatMI_EndMotionControl_Delegate)          LoadFunction<ForceSeatMI_EndMotionControl_Delegate>          ("ForceSeatMI_EndMotionControl");
				m_fsmiGetPlatformInfoEx          = (ForceSeatMI_GetPlatformInfoEx_Delegate)         LoadFunction<ForceSeatMI_GetPlatformInfoEx_Delegate>         ("ForceSeatMI_GetPlatformInfoEx");
				m_fsmiSendTelemetryACE           = (ForceSeatMI_SendTelemetryACE_Delegate)          LoadFunction<ForceSeatMI_SendTelemetryACE_Delegate>          ("ForceSeatMI_SendTelemetryACE");
				m_fsmiSendTelemetryACE3          = (ForceSeatMI_SendTelemetryACE3_Delegate)         LoadFunction<ForceSeatMI_SendTelemetryACE3_Delegate>         ("ForceSeatMI_SendTelemetryACE3");
				m_fsmiSendTopTablePosPhy         = (ForceSeatMI_SendTopTablePosPhy_Delegate)        LoadFunction<ForceSeatMI_SendTopTablePosPhy_Delegate>        ("ForceSeatMI_SendTopTablePosPhy");
				m_fsmiSendTopTablePosPhy3        = (ForceSeatMI_SendTopTablePosPhy3_Delegate)       LoadFunction<ForceSeatMI_SendTopTablePosPhy3_Delegate>       ("ForceSeatMI_SendTopTablePosPhy3");
				m_fsmiSendTopTableMatrixPhy      = (ForceSeatMI_SendTopTableMatrixPhy_Delegate)     LoadFunction<ForceSeatMI_SendTopTableMatrixPhy_Delegate>     ("ForceSeatMI_SendTopTableMatrixPhy");
				m_fsmiSendTopTableMatrixPhy3     = (ForceSeatMI_SendTopTableMatrixPhy3_Delegate)    LoadFunction<ForceSeatMI_SendTopTableMatrixPhy3_Delegate>    ("ForceSeatMI_SendTopTableMatrixPhy3");
				m_fsmiActivateProfile            = (ForceSeatMI_ActivateProfile_Delegate)           LoadFunction<ForceSeatMI_ActivateProfile_Delegate>           ("ForceSeatMI_ActivateProfile");
				m_fsmiSetAppID                   = (ForceSeatMI_SetAppID_Delegate)                  LoadFunction<ForceSeatMI_SetAppID_Delegate>                  ("ForceSeatMI_SetAppID");
				m_fsmiPark                       = (ForceSeatMI_Park_Delegate)                      LoadFunction<ForceSeatMI_Park_Delegate>                      ("ForceSeatMI_Park");

				if (m_fsmiCreate                     != null && 
					m_fsmiDelete                     != null && 
					m_fsmiBeginMotionControl         != null && 
					m_fsmiEndMotionControl           != null &&
					m_fsmiGetPlatformInfoEx          != null &&
					m_fsmiSendTelemetryACE           != null &&
					m_fsmiSendTelemetryACE3          != null &&
					m_fsmiSendTopTablePosPhy         != null &&
					m_fsmiSendTopTablePosPhy3        != null &&
					m_fsmiSendTopTableMatrixPhy      != null &&
					m_fsmiSendTopTableMatrixPhy3     != null &&
					m_fsmiActivateProfile            != null &&
					m_fsmiSetAppID                   != null &&
					m_fsmiPark                       != null)
				{
					m_api = m_fsmiCreate();
				}
			}
		}

		private void Close()
		{
			if (m_api != IntPtr.Zero)
			{
				if (m_fsmiDelete != null)
				{
					m_fsmiDelete(m_api);
				}
				m_api = IntPtr.Zero;
			}

			m_fsmiCreate                     = null;
			m_fsmiDelete                     = null;
			m_fsmiBeginMotionControl         = null;
			m_fsmiSendTelemetryACE           = null;
			m_fsmiSendTelemetryACE3          = null;
			m_fsmiEndMotionControl           = null;
			m_fsmiGetPlatformInfoEx          = null;
			m_fsmiSendTopTablePosPhy         = null;
			m_fsmiSendTopTablePosPhy3        = null;
			m_fsmiSendTopTableMatrixPhy      = null;
			m_fsmiSendTopTableMatrixPhy3     = null;
			m_fsmiActivateProfile            = null;
			m_fsmiSetAppID                   = null;
			m_fsmiPark                       = null;

			if (m_apiDll != IntPtr.Zero)
			{
				FreeLibrary(m_apiDll);
				m_apiDll = IntPtr.Zero;
			}
		}
		#endregion

		#region DLLImports
		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr LoadLibrary(string libname);

		[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
		private static extern bool FreeLibrary(IntPtr hModule);

		[DllImport("Kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
		private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate IntPtr ForceSeatMI_Create_Delegate                  ();

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void ForceSeatMI_Delete_Delegate                    (IntPtr api);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate byte ForceSeatMI_BeginMotionControl_Delegate        (IntPtr api);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate byte ForceSeatMI_EndMotionControl_Delegate          (IntPtr api);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate byte ForceSeatMI_GetPlatformInfoEx_Delegate         (IntPtr api, ref FSMI_PlatformInfo info, uint platformInfoStructSize, uint timeout);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate byte ForceSeatMI_SendTelemetryACE_Delegate          (IntPtr api, ref FSMI_TelemetryACE info);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate byte ForceSeatMI_SendTelemetryACE3_Delegate         (IntPtr api, ref FSMI_TelemetryACE info, ref FSMI_SFX sfx, ref FSMI_TactileAudioBasedFeedbackEffects audioEffects, ref FSMI_SbtData sbt);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate byte ForceSeatMI_SendTopTablePosPhy_Delegate        (IntPtr api, ref FSMI_TopTablePositionPhysical position);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate byte ForceSeatMI_SendTopTablePosPhy3_Delegate        (IntPtr api, ref FSMI_TopTablePositionPhysical position, ref FSMI_SFX sfx, ref FSMI_TactileAudioBasedFeedbackEffects audioEffects, ref FSMI_SbtData sbt);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate byte ForceSeatMI_SendTopTableMatrixPhy_Delegate     (IntPtr api, ref FSMI_TopTableMatrixPhysical matrix);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate byte ForceSeatMI_SendTopTableMatrixPhy3_Delegate     (IntPtr api, ref FSMI_TopTableMatrixPhysical matrix, ref FSMI_SFX sfx, ref FSMI_TactileAudioBasedFeedbackEffects audioEffects, ref FSMI_SbtData sbt);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate byte ForceSeatMI_ActivateProfile_Delegate(IntPtr api, string profileName);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate byte ForceSeatMI_SetAppID_Delegate(IntPtr api, string appId);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate byte ForceSeatMI_Park_Delegate(IntPtr api, byte parkMode);

		private ForceSeatMI_Create_Delegate                     m_fsmiCreate;
		private ForceSeatMI_Delete_Delegate                     m_fsmiDelete;
		private ForceSeatMI_BeginMotionControl_Delegate         m_fsmiBeginMotionControl;
		private ForceSeatMI_EndMotionControl_Delegate           m_fsmiEndMotionControl;
		private ForceSeatMI_GetPlatformInfoEx_Delegate          m_fsmiGetPlatformInfoEx;
		private ForceSeatMI_SendTelemetryACE_Delegate           m_fsmiSendTelemetryACE;
		private ForceSeatMI_SendTelemetryACE3_Delegate          m_fsmiSendTelemetryACE3;
		private ForceSeatMI_SendTopTablePosPhy_Delegate         m_fsmiSendTopTablePosPhy;
		private ForceSeatMI_SendTopTablePosPhy3_Delegate        m_fsmiSendTopTablePosPhy3;
		private ForceSeatMI_SendTopTableMatrixPhy_Delegate      m_fsmiSendTopTableMatrixPhy;
		private ForceSeatMI_SendTopTableMatrixPhy3_Delegate     m_fsmiSendTopTableMatrixPhy3;
		private ForceSeatMI_ActivateProfile_Delegate            m_fsmiActivateProfile;
		private ForceSeatMI_SetAppID_Delegate                   m_fsmiSetAppID;
		private ForceSeatMI_Park_Delegate                       m_fsmiPark;
		#endregion
	}
}
