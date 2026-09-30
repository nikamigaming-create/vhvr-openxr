using UnityEngine;

namespace Nikami.OpenXR;

// World metres, Unity left-handed transforms. Shapes are expressed in the
// controller grip frame, not the animated avatar or the OpenXR reference space.
// This is a kinematic contact solve: it never applies a force to the player.
internal sealed class OpenXRContactSolver
{
    internal struct Shape
    {
        internal Vector3 Center, Half;
        internal Quaternion Rotation;
        internal MeshCollider Hull;
        internal Shape(Vector3 center, Vector3 half, Quaternion rotation)
        { Center = center; Half = half; Rotation = rotation; Hull = null; }
    }
    internal struct Contact
    {
        internal bool Hit, Peer;
        internal Vector3 Point, Normal;
        internal Collider Collider;
        internal int Shape, PeerShape;
    }
    struct PeerShape
    {
        internal Vector3 Center, Half, Extents;
        internal Quaternion Rotation;
        internal float Radius;
        internal MeshCollider Hull;
    }
    internal const float Skin = .0015f;
    internal const float MaxTrackingGap = .08f, MaxTrackingAngle = 20;
    internal bool Yielding { get; private set; }
    internal int Yields;
    readonly RaycastHit[] hits = new RaycastHit[64];
    readonly Collider[] overlaps = new Collider[64];
    readonly BoxCollider probe, peerProbe;
    readonly int mask;
    readonly Transform owner;
    readonly PeerShape[] peerShapes = new PeerShape[9];
    readonly float[] halfRadii = new float[9];
    Vector3 probeHalf = Vector3.one * .5f, peerProbeHalf = Vector3.one * .5f;
    Vector3 peerPosition;
    Vector3 localCenter, localExtents, peerCenter, peerExtents;
    int peerCount;
    float combinedRadius;
    internal readonly Shape[] Shapes = new Shape[9];
    internal int Count;
    internal bool Valid;
    internal Pose Pose;
    internal Rigidbody IgnoredBody;
    internal Contact LastContact;
    internal int Saturations;

    internal OpenXRContactSolver(Transform owner, int mask)
    {
        this.owner = owner; this.mask = mask;
        probe = MakeProbe(); peerProbe = MakeProbe();
    }
    static BoxCollider MakeProbe()
    {
        var go = new GameObject("Nikami contact query") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
        go.transform.position = new Vector3(0, -10000, 0);
        var box = go.AddComponent<BoxCollider>();
        // PhysX needs a live shape when its dimensions change. Disabled boxes
        // silently made ComputePenetration miss rotating weapons. These query
        // shapes are triggers off-world on Ignore Raycast, with no rigid body.
        box.isTrigger = true;
        return box;
    }
    internal void Dispose()
    {
        if (probe) Object.Destroy(probe.gameObject);
        if (peerProbe) Object.Destroy(peerProbe.gameObject);
    }
    bool Solid(Collider collider) => collider && !collider.isTrigger
        && (!owner || !collider.transform.IsChildOf(owner))
        && (!IgnoredBody || collider.attachedRigidbody != IgnoredBody);

    // Solid contact has no timeout: resting, pressing and sliding stay physical.
    // Only excessive tracking separation breaks the constraint. Rejoin solid
    // contact as soon as the volume clears, without a timed collision-free grace.
    // The player's capsule and native world collision are unaffected.
    internal Pose FollowTracked(Pose desired, OpenXRContactSolver peer, float now, bool reset = false)
    {
        if (reset || !Valid) Yielding = false;
        if (Yielding)
        {
            PrepareQuery(peer, out _, out _);
            Pose = desired; Valid = true; LastContact = default;
            if (!Overlap(desired, out _, out _, Skin)) Yielding = false;
            return Pose;
        }
        Solve(desired, peer, reset);
        float gap = Vector3.Distance(Pose.position, desired.position);
        float angle = Quaternion.Angle(Pose.rotation, desired.rotation);
        if (gap > MaxTrackingGap || angle > MaxTrackingAngle)
        {
            // A withdrawal can end completely outside the blocked geometry.
            // Resume collision there immediately, including this first escape.
            Yielding = Overlap(desired, out _, out _, Skin); Yields++;
            Pose = desired;
        }
        return Pose;
    }

