namespace ValheimVRMod.VRCore.Backends
{
    // Optional gameplay policy is separate from the shared runtime/input/rig contracts.
    public readonly struct VRGameplayOptions
    {
        public readonly bool PhysicalContact, PhysicalGrabbing, CreatureGrabbing, FingerArticulation;
        public bool Any => PhysicalContact || PhysicalGrabbing || CreatureGrabbing || FingerArticulation;
        public VRGameplayOptions(bool contact, bool grabbing, bool creatures, bool fingers)
        { PhysicalContact = contact; PhysicalGrabbing = grabbing; CreatureGrabbing = creatures; FingerArticulation = fingers; }
    }
    public static class VRGameplay
    {
        public static VRGameplayOptions Options { get; private set; }
        public static void Configure(VRGameplayOptions options) { Options = options; }
    }
}
