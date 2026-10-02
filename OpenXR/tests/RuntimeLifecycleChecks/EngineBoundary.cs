// Test-only application/UI state. Production frame-gate and focus code are linked unchanged.
#nullable enable
namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object? value) => value != null;
    }
    public class Transform : Object
    {
        public Transform? Parent;
        public bool IsChildOf(Transform ancestor) => this == ancestor || (Parent?.IsChildOf(ancestor) ?? false);
    }
    public class GameObject : Object
    {
        public readonly Transform transform = new();
        public bool Active = true;
        public void SetActive(bool active) => Active = active;
    }
    public static class Application
    {
        public static event Action<bool>? focusChanged;
        public static event Action? quitting;
        public static int FocusSubscribers => focusChanged?.GetInvocationList().Length ?? 0;
        public static int QuitSubscribers => quitting?.GetInvocationList().Length ?? 0;
        public static void Focus(bool focused) => focusChanged?.Invoke(focused);
        public static void Quit() => quitting?.Invoke();
    }
}
namespace UnityEngine.EventSystems
{
    public sealed class EventSystem : UnityEngine.Object
    {
        public static EventSystem? current;
        public UnityEngine.GameObject? currentSelectedGameObject;
        public void SetSelectedGameObject(UnityEngine.GameObject? value) => currentSelectedGameObject = value;
    }
}
public sealed class ChatInput : UnityEngine.Object
{
    public readonly UnityEngine.GameObject gameObject = new();
    public UnityEngine.Transform transform => gameObject.transform;
    public string Text = "unsent draft";
}
public sealed class Chat : UnityEngine.Object
{
    public static Chat? instance;
    public readonly ChatInput m_input = new();
    public bool m_wasFocused = true;
    public bool HasFocus() => m_wasFocused;
}
namespace Nikami.OpenXR
{
    internal static class OpenXRPlugin
    {
        internal static bool Ready;
        internal static readonly TestLog Log = new();
    }
    internal sealed class TestLog
    {
        internal void LogInfo(string message) { }
    }
}