    void PrepareQuery(OpenXRContactSolver peer, out float radius, out float spacing)
    {
        radius = .1f; spacing = .025f;
        Vector3 minimum = Vector3.zero, maximum = Vector3.zero;
        for (int i = 0; i < Count; i++)
        {
            var shape = Shapes[i];
            halfRadii[i] = shape.Half.magnitude;
            radius = Mathf.Max(radius, shape.Center.magnitude + halfRadii[i]);
            spacing = Mathf.Min(spacing, Mathf.Max(.002f, Mathf.Min(shape.Half.x, Mathf.Min(shape.Half.y, shape.Half.z))));
            var extents = WorldExtents(shape.Rotation, shape.Half);
            minimum = Vector3.Min(minimum, shape.Center - extents);
            maximum = Vector3.Max(maximum, shape.Center + extents);
        }
        localCenter = (minimum + maximum) * .5f;
        localExtents = (maximum - minimum) * .5f;
        PreparePeer(peer, radius);
    }

    internal Pose Solve(Pose desired, OpenXRContactSolver peer, bool reset = false)
    {
        LastContact = default;
        PrepareQuery(peer, out float radius, out float spacing);
        if (!Valid || reset)
        {
            Pose = desired; Valid = true;
            // Reacquisition may begin inside a surface. Bounded depenetration
            // avoids keeping an invisible or permanently stuck hand there.
            for (int i = 0; i < 6 && Overlap(Pose, out var contact, out float depth); i++)
            {
                Pose.position += contact.Normal * Mathf.Min(depth + Skin, .15f);
                LastContact = contact;
            }
            return Pose;
        }
        // Doors, platforms and other actors can move into a stationary hand.
        // Resolve that changed world before sweeping the next controller pose.
        for (int i = 0; i < 6 && Overlap(Pose, out var movingContact, out float movingDepth); i++)
        {
            Pose.position += movingContact.Normal * Mathf.Min(movingDepth + Skin, .15f);
            LastContact = movingContact;
        }
        var start = Pose;
        // We already checked the changed world above. An unchanged target
        // needs no second identical overlap query within this same call.
        if (start.position.Equals(desired.position) && start.rotation.Equals(desired.rotation)) return Pose;
        float angle = Quaternion.Angle(start.rotation, desired.rotation);
        // Rotation also sweeps the tip, even when the grip does not translate.
        float travel = Vector3.Distance(start.position, desired.position) + angle * Mathf.Deg2Rad * radius;
        // Bound work by limiting this frame's motion, never by increasing the
        // gap between rotation samples. Extreme swings then catch up safely
        // over subsequent frames instead of skipping thin geometry.
        if (travel > spacing * 48)
        {
            float fraction = spacing * 48 / travel;
            desired = new Pose(Vector3.Lerp(start.position, desired.position, fraction),
                Quaternion.Slerp(start.rotation, desired.rotation, fraction));
            travel = spacing * 48;
        }
        int steps = Mathf.Clamp(Mathf.CeilToInt(travel / spacing), 1, 48);
        for (int step = 1; step <= steps; step++)
        {
            var next = new Pose(Vector3.Lerp(start.position, desired.position, (float)step / steps),
                Quaternion.Slerp(start.rotation, desired.rotation, (float)step / steps));
            // Translation and rotation are separate constraints. A blade that
            // cannot rotate into a wall must still be able to slide or withdraw.
            bool blocked = Move(next.position - Pose.position);
            next.position = Pose.position;
            if (!next.rotation.Equals(Pose.rotation) && Overlap(next, out var contact, out _, Skin))
            {
                LastContact = contact;
                blocked = true;
                float low = 0, high = 1;
                for (int i = 0; i < 7; i++)
                {
                    float mid = (low + high) * .5f;
                    var candidate = new Pose(Pose.position, Quaternion.Slerp(Pose.rotation, next.rotation, mid));
                    if (Overlap(candidate, out _, out _, Skin)) high = mid; else low = mid;
                }
                Pose.rotation = Quaternion.Slerp(Pose.rotation, next.rotation, low);
            }
            else Pose.rotation = next.rotation;
            if (blocked)
            {
                // Bounded frictionless sliding also handles another hand and
                // a second plane at an edge/corner. Never snap through a wall.
                for (int plane = 0; plane < 3; plane++)
                {
                    var slide = desired.position - Pose.position;
                    float into = Vector3.Dot(slide, LastContact.Normal);
                    if (into < 0) slide -= LastContact.Normal * into;
                    if (slide.sqrMagnitude < .000001f) break;
                    var previous = Pose.position;
                    if (!Move(slide) || (Pose.position - previous).sqrMagnitude < .0000001f) break;
                }
                break;
            }
        }
        return Pose;
    }

