/*
 * Copyright (C) 2012-2024 MotionSystems
 *
 * This file is part of ForceSeatMI SDK.
 *
 * www.motionsystems.eu
 *
 */

namespace MotionSystems
{
	interface ForceSeatMI_ITelemetryInterface
	{
		void Begin();
		void End();
		void FixedUpdate(float fixedDeltaTime, ref FSMI_TelemetryACE telemetry);
		void Pause(bool paused);
	}
}
