using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using ValheimVRMod.VRCore.Backends;

namespace Nikami.OpenXR;

// Build a neutral tracked rig. The authored prefab is a camera-settings template,
// never instantiated: no Valve player, pose, pointer or interaction behaviour runs.
internal sealed class OpenXRRigBackend : IVRRigBackend
{
    static GameObject inputModule;
    public GameObject CreateRig(GameObject authoredTemplate)
    {
        var root = new GameObject("ValheimVRPlayerOpenXR");
        root.SetActive(false);
        var rig = root.AddComponent<VRRig>();
        rig.TrackingOrigin = Child(root.transform, "XRTrackingOrigin");
        rig.Head = Child(rig.TrackingOrigin, "VRCamera");
        var camera = rig.Head.gameObject.AddComponent<Camera>();
        var template = authoredTemplate.GetComponentsInChildren<Camera>(true).FirstOrDefault(c => c.name == "VRCamera");
        if (!template) throw new InvalidOperationException("Authored VHVR VRCamera template is missing.");
        camera.CopyFrom(template);
        // CopyFrom copies camera settings, not the GameObject tag. Valheim's
        // environment and lighting find the active world view through Camera.main.
        rig.Head.gameObject.tag = template.gameObject.tag;
        camera.enabled = false;
        camera.stereoTargetEye = StereoTargetEyeMask.Both;
        rig.Head.gameObject.AddComponent<AudioListener>();
        rig.Head.gameObject.AddComponent<VRFade>();
        AddPose(rig.Head, rig.TrackingOrigin, VRInputSource.Head);
        rig.Left = Hand(rig.TrackingOrigin, "LeftHand", VRInputSource.LeftHand);
        rig.Right = Hand(rig.TrackingOrigin, "RightHand", VRInputSource.RightHand);
        rig.Left.otherHand = rig.Right; rig.Right.otherHand = rig.Left;
        // VRGUI installs its input module once. Keep that EventSystem alive
        // while the tracked rig follows scene cameras and is recreated on load.
        if (!inputModule)
        {
            inputModule = new GameObject("VHVRInputModule");
            inputModule.SetActive(false);
            inputModule.AddComponent<EventSystem>();
            inputModule.AddComponent<StandaloneInputModule>();
            Object.DontDestroyOnLoad(inputModule);
            inputModule.SetActive(true);
        }
        root.SetActive(true);
        VRPoseDriver.RefreshOpenXRPose();
        OpenXRPlugin.Log.LogInfo("Created native OpenXR rig; no managed SteamVR input, player or pose bridge.");
        return root;
    }
    static Transform Child(Transform parent, string name)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }
    static VRPoseDriver AddPose(Transform target, Transform origin, VRInputSource source)
    {
        var pose = target.gameObject.AddComponent<VRPoseDriver>();
        pose.origin = origin; pose.inputSource = source; pose.NativeOpenXR = true;
        return pose;
    }
    static VRHand Hand(Transform origin, string name, VRInputSource source)
    {
        var transform = Child(origin, name);
        var hand = transform.gameObject.AddComponent<VRHand>();
        hand.Pose = AddPose(transform, origin, source);
        var pointer = transform.gameObject.AddComponent<VRLaserPointer>();
        pointer.pose = hand.Pose;
        pointer.color = Color.blue;
        return hand;
    }
}