    bool Move(Vector3 delta)
    {
        if (delta.sqrMagnitude < .00000001f) return false;
        var start = Pose.position;
        var next = new Pose(start + delta, Pose.rotation);
        bool blocked = Sweep(Pose, delta, out float distance, out var swept);
        if (blocked)
        {
            next.position = start + delta.normalized * Mathf.Max(0, distance - Skin);
            LastContact = swept;
        }
        if (Overlap(next, out var contact, out _))
        {
            LastContact = contact;
            blocked = true;
            float low = 0, high = 1;
            for (int i = 0; i < 7; i++)
            {
                float mid = (low + high) * .5f;
                var candidate = new Pose(Vector3.Lerp(start, next.position, mid), Pose.rotation);
                if (Overlap(candidate, out _, out _)) high = mid; else low = mid;
            }
            next.position = Vector3.Lerp(start, next.position, low);
        }
        Pose.position = next.position;
        return blocked;
    }

    internal static float Support(Quaternion rotation, Vector3 half, Vector3 normal)
    {
        var local = Quaternion.Inverse(rotation) * normal;
        return Mathf.Abs(local.x) * half.x + Mathf.Abs(local.y) * half.y + Mathf.Abs(local.z) * half.z;
    }

    void PreparePeer(OpenXRContactSolver peer, float radius)
    {
        peerCount = peer != null && peer.Valid && !peer.Yielding ? peer.Count : 0;
        if (peerCount == 0) return;
        peerPosition = peer.Pose.position;
        float otherRadius = 0;
        Vector3 minimum = peerPosition, maximum = peerPosition;
        // The other solver cannot change during this synchronous Solve call.
        // Reuse this snapshot across its bounded motion and refinement steps.
        for (int i = 0; i < peerCount; i++)
        {
            var shape = peer.Shapes[i];
            float halfRadius = shape.Half.magnitude;
            var rotation = peer.Pose.rotation * shape.Rotation;
            peerShapes[i] = new PeerShape {
                Center = peerPosition + peer.Pose.rotation * shape.Center,
                Half = shape.Half, Rotation = rotation, Radius = halfRadius,
                Hull = shape.Hull,
                Extents = WorldExtents(rotation, shape.Half)
            };
            minimum = Vector3.Min(minimum, peerShapes[i].Center - peerShapes[i].Extents);
            maximum = Vector3.Max(maximum, peerShapes[i].Center + peerShapes[i].Extents);
            otherRadius = Mathf.Max(otherRadius, shape.Center.magnitude + halfRadius);
        }
        combinedRadius = radius + otherRadius + Skin;
        peerCenter = (minimum + maximum) * .5f;
        peerExtents = (maximum - minimum) * .5f;
    }
    static Vector3 WorldExtents(Quaternion rotation, Vector3 half)
    {
        var x = rotation * new Vector3(half.x, 0, 0);
        var y = rotation * new Vector3(0, half.y, 0);
        var z = rotation * new Vector3(0, 0, half.z);
        return new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
    }
    static void SetProbeHalf(BoxCollider collider, Vector3 half, ref Vector3 previous)
    {
        if (half.Equals(previous)) return;
        collider.size = half * 2;
        previous = half;
    }

