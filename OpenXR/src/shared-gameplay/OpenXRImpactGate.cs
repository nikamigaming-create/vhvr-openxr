using ValheimVRMod.VRCore.Backends;
using UnityEngine;

namespace Nikami.OpenXR;

// One impact per contact episode. Time alone never re-arms a pressed or
// rubbing contact. This is shared by both hands for equipment/equipment hits.
internal sealed class OpenXRImpactGate
{
    internal const float ClinkSpeed = 1.4f; // normal relative metres/second
    const float Separation = .04f;
    readonly float cooldown;
    OpenXRContactSolver owner, peer;
    Transform surface;
    Vector3 point, normal;
    int shape;
    float nextImpact, separatedAt = -1;
    bool latched;

    internal OpenXRImpactGate(float cooldown = .16f) { this.cooldown = cooldown; }

    internal void Reset()
    {
        owner = peer = null; surface = null;
        // Focus loss, a changed shape or tracking recovery may clear a contact
        // episode, but must not bypass the time between audible impacts.
        latched = false; separatedAt = -1;
    }

    internal void Update(float now)
    {
        if (!latched) return;
        if (owner == null || !owner.Valid || shape >= owner.Count || (peer != null ? !peer.Valid : !surface))
        { Reset(); return; }
        var frame = peer != null ? peer.Pose : surface ? new Pose(surface.position, surface.rotation) : new Pose(Vector3.zero, Quaternion.identity);
        var n = frame.rotation * normal;
        var p = frame.position + frame.rotation * point;
        var volume = owner.Shapes[shape];
        var centre = owner.Pose.position + owner.Pose.rotation * volume.Center;
        float gap = Vector3.Dot(centre - p, n) - OpenXRContactSolver.Support(owner.Pose.rotation * volume.Rotation, volume.Half, n);
        if (gap < Separation) { separatedAt = -1; return; }
        if (separatedAt < 0) separatedAt = now;
        if (now - separatedAt >= .12f) latched = false;
    }

    internal bool Begin(float now, OpenXRContactSolver solver, OpenXRContactSolver other, OpenXRContactSolver.Contact contact)
    {
        Update(now);
        if (latched || now < nextImpact) return false;
        owner = solver; peer = contact.Peer ? other : null;
        surface = contact.Collider ? contact.Collider.transform : null;
        shape = contact.Shape;
        var frame = peer != null ? peer.Pose : surface ? new Pose(surface.position, surface.rotation) : new Pose(Vector3.zero, Quaternion.identity);
        // Anchor at the accepted, nonpenetrating contact pose. Keeping the
        // plane in the other body's frame also works when it moves/rotates.
        var volume = owner.Shapes[shape];
        var centre = owner.Pose.position + owner.Pose.rotation * volume.Center;
        var p = centre - contact.Normal * OpenXRContactSolver.Support(owner.Pose.rotation * volume.Rotation, volume.Half, contact.Normal);
        point = Quaternion.Inverse(frame.rotation) * (p - frame.position);
        normal = Quaternion.Inverse(frame.rotation) * contact.Normal;
        latched = true; separatedAt = -1; nextImpact = now + cooldown;
        return true;
    }

    internal static float ClosingSpeed(Vector3 velocity, Vector3 otherVelocity, Vector3 normal) =>
        Mathf.Max(0, -Vector3.Dot(velocity - otherVelocity, normal));
}
