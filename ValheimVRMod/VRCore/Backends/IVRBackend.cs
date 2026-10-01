using System;
using UnityEngine;

namespace ValheimVRMod.VRCore.Backends
{
    public enum VRBackendKind { OpenVR, OpenXR }
    public enum VRInputSource
    {
        Any, LeftHand, RightHand, LeftFoot, RightFoot, LeftShoulder, RightShoulder,
        Waist, Chest, Head, Gamepad, Camera, Keyboard, Treadmill, LeftAnkle, RightAnkle
    }
    public struct VRDigitalState
    {
        public bool Active, Held, Down, Up;
    }
    public struct VRPoseState
    {
        public Vector3 Position, Velocity, AngularVelocity;
        public Quaternion Rotation;
        public bool Valid, Connected;
    }
    public struct VRHandControls
    {
        public float Grip, Trigger;
        public bool PrimaryTouched, SecondaryTouched, ThumbstickTouched, TriggerTouched;
    }

    // The gameplay contract contains engine values and action paths, never SDK handles.
    public interface IVRInputBackend
    {
        VRDigitalState ReadDigital(string path, VRInputSource source);
        Vector2 ReadAxis(string path, VRInputSource source);
        VRPoseState ReadPose(string path, VRInputSource source);
        VRHandControls ReadHandControls(VRInputSource source);
        bool IsBound(string path, VRInputSource source);
        void SetActionSet(string path, VRInputSource source, bool active, int priority, bool exclusive);
        bool IsActionSetActive(string path, VRInputSource source);
        void ListenDigital(string path, VRInputSource source, bool down, Action<VRInputSource> callback);
        void ListenAxis(string path, VRInputSource source, Action<VRInputSource, Vector2, Vector2> callback);
        void ListenUpdates(Action callback);
        void RemoveUpdateListener(Action callback);
        void Haptic(string path, VRInputSource source, float delay, float duration, float frequency, float amplitude);
        bool OpenBindingUI(string actionSetPath);
    }
    public interface IVRBackend
    {
        VRBackendKind Kind { get; }
        IVRInputBackend Input { get; }
        bool Initialize();
        bool Start();
        void Stop();
        void UpdateMirrorViewMode();
        void UpdateMirrorSetup();
        void Recenter();
    }

    public static class VRBackendHost
    {
        static IVRBackend active = new OpenVRBackend();
        static bool initialized;
        public static bool IsReady { get; private set; }
        public static bool IsRunning { get; private set; }
        public static bool DisplayHealthy { get; private set; }
        public static bool Focused { get; private set; } = true;
        public static IVRBackend Active { get { return active; } }
        public static void Select(IVRBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            if (initialized) throw new InvalidOperationException("VR backend selection requires a restart.");
            active = backend;
        }
        public static bool Initialize()
        {
            if (initialized) return IsReady;
            initialized = true;
            IsReady = active.Initialize();
            return IsReady;
        }
        public static bool Start() { if (!IsRunning) IsRunning = IsReady && active.Start(); return IsRunning; }
        public static void Stop()
        {
            IsRunning = false;
            IsReady = false;
            DisplayHealthy = false;
            active.Stop();
        }
        public static void UpdateDisplayState(IVRBackend producer, bool healthy, bool focused)
        {
            if (!ReferenceEquals(producer, active)) throw new InvalidOperationException("Stale VR backend display producer.");
            DisplayHealthy = IsRunning && healthy;
            Focused = focused;
        }
        public static VRBackendKind Parse(string value)
        {
            if (string.Equals(value, "openxr", StringComparison.OrdinalIgnoreCase)) return VRBackendKind.OpenXR;
            if (string.Equals(value, "openvr", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "steamvr", StringComparison.OrdinalIgnoreCase)) return VRBackendKind.OpenVR;
            throw new ArgumentException("VR backend must be openxr, openvr or steamvr.");
        }
        public static VRBackendKind Choose(string configured, string[] arguments)
        {
            string choice = configured;
            foreach (string argument in arguments)
                if (argument.StartsWith("-vrbackend=", StringComparison.OrdinalIgnoreCase))
                    choice = argument.Substring("-vrbackend=".Length);
            return Parse(choice);
        }
    }
}
