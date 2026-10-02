using ValheimVRMod.VRCore.Backends;
using System.Collections.Generic;
using UnityEngine;

namespace Nikami.OpenXR;

// Geometry is sampled once per attachment, then composed with the native
// animated transforms. No mesh baking or hierarchy scan occurs in the solve.
[DefaultExecutionOrder(31010)]
internal sealed class OpenXRPhysicalEquipment : MonoBehaviour
{
    internal static readonly List<OpenXRPhysicalEquipment> Items = new();
    struct Part
    {
        internal Transform Frame;
        internal Bounds Bounds;
        internal VRInputSource Hand;
        internal MeshCollider Hull;
    }
    readonly List<Part> parts = new();
    static int frame = -1;

    internal static void Attach(GameObject item, VRInputSource hand, VisEquipment equipment)
    {
        if (!VRGameplay.Options.PhysicalContact) return;
        var component = item.GetComponent<OpenXRPhysicalEquipment>() ?? item.AddComponent<OpenXRPhysicalEquipment>();
        component.ClearParts();
        foreach (var mesh in item.GetComponentsInChildren<MeshFilter>(true))
            if (mesh.sharedMesh)
            {
                if (mesh.sharedMesh.isReadable)
                    component.AddSegments(mesh.transform, hand, mesh.sharedMesh.vertices, mesh.sharedMesh.triangles);
                else component.parts.Add(new Part { Frame = mesh.transform, Bounds = mesh.sharedMesh.bounds, Hand = hand });
            }
        foreach (var skin in item.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            component.AddSkin(skin, hand, equipment);
        if (!Items.Contains(component)) Items.Add(component);
    }

    void AddSkin(SkinnedMeshRenderer skin, VRInputSource hand, VisEquipment equipment)
    {
        if (!skin.sharedMesh) return;
        var baked = new Mesh();
        Mesh shifted = null;
        GameObject probe = null;
        var proxies = new List<GameObject>();
        try
        {
            // Compensate for the avatar's imported skeleton scale so these
            // vertices compose once with the renderer's local-to-world matrix.
            skin.BakeMesh(baked, true);
            var vertices = baked.vertices;
            var left = equipment.m_leftHand.parent;
            var right = equipment.m_rightHand.parent;
            Vector3[] moved = null;
            if (hand == VRInputSource.Any)
            {
                // Retail meshes can be unreadable. Two CPU skin evaluations
                // identify the left-hand vertices without reading bone weights
                // or moving any actual avatar bone, even with crossed hands.
                probe = new GameObject("Nikami skin contact query") { hideFlags = HideFlags.HideAndDontSave };
                probe.transform.SetParent(skin.transform.parent, false);
                probe.transform.localPosition = skin.transform.localPosition;
                probe.transform.localRotation = skin.transform.localRotation;
                probe.transform.localScale = skin.transform.localScale;
                var copy = probe.AddComponent<SkinnedMeshRenderer>();
                copy.enabled = false;
                copy.sharedMesh = skin.sharedMesh;
                copy.rootBone = skin.rootBone;
                var bones = skin.bones;
                for (int i = 0; i < bones.Length; i++)
                {
                    var bone = bones[i];
                    if (!bone || (bone != left && !bone.IsChildOf(left))) continue;
                    var shiftedBone = new GameObject("Nikami shifted bone") { hideFlags = HideFlags.HideAndDontSave };
                    shiftedBone.transform.SetPositionAndRotation(bone.position + Vector3.up, bone.rotation);
                    shiftedBone.transform.localScale = bone.lossyScale;
                    proxies.Add(shiftedBone);
                    bones[i] = shiftedBone.transform;
                }
                copy.bones = bones;
                shifted = new Mesh();
                copy.BakeMesh(shifted, true);
                moved = shifted.vertices;
            }
            int leftCount = 0, rightCount = 0;
            var leftVertices = new bool[vertices.Length];
            var skinToWorld = skin.transform.localToWorldMatrix;
            var skinToLeft = left.worldToLocalMatrix * skinToWorld;
            var skinToRight = right.worldToLocalMatrix * skinToWorld;
            for (int i = 0; i < vertices.Length; i++)
            {
                bool isLeft = hand == VRInputSource.LeftHand
                    || (hand == VRInputSource.Any && skinToWorld.MultiplyVector(moved[i] - vertices[i]).sqrMagnitude > .1f);
                Vector3 point = (isLeft ? skinToLeft : skinToRight).MultiplyPoint3x4(vertices[i]);
                leftVertices[i] = isLeft;
                vertices[i] = point;
                if (isLeft) leftCount++; else rightCount++;
            }
            var triangles = baked.triangles;
            var leftTriangles = new List<int>();
            var rightTriangles = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i+1], c = triangles[i+2];
                // A rigid held item has no triangle spanning both hands.
                if (leftVertices[a] != leftVertices[b] || leftVertices[a] != leftVertices[c]) continue;
                var destination = leftVertices[a] ? leftTriangles : rightTriangles;
                destination.Add(a); destination.Add(b); destination.Add(c);
            }
            if (leftCount > 0) AddSegments(left, VRInputSource.LeftHand, vertices, leftTriangles.ToArray(), hand == VRInputSource.Any);
            if (rightCount > 0) AddSegments(right, VRInputSource.RightHand, vertices, rightTriangles.ToArray(), hand == VRInputSource.Any);
        }
        finally
        {
            Destroy(baked);
            if (shifted) Destroy(shifted);
            if (probe) Destroy(probe);
            foreach (var proxy in proxies) Destroy(proxy);
        }
    }

    void AddSegments(Transform frame, VRInputSource hand, Vector3[] vertices, int[] triangles, bool exactPeer = false)
    {
        if (triangles.Length == 0) return;
        var total = new Bounds(vertices[triangles[0]],Vector3.zero);
        foreach (int index in triangles) total.Encapsulate(vertices[index]);
        int axis = total.size.x > total.size.y ? 0 : 1;
        if (total.size.z > total.size[axis]) axis = 2;
        // Separate shaft/blade/head widths. A single box around an entire axe
        // would make its thin handle collide at the axe head's full width.
        const int segments = 8;
        var input = new Vector3[8]; var output = new Vector3[8];
        for (int segment = 0; segment < segments; segment++)
        {
            float low = total.min[axis] + total.size[axis] * segment / segments;
            float high = total.min[axis] + total.size[axis] * (segment + 1) / segments;
            bool any = false; Bounds bounds = default;
            var hullVertices = exactPeer ? new List<Vector3>() : null;
            var hullTriangles = exactPeer ? new List<int>() : null;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                input[0] = vertices[triangles[i]]; input[1] = vertices[triangles[i+1]]; input[2] = vertices[triangles[i+2]];
                float minimum = Mathf.Min(input[0][axis], Mathf.Min(input[1][axis], input[2][axis]));
                float maximum = Mathf.Max(input[0][axis], Mathf.Max(input[1][axis], input[2][axis]));
                if (maximum < low || minimum > high) continue;
                int count = 3;
                if (minimum < low || maximum > high)
                {
                    count = Clip(input,3,output,axis,low,true);
                    count = Clip(output,count,input,axis,high,false);
                }
                for (int j = 0; j < count; j++)
                {
                    if (!any) { bounds = new Bounds(input[j],Vector3.zero); any = true; }
                    else bounds.Encapsulate(input[j]);
                }
                if (exactPeer && count >= 3)
                {
                    int first = hullVertices.Count;
                    for (int j = 0; j < count; j++) hullVertices.Add(input[j]);
                    for (int j = 1; j < count - 1; j++)
                    { hullTriangles.Add(first); hullTriangles.Add(first + j); hullTriangles.Add(first + j + 1); }
                }
            }
            if (any) parts.Add(new Part { Frame = frame, Bounds = bounds, Hand = hand,
                Hull = exactPeer ? MakeHull(bounds, hullVertices, hullTriangles) : null });
        }
    }
    static MeshCollider MakeHull(Bounds bounds, List<Vector3> vertices, List<int> triangles)
    {
        if (triangles.Count < 12 || Mathf.Min(bounds.size.x, Mathf.Min(bounds.size.y, bounds.size.z)) < .0001f) return null;
        // Cook each clipped, convex slice once on equip. Empty corners in a
        // blade's bounding box must not stop/clink against the other weapon.
        for (int i = 0; i < vertices.Count; i++) vertices[i] -= bounds.center;
        var mesh = new Mesh { name = "Nikami paired equipment contact" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
        var query = new GameObject("Nikami equipment query") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
        query.transform.position = new Vector3(0, -10000, 0);
        var hull = query.AddComponent<MeshCollider>();
        hull.convex = true; hull.sharedMesh = mesh; hull.isTrigger = true;
        return hull;
    }
    void ClearParts()
    {
        foreach (var part in parts)
            if (part.Hull) { Destroy(part.Hull.sharedMesh); Destroy(part.Hull.gameObject); }
        parts.Clear();
    }
    static int Clip(Vector3[] input, int count, Vector3[] output, int axis, float plane, bool above)
    {
        if (count == 0) return 0;
        int written = 0;
        var previous = input[count - 1];
        bool wasInside = above ? previous[axis] >= plane : previous[axis] <= plane;
        for (int i = 0; i < count; i++)
        {
            var point = input[i];
            bool inside = above ? point[axis] >= plane : point[axis] <= plane;
            if (inside != wasInside)
                output[written++] = Vector3.LerpUnclamped(previous,point,(plane - previous[axis]) / (point[axis] - previous[axis]));
            if (inside) output[written++] = point;
            previous = point; wasInside = inside;
        }
        return written;
    }

    void LateUpdate()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        RefreshShapes();
    }
    static void RefreshShapes()
    {
        var current = OpenXRPhysicalHands.Current;
        if (!current) return;
        current.Left.Solver.Count = current.Right.Solver.Count = 1;
        var left = current.Left.Hand ? current.Left.Hand.transform : null;
        var right = current.Right.Hand ? current.Right.Hand.transform : null;
        Vector3 leftPosition = left ? left.position : Vector3.zero, rightPosition = right ? right.position : Vector3.zero;
        Quaternion leftInverse = left ? Quaternion.Inverse(left.rotation) : Quaternion.identity;
        Quaternion rightInverse = right ? Quaternion.Inverse(right.rotation) : Quaternion.identity;
        foreach (var item in Items)
        {
            if (!item || !item.isActiveAndEnabled) continue;
            Transform previous = null;
            Matrix4x4 matrix = default;
            Quaternion frameRotation = default;
            Vector3 scale = default;
            foreach (var part in item.parts)
            {
                if (!part.Frame || !part.Frame.gameObject.activeInHierarchy) continue;
                var state = part.Hand == VRInputSource.LeftHand ? current.Left : current.Right;
                if (!state.Hand || !state.Active) continue;
                // The segments of a mesh share one animated transform.
                // Sample it once in this refresh, never across frames.
                if (part.Frame != previous)
                {
                    previous = part.Frame;
                    matrix = previous.localToWorldMatrix;
                    frameRotation = previous.rotation;
                    scale = previous.lossyScale;
                    scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                }
                var inverse = state == current.Left ? leftInverse : rightInverse;
                var position = state == current.Left ? leftPosition : rightPosition;
                var half = Vector3.Scale(part.Bounds.extents, scale);
                if (half.sqrMagnitude < .0001f || half.sqrMagnitude > 9) continue;
                // Thin edges should meet at the visible blade, not an
                // artificial 16 mm slab around it.
                half = Vector3.Max(half, Vector3.one * .003f);
                var shape = new OpenXRContactSolver.Shape(
                    inverse * (matrix.MultiplyPoint3x4(part.Bounds.center) - position),
                    half, inverse * frameRotation) { Hull = part.Hull };
                if (part.Hull && !part.Hull.transform.localScale.Equals(scale)) part.Hull.transform.localScale = scale;
                if (state.Solver.Count < state.Solver.Shapes.Length) state.Solver.Shapes[state.Solver.Count++] = shape;
                else MergeOverflow(state.Solver, shape);
            }
        }
    }
    static void MergeOverflow(OpenXRContactSolver solver, OpenXRContactSolver.Shape shape)
    {
        // Preserve all parts within a bounded query budget. Rare excess parts
        // join the final hand-local box rather than silently disappearing.
        var old = solver.Shapes[solver.Count - 1];
        var bounds = new Bounds(old.Center, Vector3.zero);
        Encapsulate(ref bounds, old); Encapsulate(ref bounds, shape);
        solver.Shapes[solver.Count - 1] = new OpenXRContactSolver.Shape(bounds.center, bounds.extents, Quaternion.identity);
    }
    static void Encapsulate(ref Bounds bounds, OpenXRContactSolver.Shape shape)
    {
        for (int i = 0; i < 8; i++)
            bounds.Encapsulate(shape.Center + shape.Rotation * Vector3.Scale(shape.Half,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
    }
    void OnDisable()
    {
        frame = -1;
        RefreshShapes();
    }
    void OnDestroy() { Items.Remove(this); OnDisable(); ClearParts(); }
}
