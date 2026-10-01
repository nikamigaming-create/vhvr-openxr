using ValheimVRMod.VRCore.Backends;
using UnityEngine;
using System.Collections.Generic;

namespace Nikami.OpenXR;

// A loose native item stays a native, networked rigid body throughout the grip.
// Nothing is cloned, added to inventory, reparented, or made kinematic.
internal sealed class OpenXRPhysicalGrab : MonoBehaviour
{
    internal static readonly List<OpenXRPhysicalGrab> ProtectedItems = new();
    internal Rigidbody Body { get; private set; }
    internal ItemDrop Item => item;
    internal bool Protected => held || Time.unscaledTime < releaseAt + 1;
    internal Vector3 GripPoint => transform.TransformPoint(anchor);
    internal Quaternion HandRotation => transform.rotation * Quaternion.Inverse(relativeRotation);
    Vector3 PhysicsGripPoint => Body.position + Body.rotation * Vector3.Scale(anchor, transform.lossyScale);
    OpenXRPhysicalHands owner;
    OpenXRPhysicalHands.HandState hand;
    ItemDrop item;
    ZNetView view;
    Vector3 anchor, previousTarget;
    Quaternion relativeRotation;
    bool held, autoPickup;
    float releaseAt;
    CollisionDetectionMode collisionMode;
    RigidbodyInterpolation interpolation;
    int iterations;

    internal void Begin(OpenXRPhysicalHands owner, OpenXRPhysicalHands.HandState hand, ItemDrop item, Vector3 point)
    {
        this.owner = owner; this.hand = hand; this.item = item;
        Body = item.GetComponent<Rigidbody>(); view = item.GetComponent<ZNetView>();
        if (!Body || Body.isKinematic) { Destroy(this); return; }
        // Keep the palm/body relationship at acquisition. Snapping the palm
        // to the nearest surface used to jump the wrist by the grab radius
        // and could immediately trip the avatar reach limit.
        anchor = transform.InverseTransformPoint(hand.PhysicalPalm);
        relativeRotation = Quaternion.Inverse(hand.Solver.Pose.rotation) * Body.rotation;
        previousTarget = hand.Tracked.position + hand.Tracked.rotation * hand.Palm;
        autoPickup = item.m_autoPickup; item.m_autoPickup = false;
        collisionMode = Body.collisionDetectionMode; iterations = Body.solverIterations;
        interpolation = Body.interpolation;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.solverIterations = Mathf.Max(iterations, 12);
        held = true; Body.WakeUp();
        ProtectedItems.Add(this);
    }
    void FixedUpdate()
    {
        if (!held) return;
        if (!owner || !hand.Active || !Body || Body.isKinematic || !item
            || (view && view.IsValid() && !view.IsOwner())) { Release(); return; }
        var target = hand.Tracked.position + hand.Tracked.rotation * hand.Palm;
        var gripPoint = PhysicsGripPoint;
        var error = target - gripPoint;
        if (error.sqrMagnitude > OpenXRPhysicalHands.HeldTrackingGap * OpenXRPhysicalHands.HeldTrackingGap) { owner.Release(hand); return; }
        float dt = Time.fixedDeltaTime;
        var velocity = Vector3.ClampMagnitude((target - previousTarget) / dt, 10);
        previousTarget = target;
        // Critically damped spring with a finite force budget. Heavy items lag
        // under acceleration; walls can resist the grip instead of exploding.
        var acceleration = error * 225 + (velocity - Body.GetPointVelocity(gripPoint)) * 30;
        var force = Vector3.ClampMagnitude(acceleration * Body.mass, 180);
        Body.AddForceAtPosition(force, gripPoint, ForceMode.Force);
        if (Body.useGravity) Body.AddForce(-Physics.gravity, ForceMode.Acceleration);
        var targetRotation = hand.Tracked.rotation * relativeRotation;
        var delta = targetRotation * Quaternion.Inverse(Body.rotation);
        if (delta.w < 0) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
        delta.ToAngleAxis(out float angle, out var axis);
        if (angle > 180) angle -= 360;
        if (axis.sqrMagnitude > .0001f && !float.IsNaN(axis.x))
            Body.AddTorque(Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad * 100) - Body.angularVelocity * 20, 100), ForceMode.Acceleration);
    }
    internal void Release()
    {
        if (!held) return;
        held = false; releaseAt = Time.unscaledTime;
        if (hand != null && hand.Grab == this) hand.Grab = null;
        if (Body)
        {
            // Preserve the body's measured momentum, including impacts. Raw
            // controller velocity would launch a body trapped against a wall.
            Body.linearVelocity = Vector3.ClampMagnitude(Body.linearVelocity, 12);
            Body.angularVelocity = Vector3.ClampMagnitude(Body.angularVelocity, 20);
            // Keep CCD through the short release grace so the first throw
            // steps cannot immediately tunnel after losing the grip.
        }
    }
    void RestoreBody()
    {
        if (!Body) return;
        Body.collisionDetectionMode = collisionMode;
        Body.interpolation = interpolation;
        Body.solverIterations = iterations;
    }
    void Update()
    {
        if (!held && !Protected) Destroy(this);
    }
    void OnDisable() { Release(); Destroy(this); }
    void OnDestroy()
    {
        ProtectedItems.Remove(this);
        RestoreBody();
        if (item) item.m_autoPickup = autoPickup;
        if (hand != null && hand.Grab == this) hand.Grab = null;
    }
}
