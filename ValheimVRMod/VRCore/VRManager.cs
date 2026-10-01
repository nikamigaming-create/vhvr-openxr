using ValheimVRMod.VRCore.Backends;

namespace ValheimVRMod.VRCore
{
    // Shared gameplay calls the selected runtime through the neutral contract.
    class VRManager
    {
        public static bool InitializeVR() { return VRBackendHost.Initialize(); }
        public static bool StartVR() { return VRBackendHost.Start(); }
        public static void UpdateMirrorViewMode() { VRBackendHost.Active.UpdateMirrorViewMode(); }
        public static void UpdateMirrorSetup() { VRBackendHost.Active.UpdateMirrorSetup(); }
        public static void tryRecenter() { VRBackendHost.Active.Recenter(); }
    }
}
