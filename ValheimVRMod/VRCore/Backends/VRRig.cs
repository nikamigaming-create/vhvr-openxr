using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimVRMod.VRCore.Backends
{
    public interface IVRRigBackend
    {
        GameObject CreateRig(GameObject authoredTemplate);
    }

    // Shared gameplay owns sockets and transforms, never a runtime's player/hand classes.
    public sealed class VRRig : MonoBehaviour
    {
        public static VRRig Current { get; private set; }
        public Transform TrackingOrigin, Head;
        public VRHand Left, Right;
        public float EyeHeight => TrackingOrigin && Head ? TrackingOrigin.InverseTransformPoint(Head.position).y : 0;
        void Awake() { Current = this; }
        void OnDestroy() { if (Current == this) Current = null; }
    }

    [DefaultExecutionOrder(-20000)]
    public sealed class VRPoseDriver : MonoBehaviour
    {
        static readonly List<VRPoseDriver> Drivers = new List<VRPoseDriver>();
        public static event Action<VRPoseDriver> PosePublished;
        public VRInputSource inputSource;
        public Transform origin;
        public bool NativeOpenXR;
        public VRPoseState State { get; private set; }
        public bool isValid => State.Valid && State.Connected;
        public Vector3 GetVelocity() => State.Velocity;
        public Vector3 GetAngularVelocity() => State.AngularVelocity;
        void OnEnable() { Drivers.Add(this); }
        void OnDisable() { Drivers.Remove(this); State = default; PosePublished?.Invoke(this); }
        public static void RefreshOpenXRPose()
        {
            for (int i = 0; i < Drivers.Count; i++)
                if (Drivers[i] && Drivers[i].NativeOpenXR) Drivers[i].UpdateTransform();
        }
        public void UpdateTransform()
        {
            string path = inputSource == VRInputSource.LeftHand ? VRInputActions.valheim_PoseL.Path :
                inputSource == VRInputSource.RightHand ? VRInputActions.valheim_PoseR.Path : "/user/head";
            Publish(VRInput.Backend.ReadPose(path, inputSource), true);
        }
        // OpenVR calls this after its native pose publication; OpenXR publishes directly.
        public void Publish(VRPoseState pose, bool applyTransform)
        {
            State = pose;
            if (applyTransform && isValid)
            {
                transform.SetPositionAndRotation(origin ? origin.TransformPoint(pose.Position) : pose.Position,
                    origin ? origin.rotation * pose.Rotation : pose.Rotation);
            }
            PosePublished?.Invoke(this);
        }
    }

    public sealed class VRHand : MonoBehaviour
    {
        public VRPoseDriver Pose;
        public VRHand otherHand;
        public Action<bool> Visibility;
        public bool isActive => Pose && Pose.State.Connected;
        public bool isPoseValid => Pose && Pose.isValid;
        public void SetVisibility(bool visible) { Visibility?.Invoke(visible); }
        public Vector3 GetTrackedObjectVelocity() => Pose ? (Pose.origin ? Pose.origin.TransformDirection(Pose.GetVelocity()) : Pose.GetVelocity()) : Vector3.zero;
        public Vector3 GetTrackedObjectAngularVelocity() => Pose ? (Pose.origin ? Pose.origin.TransformDirection(Pose.GetAngularVelocity()) : Pose.GetAngularVelocity()) : Vector3.zero;
    }

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
