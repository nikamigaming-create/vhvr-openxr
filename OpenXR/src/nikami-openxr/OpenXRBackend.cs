using ValheimVRMod.VRCore.Backends;
using UnityEngine.InputSystem.Controls;

namespace Nikami.OpenXR;

// The shared gameplay interface stays independent of the native runtime and
// of Valve's managed action types. The authored rig still uses its compatibility bridge.
internal sealed class OpenXRBackend : IVRBackend
{
    public VRBackendKind Kind => VRBackendKind.OpenXR;
    public IVRInputBackend Input { get; } = new OpenXRInputBackend();
    public bool Initialize() => OpenXRPlugin.InitializeBackend();
    public bool Start() => OpenXRPlugin.StartBackend();
    public void Stop() { OpenXRPlugin.Loader?.Stop(); OpenXRPlugin.Loader?.Deinitialize(); }
    public void UpdateMirrorViewMode() { }
    public void UpdateMirrorSetup() { }
    public void Recenter() => VRRecenter.Apply();
}

internal sealed class OpenXRInputBackend : SteamVRInputBackend
{
    public override VRHandControls ReadHandControls(VRInputSource source)
    {
        var device = InputAdapter.Device(source == VRInputSource.LeftHand ? 1 : 2);
        bool Touch(string name, string alias) => (InputAdapter.Control<ButtonControl>(device, name) ?? InputAdapter.Control<ButtonControl>(device, alias))?.isPressed ?? false;
        return new VRHandControls {
            Grip = InputAdapter.Control<AxisControl>(device, "grip")?.ReadValue() ?? 0,
            Trigger = InputAdapter.Control<AxisControl>(device, "trigger")?.ReadValue() ?? 0,
            PrimaryTouched = Touch("primaryTouched", "primaryTouch"),
            SecondaryTouched = Touch("secondaryTouched", "secondaryTouch"),
            ThumbstickTouched = Touch("thumbstickTouched", "primary2DAxisTouch"),
            TriggerTouched = Touch("triggerTouched", "triggerTouch")
        };
    }
    public override bool OpenBindingUI(string actionSetPath)
    {
        OpenXRPlugin.Log.LogInfo("OpenXR uses the packaged action bindings; the SteamVR binding editor is unavailable for this backend.");
        return false;
    }
}
