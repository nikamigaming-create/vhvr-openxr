using ValheimVRMod.VRCore.Backends;
using System;

namespace Nikami.OpenXR;

// The shared gameplay interface stays independent of the native runtime and
// of Valve's managed action types. OpenXR input and rig are native providers.
internal sealed class OpenXRBackend : IVRBackend
{
    public VRBackendKind Kind => VRBackendKind.OpenXR;
    public IVRInputBackend Input { get; } = new OpenXRInputBackend();
    public IVRRigBackend Rig { get; } = new OpenXRRigBackend();
    public bool Initialize() => OpenXRPlugin.InitializeBackend();
    public bool Start() => OpenXRPlugin.StartBackend();
    public void Stop() { OpenXRPlugin.Loader?.Stop(); OpenXRPlugin.Loader?.Deinitialize(); }
    public void UpdateMirrorViewMode() { }
    public void UpdateMirrorSetup() { }
    public void Recenter() => VRRecenter.Apply();
}

internal sealed class OpenXRInputBackend : IVRInputBackend
{
    public VRDigitalState ReadDigital(string path, VRInputSource source) => InputAdapter.ReadDigital(path, source);
    public UnityEngine.Vector2 ReadAxis(string path, VRInputSource source) => InputAdapter.ReadAxis(path, source);
    public VRPoseState ReadPose(string path, VRInputSource source) => InputAdapter.ReadPose(path, source);
    public VRHandControls ReadHandControls(VRInputSource source) => InputAdapter.ReadHandControls(source);
    public bool IsBound(string path, VRInputSource source) => InputAdapter.IsBound(path, source);
    public void SetActionSet(string path, VRInputSource source, bool active, int priority, bool exclusive) => InputAdapter.SetActionSet(path, source, active, priority, exclusive);
    public bool IsActionSetActive(string path, VRInputSource source) => InputAdapter.IsActionSetActive(path, source);
    public void ListenDigital(string path, VRInputSource source, bool down, Action<VRInputSource> callback) => InputAdapter.ListenDigital(path, source, down, callback);
    public void ListenAxis(string path, VRInputSource source, Action<VRInputSource, UnityEngine.Vector2, UnityEngine.Vector2> callback) => InputAdapter.ListenAxis(path, source, callback);
    public void ListenUpdates(Action callback) => InputAdapter.ListenUpdates(callback);
    public void RemoveUpdateListener(Action callback) => InputAdapter.RemoveUpdateListener(callback);
    public void Haptic(string path, VRInputSource source, float delay, float duration, float frequency, float amplitude) => InputAdapter.Haptic(source, delay, duration, amplitude);
    public bool OpenBindingUI(string actionSetPath)
    {
        OpenXRPlugin.Log.LogInfo("OpenXR uses the packaged action bindings; the SteamVR binding editor is unavailable for this backend.");
        return false;
    }
}
