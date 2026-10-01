using System;
using UnityEngine;

namespace ValheimVRMod.VRCore.Backends
{
    public static class VRInput
    {
        public static IVRInputBackend Backend { get { return VRBackendHost.Active.Input; } }
        public static event Action onNonVisualActionsUpdated
        {
            add { Backend.ListenUpdates(value); }
            remove { Backend.RemoveUpdateListener(value); }
        }
        public static bool OpenBindingUI(VRActionSet set) { return Backend.OpenBindingUI(set.Path); }
        public static VRHapticAction Haptic { get; } = new VRHapticAction("/actions/valheim/out/Haptic");
    }
    public class VRAction
    {
        public string Path { get; }
        public VRAction(string path) { Path = path; }
        public bool activeBinding { get { return GetActiveBinding(VRInputSource.Any); } }
        public bool GetActiveBinding(VRInputSource source) { return VRInput.Backend.IsBound(Path, source); }
        public string GetShortName() { return Path.Substring(Path.LastIndexOf('/') + 1); }
    }
    public sealed class VRBooleanAction : VRAction
    {
        public VRBooleanAction(string path) : base(path) { }
        public bool state { get { return GetState(VRInputSource.Any); } }
        public VRBooleanSource this[VRInputSource source] { get { return new VRBooleanSource(this, source); } }
        public bool GetState(VRInputSource source) { return VRInput.Backend.ReadDigital(Path, source).Held; }
        public bool GetStateDown(VRInputSource source) { return VRInput.Backend.ReadDigital(Path, source).Down; }
        public bool GetStateUp(VRInputSource source) { return VRInput.Backend.ReadDigital(Path, source).Up; }
        public void AddOnStateDownListener(Action<VRBooleanAction, VRInputSource> callback, VRInputSource source)
        { VRInput.Backend.ListenDigital(Path, source, true, hand => callback(this, hand)); }
        public void AddOnStateUpListener(Action<VRBooleanAction, VRInputSource> callback, VRInputSource source)
        { VRInput.Backend.ListenDigital(Path, source, false, hand => callback(this, hand)); }
    }
    public sealed class VRBooleanSource
    {
        readonly VRBooleanAction action;
        readonly VRInputSource source;
        internal VRBooleanSource(VRBooleanAction action, VRInputSource source) { this.action = action; this.source = source; }
        public bool activeBinding { get { return action.GetActiveBinding(source); } }
    }
    public sealed class VRVector2Action : VRAction
    {
        public VRVector2Action(string path) : base(path) { }
        public Vector2 axis { get { return GetAxis(VRInputSource.Any); } }
        public Vector2 GetAxis(VRInputSource source) { return VRInput.Backend.ReadAxis(Path, source); }
        public void AddOnChangeListener(Action<VRVector2Action, VRInputSource, Vector2, Vector2> callback, VRInputSource source)
        { VRInput.Backend.ListenAxis(Path, source, (hand, value, delta) => callback(this, hand, value, delta)); }
    }
    public sealed class VRPoseAction : VRAction
    {
        public VRPoseAction(string path) : base(path) { }
        VRPoseState Read(VRInputSource source) { return VRInput.Backend.ReadPose(Path, source); }
        public Vector3 localPosition { get { return GetLocalPosition(VRInputSource.Any); } }
        public Quaternion localRotation { get { return GetLocalRotation(VRInputSource.Any); } }
        public Vector3 GetLocalPosition(VRInputSource source) { return Read(source).Position; }
        public Quaternion GetLocalRotation(VRInputSource source) { return Read(source).Rotation; }
        public Vector3 GetVelocity(VRInputSource source) { return Read(source).Velocity; }
        public Vector3 GetAngularVelocity(VRInputSource source) { return Read(source).AngularVelocity; }
        public bool GetPoseIsValid(VRInputSource source) { return Read(source).Valid; }
        public bool GetDeviceIsConnected(VRInputSource source) { return Read(source).Connected; }
    }
    public sealed class VRHapticAction : VRAction
    {
        public VRHapticAction(string path) : base(path) { }
        public void Execute(float delay, float duration, float frequency, float amplitude, VRInputSource source)
        { VRInput.Backend.Haptic(Path, source, delay, duration, frequency, amplitude); }
    }
    public sealed class VRActionSet
    {
        public string Path { get; }
        public VRActionSet(string path) { Path = path; }
        public bool IsActive(VRInputSource source = VRInputSource.Any) { return VRInput.Backend.IsActionSetActive(Path, source); }
        public void Activate(VRInputSource source = VRInputSource.Any, int priority = 0, bool disableAllOtherActionSets = false)
        { VRInput.Backend.SetActionSet(Path, source, true, priority, disableAllOtherActionSets); }
        public void Deactivate(VRInputSource source = VRInputSource.Any)
        { VRInput.Backend.SetActionSet(Path, source, false, 0, false); }
    }
}
