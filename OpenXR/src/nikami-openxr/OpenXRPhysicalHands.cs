using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;

namespace Nikami.OpenXR;

[DefaultExecutionOrder(30000)]
internal sealed class OpenXRPhysicalHands : MonoBehaviour
{
    internal sealed class HandState
    {
        internal Hand Hand;
        internal SteamVR_Input_Sources Source;
        internal OpenXRContactSolver Solver;
        internal Pose Tracked;
        internal Vector3 Origin;
        internal Quaternion LocalRotation;
        internal Vector3 AngularVelocity;
        internal int VelocityFrame = -1;
        internal float SampleTime;
        internal bool Grip, Active;
        internal readonly OpenXRImpactGate Impact = new();
        internal int Contacts, Clinks;
        internal int NativeContactFrame = -1;
        internal OpenXRPhysicalGrab Grab;
        internal OpenXRCreatureGrip CreatureGrip;
        internal Component Gesture;
        internal Func<bool> HandFree;
        internal Component Fist, Weapon;
        internal ItemDrop Pending;
        internal Collider PendingCollider;
        internal float PendingUntil;
        internal Vector3 Palm => Solver.Shapes[0].Center;
        internal Vector3 PhysicalPalm => Solver.Pose.position + Solver.Pose.rotation * Palm;
    }
    internal static OpenXRPhysicalHands Current;
    static Func<Hand> leftHand, rightHand;
    static Func<bool> useControls, firstPerson, twoHanded;
    static Func<bool> rightDominant;
    static Func<Component> leftFist, rightFist, leftWeapon, rightWeapon;
    static Func<Component> vrik;
    static Type gestureType;
    static MethodInfo gestureSource, gestureFree;
    static FieldInfo nativeGrabType;
    static object nativeGrabNone;
    static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> RightItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem");
    static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> LeftItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem");
    static readonly Collider[] Nearby = new Collider[32];
    static readonly RaycastHit[] SightHits = new RaycastHit[32];
    internal readonly HandState Left = new(), Right = new();
    internal OpenXRHandRig HandRig;
    readonly OpenXRImpactGate equipmentImpact = new(2f);
    internal const float HeldTrackingGap = .16f;
    Player player;
    Component rig;
    Transform leftWrist, rightWrist, leftTarget, rightTarget;
    static readonly WaitForEndOfFrame RenderComplete = new();
    int mask, grabMask;
    float prepareAt;
    internal double TotalSolveMs;
    internal double MaxSolveMs;
    internal int Solves;

