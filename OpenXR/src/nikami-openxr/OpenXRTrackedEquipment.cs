using System;
using HarmonyLib;
using UnityEngine;
using Valve.VR;

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
    SteamVR_Input_Sources hand;
    bool hidden;

    internal static void Attach(GameObject item, VisEquipment equipment, int itemHash, Transform joint)
    {
        SteamVR_Input_Sources source = SteamVR_Input_Sources.Any;
        var prefab = ObjectDB.instance.GetItemPrefab(itemHash);
        var attack = prefab ? prefab.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_attack.m_attackAnimation : null;
        bool paired = attack == "dualaxes" || attack == "dual_knives"
            || (item.name.StartsWith("attach_skin", StringComparison.Ordinal)
                && (joint == equipment.m_leftHand || joint == equipment.m_rightHand));
        for (var parent = item.transform.parent; parent; parent = parent.parent)
        {
            if (parent.name == "LeftHand_Attach") { source = SteamVR_Input_Sources.LeftHand; break; }
            if (parent.name == "RightHand_Attach") { source = SteamVR_Input_Sources.RightHand; break; }
        }
        // Lanterns are parented directly to a tracked controller by VHVR.
        var physical = OpenXRPhysicalHands.Current;
        if (physical && physical.Left.Hand && joint == physical.Left.Hand.transform) source = SteamVR_Input_Sources.LeftHand;
        if (physical && physical.Right.Hand && joint == physical.Right.Hand.transform) source = SteamVR_Input_Sources.RightHand;
        if (paired) source = SteamVR_Input_Sources.Any;
        if (source == SteamVR_Input_Sources.Any && !paired) return;
        var guard = item.GetComponent<OpenXRTrackedEquipment>() ?? item.AddComponent<OpenXRTrackedEquipment>();
        guard.SetSuppressed(false);
        guard.hand = source;
        OpenXRPhysicalEquipment.Attach(item, source, equipment);
        guard.renderers = item.GetComponentsInChildren<Renderer>(true);
        guard.previous = new bool[guard.renderers.Length];
        guard.LateUpdate();
    }

    void LateUpdate()
    {
        if (renderers == null) return;
        bool valid = (hand == SteamVR_Input_Sources.RightHand || Valid(SteamVR_Input_Sources.LeftHand))
            && (hand == SteamVR_Input_Sources.LeftHand || Valid(SteamVR_Input_Sources.RightHand));
        bool suppress = OpenXRPlugin.Ready && UseVrControls()
            && (!OpenXRPlugin.DisplayHealthy || !valid);
        SetSuppressed(suppress);
    }
    static bool Valid(SteamVR_Input_Sources source)
    {
        var pose = source == SteamVR_Input_Sources.LeftHand ? SteamVR_Actions.valheim_PoseL : SteamVR_Actions.valheim_PoseR;
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
