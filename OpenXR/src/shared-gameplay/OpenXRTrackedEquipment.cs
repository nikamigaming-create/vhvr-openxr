using ValheimVRMod.VRCore.Backends;
using System;
using HarmonyLib;
using UnityEngine;

namespace Nikami.OpenXR;

// Invalid controller poses otherwise leave VHVR's held items at the head
// origin. A shield viewed from inside then covers the mirror with a black,
// striped surface. Keep native materials and hide only untracked equipment.
[DefaultExecutionOrder(31000)]
internal sealed class OpenXRTrackedEquipment : MonoBehaviour
{
    static readonly Func<bool> UseVrControls = AccessTools.MethodDelegate<Func<bool>>(
        AccessTools.Method("ValheimVRMod.Utilities.VHVRConfig:UseVrControls"));
    Renderer[] renderers;
    bool[] previous;
    VRInputSource hand;
    bool hidden;

    internal static void Attach(GameObject item, VisEquipment equipment, int itemHash, Transform joint)
    {
        VRInputSource source = VRInputSource.Any;
        var prefab = ObjectDB.instance.GetItemPrefab(itemHash);
        var attack = prefab ? prefab.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_attack.m_attackAnimation : null;
        bool paired = attack == "dualaxes" || attack == "dual_knives"
            || (item.name.StartsWith("attach_skin", StringComparison.Ordinal)
                && (joint == equipment.m_leftHand || joint == equipment.m_rightHand));
        for (var parent = item.transform.parent; parent; parent = parent.parent)
        {
            if (parent.name == "LeftHand_Attach") { source = VRInputSource.LeftHand; break; }
            if (parent.name == "RightHand_Attach") { source = VRInputSource.RightHand; break; }
        }
        // Lanterns are parented directly to a tracked controller by VHVR.
        var rig = VRRig.Current;
        if (rig && rig.Left && joint == rig.Left.transform) source = VRInputSource.LeftHand;
        if (rig && rig.Right && joint == rig.Right.transform) source = VRInputSource.RightHand;
        if (paired) source = VRInputSource.Any;
        if (source == VRInputSource.Any && !paired) return;
        var guard = item.GetComponent<OpenXRTrackedEquipment>() ?? item.AddComponent<OpenXRTrackedEquipment>();
        guard.SetSuppressed(false);
        guard.hand = source;
        if (VRGameplay.Options.PhysicalContact) OpenXRPhysicalEquipment.Attach(item, source, equipment);
        guard.renderers = item.GetComponentsInChildren<Renderer>(true);
        guard.previous = new bool[guard.renderers.Length];
        guard.LateUpdate();
    }

    void LateUpdate()
    {
        if (renderers == null) return;
        bool valid = (hand == VRInputSource.RightHand || Valid(VRInputSource.LeftHand))
            && (hand == VRInputSource.LeftHand || Valid(VRInputSource.RightHand));
        bool suppress = VRBackendHost.IsReady && UseVrControls()
            && (!VRBackendHost.DisplayHealthy || !valid);
        SetSuppressed(suppress);
    }
    static bool Valid(VRInputSource source)
    {
        var pose = source == VRInputSource.LeftHand ? VRInputActions.valheim_PoseL : VRInputActions.valheim_PoseR;
        return pose != null && pose.GetPoseIsValid(source);
    }

    void SetSuppressed(bool suppress)
    {
        if (hidden == suppress) return;
        hidden = suppress;
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (!renderer) continue;
            if (suppress) { previous[i] = renderer.forceRenderingOff; renderer.forceRenderingOff = true; }
            else renderer.forceRenderingOff = previous[i];
        }
    }

    void OnDisable() => SetSuppressed(false);
}
