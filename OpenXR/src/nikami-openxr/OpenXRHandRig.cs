using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using Valve.VR;

namespace Nikami.OpenXR;

// OpenXR grip -> anatomical palm -> native wrist -> the existing VRIK arm.
// Unity metres/+Z forward; no OpenVR model origin or activation-pose offset.
// Finger poses are sampled from the user's humanoid Avatar once, then only
// thirty native joint rotations are written. Gloves use these same skin bones.
internal sealed class OpenXRHandRig
{
    internal sealed class HandRig
    {
        internal OpenXRPhysicalHands.HandState State;
        internal Transform Wrist, Target, Proxy;
        internal readonly Transform[] Bones = new Transform[15];
        internal readonly Quaternion[,] Poses = new Quaternion[9, 15];
        internal readonly float[] Curls = new float[5];
        internal readonly float[] ContactLimits = { 1, 1, 1, 1, 1 };
        internal Collider ContactTarget;
        internal Collider SurfaceTarget;
        internal Vector3 ContactPosition;
        internal Quaternion ContactRotation;
        internal Vector3 PalmInWrist;
        internal Pose GripFromWrist;
        internal Func<bool> FingersFree;
        internal int AppliedFrame = -1;
    }
    internal readonly HandRig Left, Right;
    readonly object solver;
    internal bool Ready { get; private set; }

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method("RootMotion.FinalIK.IKSolverVR:OnUpdate"),
            prefix: new HarmonyMethod(typeof(OpenXRHandRig), nameof(BeforeArmSolve)));
        harmony.Patch(AccessTools.Method("ValheimVRMod.Scripts.HandGesture:Update"),
            prefix: new HarmonyMethod(typeof(OpenXRHandRig), nameof(NativeGesture)));
    }
    static bool NativeGesture(Component __instance)
    {
        var hands = OpenXRPhysicalHands.Current;
        // The OpenVR render-model skeleton is intentionally absent. In
        // addition to doing nothing for fingers, the old Update restores the
        // previous render's wrist rotation before this frame's arm solve.
        return !hands || hands.HandRig?.Ready != true ||
            (__instance != hands.Left.Gesture && __instance != hands.Right.Gesture);
    }
    static void BeforeArmSolve(object __instance)
    {
        var rig = OpenXRPhysicalHands.Current?.HandRig;
        if (rig?.Ready != true || !ReferenceEquals(rig.solver, __instance)) return;
        rig.Align(rig.Left); rig.Align(rig.Right);
    }

    internal OpenXRHandRig(Player player, Component rig, OpenXRPhysicalHands.HandState left,
        OpenXRPhysicalHands.HandState right, Transform leftWrist, Transform rightWrist, Transform leftTarget, Transform rightTarget)
    {
        solver = Traverse.Create(rig).Field("solver").GetValue();
        Left = Make(left, leftWrist, leftTarget, "leftHandBone");
        Right = Make(right, rightWrist, rightTarget, "rightHandBone");
        var animator = player.GetComponentInChildren<Animator>();
        if (!animator || !animator.isHuman || !left.Gesture || !right.Gesture) return;
        Transform clone = null;
        try
        {
            var copies = new Dictionary<Transform, Transform>();
            clone = CopySkeleton(animator.transform, null, copies);
            clone.gameObject.SetActive(false);
            using var handler = new HumanPoseHandler(animator.avatar, clone);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            BindBones(Left, animator, HumanBodyBones.LeftThumbProximal);
            BindBones(Right, animator, HumanBodyBones.RightThumbProximal);
            var names = HumanTrait.MuscleName;
            for (int sample = 0; sample < 9; sample++)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i];
                    if (!(name.StartsWith("Left ") || name.StartsWith("Right ")) ||
                        !(name.Contains("Thumb") || name.Contains("Index") || name.Contains("Middle") || name.Contains("Ring") || name.Contains("Little"))) continue;
                    float curl = sample / 8f;
                    pose.muscles[i] = name.Contains("Spread") ? 0 : name.Contains("Thumb") ? Mathf.Lerp(.65f, -.65f, curl) : 1 - curl * 2;
                }
                handler.SetHumanPose(ref pose);
                for (int i = 0; i < 15; i++)
                {
                    Left.Poses[sample, i] = copies[Left.Bones[i]].localRotation;
                    Right.Poses[sample, i] = copies[Right.Bones[i]].localRotation;
                }
            }
            Ready = Calibrate(Left) && Calibrate(Right);
            if (Ready)
                OpenXRPlugin.Log.LogInfo($"OpenXR native hand rig: 30 avatar finger joints; anatomical grip-to-wrist L={Left.GripFromWrist.position:F3}, R={Right.GripFromWrist.position:F3} m.");
        }
        catch (Exception error) { OpenXRPlugin.Log.LogError("OpenXR native hand rig initialization failed: " + error); }
        finally { if (clone) UnityEngine.Object.Destroy(clone.gameObject); }
    }

    static Transform CopySkeleton(Transform original, Transform parent, Dictionary<Transform, Transform> copies)
    {
        if (copies.Count > 512) throw new InvalidOperationException("Unexpected humanoid hierarchy size.");
        var copy = new GameObject(original.name) { hideFlags = HideFlags.HideAndDontSave }.transform;
        copy.SetParent(parent, false);
        copy.localPosition = original.localPosition;
        copy.localRotation = original.localRotation;
        copy.localScale = original.localScale;
        copies.Add(original, copy);
        for (int i = 0; i < original.childCount; i++) CopySkeleton(original.GetChild(i), copy, copies);
        return copy;
    }
    static HandRig Make(OpenXRPhysicalHands.HandState state, Transform wrist, Transform target, string proxyName)
    {
        var vr = AccessTools.TypeByName("ValheimVRMod.VRCore.VRPlayer");
        var result = new HandRig { State = state, Wrist = wrist, Target = target,
            Proxy = (Transform)AccessTools.PropertyGetter(vr, proxyName).Invoke(null, null) };
        if (state.Gesture)
            result.FingersFree = AccessTools.MethodDelegate<Func<bool>>(AccessTools.Method(state.Gesture.GetType(), "areFingersFree"), state.Gesture);
        return result;
    }
    static void BindBones(HandRig hand, Animator animator, HumanBodyBones first)
    {
        for (int i = 0; i < 15; i++)
            hand.Bones[i] = animator.GetBoneTransform(first + i) ?? throw new InvalidOperationException("Missing native finger bone " + (first + i));
    }
    static bool Calibrate(HandRig hand)
    {
        // Same anatomical landmarks used by the local MGS5VR rig, converted
        // from OpenXR's -Z forward convention to Unity's +Z forward convention.
        var wrist = hand.Wrist;
        var index = hand.Bones[3].position;
        var little = hand.Bones[12].position;
        var along = (index + little) * .5f - wrist.position;
        var across = index - little;
        if (along.magnitude < .035f || along.magnitude > .16f || across.magnitude < .02f || across.magnitude > .13f) return false;
        var z = across.normalized;
        var x = Vector3.Cross(z, along).normalized;
        var y = Vector3.Cross(z, x).normalized;
        var palmRotation = Quaternion.LookRotation(z, y);
        var centre = wrist.position + along * .55f;
        hand.PalmInWrist = Quaternion.Inverse(wrist.rotation) * (centre - wrist.position);
        hand.GripFromWrist = new Pose(Quaternion.Inverse(palmRotation) * (wrist.position - centre), Quaternion.Inverse(palmRotation) * wrist.rotation);
        return true;
    }
    void Align(HandRig hand)
    {
        var state = hand.State;
        if (!state.Active || !hand.Target || !hand.Proxy || hand.FingersFree == null) return;
        // Equipped items retain their authored wrist orientation and native
        // two-hand connectors; an empty hand uses the OpenXR anatomical basis.
        if (hand.FingersFree()) hand.Target.localRotation = hand.GripFromWrist.rotation;
        hand.Target.localPosition = hand.Target.parent.InverseTransformVector(-(hand.Target.rotation * hand.PalmInWrist));
        hand.Proxy.SetPositionAndRotation(hand.Target.position, hand.Target.rotation);
        var centre = hand.Target.position + hand.Target.rotation * hand.PalmInWrist;
        var rotation = hand.Target.rotation * Quaternion.Inverse(hand.GripFromWrist.rotation);
        var localRotation = Quaternion.Inverse(state.Hand.transform.rotation) * rotation;
        var localCentre = Quaternion.Inverse(state.Hand.transform.rotation) * (centre - state.Hand.transform.position);
        state.Solver.Shapes[0] = new OpenXRContactSolver.Shape(localCentre, new Vector3(.022f, .047f, .042f), localRotation);
    }

    internal void UpdateFingers()
    {
        if (!Ready) return;
        UpdateFingers(Left); UpdateFingers(Right);
    }
    void UpdateFingers(HandRig hand)
    {
        var state = hand.State;
        if (!state.Active || hand.FingersFree == null || !hand.FingersFree()) return;
        var device = InputAdapter.Device(state.Source == SteamVR_Input_Sources.LeftHand ? 1 : 2);
        float squeeze = Mathf.Clamp01(InputAdapter.Control<AxisControl>(device, "grip")?.ReadValue() ?? 0);
        float trigger = Mathf.Clamp01(InputAdapter.Control<AxisControl>(device, "trigger")?.ReadValue() ?? 0);
        bool indexTouch = Touched(device, "triggerTouched", "triggerTouch");
        bool thumbTouch = Touched(device, "primaryTouched", "primaryTouch") || Touched(device, "secondaryTouched", "secondaryTouch") || Touched(device, "thumbstickTouched", "primary2DAxisTouch");
        bool held = state.Grab || state.CreatureGrip;
        if (!state.Grip || held || state.Solver.Yielding) hand.SurfaceTarget = null;
        else if (state.Solver.LastContact.Collider) hand.SurfaceTarget = state.Solver.LastContact.Collider;
        if (hand.SurfaceTarget)
        {
            var surface = hand.SurfaceTarget;
            var nearest = surface is MeshCollider mesh && !mesh.convex ? surface.bounds.ClosestPoint(state.PhysicalPalm) : surface.ClosestPoint(state.PhysicalPalm);
            if ((nearest - state.PhysicalPalm).sqrMagnitude > .12f * .12f) hand.SurfaceTarget = null;
        }
        UpdateContactLimits(hand, held ? state.PendingCollider : hand.SurfaceTarget);
        held |= hand.SurfaceTarget;
        for (int finger = 0; finger < 5; finger++)
        {
            // A raised, untouched thumb stays independent of the squeezed
            // fingers. Held-object contact still closes it around the grip.
            float curl = finger == 0 ? (thumbTouch ? Mathf.Max(squeeze, .65f) : .1f) : finger == 1 ? Mathf.Max(trigger, indexTouch ? .35f : .06f) : Mathf.Max(.06f, squeeze);
            if (held) curl = Mathf.Max(curl, finger == 0 ? .6f : .75f);
            curl = Mathf.Min(curl, hand.ContactLimits[finger]);
            hand.Curls[finger] = Mathf.MoveTowards(hand.Curls[finger], curl, Time.unscaledDeltaTime * 18);
            ApplyFinger(hand, finger, hand.Curls[finger]);
        }
        hand.AppliedFrame = Time.frameCount;
    }

    static void UpdateContactLimits(HandRig hand, Collider target)
    {
        if (!target)
        {
            for (int finger = 0; finger < 5; finger++) hand.ContactLimits[finger] = 1;
            hand.ContactTarget = null;
            return;
        }
        var position = Quaternion.Inverse(target.transform.rotation) * (hand.Wrist.position - target.transform.position);
        var rotation = Quaternion.Inverse(target.transform.rotation) * hand.Wrist.rotation;
        if (target == hand.ContactTarget && (position - hand.ContactPosition).sqrMagnitude < .000004f
            && Quaternion.Angle(rotation,hand.ContactRotation) < 2) return;
        hand.ContactTarget = target; hand.ContactPosition = position; hand.ContactRotation = rotation;
        // Only the held collider is queried. Cache in its frame, so an item
        // following the hand doesn't cause another solve every render frame.
        for (int finger = 0; finger < 5; finger++)
        {
            ApplyFinger(hand, finger, 0);
            // A coarse creature hurtbox can already contain a knuckle. It
            // cannot meaningfully constrain that finger; retain articulation.
            if (FingerIntersects(hand, finger, target)) { hand.ContactLimits[finger] = .7f; continue; }
            float low = 0, high = 1;
            bool blocked = false;
            for (int sample = 1; sample <= 8; sample++)
            {
                float curl = sample / 8f;
                ApplyFinger(hand, finger, curl);
                if (FingerIntersects(hand, finger, target)) { high = curl; blocked = true; break; }
                low = curl;
            }
            if (blocked)
                for (int refinement = 0; refinement < 3; refinement++)
                {
                    float mid = (low + high) * .5f;
                    ApplyFinger(hand, finger, mid);
                    if (FingerIntersects(hand, finger, target)) high = mid; else low = mid;
                }
            hand.ContactLimits[finger] = blocked ? low : 1;
        }
    }
    static bool FingerIntersects(HandRig hand, int finger, Collider target)
    {
        int first = finger * 3;
        Vector3 previous = hand.Bones[first].position;
        for (int joint = 1; joint <= 3; joint++)
        {
            var bone = hand.Bones[first + Mathf.Min(joint, 2)];
            var end = joint < 3 ? bone.position : bone.position + (bone.position - hand.Bones[first+1].position) * .75f;
            var segment = end - previous;
            if (segment.sqrMagnitude > .000001f && target.Raycast(new Ray(previous,segment.normalized), out _,segment.magnitude)) return true;
            if (!(target is MeshCollider mesh) || mesh.convex)
                if ((target.ClosestPoint(end) - end).sqrMagnitude < .004f * .004f) return true;
            previous = end;
        }
        return false;
    }
    static bool Touched(UnityEngine.InputSystem.InputDevice device, string name, string alias) =>
        (InputAdapter.Control<ButtonControl>(device, name) ?? InputAdapter.Control<ButtonControl>(device, alias))?.isPressed ?? false;
    static void ApplyFinger(HandRig hand, int finger, float curl)
    {
        float sample = Mathf.Clamp01(curl) * 8;
        int low = Mathf.Min(7, (int)sample);
        for (int joint = 0; joint < 3; joint++)
        {
            int index = finger * 3 + joint;
            hand.Bones[index].localRotation = Quaternion.Slerp(hand.Poses[low, index], hand.Poses[low + 1, index], sample - low);
        }
    }
}
