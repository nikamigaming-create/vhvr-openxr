// Managed lifecycle boundary only. These doubles do not simulate Unity physics,
// native destruction timing, transforms, networking, or headset/controller input.
using System.Reflection;
using Nikami.OpenXR;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        static readonly HashSet<Object> pending = new();
        public static implicit operator bool(Object value) => value != null && !value.Destroyed;
        public static void Destroy(Object value) { if (value) pending.Add(value); }
        internal static void FlushDestroyed()
        {
            while (pending.Count > 0)
            {
                var value = pending.First(); pending.Remove(value);
                if (!value) continue;
                if (value is MonoBehaviour behaviour) behaviour.InvokeMessage("OnDestroy");
                value.Destroyed = true;
            }
        }
    }
    public class GameObject : Object
    {
        readonly Dictionary<Type, Component> components = new();
        public Transform transform { get; } = new();
        public T AddComponent<T>() where T : Component, new()
        {
            var result = new T { gameObject = this };
            components.Add(typeof(T), result);
            return result;
        }
        public T GetComponent<T>() where T : Component => components.TryGetValue(typeof(T), out var result) ? (T)result : null;
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component
    {
        bool active = true;
        public bool enabled
        {
            get => active;
            set { bool disable = active && !value; active = value; if (disable) InvokeMessage("OnDisable"); }
        }
        internal void InvokeMessage(string method) => GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(this, null);
    }
    public class Transform
    {
        public Quaternion rotation = Quaternion.identity;
        public Vector3 lossyScale = new(1, 1, 1);
        public Vector3 TransformPoint(Vector3 point) => point;
        public Vector3 InverseTransformPoint(Vector3 point) => point;
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 ClampMagnitude(Vector3 value, float maximum) => value.sqrMagnitude > maximum * maximum ? value * (maximum / MathF.Sqrt(value.sqrMagnitude)) : value;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 value) => new(-value.x, -value.y, -value.z);
        public static Vector3 operator *(Vector3 value, float scale) => new(value.x * scale, value.y * scale, value.z * scale);
        public static Vector3 operator /(Vector3 value, float scale) => value * (1 / scale);
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new(0, 0, 0, 1);
        public static Quaternion Inverse(Quaternion value) => value;
        public static Quaternion operator *(Quaternion a, Quaternion b) => a;
        public static Vector3 operator *(Quaternion rotation, Vector3 value) => value;
        public void ToAngleAxis(out float angle, out Vector3 axis) { angle = 0; axis = default; }
    }
    public struct Pose { public Vector3 position; public Quaternion rotation; }
    public enum CollisionDetectionMode { Discrete, ContinuousDynamic, ContinuousSpeculative }
    public enum RigidbodyInterpolation { None, Interpolate, Extrapolate }
    public enum ForceMode { Force, Acceleration }
    public class Rigidbody : Component
    {
        CollisionDetectionMode mode;
        RigidbodyInterpolation smoothing;
        int iterations = 6;
        public int SettingWrites;
        public bool isKinematic, useGravity;
        public float mass = 1;
        public Vector3 position, linearVelocity, angularVelocity;
        public Quaternion rotation = Quaternion.identity;
        public CollisionDetectionMode collisionDetectionMode { get => mode; set { mode = value; SettingWrites++; } }
        public RigidbodyInterpolation interpolation { get => smoothing; set { smoothing = value; SettingWrites++; } }
        public int solverIterations { get => iterations; set { if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value)); iterations = value; SettingWrites++; } }
        public void WakeUp() { }
        public Vector3 GetPointVelocity(Vector3 point) => linearVelocity;
        public void AddForceAtPosition(Vector3 force, Vector3 point, ForceMode mode) => throw new InvalidOperationException("Native physics is outside this test boundary.");
        public void AddForce(Vector3 force, ForceMode mode) => throw new InvalidOperationException("Native physics is outside this test boundary.");
        public void AddTorque(Vector3 force, ForceMode mode) => throw new InvalidOperationException("Native physics is outside this test boundary.");
    }
    public static class Time { public static float unscaledTime, fixedDeltaTime = .02f; }
    public static class Physics { public static Vector3 gravity = new(0, -9.81f, 0); }
    public static class Mathf { public const float Deg2Rad = MathF.PI / 180; public static int Max(int a, int b) => Math.Max(a, b); }
}

public class ItemDrop : UnityEngine.Component { public bool m_autoPickup; }
public class ZNetView : UnityEngine.Component { public bool IsValid() => true; public bool IsOwner() => true; }
namespace ValheimVRMod.VRCore.Backends { }
namespace Nikami.OpenXR
{
    internal class OpenXRContactSolver { internal UnityEngine.Pose Pose = new() { rotation = UnityEngine.Quaternion.identity }; }
    internal class OpenXRPhysicalHands : UnityEngine.MonoBehaviour
    {
        internal const float HeldTrackingGap = .16f;
        internal class HandState
        {
            internal bool Active = true;
            internal OpenXRContactSolver Solver = new();
            internal UnityEngine.Pose Tracked = new() { rotation = UnityEngine.Quaternion.identity };
            internal UnityEngine.Vector3 Palm = default, PhysicalPalm = default;
            internal OpenXRPhysicalGrab Grab;
        }
        internal void Release(HandState hand) => hand.Grab?.Release();
    }
}
