using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace ValheimVRMod.VRCore.Backends
{
    public static class VRRecenter
    {
        public static void Apply()
        {
            if (VRPlayer.ShouldPauseMovement) return;
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var subsystem in subsystems) if (subsystem.running) subsystem.TryRecenter();
            VRPlayer.RequestRecentering(recaliberateHeight: true);
            VRPlayer.RequestPelvisCaliberation();
            VRPlayer.vrPlayerInstance?.ResetRoomscaleCamera();
        }
    }
}
