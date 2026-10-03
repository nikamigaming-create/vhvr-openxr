// Only the clock, controls and runtime boundary are fake. Tests compile the
// production adapter and contract unchanged; no native Unity/OpenXR calls run.
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator /(Vector3 a, float b) => new(a.x / b, a.y / b, a.z / b);
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new(0, 0, 0, 1);
    }
    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static float Clamp01(float value) => Clamp(value, 0, 1);
        public static float Max(float a, float b) => Math.Max(a, b);
    }
    public static class Time { public static int frameCount; public static float unscaledTime; }
    public static class Application
    {
        public static string streamingAssetsPath;
        public static event Action onBeforeRender;
        public static void BeforeRender() => onBeforeRender?.Invoke();
    }
}
namespace UnityEngine.InputSystem
{
    public enum InputDeviceChange { Added, Removed, ConfigurationChanged }
    public class InputControl { }
    public class InputDevice
    {
        public string layout = "OculusTouchControllerOpenXR";
        public bool added = true, enabled = true;
        public readonly HashSet<string> usages = new();
        readonly Dictionary<string, InputControl> controls = new(StringComparer.OrdinalIgnoreCase);
        public T Add<T>(string name, T control) where T : InputControl { controls.Add(name, control); return control; }
        public T TryGetChildControl<T>(string name) where T : InputControl => controls.TryGetValue(name, out var value) ? value as T : null;
    }
    public static class CommonUsages { public const string LeftHand = "LeftHand", RightHand = "RightHand"; }
    public static class InputSystem
    {
        public static readonly List<InputDevice> devices = new();
        public static event Action<InputDevice, InputDeviceChange> onDeviceChange;
        public static void Notify(InputDevice device, InputDeviceChange change) => onDeviceChange?.Invoke(device, change);
    }
}
namespace UnityEngine.InputSystem.Controls
{
    public class AxisControl : UnityEngine.InputSystem.InputControl { public float Value; public float ReadValue() => Value; }
    public sealed class ButtonControl : AxisControl { public bool isPressed => Value >= .5f; }
    public sealed class Vector2Control : UnityEngine.InputSystem.InputControl { public UnityEngine.Vector2 Value; public UnityEngine.Vector2 ReadValue() => Value; }
    public sealed class Vector3Control : UnityEngine.InputSystem.InputControl { public UnityEngine.Vector3 Value; public UnityEngine.Vector3 ReadValue() => Value; }
    public sealed class QuaternionControl : UnityEngine.InputSystem.InputControl { public UnityEngine.Quaternion Value = UnityEngine.Quaternion.identity; public UnityEngine.Quaternion ReadValue() => Value; }
    public sealed class IntegerControl : UnityEngine.InputSystem.InputControl { public int Value; public int ReadValue() => Value; }
}
namespace UnityEngine.InputSystem.XR
{
    public sealed class XRHMD : UnityEngine.InputSystem.InputDevice { }
    public sealed class XRControllerWithRumble : UnityEngine.InputSystem.InputDevice
    {
        public readonly List<(float Amplitude, float Duration)> Pulses = new();
        public void SendImpulse(float amplitude, float duration) => Pulses.Add((amplitude, duration));
    }
}
namespace UnityEngine.XR
{
    public enum XRNode { LeftHand, RightHand }
    public struct HapticCapabilities { public bool supportsImpulse; }
    public struct InputDevice
    {
        public bool TryGetHapticCapabilities(out HapticCapabilities value) { value = default; return false; }
        public void SendHapticImpulse(uint channel, float amplitude, float duration) => throw new InvalidOperationException("Unexpected native haptic boundary.");
    }
    public static class InputDevices { public static InputDevice GetDeviceAtXRNode(XRNode node) => default; }
}
namespace ValheimVRMod.VRCore.Backends
{
    public interface IVRRigBackend { }
    public static class VRPoseDriver { public static void RefreshOpenXRPose() { } }
    public sealed class OpenVRBackend : IVRBackend
    {
        public VRBackendKind Kind => VRBackendKind.OpenVR;
        public IVRInputBackend Input => throw new NotSupportedException("OpenVR is outside these adapter checks.");
        public IVRRigBackend Rig => null;
        public bool Initialize() => throw new NotSupportedException();
        public bool Start() => throw new NotSupportedException();
        public void Stop() { }
        public void UpdateMirrorViewMode() { }
        public void UpdateMirrorSetup() { }
        public void Recenter() { }
    }
}
namespace Nikami.OpenXR
{
    internal static class OpenXRPlugin
    {
        internal sealed class LogBoundary { public void LogInfo(string text) { } public void LogWarning(string text) { } }
        internal static readonly LogBoundary Log = new();
    }
}
