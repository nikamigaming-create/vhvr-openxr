using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using UnityEngine;
using ValheimVRMod.VRCore.Backends;

static class Program
{
    static int checks;
    static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
    }
    static void Throws<T>(Action action, string name) where T : Exception
    { try { action(); } catch (T) { checks++; return; } throw new Exception("FAIL: " + name); }
    static void Main(string[] args)
    {
        string game = args[0];
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            foreach (var folder in new[] { Path.Combine(game, "Valheim_Data/Managed"), Path.Combine(game, "BepInEx/core") })
            {
                string file = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(file)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
            }
            return null;
        };
        Run(args.Contains("failure"));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run(bool failure)
    {
        // Exercise the real SDK dictionary lookup too: its case-folded action-set
        // fallback indexes the wrong dictionary. Catalog spelling must be preserved.
        Valve.VR.SteamVR_Input.actionSets = new[] {
            Valve.VR.SteamVR_ActionSet.Create<Valve.VR.SteamVR_ActionSet>("/actions/default"),
            Valve.VR.SteamVR_ActionSet.Create<Valve.VR.SteamVR_ActionSet>("/actions/Valheim")
        };
        Valve.VR.SteamVR_Input.PreinitializeActionSetDictionaries();
        var nativeInput = new SteamVRInputBackend();
        Require(!nativeInput.IsActionSetActive(VRInputActions.Default.Path, VRInputSource.Any), "real SDK default action-set lookup");
        Require(!nativeInput.IsActionSetActive(VRInputActions.Valheim.Path, VRInputSource.Any), "real SDK Valheim action-set lookup");
        Require(VRBackendHost.Choose("openxr", Array.Empty<string>()) == VRBackendKind.OpenXR, "configured XR default");
        Require(VRBackendHost.Choose("openvr", new[] { "-vrbackend=OPENXR" }) == VRBackendKind.OpenXR, "command overrides config");
        Require(VRBackendHost.Choose("openxr", new[] { "-vrbackend=openvr" }) == VRBackendKind.OpenVR, "explicit OpenVR");
        Require(VRBackendHost.Choose("openxr", new[] { "-vrbackend=steamvr" }) == VRBackendKind.OpenVR, "legacy SteamVR alias");
        Throws<ArgumentException>(() => VRBackendHost.Choose("openxr", new[] { "-vrbackend=typo" }), "reject unknown backend");
        Throws<ArgumentException>(() => VRBackendHost.Choose("broken", Array.Empty<string>()), "reject unknown config");
        foreach (var method in typeof(IVRInputBackend).GetMethods())
            Require(!method.ToString()!.Contains("Valve."), "neutral input method: " + method.Name);
        foreach (var method in typeof(IVRRigBackend).GetMethods())
            Require(!method.ToString()!.Contains("Valve."), "neutral rig method: " + method.Name);
        Require(!VRGameplay.Options.Any, "all added gameplay disabled by default");
        foreach (var source in Enum.GetValues<VRInputSource>())
            Require(Enum.GetName(typeof(Valve.VR.SteamVR_Input_Sources), (int)source) == source.ToString(), "SDK boundary source mapping " + source);

        FakeBackend? last = null;
        foreach (var kind in new[] { VRBackendKind.OpenVR, VRBackendKind.OpenXR })
        {
            var backend = new FakeBackend(kind);
            VRBackendHost.Select(backend);
            last = backend;
            var action = VRInputActions.valheim_Grab;
            backend.Digital = new VRDigitalState { Active = true, Held = true, Down = true };
            Require(action.GetState(VRInputSource.LeftHand) && action.GetStateDown(VRInputSource.LeftHand) && !action.GetStateUp(VRInputSource.LeftHand), "grip edges " + kind);
            Require(string.Equals(backend.Path, "/actions/valheim/in/Grab", StringComparison.OrdinalIgnoreCase) && backend.Source == VRInputSource.LeftHand, "grip path/source " + kind);
            backend.Digital = new VRDigitalState { Active = true, Up = true };
            Require(!action.GetState(VRInputSource.RightHand) && action.GetStateUp(VRInputSource.RightHand), "release edges " + kind);
            backend.Axis = new Vector2(.2f, -.4f);
            Require(VRInputActions.valheim_Walk.axis.x == .2f && VRInputActions.valheim_Walk.axis.y == -.4f, "locomotion axes " + kind);
            backend.Pose = new VRPoseState { Position = new Vector3(1, 2, 3), Velocity = new Vector3(4, 5, 6), Valid = true, Connected = true };
            Require(VRInputActions.valheim_PoseL.GetLocalPosition(VRInputSource.LeftHand).y == 2 && VRInputActions.valheim_PoseL.GetVelocity(VRInputSource.LeftHand).z == 6, "pose/velocity " + kind);
            backend.Pose.Valid = false;
            Require(!VRInputActions.valheim_PoseL.GetPoseIsValid(VRInputSource.LeftHand), "tracking loss " + kind);
            backend.Controls = new VRHandControls { Grip = .8f, Trigger = .3f, PrimaryTouched = true };
            Require(VRInput.Backend.ReadHandControls(VRInputSource.RightHand).Grip == .8f, "finger control provider " + kind);
            VRInputActions.Valheim.Activate(VRInputSource.Any, 7);
            Require(backend.SetActive && backend.Priority == 7 && VRInputActions.Valheim.IsActive(), "action-set arbitration " + kind);
            VRInputActions.Valheim.Deactivate();
            Require(!VRInputActions.Valheim.IsActive(), "action-set deactivation " + kind);
            VRInput.Haptic.Execute(0, .1f, 75, .4f, VRInputSource.RightHand);
            Require(backend.Source == VRInputSource.RightHand && backend.Amplitude == .4f, "haptic routing " + kind);
            int downs = 0, ups = 0, updates = 0, axisChanges = 0;
            action.AddOnStateDownListener((_, source) => { Require(source == VRInputSource.LeftHand, "listener source"); downs++; }, VRInputSource.LeftHand);
            action.AddOnStateUpListener((_, source) => ups++, VRInputSource.LeftHand);
            VRInputActions.valheim_Walk.AddOnChangeListener((_, source, value, delta) => {
                Require(source == VRInputSource.LeftHand && value.y == -.4f && delta.x == .1f, "axis listener payload " + kind);
                axisChanges++;
            }, VRInputSource.LeftHand);
            Action listener = () => updates++;
            VRInput.onNonVisualActionsUpdated += listener;
            backend.Down!(VRInputSource.LeftHand); backend.Up!(VRInputSource.LeftHand); backend.Updates!();
            backend.AxisChange!(VRInputSource.LeftHand, backend.Axis, new Vector2(.1f, 0));
            Require(downs == 1 && ups == 1 && updates == 1 && axisChanges == 1, "shared semantic subscriptions " + kind);
            VRInput.onNonVisualActionsUpdated -= listener;
            Require(backend.Updates == null, "subscription cleanup " + kind);
        }
        last!.InitializeSuccess = !failure;
        Require(VRBackendHost.Initialize() == !failure, "initialization result retained");
        Require(VRBackendHost.Initialize() == !failure && last.Initializations == 1, "single runtime initialization");
        Require(VRBackendHost.Start() == !failure && last.Starts == (failure ? 0 : 1), "failed initialization never starts another backend");
        VRBackendHost.Start();
        Require(last.Starts == (failure ? 0 : 1), "single runtime start");
        Throws<InvalidOperationException>(() => VRBackendHost.Select(new FakeBackend(VRBackendKind.OpenVR)), "no unsupported hot swap");
        Throws<InvalidOperationException>(() => VRBackendHost.UpdateDisplayState(new FakeBackend(VRBackendKind.OpenVR), true, true), "reject stale display producer");
        VRBackendHost.UpdateDisplayState(last, true, false);
        Require(VRBackendHost.DisplayHealthy == !failure && !VRBackendHost.Focused, "focus and runtime display gate");
        VRBackendHost.Stop();
        Require(!VRBackendHost.IsReady && !VRBackendHost.IsRunning && !VRBackendHost.DisplayHealthy && last.Stops == 1, "session cleanup");
        Console.WriteLine($"PASS: {checks} production backend contract checks ({(failure ? "failed" : "successful")} initialization). Native hardware playback is a separate gate.");
    }
    sealed class FakeBackend : IVRBackend, IVRInputBackend
    {
        public VRBackendKind Kind { get; }
        public IVRInputBackend Input => this;
        public IVRRigBackend Rig => null!;
        public FakeBackend(VRBackendKind kind) { Kind = kind; }
        public VRDigitalState Digital;
        public Vector2 Axis;
        public VRPoseState Pose;
        public VRHandControls Controls;
        public string Path = "";
        public VRInputSource Source;
        public bool SetActive, InitializeSuccess = true;
        public int Priority, Initializations, Starts, Stops;
        public float Amplitude;
        public Action<VRInputSource>? Down, Up;
        public Action<VRInputSource, Vector2, Vector2>? AxisChange;
        public Action? Updates;
        public bool Initialize() { Initializations++; return InitializeSuccess; }
        public bool Start() { Starts++; return true; }
        public void Stop() { Stops++; }
        public void UpdateMirrorViewMode() { }
        public void UpdateMirrorSetup() { }
        public void Recenter() { }
        public VRDigitalState ReadDigital(string path, VRInputSource source) { Path = path; Source = source; return Digital; }
        public Vector2 ReadAxis(string path, VRInputSource source) { Path = path; Source = source; return Axis; }
        public VRPoseState ReadPose(string path, VRInputSource source) { Path = path; Source = source; return Pose; }
        public VRHandControls ReadHandControls(VRInputSource source) => Controls;
        public bool IsBound(string path, VRInputSource source) => true;
        public void SetActionSet(string path, VRInputSource source, bool active, int priority, bool exclusive) { SetActive = active; Priority = priority; }
        public bool IsActionSetActive(string path, VRInputSource source) => SetActive;
        public void ListenDigital(string path, VRInputSource source, bool down, Action<VRInputSource> callback) { if (down) Down = callback; else Up = callback; }
        public void ListenAxis(string path, VRInputSource source, Action<VRInputSource, Vector2, Vector2> callback) { AxisChange = callback; }
        public void ListenUpdates(Action callback) { Updates += callback; }
        public void RemoveUpdateListener(Action callback) { Updates -= callback; }
        public void Haptic(string path, VRInputSource source, float delay, float duration, float frequency, float amplitude) { Source = source; Amplitude = amplitude; }
        public bool OpenBindingUI(string actionSetPath) => Kind == VRBackendKind.OpenVR;
    }
}