    internal static void Install(Harmony harmony)
    {
        var vr = AccessTools.TypeByName("ValheimVRMod.VRCore.VRPlayer");
        leftHand = AccessTools.MethodDelegate<Func<Hand>>(AccessTools.PropertyGetter(vr, "leftHand"));
        rightHand = AccessTools.MethodDelegate<Func<Hand>>(AccessTools.PropertyGetter(vr, "rightHand"));
        firstPerson = AccessTools.MethodDelegate<Func<bool>>(AccessTools.PropertyGetter(vr, "inFirstPerson"));
        rightDominant = AccessTools.MethodDelegate<Func<bool>>(AccessTools.PropertyGetter(vr, "isRightHandMainWeaponHand"));
        vrik = AccessTools.MethodDelegate<Func<Component>>(AccessTools.PropertyGetter(vr, "vrikRef"));
        leftFist = AccessTools.MethodDelegate<Func<Component>>(AccessTools.Method("ValheimVRMod.Utilities.StaticObjects:leftFist"));
        rightFist = AccessTools.MethodDelegate<Func<Component>>(AccessTools.Method("ValheimVRMod.Utilities.StaticObjects:rightFist"));
        leftWeapon = AccessTools.MethodDelegate<Func<Component>>(AccessTools.Method("ValheimVRMod.Utilities.StaticObjects:leftWeaponCollider"));
        rightWeapon = AccessTools.MethodDelegate<Func<Component>>(AccessTools.Method("ValheimVRMod.Utilities.StaticObjects:rightWeaponCollider"));
        useControls = AccessTools.MethodDelegate<Func<bool>>(AccessTools.Method("ValheimVRMod.Utilities.VHVRConfig:UseVrControls"));
        twoHanded = AccessTools.MethodDelegate<Func<bool>>(AccessTools.Method("ValheimVRMod.Scripts.LocalWeaponWield:isCurrentlyTwoHanded"));
        gestureType = AccessTools.TypeByName("ValheimVRMod.Scripts.HandGesture");
        gestureSource = AccessTools.PropertyGetter(gestureType, "sourceHand");
        gestureFree = AccessTools.Method(gestureType, "isHandFree");
        harmony.Patch(AccessTools.Method(vr, "Update"), postfix: new HarmonyMethod(typeof(OpenXRPhysicalHands), nameof(Ensure)));
        // UpdateTransform completes before onTransformUpdated and before the
        // existing hand/weapon/IK consumers. The action pose and its velocity
        // remain the untouched tracked input used by native swing detection.
        harmony.Patch(AccessTools.Method(typeof(SteamVR_Behaviour_Pose), "UpdateTransform"),
            postfix: new HarmonyMethod(typeof(OpenXRPhysicalHands), nameof(ConstrainPose)));
        harmony.Patch(AccessTools.Method(typeof(ItemDrop), "CanPickup"),
            postfix: new HarmonyMethod(typeof(OpenXRPhysicalHands), nameof(ProtectHeldItem)));
        harmony.Patch(AccessTools.Method(typeof(ItemDrop), "AutoStackItems"),
            prefix: new HarmonyMethod(typeof(OpenXRPhysicalHands), nameof(AllowStack)));
        harmony.Patch(AccessTools.Method(typeof(ItemDrop), "RPC_RequestOwn"),
            prefix: new HarmonyMethod(typeof(OpenXRPhysicalHands), nameof(AllowOwnershipTransfer)));
        OpenXRCreatureGrip.Install(harmony);
        OpenXRHandRig.Install(harmony);
        foreach (var method in new[] { "OnTriggerEnter", "OnTriggerStay" })
            harmony.Patch(AccessTools.Method("ValheimVRMod.Scripts.FistCollision:" + method),
                prefix: new HarmonyMethod(typeof(OpenXRPhysicalHands), nameof(FreeFist)));
        var fistType = AccessTools.TypeByName("ValheimVRMod.Scripts.FistCollision");
        nativeGrabType = AccessTools.Field(fistType, "lastGrabbedType");
        nativeGrabNone = Enum.ToObject(nativeGrabType.FieldType, 0);
        harmony.Patch(AccessTools.PropertyGetter(fistType, "isGrabbingJumpingAid"),
            postfix: new HarmonyMethod(typeof(OpenXRPhysicalHands), nameof(ReleaseWorldGrip)));
    }
    static void Ensure()
    {
        if (!OpenXRPlugin.Ready || !Player.m_localPlayer || !useControls()) return;
        if (Current && Current.player != Player.m_localPlayer) { Destroy(Current); Current = null; }
        if (!Current) Current = Player.m_localPlayer.gameObject.AddComponent<OpenXRPhysicalHands>();
    }
    void Awake()
    {
        player = GetComponent<Player>();
        mask = LayerMask.GetMask("Default", "Default_small", "piece", "terrain", "static_solid", "item", "vehicle", "character", "character_net", "hitbox");
        grabMask = LayerMask.GetMask("item", "character", "character_net", "hitbox");
        Left.Source = SteamVR_Input_Sources.LeftHand;
        Right.Source = SteamVR_Input_Sources.RightHand;
        Left.Solver = new OpenXRContactSolver(transform, mask);
        Right.Solver = new OpenXRContactSolver(transform, mask);
        SetPalm(Left); SetPalm(Right);
        OpenXRPlugin.Log.LogInfo("OpenXR physical interaction active: swept hand/equipment contacts and native loose-item grips.");
    }
    static void SetPalm(HandState state)
    {
        // VHVR's anatomical hand targets are behind the controller grip in
        // Unity +Z space. A palm box has real thickness but excludes fingers.
        state.Solver.Shapes[0] = new OpenXRContactSolver.Shape(
            new Vector3(state.Source == SteamVR_Input_Sources.LeftHand ? -.025f : .025f, .045f, -.12f),
            new Vector3(.045f, .035f, .07f), Quaternion.identity);
        state.Solver.Count = 1;
    }
    bool Available => isActiveAndEnabled && OpenXRPlugin.Ready && OpenXRPlugin.DisplayHealthy && OpenXRPlugin.DisplayFocused
        && player && player == Player.m_localPlayer && !player.IsDead() && !player.IsTeleporting()
        && !Game.IsPaused() && !InventoryGui.IsVisible() && !Menu.IsVisible() && useControls() && firstPerson();
    bool SharedGrip => twoHanded() || RightItem(player)?.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow
        || LeftItem(player)?.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow;

