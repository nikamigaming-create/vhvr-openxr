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
        internal string Output, Set, Path, Component, Mode;
        internal int Hand;
        internal float Press = .55f, Release = .45f;
        internal bool Held;
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
    static readonly Dictionary<string, List<Binding>> Bindings = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, List<Chord>> Chords = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<Binding> AllBindings = new();
    static readonly Dictionary<Binding, int> ActivePriorities = new();
    static readonly Dictionary<string, int> ControlPriorities = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<(string, int), Sample> Samples = new();
    static readonly Dictionary<int, PoseSample> PoseSamples = new();
    static readonly HashSet<string> Warned = new();
    static readonly XRDevice[] Devices = new XRDevice[4];
    static readonly Dictionary<XRDevice, Dictionary<string, InputControl>> Controls = new();
    static bool devicesResolved;
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
        if (dispatchedFrame == Time.frameCount) return;
        dispatchedFrame = Time.frameCount;
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
        Sets.Clear(); Pulses.Clear(); Samples.Clear(); dispatchedFrame = -1;
        DeviceChanged(null, default);
    }
    static void DeviceChanged(XRDevice device, InputDeviceChange change)
    {
        devicesResolved = false;
        Array.Clear(Devices, 0, Devices.Length);
        Controls.Clear();
        foreach (var sample in Samples.Values) sample.Frame = -1;
        PoseSamples.Clear();
    }
    internal static void LoadBindings()
    {
        Bindings.Clear(); Chords.Clear(); AllBindings.Clear(); Samples.Clear();
        var file = Path.Combine(Application.streamingAssetsPath, "SteamVR", "bindings_oculus_touch.json");
        var root = JObject.Parse(File.ReadAllText(file));
        foreach (var set in ((JObject)root["bindings"]).Properties())
        {
            foreach (var source in set.Value["sources"] ?? new JArray())
            foreach (var input in ((JObject)source["inputs"]).Properties())
            {
                var b = MakeBinding((string)input.Value["output"], (string)source["path"], input.Name,
                    (string)source["mode"], source["parameters"]);
                if (!Bindings.TryGetValue(b.Output, out var list)) Bindings[b.Output] = list = new();
                list.Add(b);
            }
            foreach (JObject chordJson in set.Value["chords"] ?? new JArray())
            {
                var chord = new Chord { Output = (string)chordJson["output"] };
                chord.Inputs = ((JArray)chordJson["inputs"]).Select(input =>
                    MakeBinding(chord.Output, (string)input[0], (string)input[1], "button", null)).ToArray();
                if (chord.Inputs.Length == 0) continue;
                if (!Chords.TryGetValue(chord.Output, out var list)) Chords[chord.Output] = list = new();
                list.Add(chord);
            }
        }
        int sourceCount = Bindings.Values.Sum(list => list.Count);
        int chordCount = Chords.Values.Sum(list => list.Count);
        int actionCount = Bindings.Keys.Union(Chords.Keys, StringComparer.OrdinalIgnoreCase).Count();
        OpenXRPlugin.Log.LogInfo($"Imported {actionCount} upstream Oculus Touch actions into OpenXR ({sourceCount} source bindings, {chordCount} chords).");
    }
    static Binding MakeBinding(string output, string path, string component, string mode, JToken parameters)
    {
        var binding = new Binding { Output = output, Set = output.Substring(0, output.IndexOf("/in/", StringComparison.OrdinalIgnoreCase)), Path = path, Component = component, Mode = mode };
        binding.Hand = path.Contains("/left/") ? 1 : path.Contains("/right/") ? 2 : 3;
        binding.Press = (float?)parameters?["click_activate_threshold"] ?? binding.Press;
        binding.Release = (float?)parameters?["click_deactivate_threshold"] ?? binding.Release;
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
        ActivePriorities.Clear(); ControlPriorities.Clear();
        foreach (var set in Sets)
        foreach (var binding in AllBindings)
        {
            if (!binding.Set.Equals(set.Path, StringComparison.OrdinalIgnoreCase) || (set.Hand != 0 && set.Hand != binding.Hand)) continue;
            if (!ActivePriorities.TryGetValue(binding, out int current) || set.Priority > current) ActivePriorities[binding] = set.Priority;
            if (!ControlPriorities.TryGetValue(binding.Path, out current) || set.Priority > current) ControlPriorities[binding.Path] = set.Priority;
        }
    }
    internal static bool IsActionSetActive(string path, VRInputSource source) => Hand(source) >= 0 &&
        Sets.Any(set => set.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && (set.Hand == 0 || set.Hand == Hand(source) || source == VRInputSource.Any));
    static bool BindingActive(Binding binding)
    {
        // VHVR gives its UI action set priority over gameplay. Without this
        // arbitration the Craft trigger also fires Use, closing the inventory
        // before Unity's UI can receive its pointer-release event.
        return ActivePriorities.TryGetValue(binding, out int priority) &&
            ControlPriorities.TryGetValue(binding.Path, out int highest) && priority == highest;
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
    internal static bool TryGetVelocity(int hand, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        if (hand < 1 || hand > 2 || !PoseSamples.TryGetValue(hand, out var sample) || !sample.Valid)
            return false;
        // PhysicsEstimator is normally sampled during FixedUpdate while the
        // OpenXR pose is published during the render/input update.  Accept a
        // short handoff window so the derived velocity remains available for
        // the collision tick without retaining stale swings after the hand
        // has stopped.
        if (sample.Frame < 0 || Time.frameCount - sample.Frame > 8 || sample.Velocity.sqrMagnitude < .0001f)
            return false;
        velocity = sample.Velocity;
        return true;
    }
    internal static T Control<T>(XRDevice d, string name) where T : InputControl
    {
        if (d == null) return null;
        if (!Controls.TryGetValue(d, out var controls)) Controls[d] = controls = new(StringComparer.OrdinalIgnoreCase);
        if (!controls.TryGetValue(name, out var control)) controls[name] = control = d.TryGetChildControl<InputControl>(name);
        return control as T;
    }
    static Vector2 ReadAxis(Binding b)
    {
        var d = Device(b.Hand);
        if (b.Path.EndsWith("joystick")) return (Control<Vector2Control>(d, "thumbstick") ?? Control<Vector2Control>(d, "primary2DAxis"))?.ReadValue() ?? Vector2.zero;
        if (b.Path.EndsWith("trigger")) return new Vector2(Control<AxisControl>(d, "trigger")?.ReadValue() ?? 0, 0);
        if (b.Path.EndsWith("grip")) return new Vector2(Control<AxisControl>(d, "grip")?.ReadValue() ?? 0, 0);
        return Vector2.zero;
    }
    static bool ReadButton(Binding b)
    {
        var d = Device(b.Hand);
        if (d == null) return b.Held = false;
        var path = b.Path.Substring(b.Path.LastIndexOf('/') + 1);
        float value;
        if (path == "joystick" && b.Mode == "dpad")
        {
            var axis = ReadAxis(b);
            value = b.Component switch { "north" => axis.y, "south" => -axis.y, "east" => axis.x, "west" => -axis.x, "center" => axis.magnitude < .25f ? 1 : 0, _ => 0 };
        }
        else if ((path == "trigger" || path == "grip") && b.Component != "touch") value = ReadAxis(b).x;
        else
        {
            string name = path switch {
                "a" or "x" => b.Component == "touch" ? "primaryTouched" : "primaryButton",
                "b" or "y" => b.Component == "touch" ? "secondaryTouched" : "secondaryButton",
                "joystick" => b.Component == "touch" ? "thumbstickTouched" : "thumbstickClicked",
                "trigger" => "triggerTouched", "application_menu" or "menu" => "menu", _ => path
            };
            string alias = name switch {
                "thumbstickClicked" => "primary2DAxisClick", "thumbstickTouched" => "primary2DAxisTouch",
                "triggerTouched" => "triggerTouch", "primaryTouched" => "primaryTouch", "secondaryTouched" => "secondaryTouch", _ => name
            };
            value = (Control<ButtonControl>(d, name) ?? Control<ButtonControl>(d, alias))?.ReadValue() ?? 0;
        }
        return b.Held = value >= (b.Held ? b.Release : b.Press);
    }
    static Sample GetSample(string path, int source)
    {
        var key = (path.ToLowerInvariant(), source);
        if (!Samples.TryGetValue(key, out var sample)) Samples[key] = sample = new();
        if (sample.Frame == Time.frameCount) return sample;
        sample.Frame = Time.frameCount;
        bool held = false, active = false;
        Vector2 axis = Vector2.zero;
        if (Bindings.TryGetValue(path, out var bindings))
        foreach (var b in bindings)
        {
            if (source != 0 && source != b.Hand) continue;
            if (Device(b.Hand) == null || !BindingActive(b)) continue;
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
            bool chordMatchesSource = source == 0 || chord.Inputs.Any(binding => source == binding.Hand);
            bool chordHeld = chordMatchesSource && chord.Inputs.Length > 0;
            foreach (var b in chord.Inputs)
            {
                if (Device(b.Hand) == null || !BindingActive(b))
                {
                    b.Held = false;
                    chordActive = chordHeld = false;
                    continue;
                }
                chordHeld &= ReadButton(b);
            }
            active |= chordActive && chordMatchesSource;
            if (chordHeld)
            {
                held = true;
            }
        }
        sample.Digital = new VRDigitalState { Active = active, Held = held, Down = held && !sample.Held, Up = !held && sample.Held };
        sample.Delta = axis - sample.Axis;
        sample.Held = held; sample.Axis = axis;
        return sample;
    }
    internal static VRDigitalState ReadDigital(string path, VRInputSource source) => GetSample(path, Hand(source)).Digital;
    internal static Vector2 ReadAxis(string path, VRInputSource source) => GetSample(path, Hand(source)).Axis;
    internal static bool IsBound(string path, VRInputSource source)
    {
        int hand = Hand(source);
        if (hand < 0) return false;
        bool Available(Binding binding) => (hand == 0 || binding.Hand == hand) && Device(binding.Hand) != null && BindingActive(binding);
        if (Bindings.TryGetValue(path, out var bindings) && bindings.Any(Available)) return true;
        if (Chords.TryGetValue(path, out var chords) && chords.Any(chord => chord.Inputs.Length > 0 &&
            (hand == 0 || chord.Inputs.Any(binding => binding.Hand == hand)) && chord.Inputs.All(binding => Device(binding.Hand) != null && BindingActive(binding)))) return true;
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
        if (!valid) velocity = Vector3.zero;
        if (!PoseSamples.TryGetValue(hand, out var previous)) PoseSamples[hand] = previous = new PoseSample();
        if (previous.Frame != Time.frameCount)
        {
            float dt = Mathf.Clamp(Time.unscaledTime - previous.Time, 1f / 240f, .25f);
            if (velocity.sqrMagnitude < .0001f && previous.Valid && valid) velocity = (position - previous.Position) / dt;
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
