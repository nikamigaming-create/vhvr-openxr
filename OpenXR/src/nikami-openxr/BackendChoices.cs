using BepInEx.Configuration;

namespace Nikami.OpenXR;

internal sealed class BackendChoices : AcceptableValueList<string>
{
    internal BackendChoices() : base("openxr", "openvr", "steamvr") { }
    // Keep unknown names for the backend host to reject. BepInEx's default
    // list clamp silently replaces them with its first entry.
    public override object Clamp(object value) => value is string name ? name.ToLowerInvariant() : value;
    public override bool IsValid(object value) => base.IsValid(Clamp(value));
}
