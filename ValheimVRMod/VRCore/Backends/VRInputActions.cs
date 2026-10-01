// Generated from the tracked upstream action catalog by Update-ActionFacade.py.
namespace ValheimVRMod.VRCore.Backends
{
    public static class VRInputActions
    {
        public static VRActionSet Default { get; } = new VRActionSet("/actions/default");
        public static VRActionSet Valheim { get; } = new VRActionSet("/actions/Valheim");
        public static VRBooleanAction default_InteractUI { get; } = new VRBooleanAction("/actions/default/in/InteractUI");
        public static VRBooleanAction default_Teleport { get; } = new VRBooleanAction("/actions/default/in/Teleport");
        public static VRBooleanAction default_GrabPinch { get; } = new VRBooleanAction("/actions/default/in/GrabPinch");
        public static VRBooleanAction default_GrabGrip { get; } = new VRBooleanAction("/actions/default/in/GrabGrip");
        public static VRPoseAction default_Pose { get; } = new VRPoseAction("/actions/default/in/Pose");
        public static VRBooleanAction default_HeadsetOnHead { get; } = new VRBooleanAction("/actions/default/in/HeadsetOnHead");
        public static VRHapticAction default_Haptic { get; } = new VRHapticAction("/actions/default/out/Haptic");
        public static VRBooleanAction valheim_ToggleInventory { get; } = new VRBooleanAction("/actions/Valheim/in/ToggleInventory");
        public static VRBooleanAction valheim_ToggleMenu { get; } = new VRBooleanAction("/actions/Valheim/in/ToggleMenu");
        public static VRBooleanAction valheim_Jump { get; } = new VRBooleanAction("/actions/Valheim/in/Jump");
        public static VRBooleanAction valheim_Use { get; } = new VRBooleanAction("/actions/Valheim/in/Use");
        public static VRBooleanAction valheim_Sit { get; } = new VRBooleanAction("/actions/Valheim/in/Sit");
        public static VRVector2Action valheim_PitchAndYaw { get; } = new VRVector2Action("/actions/Valheim/in/PitchAndYaw");
        public static VRPoseAction valheim_PoseL { get; } = new VRPoseAction("/actions/Valheim/in/PoseL");
        public static VRPoseAction valheim_PoseR { get; } = new VRPoseAction("/actions/Valheim/in/PoseR");
        public static VRPoseAction valheim_BodyPose { get; } = new VRPoseAction("/actions/Valheim/in/BodyPose");
        public static VRBooleanAction valheim_HotbarUp { get; } = new VRBooleanAction("/actions/Valheim/in/HotbarUp");
        public static VRBooleanAction valheim_HotbarDown { get; } = new VRBooleanAction("/actions/Valheim/in/HotbarDown");
        public static VRVector2Action valheim_HotbarScroll { get; } = new VRVector2Action("/actions/Valheim/in/HotbarScroll");
        public static VRBooleanAction valheim_ToggleMap { get; } = new VRBooleanAction("/actions/Valheim/in/ToggleMap");
        public static VRBooleanAction valheim_HotbarUse { get; } = new VRBooleanAction("/actions/Valheim/in/HotbarUse");
        public static VRVector2Action valheim_ContextScroll { get; } = new VRVector2Action("/actions/Valheim/in/ContextScroll");
        public static VRBooleanAction valheim_Grab { get; } = new VRBooleanAction("/actions/Valheim/in/Grab");
        public static VRBooleanAction valheim_QuickSwitch { get; } = new VRBooleanAction("/actions/Valheim/in/QuickSwitch");
        public static VRVector2Action valheim_Walk { get; } = new VRVector2Action("/actions/Valheim/in/Walk");
        public static VRBooleanAction valheim_QuickActions { get; } = new VRBooleanAction("/actions/Valheim/in/QuickActions");
        public static VRBooleanAction valheim_StopGesturedLocomotion { get; } = new VRBooleanAction("/actions/Valheim/in/StopGesturedLocomotion");
        public static VRBooleanAction valheim_ToggleRun { get; } = new VRBooleanAction("/actions/Valheim/in/ToggleRun");
        public static VRBooleanAction valheim_HoldRun { get; } = new VRBooleanAction("/actions/Valheim/in/HoldRun");
        public static VRBooleanAction valheim_ToggleCrouch { get; } = new VRBooleanAction("/actions/Valheim/in/ToggleCrouch");
        public static VRBooleanAction valheim_Dodge { get; } = new VRBooleanAction("/actions/Valheim/in/Dodge");
        public static VRBooleanAction valheim_ToggleAutoPickup { get; } = new VRBooleanAction("/actions/Valheim/in/ToggleAutoPickup");
        public static VRBooleanAction valheim_RightClick { get; } = new VRBooleanAction("/actions/Valheim/in/RightClick");
        public static VRBooleanAction valheim_LeftClick { get; } = new VRBooleanAction("/actions/Valheim/in/LeftClick");
        public static VRBooleanAction valheim_AddMapPin { get; } = new VRBooleanAction("/actions/Valheim/in/AddMapPin");
        public static VRBooleanAction valheim_MiddleClick { get; } = new VRBooleanAction("/actions/Valheim/in/MiddleClick");
        public static VRBooleanAction valheim_DiscardItem { get; } = new VRBooleanAction("/actions/Valheim/in/DiscardItem");
        public static VRBooleanAction valheim_SplitStack { get; } = new VRBooleanAction("/actions/Valheim/in/SplitStack");
        public static VRBooleanAction valheim_ScrollUp { get; } = new VRBooleanAction("/actions/Valheim/in/ScrollUp");
        public static VRBooleanAction valheim_ScrollDown { get; } = new VRBooleanAction("/actions/Valheim/in/ScrollDown");
        public static VRBooleanAction valheim_RemovePiece { get; } = new VRBooleanAction("/actions/Valheim/in/RemovePiece");
        public static VRHapticAction valheim_Haptic { get; } = new VRHapticAction("/actions/Valheim/out/Haptic");
    }
}
