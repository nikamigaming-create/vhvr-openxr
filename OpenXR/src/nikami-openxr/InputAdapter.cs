using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using Valve.Newtonsoft.Json.Linq;
using Valve.VR;
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
        internal InputDigitalActionData_t Digital;
        internal InputAnalogActionData_t Analog;
    }
    sealed class PoseSample
    {
        internal int Frame = -1;
        internal bool Valid;
        internal Vector3 Position;
        internal Vector3 Velocity;
    }
    static readonly Dictionary<string, ulong> Handles = new(StringComparer.OrdinalIgnoreCase) {
        ["/user/hand/left"] = 1, ["/user/hand/right"] = 2, ["/user/head"] = 3
    };
    static readonly Dictionary<ulong, string> Paths = new() { [1] = "/user/hand/left", [2] = "/user/hand/right", [3] = "/user/head" };
    static readonly Dictionary<ulong, string> ActionPaths = new();
    static readonly Dictionary<string, List<Binding>> Bindings = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, List<Chord>> Chords = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<Binding> AllBindings = new();
    static readonly Dictionary<Binding, int> ActivePriorities = new();
    static readonly Dictionary<string, int> ControlPriorities = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<(ulong, ulong), Sample> Samples = new();
    static readonly Dictionary<ulong, PoseSample> PoseSamples = new();
    static readonly HashSet<string> Warned = new();
    static ulong nextHandle = 100;
    internal static void Install(Harmony harmony)
    {
        foreach (var method in typeof(CVRInput).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            if (method.ReturnType == typeof(EVRInputError)) harmony.Patch(method, new HarmonyMethod(typeof(InputAdapter), nameof(Dispatch)));
    }
    internal static void LoadBindings()
    {
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
    static void UpdateActionSets(VRActiveActionSet_t[] sets)
    {
        ActivePriorities.Clear();
        ControlPriorities.Clear();
        foreach (var set in sets)
        {
            if (!Paths.TryGetValue(set.ulActionSet, out var path)) continue;
            foreach (var binding in AllBindings)
            {
                if (!binding.Set.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                    (set.ulRestrictedToDevice != 0 && set.ulRestrictedToDevice != (ulong)binding.Hand)) continue;
                if (!ActivePriorities.TryGetValue(binding, out int priority) || set.nPriority > priority)
                    ActivePriorities[binding] = set.nPriority;
                if (!ControlPriorities.TryGetValue(binding.Path, out priority) || set.nPriority > priority)
                    ControlPriorities[binding.Path] = set.nPriority;
            }
        }
    }
    static bool BindingActive(Binding binding)
    {
        // VHVR gives its UI action set priority over gameplay. Without this
        // arbitration the Craft trigger also fires Use, closing the inventory
        // before Unity's UI can receive its pointer-release event.
        return ActivePriorities.TryGetValue(binding, out int priority) &&
            ControlPriorities.TryGetValue(binding.Path, out int highest) && priority == highest;
    }
    static ulong Handle(string path)
    {
        if (!Handles.TryGetValue(path ?? "", out var id)) { id = nextHandle++; Handles[path ?? ""] = id; Paths[id] = path ?? ""; }
        if (path != null && path.StartsWith("/actions/", StringComparison.OrdinalIgnoreCase)) ActionPaths[id] = path;
        return id;
    }
    internal static XRDevice Device(int hand)
    {
        foreach (var d in InputSystem.devices)
            if (hand == 3 ? d is XRHMD : d.usages.Contains(hand == 1 ? UnityEngine.InputSystem.CommonUsages.LeftHand : UnityEngine.InputSystem.CommonUsages.RightHand)) return d;
        return null;
    }
    internal static bool TryGetVelocity(int hand, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        if (hand < 1 || hand > 2 || !PoseSamples.TryGetValue((ulong)hand, out var sample) || !sample.Valid)
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
    static T Control<T>(XRDevice d, string name) where T : InputControl => d?.TryGetChildControl<T>(name);
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
    static Sample GetSample(ulong action, ulong source)
    {
        var key = (action, source);
        if (!Samples.TryGetValue(key, out var sample)) Samples[key] = sample = new();
        if (sample.Frame == Time.frameCount) return sample;
        sample.Frame = Time.frameCount;
        bool held = false, active = false;
        Vector2 axis = Vector2.zero;
        ulong origin = source;
        if (Paths.TryGetValue(action, out var path) && Bindings.TryGetValue(path, out var bindings))
        foreach (var b in bindings)
        {
            if (source != 0 && source != (ulong)b.Hand) continue;
            if (Device(b.Hand) == null || !BindingActive(b)) continue;
            active = true;
            var press = ReadButton(b);
            var value = ReadAxis(b);
            if (press || value.sqrMagnitude > axis.sqrMagnitude) origin = (ulong)b.Hand;
            held |= press;
            if (value.sqrMagnitude > axis.sqrMagnitude) axis = value;
        }
        if (Paths.TryGetValue(action, out path) && Chords.TryGetValue(path, out var chords))
        foreach (var chord in chords)
        {
            bool chordActive = chord.Inputs.Length > 0;
            bool chordMatchesSource = source == 0 || chord.Inputs.Any(binding => source == (ulong)binding.Hand);
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
                origin = (ulong)chord.Inputs[chord.Inputs.Length - 1].Hand;
            }
        }
        sample.Digital = new InputDigitalActionData_t { bActive = active, bState = held, bChanged = held != sample.Held, activeOrigin = origin };
        sample.Analog = new InputAnalogActionData_t { bActive = active, activeOrigin = origin, x = axis.x, y = axis.y, deltaX = axis.x - sample.Axis.x, deltaY = axis.y - sample.Axis.y };
        sample.Held = held; sample.Axis = axis;
        return sample;
    }
    static InputPoseActionData_t Pose(ulong action, ulong source)
    {
        int hand = (int)source;
        // SteamVR pose actions commonly request unrestricted action data
        // (ulRestrictToDevice == 0) and select the hand from the action path.
        // Digital actions happen to carry a hand source, which hid this gap
        // until the native weapon collider began consuming pose velocity.
        if ((hand < 1 || hand > 3) && ActionPaths.TryGetValue(action, out var actionPath))
        {
            hand = actionPath.EndsWith("/posel", StringComparison.OrdinalIgnoreCase) ? 1 :
                   actionPath.EndsWith("/poser", StringComparison.OrdinalIgnoreCase) ? 2 :
                   actionPath.EndsWith("/bodypose", StringComparison.OrdinalIgnoreCase) ? 3 : 0;
        }
        var device = hand >= 1 && hand <= 3 ? Device(hand) : null;
        var position = Control<Vector3Control>(device, "devicePosition")?.ReadValue() ?? Vector3.zero;
        bool tracked = Control<ButtonControl>(device, "isTracked")?.isPressed ?? false;
        // Meta XR Simulator publishes a valid pose before it raises the
        // optional isTracked button.  A non-zero controller pose is still a
        // real tracked sample; accepting it keeps the SteamVR action's
        // bPoseIsValid bit aligned with the data we are passing through.
        bool valid = device != null && (tracked || position.sqrMagnitude > .0001f);
        if (device != null && Warned.Add("pose-device-" + action))
            OpenXRPlugin.Log.LogInfo($"OpenXR pose device action={action}, hand={hand}, position={position}, tracked={tracked}, valid={valid}");
        if (Warned.Add("pose-request-" + action))
            OpenXRPlugin.Log.LogInfo($"OpenXR pose request action={action}, source={source}, path={(ActionPaths.TryGetValue(action, out var p) ? p : "<unknown>")}, hand={hand}, device={(device == null ? "null" : device.name)}, position={position}, valid={valid}");
        var rotation = Control<QuaternionControl>(device, "deviceRotation")?.ReadValue() ?? Quaternion.identity;
        var velocity = Control<Vector3Control>(device, "deviceVelocity")?.ReadValue() ?? Vector3.zero;
        // The Meta simulator updates the pose location but intentionally leaves
        // deviceVelocity at zero.  VHVR's native WeaponCollision reads the
        // SteamVR tracked-hand velocity (rather than differentiating the
        // rendered mesh), so preserve one velocity source by deriving a
        // frame-stamped velocity only when the runtime did not provide one.
        // Unrestricted requests use source=0 for both hands.  Keep history by
        // resolved hand so the left hand cannot overwrite the right hand's
        // previous position before velocity is derived.
        ulong sampleKey = (ulong)Mathf.Max(hand, 0);
        if (!PoseSamples.TryGetValue(sampleKey, out var previous)) PoseSamples[sampleKey] = previous = new PoseSample();
        if (previous.Frame != Time.frameCount)
        {
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 1f / 240f, .25f);
            if (velocity.sqrMagnitude < .0001f && previous.Valid && valid)
                velocity = (position - previous.Position) / dt;
            previous.Frame = Time.frameCount;
            previous.Valid = valid;
            previous.Position = position;
            previous.Velocity = velocity;
        }
        else
        {
            velocity = previous.Velocity;
        }
        if (velocity.sqrMagnitude > .0001f && Warned.Add("pose-velocity-" + hand))
            OpenXRPlugin.Log.LogInfo($"OpenXR pose velocity hand={hand}, velocity={velocity}, frame={Time.frameCount}");
        var angular = Control<Vector3Control>(device, "deviceAngularVelocity")?.ReadValue() ?? Vector3.zero;
        var matrix = new SteamVR_Utils.RigidTransform(position, rotation).ToHmdMatrix34();
        // For unrestricted pose requests the runtime passes source=0 while
        // the action path identifies the hand.  SteamVR_Action_Pose uses
        // activeOrigin to look up its source record; returning zero here makes
        // the pose appear valid to OpenXR but invisible to VHVR's hand and
        // weapon code.  Publish the resolved hand as the origin.
        return new InputPoseActionData_t {
            bActive = valid, activeOrigin = (ulong)Mathf.Max(hand, 0),
            pose = new TrackedDevicePose_t { bDeviceIsConnected = device != null, bPoseIsValid = valid,
                eTrackingResult = valid ? ETrackingResult.Running_OK : ETrackingResult.Uninitialized,
                vVelocity = new HmdVector3_t { v0 = velocity.x, v1 = velocity.y, v2 = -velocity.z },
                vAngularVelocity = new HmdVector3_t { v0 = -angular.x, v1 = -angular.y, v2 = angular.z },
                mDeviceToAbsoluteTracking = matrix }
        };
    }
    static bool Dispatch(MethodBase __originalMethod, object[] __args, ref EVRInputError __result)
    {
        __result = EVRInputError.None;
        switch (__originalMethod.Name)
        {
            case "GetActionHandle": case "GetActionSetHandle": case "GetInputSourceHandle": __args[1] = Handle((string)__args[0]); break;
            case "GetDigitalActionData": __args[1] = GetSample((ulong)__args[0], (ulong)__args[3]).Digital; break;
            case "GetAnalogActionData": __args[1] = GetSample((ulong)__args[0], (ulong)__args[3]).Analog; break;
            case "GetPoseActionDataForNextFrame": __args[2] = Pose((ulong)__args[0], (ulong)__args[4]); break;
            case "GetPoseActionDataRelativeToNow": __args[3] = Pose((ulong)__args[0], (ulong)__args[5]); break;
            case "GetOriginTrackedDeviceInfo":
                var origin = (ulong)__args[0];
                __args[1] = new InputOriginInfo_t { devicePath = origin, trackedDeviceIndex = origin == 1 ? 1u : origin == 2 ? 2u : origin == 3 ? 0u : uint.MaxValue }; break;
            case "TriggerHapticVibrationAction":
                int hand = (int)(ulong)__args[5];
                var haptic = Device(hand) as XRControllerWithRumble;
                float amplitude = Mathf.Clamp01((float)__args[4]), duration = Mathf.Clamp((float)__args[2], 0, 5);
                if (haptic != null) haptic.SendImpulse(amplitude, duration);
                else if (hand == 1 || hand == 2)
                {
                    var nativeDevice = InputDevices.GetDeviceAtXRNode(hand == 1 ? XRNode.LeftHand : XRNode.RightHand);
                    if (nativeDevice.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse) nativeDevice.SendHapticImpulse(0, amplitude, duration);
                }
                break;
            case "UpdateActionState": UpdateActionSets((VRActiveActionSet_t[])__args[0]); break;
            case "SetActionManifestPath": break;
            case "GetSkeletalActionData": __args[1] = new InputSkeletalActionData_t(); break;
            case "GetBoneCount": __args[1] = 0u; break;
            case "GetSkeletalTrackingLevel": __args[1] = EVRSkeletalTrackingLevel.VRSkeletalTracking_Estimated; break;
            case "GetOriginLocalizedName": ((System.Text.StringBuilder)__args[1]).Append("OpenXR controller"); break;
            default:
                // Unsupported optional SteamVR services never escape to openvr_api.dll.
                __result = EVRInputError.NoData;
                if (Warned.Add(__originalMethod.Name)) OpenXRPlugin.Log.LogDebug("OpenXR optional service unavailable: " + __originalMethod.Name);
                break;
        }
        return false;
    }
}
