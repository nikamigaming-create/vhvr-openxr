using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using Valve.Newtonsoft.Json.Linq;
using ValheimVRMod.VRCore.Backends;
using XRDevice = UnityEngine.InputSystem.InputDevice;

namespace Nikami.OpenXR;

internal static class InputAdapter
{
    sealed class Binding
    {
        internal string Output, Set, Path, ControlName, Component, Mode, ControlKey, SubMode, ForceControl, PressedControl;
        internal int Hand;
        internal float Press = .55f, Release = .45f;
        internal float Deadzone, Overlap, ScrollScale = 1;
        internal bool Force;
        internal bool Held;
        internal int ScrollFrame = -1;
        internal bool WasTouched;
        internal Vector2 PreviousAxis, Scroll;
    }
    sealed class Chord
    {
        internal string Output;
        internal Binding[] Inputs;
    }
    sealed class Sample
    {
        internal int Frame = -1;
        internal Vector2 Axis;
        internal bool Held;
        internal VRDigitalState Digital;
        internal Vector2 Delta;
    }
    sealed class PoseSample
    {
        internal int Frame = -1;
        internal bool Valid;
        internal Vector3 Position;
        internal Vector3 Velocity;
        internal float Time;
    }
    sealed class SampleKeyComparer : IEqualityComparer<(string Path, int Source)>
    {
        public bool Equals((string Path, int Source) x, (string Path, int Source) y) =>
            x.Source == y.Source && StringComparer.OrdinalIgnoreCase.Equals(x.Path, y.Path);
        public int GetHashCode((string Path, int Source) key) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(key.Path) ^ key.Source;
    }
    static readonly Dictionary<string, List<Binding>> Bindings = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, List<Chord>> Chords = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<Binding> AllBindings = new();
    static readonly Dictionary<Binding, int> ActivePriorities = new();
    static readonly Dictionary<string, int> ControlPriorities = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<(string Path, int Source), Sample> Samples = new(new SampleKeyComparer());
    static readonly List<(string Path, int Source)> BoundSamples = new();
    static readonly Dictionary<int, PoseSample> PoseSamples = new();
    static readonly XRDevice[] Devices = new XRDevice[4];
    static readonly Dictionary<XRDevice, Dictionary<string, InputControl>> Controls = new();
    static bool devicesResolved;
    static bool profilesDirty, forceReload, bindingsLoaded, editing;
    static readonly BindingProfile[] HandProfiles = new BindingProfile[4];
    static bool primeStates;
    readonly struct SetState
    {
        internal readonly string Path;
        internal readonly int Hand, Priority;
        internal SetState(string path, int hand, int priority) { Path = path; Hand = hand; Priority = priority; }
    }
    sealed class DigitalListener
    {
        internal string Path;
        internal VRInputSource Source;
        internal bool Down;
        internal Action<VRInputSource> Callback;
    }
    sealed class AxisListener
    {
        internal string Path;
        internal VRInputSource Source;
        internal Action<VRInputSource, Vector2, Vector2> Callback;
    }
    readonly struct Pulse
    {
        internal readonly VRInputSource Source;
        internal readonly float At, Duration, Amplitude;
        internal Pulse(VRInputSource source, float at, float duration, float amplitude)
        { Source = source; At = at; Duration = duration; Amplitude = amplitude; }
    }
    static readonly List<SetState> Sets = new();
    static readonly List<DigitalListener> DigitalListeners = new();
    static readonly List<AxisListener> AxisListeners = new();
    static readonly List<Pulse> Pulses = new();
    static readonly HashSet<Action> UpdateListeners = new();
    static event Action Updated;
    static int dispatchedFrame = -1;
    internal static void Install()
    {
        InputSystem.onDeviceChange += DeviceChanged;
        Application.onBeforeRender += VRPoseDriver.RefreshOpenXRPose;
    }
    internal static void Pump()
    {
        VRPoseDriver.RefreshOpenXRPose();
        EnsureProfiles();
        if (dispatchedFrame == Time.frameCount) return;
        dispatchedFrame = Time.frameCount;
        // Edges belong to the input frame, even when gameplay does not read an
        // action in that frame (e.g. a trigger before a crossbow is equipped).
        for (int i = 0; i < BoundSamples.Count; i++)
        {
            var key = BoundSamples[i];
            GetSample(key.Path, key.Source);
        }
        primeStates = false;
        for (int i = Pulses.Count - 1; i >= 0; i--)
            if (Time.unscaledTime >= Pulses[i].At)
            { var pulse = Pulses[i]; Pulses.RemoveAt(i); SendPulse(pulse); }
        for (int i = 0, count = DigitalListeners.Count; i < count; i++)
        {
            var listener = DigitalListeners[i];
            var state = ReadDigital(listener.Path, listener.Source);
            if (listener.Down ? state.Down : state.Up) listener.Callback(listener.Source);
        }
        for (int i = 0, count = AxisListeners.Count; i < count; i++)
        {
            var listener = AxisListeners[i];
            var state = GetSample(listener.Path, Hand(listener.Source));
            if (state.Delta.sqrMagnitude > .0000001f) listener.Callback(listener.Source, state.Axis, state.Delta);
        }
        Updated?.Invoke();
    }
    internal static void Shutdown()
    {
        InputSystem.onDeviceChange -= DeviceChanged;
        Application.onBeforeRender -= VRPoseDriver.RefreshOpenXRPose;
        DigitalListeners.Clear(); AxisListeners.Clear(); UpdateListeners.Clear(); Updated = null;
        Sets.Clear(); Pulses.Clear(); Samples.Clear(); BoundSamples.Clear(); dispatchedFrame = -1;
        ActivePriorities.Clear(); ControlPriorities.Clear(); Bindings.Clear(); Chords.Clear(); AllBindings.Clear();
        bindingsLoaded = editing = primeStates = false;
        Array.Clear(HandProfiles, 0, HandProfiles.Length);
        BindingProfiles.Clear();
        DeviceChanged(null, default);
    }
    static void DeviceChanged(XRDevice device, InputDeviceChange change)
    {
        devicesResolved = false;
        Array.Clear(Devices, 0, Devices.Length);
        Controls.Clear();
        profilesDirty = true;
        foreach (var sample in Samples.Values) sample.Frame = -1;
        PoseSamples.Clear();
    }
    internal static void LoadBindings()
    {
        BindingProfiles.Load(Path.Combine(Application.streamingAssetsPath, "SteamVR"));
        bindingsLoaded = profilesDirty = forceReload = true;
        EnsureProfiles();
    }
    internal static BindingProfile ProfileForHand(int hand) { EnsureProfiles(); return hand > 0 && hand < 3 ? HandProfiles[hand] : null; }
    internal static void RefreshBindings() { profilesDirty = forceReload = true; }
    internal static void EditBindings(bool open) { editing = open; RefreshBindings(); }
    static void EnsureProfiles()
    {
        if (!bindingsLoaded || !profilesDirty) return;
        profilesDirty = false;
        bool changed = forceReload;
        for (int hand = 1; hand <= 2; hand++)
        {
            var device = Device(hand);
            var profile = device == null ? HandProfiles[hand] ?? BindingProfiles.Find("oculus_touch") :
                BindingProfiles.Detect(device.layout,
                    Stick(device) != null && !ReferenceEquals(Stick(device), Pad(device)), Pad(device) != null, Control<ButtonControl>(device, "primaryButton") != null);
            changed |= profile != HandProfiles[hand];
            HandProfiles[hand] = profile;
        }
        if (!changed) return;
        forceReload = false;
        Bindings.Clear(); Chords.Clear(); AllBindings.Clear(); BoundSamples.Clear();
        var chordKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int hand = 1; hand <= 2; hand++)
        {
            var profile = HandProfiles[hand];
            if (profile == null) continue;
            Import(editing ? profile.Defaults : profile.Current, hand, chordKeys);
        }
        foreach (var path in Bindings.Keys.Union(Chords.Keys, StringComparer.OrdinalIgnoreCase))
        for (int source = 0; source < Devices.Length; source++)
        {
            var key = (path, source);
            if (!Samples.ContainsKey(key)) Samples.Add(key, new Sample());
            BoundSamples.Add(key);
        }
        foreach (var sample in Samples.Values) sample.Frame = -1;
        primeStates = true;
        RebuildPriorities();
        OpenXRPlugin.Log.LogInfo($"OpenXR bindings: left={HandProfiles[1]?.Name ?? "unrecognized"}, right={HandProfiles[2]?.Name ?? "unrecognized"}, actions={Bindings.Keys.Union(Chords.Keys).Count()}. Personal layouts are separate from upstream defaults.");
    }
    static void Import(JObject root, int hand, HashSet<string> chordKeys)
    {
        foreach (var set in ((JObject)root["bindings"]).Properties())
        {
            foreach (var source in set.Value["sources"] ?? new JArray())
            {
            if (PathHand((string)source["path"]) != hand) continue;
            foreach (var input in ((JObject)source["inputs"]).Properties())
            {
                var b = MakeBinding((string)input.Value["output"], (string)source["path"], input.Name,
                    (string)source["mode"], source["parameters"]);
                if (!Bindings.TryGetValue(b.Output, out var list)) Bindings[b.Output] = list = new();
                list.Add(b);
            }
            }
            foreach (JObject chordJson in set.Value["chords"] ?? new JArray())
            {
                if (!((JArray)chordJson["inputs"]).Any(input => PathHand((string)input[0]) == hand) ||
                    !chordKeys.Add(chordJson.ToString(Valve.Newtonsoft.Json.Formatting.None))) continue;
                var chord = new Chord { Output = (string)chordJson["output"] };
                chord.Inputs = ((JArray)chordJson["inputs"]).Select(input =>
                    MakeBinding(chord.Output, (string)input[0], (string)input[1], "button", null)).ToArray();
                if (chord.Inputs.Length == 0) continue;
                if (!Chords.TryGetValue(chord.Output, out var list)) Chords[chord.Output] = list = new();
                list.Add(chord);
            }
        }
    }
    static int PathHand(string path) => path.Contains("/left/") ? 1 : path.Contains("/right/") ? 2 : 3;
    static Binding MakeBinding(string output, string path, string component, string mode, JToken parameters)
    {
        var binding = new Binding { Output = output, Set = output.Substring(0, output.IndexOf("/in/", StringComparison.OrdinalIgnoreCase)), Path = path, Component = component, Mode = mode };
        binding.ControlName = path.Substring(path.LastIndexOf('/') + 1);
        binding.ForceControl = binding.ControlName + "Force";
        binding.PressedControl = binding.ControlName + "Pressed";
        binding.Hand = PathHand(path);
        binding.Press = (float?)parameters?["click_activate_threshold"] ?? binding.Press;
        binding.Release = (float?)parameters?["click_deactivate_threshold"] ?? Math.Min(binding.Release, binding.Press);
        binding.Force = (string)parameters?["force_input"] == "force";
        binding.SubMode = (string)parameters?["sub_mode"];
        binding.Deadzone = ((float?)parameters?["deadzone_pct"] ?? (mode == "dpad" ? 25 : 0)) / 100;
        binding.Overlap = ((float?)parameters?["overlap_pct"] ?? 0) / 100;
        binding.ScrollScale = (float?)parameters?["discrete_scroll_trackpad_globalscalefactor"] ?? 1;
        string control = binding.ControlName switch {
            "joystick" or "thumbstick" => "stick", "touchpad" or "trackpad" => "pad",
            "a" or "x" => "primary", "b" or "y" => "secondary", "application_menu" => "menu", _ => binding.ControlName
        };
        binding.ControlKey = binding.Hand + ":" + control;
        AllBindings.Add(binding);
        return binding;
    }
    static int Hand(VRInputSource source) => source == VRInputSource.LeftHand ? 1 :
        source == VRInputSource.RightHand ? 2 : source == VRInputSource.Head ? 3 : source == VRInputSource.Any ? 0 : -1;
    internal static void SetActionSet(string path, VRInputSource source, bool active, int priority, bool exclusive)
    {
        int hand = Hand(source);
        if (hand < 0) return;
        if (exclusive && active) Sets.Clear();
        Sets.RemoveAll(set => set.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && set.Hand == hand);
        if (active) Sets.Add(new SetState(path, hand, priority));
        RebuildPriorities();
    }
    static void RebuildPriorities()
    {
        ActivePriorities.Clear(); ControlPriorities.Clear();
        foreach (var set in Sets)
        foreach (var binding in AllBindings)
        {
            if (!binding.Set.Equals(set.Path, StringComparison.OrdinalIgnoreCase) || (set.Hand != 0 && set.Hand != binding.Hand)) continue;
            if (!ActivePriorities.TryGetValue(binding, out int current) || set.Priority > current) ActivePriorities[binding] = set.Priority;
            if (!ControlPriorities.TryGetValue(binding.ControlKey, out current) || set.Priority > current) ControlPriorities[binding.ControlKey] = set.Priority;
        }
    }
    internal static bool IsActionSetActive(string path, VRInputSource source)
    {
        int hand = Hand(source);
        if (hand < 0) return false;
        foreach (var set in Sets)
            if (set.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && (set.Hand == 0 || set.Hand == hand || hand == 0))
                return true;
        return false;
    }
    static bool BindingActive(Binding binding)
    {
        // VHVR gives its UI action set priority over gameplay. Without this
        // arbitration the Craft trigger also fires Use, closing the inventory
        // before Unity's UI can receive its pointer-release event.
        return ActivePriorities.TryGetValue(binding, out int priority) &&
            ControlPriorities.TryGetValue(binding.ControlKey, out int highest) && priority == highest;
    }
    internal static XRDevice Device(int hand)
    {
        if (hand < 1 || hand > 3) return null;
        if (!devicesResolved)
        {
            foreach (var d in InputSystem.devices)
            {
                if (!d.added || !d.enabled) continue;
                if (Devices[3] == null && d is XRHMD) Devices[3] = d;
                if (Devices[1] == null && d.usages.Contains(UnityEngine.InputSystem.CommonUsages.LeftHand)) Devices[1] = d;
                if (Devices[2] == null && d.usages.Contains(UnityEngine.InputSystem.CommonUsages.RightHand)) Devices[2] = d;
            }
            devicesResolved = true;
        }
        return Devices[hand];
    }
    internal static T Control<T>(XRDevice d, string name) where T : InputControl
    {
        if (d == null) return null;
        if (!Controls.TryGetValue(d, out var controls)) Controls[d] = controls = new(StringComparer.OrdinalIgnoreCase);
        if (!controls.TryGetValue(name, out var control)) controls[name] = control = d.TryGetChildControl<InputControl>(name);
        return control as T;
    }
    static Vector2Control Stick(XRDevice device) => Control<Vector2Control>(device, "thumbstick") ?? Control<Vector2Control>(device, "joystick") ?? Control<Vector2Control>(device, "primary2DAxis");
    static Vector2Control Pad(XRDevice device) => Control<Vector2Control>(device, "trackpad") ?? Control<Vector2Control>(device, "touchpad") ?? Control<Vector2Control>(device, "secondary2DAxis");
    static bool IsStick(string name) => name == "joystick" || name == "thumbstick";
    static bool IsPad(string name) => name == "trackpad" || name == "touchpad";
    static AxisControl Analog(Binding b)
    {
        var device = Device(b.Hand);
        if (b.Force)
        {
            var force = Control<AxisControl>(device, b.ForceControl);
            if (force != null) return force;
        }
        return Control<AxisControl>(device, b.ControlName) ?? Control<AxisControl>(device, b.PressedControl);
    }
    static ButtonControl Button(Binding b, bool touch)
    {
        var device = Device(b.Hand);
        string name = b.ControlName switch {
            "a" or "x" => touch ? "primaryTouched" : "primaryButton",
            "b" or "y" => touch ? "secondaryTouched" : "secondaryButton",
            "joystick" or "thumbstick" => touch ? "thumbstickTouched" : "thumbstickClicked",
            "trackpad" or "touchpad" => touch ? "trackpadTouched" : "trackpadClicked",
            "trigger" => touch ? "triggerTouched" : "triggerPressed",
            "grip" => touch ? "gripTouched" : "gripPressed",
            "application_menu" or "menu" => "menu", "system" => touch ? "systemTouched" : "system", _ => b.ControlName
        };
        var control = Control<ButtonControl>(device, name);
        if (control != null) return control;
        string alias = name switch {
            "thumbstickClicked" => "joystickClicked", "thumbstickTouched" => "joystickTouched",
            "trackpadClicked" => "touchpadClicked", "trackpadTouched" => "touchpadTouched",
            "primaryTouched" => "primaryTouch", "secondaryTouched" => "secondaryTouch",
            "triggerTouched" => "triggerTouch", _ => name
        };
        control = Control<ButtonControl>(device, alias);
        if (control != null) return control;
        if (IsStick(b.ControlName)) return Control<ButtonControl>(device, touch ? "primary2DAxisTouch" : "primary2DAxisClick");
        if (IsPad(b.ControlName)) return Control<ButtonControl>(device, touch ? "secondary2DAxisTouch" : "secondary2DAxisClick");
        return null;
    }
    static bool Available(Binding b)
    {
        var device = Device(b.Hand);
        if (device == null) return false;
        if (b.Mode == "dpad" || b.Component == "position" || b.Component == "scroll")
            return (IsStick(b.ControlName) ? Stick(device) : Pad(device)) != null;
        if (b.Component != "touch" && (b.ControlName == "trigger" || b.ControlName == "grip" || b.Force)) return Analog(b) != null;
        if (b.ControlName == "grip" && b.Component == "touch") return Analog(b) != null;
        return Button(b, b.Component == "touch") != null || (IsPad(b.ControlName) && b.Component == "click" && Control<AxisControl>(device, "trackpadForce") != null);
    }
    static Vector2 RawAxis(Binding b)
    {
        var d = Device(b.Hand);
        if (IsStick(b.ControlName)) return Stick(d)?.ReadValue() ?? Vector2.zero;
        if (IsPad(b.ControlName)) return Pad(d)?.ReadValue() ?? Vector2.zero;
        if (b.ControlName == "trigger" || b.ControlName == "grip") return new Vector2(Analog(b)?.ReadValue() ?? 0, 0);
        return Vector2.zero;
    }
    static Vector2 ReadAxis(Binding b)
    {
        var axis = RawAxis(b);
        if (b.Component == "scroll")
        {
            if (b.ScrollFrame == Time.frameCount) return b.Scroll;
            bool touched = Button(b, true)?.isPressed ?? axis.sqrMagnitude > 0;
            b.Scroll = touched && b.WasTouched ? new Vector2((axis.x - b.PreviousAxis.x) * b.ScrollScale, (axis.y - b.PreviousAxis.y) * b.ScrollScale) : Vector2.zero;
            b.ScrollFrame = Time.frameCount; b.PreviousAxis = axis; b.WasTouched = touched;
            return b.Scroll;
        }
        if (b.Component == "position" && b.Deadzone > 0)
        {
            float magnitude = axis.magnitude;
            float factor = magnitude > b.Deadzone ? Math.Min(1, (magnitude - b.Deadzone) / (1 - b.Deadzone)) / magnitude : 0;
            return new Vector2(axis.x * factor, axis.y * factor);
        }
        return axis;
    }
    static bool ReadButton(Binding b)
    {
        var d = Device(b.Hand);
        if (d == null) return b.Held = false;
        var path = b.ControlName;
        float value;
        if ((IsStick(path) || IsPad(path)) && b.Mode == "dpad")
        {
            var axis = RawAxis(b);
            bool gate = b.SubMode == "click" ? (Button(b, false)?.isPressed ?? (Control<AxisControl>(d, "trackpadForce")?.ReadValue() > b.Press)) :
                b.SubMode == "touch" ? Button(b, true)?.isPressed ?? axis.sqrMagnitude > 0 : true;
            float angle = (float)(Math.Atan2(axis.y, axis.x) * 180 / Math.PI);
            float target = b.Component switch { "north" => 90, "south" => -90, "west" => 180, _ => 0 };
            float separation = Math.Abs(angle - target);
            if (separation > 180) separation = 360 - separation;
            bool direction = b.Component == "center" ? axis.magnitude <= b.Deadzone :
                axis.magnitude > b.Deadzone && separation <= 45 + Math.Min(.99f, b.Overlap) * 45;
            value = gate && direction ? 1 : 0;
        }
        else if ((path == "trigger" || path == "grip" || b.Force) && b.Component != "touch") value = Analog(b)?.ReadValue() ?? 0;
        else
        {
            value = Button(b, b.Component == "touch")?.ReadValue() ??
                (path == "grip" && b.Component == "touch" ? ((Analog(b)?.ReadValue() ?? 0) > .02f ? 1 : 0) :
                (IsPad(path) && b.Component == "click" ? Control<AxisControl>(d, "trackpadForce")?.ReadValue() ?? 0 : 0));
        }
        return b.Held = value > 0 && value >= (b.Held ? b.Release : b.Press);
    }
    static Sample GetSample(string path, int source)
    {
        EnsureProfiles();
        var key = (path, source);
        if (!Samples.TryGetValue(key, out var sample)) Samples[key] = sample = new();
        if (sample.Frame == Time.frameCount) return sample;
        sample.Frame = Time.frameCount;
        bool held = false, active = false;
        Vector2 axis = Vector2.zero;
        if (Bindings.TryGetValue(path, out var bindings))
        foreach (var b in bindings)
        {
            if (source != 0 && source != b.Hand) continue;
            if (!Available(b) || !BindingActive(b)) continue;
            active = true;
            var press = ReadButton(b);
            var value = ReadAxis(b);
            held |= press;
            if (value.sqrMagnitude > axis.sqrMagnitude) axis = value;
        }
        if (Chords.TryGetValue(path, out var chords))
        foreach (var chord in chords)
        {
            bool chordActive = chord.Inputs.Length > 0;
            bool chordMatchesSource = source == 0;
            bool chordHeld = chord.Inputs.Length > 0;
            foreach (var b in chord.Inputs)
            {
                chordMatchesSource |= source == b.Hand;
                if (!Available(b) || !BindingActive(b))
                {
                    b.Held = false;
                    chordActive = chordHeld = false;
                    continue;
                }
                chordHeld &= ReadButton(b);
            }
            active |= chordActive && chordMatchesSource;
            if (chordHeld && chordMatchesSource)
            {
                held = true;
            }
        }
        sample.Digital = new VRDigitalState { Active = active, Held = held, Down = !primeStates && held && !sample.Held, Up = !primeStates && !held && sample.Held };
        sample.Delta = primeStates ? Vector2.zero : axis - sample.Axis;
        sample.Held = held; sample.Axis = axis;
        return sample;
    }
    internal static VRDigitalState ReadDigital(string path, VRInputSource source) => GetSample(path, Hand(source)).Digital;
    internal static Vector2 ReadAxis(string path, VRInputSource source) => GetSample(path, Hand(source)).Axis;
    internal static bool IsBound(string path, VRInputSource source)
    {
        EnsureProfiles();
        int hand = Hand(source);
        if (hand < 0) return false;
        if (Bindings.TryGetValue(path, out var bindings))
            foreach (var binding in bindings)
                if ((hand == 0 || binding.Hand == hand) && Available(binding) && BindingActive(binding)) return true;
        if (Chords.TryGetValue(path, out var chords))
            foreach (var chord in chords)
            {
                bool available = chord.Inputs.Length > 0, matchesSource = hand == 0;
                foreach (var binding in chord.Inputs)
                {
                    matchesSource |= binding.Hand == hand;
                    available &= Available(binding) && BindingActive(binding);
                }
                if (available && matchesSource) return true;
            }
        if (path.EndsWith("/posel", StringComparison.OrdinalIgnoreCase)) return Device(1) != null;
        if (path.EndsWith("/poser", StringComparison.OrdinalIgnoreCase)) return Device(2) != null;
        return false;
    }
    internal static VRPoseState ReadPose(string path, VRInputSource source)
    {
        int hand = Hand(source);
        if (hand == 0) hand = path.EndsWith("/posel", StringComparison.OrdinalIgnoreCase) ? 1 :
            path.EndsWith("/poser", StringComparison.OrdinalIgnoreCase) ? 2 : path == "/user/head" ? 3 : 0;
        var device = Device(hand);
        var position = Control<Vector3Control>(device, "devicePosition")?.ReadValue() ?? Vector3.zero;
        var rotation = Control<QuaternionControl>(device, "deviceRotation")?.ReadValue() ?? Quaternion.identity;
        var tracking = Control<ButtonControl>(device, "isTracked");
        var trackingState = Control<IntegerControl>(device, "trackingState");
        // A lost device may retain its last nonzero position. Only native tracking
        // state can validate it; a remembered position must never keep a grip alive.
        bool valid = device != null && (tracking != null ? tracking.isPressed :
            trackingState != null && (trackingState.ReadValue() & 3) == 3);
        var velocity = Control<Vector3Control>(device, "deviceVelocity")?.ReadValue() ?? Vector3.zero;
        if (!PoseSamples.TryGetValue(hand, out var previous)) PoseSamples[hand] = previous = new PoseSample();
        if (!valid)
        {
            velocity = Vector3.zero;
            previous.Valid = false;
            previous.Velocity = Vector3.zero;
        }
        else if (previous.Frame != Time.frameCount || !previous.Valid)
        {
            float dt = Mathf.Clamp(Time.unscaledTime - previous.Time, 1f / 240f, .25f);
            if (velocity.sqrMagnitude < .0001f && previous.Valid) velocity = (position - previous.Position) / dt;
            previous.Frame = Time.frameCount; previous.Valid = valid; previous.Position = position;
            previous.Velocity = velocity; previous.Time = Time.unscaledTime;
        }
        else velocity = previous.Velocity;
        return new VRPoseState { Position = position, Rotation = rotation, Velocity = velocity,
            AngularVelocity = valid ? Control<Vector3Control>(device, "deviceAngularVelocity")?.ReadValue() ?? Vector3.zero : Vector3.zero,
            Valid = valid, Connected = device != null };
    }
    internal static VRHandControls ReadHandControls(VRInputSource source)
    {
        var device = Device(Hand(source));
        bool Touch(string name, string alias) => (Control<ButtonControl>(device, name) ?? Control<ButtonControl>(device, alias))?.isPressed ?? false;
        return new VRHandControls { Grip = Control<AxisControl>(device, "grip")?.ReadValue() ?? 0,
            Trigger = Control<AxisControl>(device, "trigger")?.ReadValue() ?? 0,
            PrimaryTouched = Touch("primaryTouched", "primaryTouch"), SecondaryTouched = Touch("secondaryTouched", "secondaryTouch"),
            ThumbstickTouched = Touch("thumbstickTouched", "primary2DAxisTouch"), TriggerTouched = Touch("triggerTouched", "triggerTouch") };
    }
    internal static void ListenDigital(string path, VRInputSource source, bool down, Action<VRInputSource> callback) =>
        DigitalListeners.Add(new DigitalListener { Path = path, Source = source, Down = down, Callback = callback });
    internal static void ListenAxis(string path, VRInputSource source, Action<VRInputSource, Vector2, Vector2> callback) =>
        AxisListeners.Add(new AxisListener { Path = path, Source = source, Callback = callback });
    internal static void ListenUpdates(Action callback) { if (UpdateListeners.Add(callback)) Updated += callback; }
    internal static void RemoveUpdateListener(Action callback) { if (UpdateListeners.Remove(callback)) Updated -= callback; }
    internal static void Haptic(VRInputSource source, float delay, float duration, float amplitude)
    {
        var pulse = new Pulse(source, Time.unscaledTime + Mathf.Max(0, delay), Mathf.Clamp(duration, 0, 5), Mathf.Clamp01(amplitude));
        if (delay > 0) Pulses.Add(pulse); else SendPulse(pulse);
    }
    static void SendPulse(Pulse pulse)
    {
        if (pulse.Source == VRInputSource.Any)
        {
            SendPulse(new Pulse(VRInputSource.LeftHand, pulse.At, pulse.Duration, pulse.Amplitude));
            SendPulse(new Pulse(VRInputSource.RightHand, pulse.At, pulse.Duration, pulse.Amplitude));
            return;
        }
        int hand = Hand(pulse.Source);
        if (hand != 1 && hand != 2) return;
        if (Device(hand) is XRControllerWithRumble device) device.SendImpulse(pulse.Amplitude, pulse.Duration);
        else
        {
            var native = InputDevices.GetDeviceAtXRNode(hand == 1 ? XRNode.LeftHand : XRNode.RightHand);
            if (native.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse) native.SendHapticImpulse(0, pulse.Amplitude, pulse.Duration);
        }
    }
}
