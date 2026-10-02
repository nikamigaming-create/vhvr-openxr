using HarmonyLib;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;

namespace ValheimVRMod.VRCore.Backends
{
    // Valve types stay inside the original OpenVR provider; shared gameplay uses VRRig/VRHand.
    public sealed class OpenVRRigBackend : IVRRigBackend
    {
        static bool installed;
        public static bool LoadShaders(string path)
        {
            if (!ShaderLoader.Initialize(path)) return false;
            foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>()) VRShaders.Remember(shader);
            return true;
        }
        public GameObject CreateRig(GameObject authoredTemplate)
        {
            if (!installed)
            {
                var harmony = new Harmony("vhvr.openvr.rig");
                harmony.Patch(AccessTools.Method(typeof(SteamVR_Behaviour_Pose), "UpdateTransform"),
                    postfix: new HarmonyMethod(typeof(OpenVRRigBackend), nameof(PoseUpdated)));
                installed = true;
            }
            var root = Object.Instantiate(authoredTemplate);
            var native = root.GetComponent<Valve.VR.InteractionSystem.Player>();
            var rig = root.AddComponent<VRRig>();
            rig.TrackingOrigin = native.trackingOriginTransform;
            rig.Head = native.hmdTransform;
            var fade = rig.Head.GetComponent<SteamVR_Fade>();
            if (fade) fade.enabled = false;
            rig.Head.gameObject.AddComponent<VRFade>();
            foreach (var hand in root.GetComponentsInChildren<Hand>(true))
            {
                if (hand.name != "LeftHand" && hand.name != "RightHand") continue;
                var shared = hand.gameObject.AddComponent<VRHand>();
                var pose = hand.GetComponent<SteamVR_Behaviour_Pose>();
                shared.Pose = hand.gameObject.AddComponent<VRPoseDriver>();
                shared.Pose.inputSource = (VRInputSource)(int)pose.inputSource;
                shared.Pose.origin = pose.origin ? pose.origin : rig.TrackingOrigin;
                shared.Visibility = visible => { hand.enabled = shared.enabled; hand.SetVisibility(visible); };
                var pointer = hand.GetComponent<Valve.VR.Extras.SteamVR_LaserPointer>();
                var sharedPointer = hand.gameObject.AddComponent<VRLaserPointer>();
                sharedPointer.pose = shared.Pose;
                if (pointer) { sharedPointer.color = pointer.color; sharedPointer.thickness = pointer.thickness; pointer.enabled = false; }
                if (shared.Pose.inputSource == VRInputSource.LeftHand) rig.Left = shared; else rig.Right = shared;
                PoseUpdated(pose);
            }
            rig.Left.otherHand = rig.Right; rig.Right.otherHand = rig.Left;
            return root;
        }
        static void PoseUpdated(SteamVR_Behaviour_Pose __instance)
        {
            var driver = __instance.GetComponent<VRPoseDriver>();
            if (!driver || driver.NativeOpenXR) return;
            var action = __instance.poseAction;
            var source = __instance.inputSource;
            driver.Publish(new VRPoseState {
                Position = action.GetLocalPosition(source), Rotation = action.GetLocalRotation(source),
                Velocity = __instance.GetVelocity(), AngularVelocity = __instance.GetAngularVelocity(),
                Valid = __instance.isValid, Connected = action.GetDeviceIsConnected(source)
            }, false);
        }
    }
}
