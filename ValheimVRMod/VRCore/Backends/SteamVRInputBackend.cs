using System;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;

namespace ValheimVRMod.VRCore.Backends
{
    // SDK adapter shared by the original OpenVR runtime and the authored rig's
    // managed-action bridge under OpenXR. Only this layer exposes Valve action types.
    public class SteamVRInputBackend : IVRInputBackend
    {
        readonly Dictionary<string, SteamVR_Action_Boolean> booleans = new Dictionary<string, SteamVR_Action_Boolean>();
        readonly Dictionary<string, SteamVR_Action_Vector2> axes = new Dictionary<string, SteamVR_Action_Vector2>();
        readonly Dictionary<string, SteamVR_Action_Pose> poses = new Dictionary<string, SteamVR_Action_Pose>();
        readonly Dictionary<string, SteamVR_Action_Vibration> haptics = new Dictionary<string, SteamVR_Action_Vibration>();
        readonly HashSet<Action> updates = new HashSet<Action>();
        static SteamVR_Input_Sources Native(VRInputSource source) { return (SteamVR_Input_Sources)(int)source; }
        SteamVR_Action_Boolean Boolean(string path)
        { if (!booleans.TryGetValue(path, out var value)) booleans[path] = value = SteamVR_Input.GetActionFromPath<SteamVR_Action_Boolean>(path); return value; }
        SteamVR_Action_Vector2 Axis(string path)
        { if (!axes.TryGetValue(path, out var value)) axes[path] = value = SteamVR_Input.GetActionFromPath<SteamVR_Action_Vector2>(path); return value; }
        SteamVR_Action_Pose Pose(string path)
        { if (!poses.TryGetValue(path, out var value)) poses[path] = value = SteamVR_Input.GetActionFromPath<SteamVR_Action_Pose>(path); return value; }
        public VRDigitalState ReadDigital(string path, VRInputSource source)
        {
            var action = Boolean(path); var hand = Native(source);
            return new VRDigitalState { Active = action.GetActiveBinding(hand), Held = action.GetState(hand), Down = action.GetStateDown(hand), Up = action.GetStateUp(hand) };
        }
        public Vector2 ReadAxis(string path, VRInputSource source) { return Axis(path).GetAxis(Native(source)); }
        public VRPoseState ReadPose(string path, VRInputSource source)
        {
            var action = Pose(path); var hand = Native(source);
            return new VRPoseState { Position = action.GetLocalPosition(hand), Rotation = action.GetLocalRotation(hand), Velocity = action.GetVelocity(hand),
                AngularVelocity = action.GetAngularVelocity(hand), Valid = action.GetPoseIsValid(hand), Connected = action.GetDeviceIsConnected(hand) };
        }
        public virtual VRHandControls ReadHandControls(VRInputSource source)
        {
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(source == VRInputSource.LeftHand ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand);
            var value = new VRHandControls();
            if (!device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out value.Grip))
                value.Grip = ReadDigital("/actions/default/in/GrabGrip", source).Held ? 1 : 0;
            if (!device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out value.Trigger))
                value.Trigger = ReadDigital("/actions/default/in/GrabPinch", source).Held ? 1 : 0;
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryTouch, out value.PrimaryTouched);
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryTouch, out value.SecondaryTouched);
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisTouch, out value.ThumbstickTouched);
            value.TriggerTouched = value.Trigger > .01f;
            return value;
        }
        public bool IsBound(string path, VRInputSource source)
        {
            var action = SteamVR_Input.GetBaseActionFromPath(path);
            return action != null && action.GetActiveBinding(Native(source));
        }
        public void SetActionSet(string path, VRInputSource source, bool active, int priority, bool exclusive)
        {
            var set = SteamVR_Input.GetActionSetFromPath(path);
            if (active) set.Activate(Native(source), priority, exclusive); else set.Deactivate(Native(source));
        }
        public bool IsActionSetActive(string path, VRInputSource source) { return SteamVR_Input.GetActionSetFromPath(path).IsActive(Native(source)); }
        public void ListenDigital(string path, VRInputSource source, bool down, Action<VRInputSource> callback)
        {
            var action = Boolean(path);
            if (down) action.AddOnStateDownListener((a, hand) => callback((VRInputSource)(int)hand), Native(source));
            else action.AddOnStateUpListener((a, hand) => callback((VRInputSource)(int)hand), Native(source));
        }
        public void ListenAxis(string path, VRInputSource source, Action<VRInputSource, Vector2, Vector2> callback)
        { Axis(path).AddOnChangeListener((a, hand, value, delta) => callback((VRInputSource)(int)hand, value, delta), Native(source)); }
        public void ListenUpdates(Action callback) { if (updates.Add(callback)) SteamVR_Input.onNonVisualActionsUpdated += callback; }
        public void RemoveUpdateListener(Action callback) { if (updates.Remove(callback)) SteamVR_Input.onNonVisualActionsUpdated -= callback; }
        public void Haptic(string path, VRInputSource source, float delay, float duration, float frequency, float amplitude)
        {
            if (!haptics.TryGetValue(path, out var action)) haptics[path] = action = SteamVR_Input.GetActionFromPath<SteamVR_Action_Vibration>(path);
            action.Execute(delay, duration, frequency, amplitude, Native(source));
        }
        public virtual bool OpenBindingUI(string actionSetPath) { SteamVR_Input.OpenBindingUI(SteamVR_Input.GetActionSetFromPath(actionSetPath)); return true; }
    }
    public sealed class OpenVRBackend : IVRBackend
    {
        public VRBackendKind Kind { get { return VRBackendKind.OpenVR; } }
        public IVRInputBackend Input { get; } = new SteamVRInputBackend();
        public bool Initialize() { return OpenVRRuntime.InitializeVR(); }
        public bool Start() { return OpenVRRuntime.StartVR(); }
        public void Stop()
        {
            var manager = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager;
            if (manager != null) { manager.StopSubsystems(); manager.DeinitializeLoader(); }
            SteamVR.SafeDispose();
        }
        public void UpdateMirrorViewMode() { OpenVRRuntime.UpdateMirrorViewMode(); }
        public void UpdateMirrorSetup() { OpenVRRuntime.UpdateMirrorSetup(); }
        public void Recenter() { VRRecenter.Apply(); }
    }
}