    bool Sweep(Pose pose, Vector3 delta, out float distance, out Contact contact)
    {
        distance = delta.magnitude; contact = default;
        if (distance < .00001f) return false;
        float length = distance;
        var direction = delta / length;
        for (int shapeIndex = 0; shapeIndex < Count; shapeIndex++)
        {
            var shape = Shapes[shapeIndex];
            int count = Physics.BoxCastNonAlloc(pose.position + pose.rotation * shape.Center,
                shape.Half, direction, hits, pose.rotation * shape.Rotation,
                length + Skin, mask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length)
            {
                Saturations++; distance = 0;
                contact = new Contact { Hit = true, Normal = -direction, Point = pose.position };
                return true;
            }
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!Solid(hit.collider) || hit.distance > distance || Vector3.Dot(hit.normal, delta) >= 0) continue;
                var hitNormal = hit.normal;
                var hitPoint = hit.point;
                if (hit.distance <= .0001f)
                {
                    // PhysX reports -castDirection for a cast beginning in
                    // contact. That is not a surface normal and makes a
                    // tangential slide stick. Recover the actual MTD plane.
                    SetProbeHalf(probe, shape.Half + Vector3.one * Skin, ref probeHalf);
                    var frame = hit.collider.transform;
                    if (Physics.ComputePenetration(probe, pose.position + pose.rotation * shape.Center, pose.rotation * shape.Rotation,
                        hit.collider, frame.position, frame.rotation, out var normal, out float depth))
                    {
                        if (Vector3.Dot(normal, delta) >= -.000001f) continue;
                        hitNormal = normal;
                        // An initial-overlap cast can also return a zero point.
                        // Derive the real surface point before angular velocity,
                        // haptics or a body impulse use its lever arm.
                        var castCentre = pose.position + pose.rotation * shape.Center;
                        hitPoint = castCentre - normal * (Support(pose.rotation * shape.Rotation, shape.Half + Vector3.one * Skin,normal) - depth);
                    }
                }
                distance = hit.distance;
                contact = new Contact { Hit = true, Collider = hit.collider, Point = hitPoint, Normal = hitNormal, Shape = shapeIndex };
            }
            // A slide can span more than one rotation sample. Sweep against
            // the other hand too, rather than only testing its end position.
            var centre = pose.position + pose.rotation * shape.Center;
            var rotation = pose.rotation * shape.Rotation;
            for (int i = 0; i < peerCount; i++)
            {
                var other = peerShapes[i];
                var offset = other.Center - centre;
                float along = Mathf.Clamp(Vector3.Dot(offset, direction), 0, distance);
                float reach = halfRadii[shapeIndex] + other.Radius + Skin;
                if ((offset - direction * along).sqrMagnitude > reach * reach) continue;
                if (!CastBoxes(centre, rotation, shape.Half, other, delta, out float fraction, out float exit, out var normal)) continue;
                if ((shape.Hull || other.Hull) && !CastHulls(shape, centre, rotation, other, delta, fraction, exit, out fraction, out normal)) continue;
                float d = fraction * length;
                if (d > distance) continue;
                distance = d;
                var point = shape.Hull ? Physics.ClosestPoint(other.Center, shape.Hull, centre + direction * d, rotation)
                    : centre + direction * d - normal * Support(rotation, shape.Half, normal);
                contact = new Contact { Hit = true, Peer = true, Point = point, Normal = normal, Shape = shapeIndex, PeerShape = i };
            }
        }
        return contact.Hit;
    }

    static bool CastBoxes(Vector3 centre, Quaternion rotation, Vector3 half, PeerShape other,
        Vector3 delta, out float entry, out float exit, out Vector3 normal)
    {
        entry = 0; exit = 1; normal = Vector3.zero;
        var x = rotation * Vector3.right; var y = rotation * Vector3.up; var z = rotation * Vector3.forward;
        var ox = other.Rotation * Vector3.right; var oy = other.Rotation * Vector3.up; var oz = other.Rotation * Vector3.forward;
        var offset = centre - other.Center;
        for (int axisIndex = 0; axisIndex < 15; axisIndex++)
        {
            Vector3 axis;
            if (axisIndex < 3) axis = axisIndex == 0 ? x : axisIndex == 1 ? y : z;
            else if (axisIndex < 6) axis = axisIndex == 3 ? ox : axisIndex == 4 ? oy : oz;
            else
            {
                int a = (axisIndex - 6) / 3, b = (axisIndex - 6) % 3;
                axis = Vector3.Cross(a == 0 ? x : a == 1 ? y : z, b == 0 ? ox : b == 1 ? oy : oz);
                if (axis.sqrMagnitude < .000001f) continue;
                axis.Normalize();
            }
            float radius = Mathf.Abs(Vector3.Dot(axis, x)) * half.x + Mathf.Abs(Vector3.Dot(axis, y)) * half.y + Mathf.Abs(Vector3.Dot(axis, z)) * half.z
                + Mathf.Abs(Vector3.Dot(axis, ox)) * other.Half.x + Mathf.Abs(Vector3.Dot(axis, oy)) * other.Half.y + Mathf.Abs(Vector3.Dot(axis, oz)) * other.Half.z;
            float start = Vector3.Dot(offset, axis), speed = Vector3.Dot(delta, axis);
            if (Mathf.Abs(speed) < .000001f) { if (Mathf.Abs(start) > radius) return false; continue; }
            float enter = (-radius - start) / speed, leave = (radius - start) / speed;
            if (enter > leave) { float swap = enter; enter = leave; leave = swap; }
            if (enter >= entry) { entry = enter; normal = speed > 0 ? -axis : axis; }
            exit = Mathf.Min(exit, leave);
            if (entry > exit) return false;
        }
        return exit >= 0 && entry <= 1 && normal.sqrMagnitude > .5f && Vector3.Dot(delta, normal) < 0;
    }

    bool PenetratesPeer(Shape shape, Vector3 centre, Quaternion rotation, PeerShape other, out Vector3 normal, out float depth)
    {
        if (!shape.Hull) SetProbeHalf(probe, shape.Half, ref probeHalf);
        if (!other.Hull) SetProbeHalf(peerProbe, other.Half, ref peerProbeHalf);
        return Physics.ComputePenetration(shape.Hull ? (Collider)shape.Hull : probe, centre, rotation,
            other.Hull ? (Collider)other.Hull : peerProbe, other.Center, other.Rotation, out normal, out depth);
    }
    bool CastHulls(Shape shape, Vector3 centre, Quaternion rotation, PeerShape other, Vector3 delta,
        float entry, float exit, out float fraction, out Vector3 normal)
    {
        fraction = 0; normal = Vector3.zero;
        entry = Mathf.Clamp01(entry); exit = Mathf.Clamp01(exit);
        int samples = Mathf.Clamp(Mathf.CeilToInt((exit - entry) * delta.magnitude / .002f), 1, 16);
        float previous = entry;
        for (int i = 0; i <= samples; i++)
        {
            float t = Mathf.Lerp(entry, exit, (float)i / samples);
            if (!PenetratesPeer(shape, centre + delta * t, rotation, other, out var hitNormal, out _)) { previous = t; continue; }
            float low = previous, high = t;
            for (int j = 0; j < 7; j++)
            {
                float mid = (low + high) * .5f;
                if (PenetratesPeer(shape, centre + delta * mid, rotation, other, out var n, out _)) { high = mid; hitNormal = n; }
                else low = mid;
            }
            fraction = high; normal = hitNormal;
            return Vector3.Dot(delta, normal) < 0;
        }
        return false;
    }

    bool Overlap(Pose pose, out Contact contact, out float depth, float margin = 0)
    {
        contact = default; depth = 0;
        bool checkPeer = peerCount > 0 && (pose.position - peerPosition).sqrMagnitude <= combinedRadius * combinedRadius;
        if (checkPeer)
        {
            var delta = pose.position + pose.rotation * localCenter - peerCenter;
            var total = WorldExtents(pose.rotation, localExtents) + peerExtents + Vector3.one * Skin;
            checkPeer = Mathf.Abs(delta.x) <= total.x && Mathf.Abs(delta.y) <= total.y && Mathf.Abs(delta.z) <= total.z;
        }
        for (int shapeIndex = 0; shapeIndex < Count; shapeIndex++)
        {
            var shape = Shapes[shapeIndex];
            shape.Half += Vector3.one * margin;
            var center = pose.position + pose.rotation * shape.Center;
            var rotation = pose.rotation * shape.Rotation;
            int count = Physics.OverlapBoxNonAlloc(center, shape.Half, overlaps, rotation, mask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length)
            {
                Saturations++;
                contact = new Contact { Hit = true, Point = center, Normal = Vector3.up };
                return true;
            }
            for (int i = 0; i < count; i++)
            {
                var collider = overlaps[i];
                if (!Solid(collider)) continue;
                SetProbeHalf(probe, shape.Half, ref probeHalf);
                var frame = collider.transform;
                if (Physics.ComputePenetration(probe, center, rotation, collider,
                    frame.position, frame.rotation, out var direction, out float amount) && amount > depth)
                {
                    depth = amount;
                    // ClosestPoint is unsupported on Valheim's non-convex
                    // terrain meshes and used to emit a warning per contact.
                    var point = center - direction * (Support(rotation, shape.Half, direction) - amount);
                    contact = new Contact { Hit = true, Collider = collider, Normal = direction, Point = point, Shape = shapeIndex };
                }
            }
            if (!checkPeer) continue;
            var extents = WorldExtents(rotation, shape.Half);
            for (int i = 0; i < peerCount; i++)
            {
                var other = peerShapes[i];
                var delta = center - other.Center;
                float reach = halfRadii[shapeIndex] + other.Radius + Skin;
                if (delta.sqrMagnitude > reach * reach) continue;
                var total = extents + other.Extents + Vector3.one * Skin;
                if (Mathf.Abs(delta.x) > total.x || Mathf.Abs(delta.y) > total.y || Mathf.Abs(delta.z) > total.z) continue;
                if (PenetratesPeer(shape, center, rotation, other, out var direction, out float amount) && amount > depth)
                {
                    depth = amount;
                    var point = shape.Hull ? Physics.ClosestPoint(other.Center, shape.Hull, center, rotation)
                        : center - direction * (Support(rotation, shape.Half, direction) - amount);
                    contact = new Contact { Hit = true, Peer = true, Normal = direction, Point = point, Shape = shapeIndex, PeerShape = i };
                }
            }
        }
        return contact.Hit;
    }
}