    static void ConstrainPose(SteamVR_Behaviour_Pose __instance)
    {
        var current = Current;
        if (!current) return;
        var state = __instance.inputSource == SteamVR_Input_Sources.LeftHand ? current.Left :
            __instance.inputSource == SteamVR_Input_Sources.RightHand ? current.Right : null;
        if (state == null) return;
        state.Hand = state == current.Left ? leftHand() : rightHand();
        if (!state.Hand || state.Hand.transform != __instance.transform) return;
        bool active = current.Available && __instance.isValid;
        if (!active)
        {
            state.Active = false; state.Solver.Valid = false;
            current.Release(state); return;
        }
        var tracked = new Pose(__instance.transform.position, __instance.transform.rotation);
        var origin = __instance.origin ? __instance.origin.position : current.transform.position;
        bool reset = !state.Active || (origin - state.Origin).sqrMagnitude > 1;
        if (reset && state.Active) current.Release(state);
        if (state.VelocityFrame != Time.frameCount)
        {
            var originRotation = __instance.origin ? __instance.origin.rotation : Quaternion.identity;
            var localRotation = Quaternion.Inverse(originRotation) * tracked.rotation;
            var angular = __instance.GetAngularVelocity();
            if (angular.sqrMagnitude < .0001f && !reset)
            {
                var difference = localRotation * Quaternion.Inverse(state.LocalRotation);
                difference.ToAngleAxis(out float angle, out var axis);
                if (angle > 180) angle -= 360;
                if (!float.IsNaN(axis.x)) angular = axis * (angle * Mathf.Deg2Rad / Mathf.Max(1f / 240, Time.unscaledTime - state.SampleTime));
            }
            state.AngularVelocity = originRotation * Vector3.ClampMagnitude(angular,50);
            state.LocalRotation = localRotation; state.SampleTime = Time.unscaledTime; state.VelocityFrame = Time.frameCount;
        }
        state.Tracked = tracked; state.Origin = origin; state.Active = true;
        var peer = state == current.Left ? current.Right : current.Left;
        state.Solver.IgnoredBody = state.Grab ? state.Grab.Body : state.CreatureGrip ? state.CreatureGrip.Body : null;
        long begin = System.Diagnostics.Stopwatch.GetTimestamp();
        // Two hands on one weapon must not repel each other at the grip.
        var solved = state.Solver.FollowTracked(tracked, peer.Active && !current.SharedGrip ? peer.Solver : null, Time.unscaledTime, reset);
        // Check the actual held anchor before it can replace the tracked hand
        // pose. FixedUpdate and avatar reach checks alone can leave a hand
        // trailing a wedged item while the player is walking away.
        var trackedPalm = tracked.position + tracked.rotation * state.Palm;
        if ((state.Grab && (state.Grab.GripPoint - trackedPalm).sqrMagnitude > HeldTrackingGap * HeldTrackingGap)
            || (state.CreatureGrip && (state.CreatureGrip.GripPoint - trackedPalm).sqrMagnitude > HeldTrackingGap * HeldTrackingGap))
        {
            current.Release(state);
            solved = tracked; state.Solver.Pose = tracked;
        }
        if (state.Grab)
        {
            // Render the palm at the body's actual grip point. The tracked
            // target drives forces in FixedUpdate; it never teleports the body.
            solved.rotation = state.Grab.HandRotation;
            solved.position = state.Grab.GripPoint - solved.rotation * state.Palm;
            state.Solver.Pose = solved;
        }
        else if (state.CreatureGrip && state.CreatureGrip.Held)
        {
            solved.position = state.CreatureGrip.GripPoint - solved.rotation * state.Palm;
            state.Solver.Pose = solved;
        }
        if ((state.Grab || state.CreatureGrip) && (Vector3.Distance(solved.position, tracked.position) > HeldTrackingGap
            || Quaternion.Angle(solved.rotation, tracked.rotation) > 60))
        {
            current.Release(state);
            solved = tracked; state.Solver.Pose = tracked;
        }
        __instance.transform.SetPositionAndRotation(solved.position, solved.rotation);
        double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - begin) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        current.TotalSolveMs += elapsed;
        current.MaxSolveMs = Math.Max(current.MaxSolveMs, elapsed);
        current.Solves++;
        state.Impact.Update(Time.unscaledTime);
        current.equipmentImpact.Update(Time.unscaledTime);
        if (state.Solver.LastContact.Hit) current.ContactFeedback(state, peer);
    }

    void ContactFeedback(HandState state, HandState peer)
    {
        var contact = state.Solver.LastContact;
        var velocity = PointVelocity(state, contact.Point);
        var body = contact.Collider ? contact.Collider.attachedRigidbody : null;
        var otherVelocity = contact.Peer ? PointVelocity(peer, contact.Point) : body ? body.GetPointVelocity(contact.Point) : Vector3.zero;
        float speed = OpenXRImpactGate.ClosingSpeed(velocity, otherVelocity, contact.Normal);
        bool equipmentContact = contact.Peer && contact.Shape > 0 && contact.PeerShape > 0;
        if (contact.Collider && speed > .2f && !state.Grab && !state.CreatureGrip && state.NativeContactFrame != Time.frameCount)
        {
            // A solid surface can stop just outside a trigger volume. Deliver
            // that actual contact to the same native handlers; momentum,
            // stamina, friendliness and target cooldowns still decide damage.
            // Keep native combat independent of the sound latch: a soft first
            // touch must not swallow a later qualifying punch or swing. Pose
            // updates can run for both eyes; deliver at most once per frame.
            state.NativeContactFrame = Time.frameCount;
            state.Fist = state == Left ? leftFist() : rightFist();
            state.Weapon = (state == Right) == rightDominant() ? rightWeapon() : leftWeapon();
            if (state.Fist && state.Fist.gameObject.activeInHierarchy)
                state.Fist.SendMessage("OnTriggerEnter", contact.Collider, SendMessageOptions.DontRequireReceiver);
            if (state.Weapon && state.Weapon.gameObject.activeInHierarchy)
                state.Weapon.SendMessage("OnTriggerEnter", contact.Collider, SendMessageOptions.DontRequireReceiver);
        }
        if (!contact.Peer && !contact.Collider) return;
        // Brushing or squeezing one's own weapons together is silent. Only
        // a deliberate closing impact can arm the shared two-second gate.
        if (equipmentContact && speed < OpenXRImpactGate.ClinkSpeed) return;
        var gate = equipmentContact ? equipmentImpact : state.Impact;
        if (!gate.Begin(Time.unscaledTime, state.Solver, peer.Solver, contact)) return;
        state.Contacts++;
        float strength = Mathf.Clamp(.08f + speed * .12f, .08f, .65f);
        state.Hand.hapticAction.Execute(0, .035f, 100, strength, state.Source);
        if (equipmentContact && speed >= OpenXRImpactGate.ClinkSpeed)
        {
            peer.Hand.hapticAction.Execute(0, .035f, 100, strength, peer.Source);
            state.Clinks++;
            // Native equipment impact effects supply the game's own sound and
            // material response; touching your own gear does not start Attack.
            var item = RightItem(player) ?? LeftItem(player);
            item?.m_shared.m_blockEffect.Create(contact.Point, Quaternion.identity);
        }
        if (body && !body.isKinematic && !body.GetComponent<Character>())
        {
            var view = body.GetComponent<ZNetView>();
            if (view && view.IsValid() && !view.IsOwner()) return;
            float impulse = Mathf.Min(speed * .15f, .6f);
            body.AddForceAtPosition(-contact.Normal * impulse, contact.Point, ForceMode.Impulse);
        }
    }

    static Vector3 PointVelocity(HandState state, Vector3 point)
    {
        if (!state.Active || !state.Hand) return Vector3.zero;
        var pose = state.Hand.trackedObject;
        var velocity = pose ? pose.GetVelocity() : Vector3.zero;
        if (pose && pose.origin) velocity = pose.origin.TransformDirection(velocity);
        return velocity + Vector3.Cross(state.AngularVelocity, point - state.Tracked.position);
    }

    void Update()
    {
        // Resolve native gesture delegates and IK fields during loading, rather
        // than putting first-use reflection on the first grab/contact frame.
        if (Time.unscaledTime >= prepareAt && (!Left.Gesture || !Right.Gesture || !rig))
        {
            prepareAt = Time.unscaledTime + .25f;
            if (!Left.Hand) Left.Hand = leftHand();
            if (!Right.Hand) Right.Hand = rightHand();
            BindGestures();
            var currentRig = vrik();
            if (currentRig) CacheRig(currentRig);
        }
        if (!Available) { ResetHands(); return; }
        UpdateGrip(Left); UpdateGrip(Right);
    }
    void OnEnable() => StartCoroutine(MonitorReach());
    void LateUpdate() => HandRig?.UpdateFingers();
    IEnumerator MonitorReach()
    {
        while (isActiveAndEnabled)
        {
            yield return RenderComplete;
            CheckReach();
        }
    }
    void CheckReach()
    {
        if (!Left.Grab && !Right.Grab && !Left.CreatureGrip && !Right.CreatureGrip) return;
        var currentRig = vrik();
        if (!currentRig || !((Behaviour)currentRig).enabled) { Release(Left); Release(Right); return; }
        if (rig != currentRig) CacheRig(currentRig);
        // Observe after the actual render, including native rig callbacks.
        // If a struck creature or an
        // obstructed object leaves the avatar's reach, let go rather than
        // displaying a grip floating beyond the actual rendered fingers.
        CheckReach(Left,leftWrist,leftTarget);
        CheckReach(Right,rightWrist,rightTarget);
    }
    void CacheRig(Component currentRig)
    {
        var data = Traverse.Create(currentRig);
        leftWrist = data.Field("references").Field("leftHand").GetValue<Transform>();
        rightWrist = data.Field("references").Field("rightHand").GetValue<Transform>();
        leftTarget = data.Field("solver").Field("leftArm").Field("target").GetValue<Transform>();
        rightTarget = data.Field("solver").Field("rightArm").Field("target").GetValue<Transform>();
        // Retry an avatar that is still being constructed; do not retain a
        // partially initialized skeleton as the ready rig.
        if (leftWrist && rightWrist && leftTarget && rightTarget && Left.Gesture && Right.Gesture)
        {
            if (rig != currentRig)
                HandRig = new OpenXRHandRig(player, currentRig, Left, Right, leftWrist, rightWrist, leftTarget, rightTarget);
            rig = currentRig;
        }
    }
    void CheckReach(HandState state, Transform wrist, Transform target)
    {
        if ((!state.Grab && !state.CreatureGrip) || !wrist || !target) return;
        float error = Vector3.Distance(wrist.position,target.position);
        if (error <= .12f) return;
        OpenXRPlugin.Log.LogInfo($"OpenXR grip released outside avatar reach: {state.Source}, gap={error:F3} m, creature={(bool)state.CreatureGrip}.");
        Release(state);
    }
    void BindGestures()
    {
        if ((!Left.Hand && !Right.Hand) || (Left.Gesture && Right.Gesture)) return;
        foreach (var gesture in player.GetComponentsInChildren(gestureType, true))
        {
            var source = gestureSource.Invoke(gesture, null) as Hand;
            if (!source) continue;
            var state = source == Left.Hand ? Left : source == Right.Hand ? Right : null;
            if (state == null || state.Gesture) continue;
            state.Gesture = gesture;
            state.HandFree = AccessTools.MethodDelegate<Func<bool>>(gestureFree, gesture);
        }
    }
    bool CanGrab(HandState state)
    {
        if (!state.Gesture) BindGestures();
        return state.Gesture && state.HandFree();
    }
    void UpdateGrip(HandState state)
    {
        bool down = SteamVR_Actions.valheim_Grab.GetState(state.Source);
        if (!state.Active || !down) { Release(state); state.Grip = down; return; }
        if (state.Grab || state.CreatureGrip)
        {
            if (state.Solver.Count > 1 || twoHanded() || !CanGrab(state)) Release(state);
            state.Grip = down; return;
        }
        if (!state.Grip && state.Solver.Count == 1 && !twoHanded() && CanGrab(state))
        {
            Vector3 palm = state.PhysicalPalm;
            int count = Physics.OverlapSphereNonAlloc(palm, .1f, Nearby, grabMask, QueryTriggerInteraction.Ignore);
            float nearest = .1f;
            Character creature = null;
            if (count < Nearby.Length)
                for (int i = 0; i < count; i++)
                {
                    var collider = Nearby[i];
                    var item = collider.GetComponentInParent<ItemDrop>();
                    var actor = collider.GetComponentInParent<Character>();
                    var body = actor ? actor.GetComponent<Rigidbody>() : collider.attachedRigidbody;
                    if (!body || body.isKinematic) continue;
                    if (collider is MeshCollider mesh && !mesh.convex) continue;
                    if (item && (item.IsPiece() || item.InTar() || item.GetComponent<OpenXRPhysicalGrab>())) continue;
                    if (!item && (!actor || actor.IsPlayer() || actor.IsDead() || actor.GetComponent<OpenXRCreatureGrip>())) continue;
                    var point = collider.ClosestPoint(palm);
                    float distance = Vector3.Distance(palm, point);
                    if (distance > nearest) continue;
                    if (!Reachable(palm, point, body)) continue;
                    nearest = distance; state.Pending = item; state.PendingCollider = collider; creature = actor;
                }
            if (creature)
            {
                state.Pending = null;
                if (!OpenXRCreatureGrip.CanRestrain(player, creature)) OpenXRCreatureGrip.Repel(player, creature, state);
                else
                {
                    var view = creature.GetComponent<ZNetView>();
                    // Never take authority from another client's live AI.
                    if (!view || !view.IsValid() || view.IsOwner())
                    {
                        state.CreatureGrip = creature.gameObject.AddComponent<OpenXRCreatureGrip>();
                        state.CreatureGrip.Begin(this, state, creature, state.PendingCollider.ClosestPoint(palm));
                        state.Hand.hapticAction.Execute(0, .05f, 100, .3f, state.Source);
                    }
                }
            }
            state.PendingUntil = Time.unscaledTime + .75f;
        }
        state.Grip = down;
        if (!state.Pending) return;
        if (Time.unscaledTime > state.PendingUntil || !state.PendingCollider
            || Vector3.Distance(state.PendingCollider.ClosestPoint(state.PhysicalPalm), state.PhysicalPalm) > .1f
            || !Reachable(state.PhysicalPalm, state.PendingCollider.ClosestPoint(state.PhysicalPalm), state.PendingCollider.attachedRigidbody))
        { state.Pending = null; return; }
        if (!state.Pending.CanPickup(false)) { state.Pending.RequestOwn(); return; }
        var grabbed = state.Pending;
        state.Pending = null;
        state.Grab = grabbed.gameObject.AddComponent<OpenXRPhysicalGrab>();
        state.Grab.Begin(this, state, grabbed, state.PendingCollider.ClosestPoint(state.PhysicalPalm));
        state.Hand.hapticAction.Execute(0, .05f, 100, .25f, state.Source);
    }
    bool Reachable(Vector3 palm, Vector3 point, Rigidbody target)
    {
        var ray = point - palm;
        if (ray.sqrMagnitude < .000001f) return true;
        int count = Physics.RaycastNonAlloc(palm, ray.normalized, SightHits, ray.magnitude, mask, QueryTriggerInteraction.Ignore);
        if (count == SightHits.Length) return false;
        for (int i = 0; i < count; i++)
            if (SightHits[i].rigidbody != target && !SightHits[i].transform.IsChildOf(target.transform)
                && !SightHits[i].transform.IsChildOf(player.transform)) return false;
        return true;
    }
    internal void Release(HandState state)
    {
        state.Pending = null;
        if (state.Grab) state.Grab.Release();
        state.Grab = null;
        if (state.CreatureGrip) state.CreatureGrip.Release();
        state.CreatureGrip = null;
    }
    void ResetHands()
    {
        Release(Left); Release(Right);
        Left.Active = Right.Active = false;
        Left.Impact.Reset(); Right.Impact.Reset(); equipmentImpact.Reset();
        if (Left.Solver != null) Left.Solver.Valid = false;
        if (Right.Solver != null) Right.Solver.Valid = false;
    }
    static void ProtectHeldItem(ItemDrop __instance, ref bool __result)
    {
        if (__result && OpenXRPhysicalGrab.ProtectedItems.Count != 0
            && __instance.TryGetComponent<OpenXRPhysicalGrab>(out var grab) && grab.Protected) __result = false;
    }
    static bool FreeFist(bool ___isRightHand)
    {
        if (!Current) return true;
        var state = ___isRightHand ? Current.Right : Current.Left;
        return !state.Grab && !state.CreatureGrip;
    }
    static void ReleaseWorldGrip(Component __instance, bool ___isRightHand, Vector3 ___lastGrabbedPoint, ref bool __result)
    {
        var current = Current;
        if (!__result || !current) return;
        var state = ___isRightHand ? current.Right : current.Left;
        bool release = !state.Active || state.Solver.Yielding
            || SteamVR_Actions.valheim_Walk.GetAxis(SteamVR_Input_Sources.Any).sqrMagnitude > .04f;
        if (!release && state.Hand)
        {
            var offset = state.Hand.transform.InverseTransformPoint(__instance.transform.position);
            var trackedFist = state.Tracked.position + state.Tracked.rotation * offset;
            release = (trackedFist - ___lastGrabbedPoint).sqrMagnitude > HeldTrackingGap * HeldTrackingGap;
        }
        if (!release) return;
        // Clear the native latch too, so it cannot keep blocking punches or
        // turn a contact-recovery jump into gestured locomotion.
        nativeGrabType.SetValue(__instance, nativeGrabNone);
        __result = false;
    }
    static bool AllowOwnershipTransfer(ItemDrop __instance) => OpenXRPhysicalGrab.ProtectedItems.Count == 0
        || !__instance.TryGetComponent<OpenXRPhysicalGrab>(out var grab) || !grab.Protected;
    static bool AllowStack(ItemDrop __instance)
    {
        if (!AllowOwnershipTransfer(__instance)) return false;
        // Native stacking can delete a neighbouring drop without CanPickup.
        // Do not let another stack absorb either protected hand-held item.
        foreach (var protectedItem in OpenXRPhysicalGrab.ProtectedItems)
            if (Absorbs(__instance, protectedItem)) return false;
        return true;
    }
    static bool Absorbs(ItemDrop item, OpenXRPhysicalGrab grab) => grab && grab.Protected
        && grab.Item && item.m_itemData.m_shared.m_name == grab.Item.m_itemData.m_shared.m_name
        && (item.transform.position - grab.transform.position).sqrMagnitude < 25;
    void OnDisable() { StopAllCoroutines(); ResetHands(); }
    void OnDestroy()
    {
        ResetHands(); Left.Solver?.Dispose(); Right.Solver?.Dispose();
        if (Current == this) Current = null;
    }
}
