using Nikami.OpenXR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using ValheimVRMod.VRCore.Backends;
using Valve.Newtonsoft.Json.Linq;

static class Program
{
    const string Use = "/actions/Valheim/in/Use", Grab = "/actions/Valheim/in/Grab";
    const string Walk = "/actions/Valheim/in/Walk", Split = "/actions/Valheim/in/SplitStack";
    static int checks;
    static Controller left, right;
    sealed class Controller
    {
        internal readonly XRControllerWithRumble Device = new();
        internal readonly AxisControl Trigger, Grip;
        internal readonly Vector2Control Stick;
        internal readonly ButtonControl Tracked;
        internal readonly Vector3Control Position, Velocity, AngularVelocity;
        internal Controller(string hand, string layout = "OculusTouchControllerOpenXR", string stickName = "thumbstick")
        {
            Device.layout = layout;
            Device.usages.Add(hand);
            Trigger = Device.Add("trigger", new AxisControl());
            Grip = Device.Add("grip", new AxisControl());
            Stick = Device.Add(stickName, new Vector2Control());
            Tracked = Device.Add("isTracked", new ButtonControl { Value = 1 });
            Position = Device.Add("devicePosition", new Vector3Control());
            Velocity = Device.Add("deviceVelocity", new Vector3Control());
            AngularVelocity = Device.Add("deviceAngularVelocity", new Vector3Control());
            Device.Add("deviceRotation", new QuaternionControl());
        }
    }
    static int Main()
    {
        Action[] scenarios = { SkippedReads, SourceEdgesAndCallbacks, ChordsAndAxis, ActionSetsAndHysteresis,
            SameFrameTrackingLoss, TrackingStateFallback, DeviceReplacement, HapticsAndShutdown, SteadyInputAllocations,
            ControllerProfiles, TrackpadModes, SavedLayoutsAndRecovery, InvalidSavedLayout, BackendConfigChoices };
        int failures = 0;
        foreach (var scenario in scenarios)
        {
            try { scenario(); Console.WriteLine("PASS: " + scenario.Method.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL: " + scenario.Method.Name + ": " + error.Message); }
        }
        InputAdapter.Shutdown();
        BindingProfiles.Configure(null);
        Console.WriteLine($"{checks} checks; {failures} failed scenarios. Production InputAdapter + contract; fake clock/Unity controls, no native hardware.");
        return failures == 0 ? 0 : 1;
    }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }
    static void BackendConfigChoices()
    {
        var choices = new BackendChoices();
        Require((string)choices.Clamp("OpenXR") == "openxr" && choices.IsValid("OpenXR"), "case-insensitive backend config remains valid");
        Require((string)choices.Clamp("STEAMVR") == "steamvr" && choices.IsValid("STEAMVR"), "legacy SteamVR config alias remains valid");
        Require((string)choices.Clamp("invalid") == "invalid" && !choices.IsValid("invalid"), "invalid backend survives BepInEx clamp for host rejection");
        Require(choices.AcceptableValues.SequenceEqual(new[] { "openxr", "openvr", "steamvr" }), "native backend chooser has both providers and legacy alias");
    }
    static void Reset(string fixture = "upstream")
    {
        InputAdapter.Shutdown();
        InputSystem.devices.Clear();
        Time.frameCount = 0; Time.unscaledTime = 0;
        left = new Controller(CommonUsages.LeftHand); right = new Controller(CommonUsages.RightHand);
        InputSystem.devices.Add(left.Device); InputSystem.devices.Add(right.Device);
        Application.streamingAssetsPath = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);
        InputAdapter.Install(); InputAdapter.LoadBindings();
        if (fixture == "upstream") InputAdapter.SetActionSet("/actions/Valheim", VRInputSource.Any, true, 0, false);
    }
    static void Frame()
    {
        Time.frameCount++; Time.unscaledTime += 1f / 90;
        InputAdapter.Pump();
    }
    static VRDigitalState Digital(string path, VRInputSource source = VRInputSource.LeftHand) => InputAdapter.ReadDigital(path, source);
    static void SkippedReads()
    {
        Reset(); Frame();
        left.Trigger.Value = 1; Frame();
        // The game did not ask for a left trigger until the crossbow became usable.
        Frame();
        var firstRead = Digital(Use);
        Require(firstRead.Held && !firstRead.Down, "Reading an already held trigger must not invent a new press.");
        left.Trigger.Value = 0; Frame(); // No read on release.
        left.Trigger.Value = 1; Frame();
        Require(Digital(Use).Down, "A release/press during skipped gameplay reads must still produce the new press.");
        left.Trigger.Value = 0; Frame(); Frame();
        Require(!Digital(Use).Up, "A release edge must not be delivered several frames late.");
    }
    static void SourceEdgesAndCallbacks()
    {
        Reset();
        int downs = 0, ups = 0, updates = 0;
        InputAdapter.ListenDigital(Use, VRInputSource.LeftHand, true, source => { Require(source == VRInputSource.LeftHand, "Callback source."); downs++; });
        InputAdapter.ListenDigital(Use, VRInputSource.LeftHand, false, _ => ups++);
        Action update = () => updates++;
        InputAdapter.ListenUpdates(update); InputAdapter.ListenUpdates(update);
        Frame(); left.Trigger.Value = 1; Frame();
        Require(Digital(Use).Down && Digital(Use, VRInputSource.Any).Down, "Left and Any have the same real first press.");
        Require(!Digital(Use, VRInputSource.RightHand).Held && !Digital(Use, VRInputSource.Head).Held, "Unpressed sources remain clear.");
        InputAdapter.Pump();
        Require(downs == 1 && updates == 2, "Repeated Pump or duplicate update subscription must not dispatch twice.");
        right.Trigger.Value = 1; Frame();
        Require(Digital(Use, VRInputSource.RightHand).Down && !Digital(Use, VRInputSource.Any).Down, "Any stays held while the other hand presses.");
        left.Trigger.Value = 0; Frame();
        Require(Digital(Use).Up && !Digital(Use, VRInputSource.Any).Up && ups == 1, "Any stays held while one hand releases.");
        Require(Digital("/ACTIONS/VALHEIM/IN/USE", VRInputSource.Any).Held, "Mixed casing resolves the same imported action.");
        InputAdapter.RemoveUpdateListener(update); Frame();
        Require(updates == 4, "Removed update listener stays removed.");
    }
    static void ChordsAndAxis()
    {
        Reset(); Frame();
        Vector2 delivered = default, delta = default; int changes = 0;
        InputAdapter.ListenAxis(Walk, VRInputSource.LeftHand, (_, value, change) => { delivered = value; delta = change; changes++; });
        left.Stick.Value = new Vector2(.2f, .4f); left.Grip.Value = 1; right.Trigger.Value = 1; Frame();
        Require(changes == 1 && delivered.x == .2f && delta.y == .4f, "Axis callback carries the real value and delta.");
        Require(Digital(Split).Down && Digital(Split, VRInputSource.RightHand).Down && Digital(Split, VRInputSource.Any).Down, "Cross-hand chord is available on its participating sources and Any.");
        Require(!Digital(Split, VRInputSource.Head).Held && !InputAdapter.IsBound(Split, VRInputSource.Head), "Unrelated source cannot activate a chord.");
        Frame(); Require(changes == 1 && !Digital(Split).Down, "Held axes/chords do not repeat edges.");
        right.Trigger.Value = 0; Frame(); Require(Digital(Split).Up, "Releasing either chord input produces release.");
        Require(InputAdapter.IsBound(Split, VRInputSource.LeftHand), "A released chord can remain bound.");
    }
    static void ActionSetsAndHysteresis()
    {
        Reset("priority");
        const string fire = "/actions/gameplay/in/Fire", select = "/actions/ui/in/Select";
        InputAdapter.SetActionSet("/actions/gameplay", VRInputSource.Any, true, 0, false);
        left.Trigger.Value = .75f; Frame(); Require(!Digital(fire).Held, "Imported press threshold is used.");
        left.Trigger.Value = .85f; Frame(); Require(Digital(fire).Down, "Crossing press threshold produces Down.");
        left.Trigger.Value = .75f; Frame(); Require(Digital(fire).Held && !Digital(fire).Up, "Release hysteresis is preserved.");
        left.Trigger.Value = .65f; Frame(); Require(Digital(fire).Up, "Crossing release threshold produces Up.");
        left.Trigger.Value = 1; right.Trigger.Value = 1; Frame();
        Require(Digital(fire).Held, "Gameplay is sampled before changing priority.");
        InputAdapter.SetActionSet("/actions/ui", VRInputSource.LeftHand, true, 7, false);
        Require(Digital(fire).Held, "Set changes preserve the already published input snapshot until the next Pump.");
        Frame();
        Require(!Digital(fire).Active && Digital(fire, VRInputSource.RightHand).Held, "Higher-priority UI masks only its matching control/hand.");
        Require(Digital(select).Held && InputAdapter.IsActionSetActive("/ACTIONS/UI", VRInputSource.LeftHand), "Case-insensitive set query and UI activation.");
        InputAdapter.SetActionSet("/actions/ui", VRInputSource.LeftHand, false, 0, false); Frame();
        Require(Digital(fire).Held && !Digital(select).Active, "Deactivation restores gameplay.");
        InputAdapter.SetActionSet("/actions/ui", VRInputSource.LeftHand, true, 7, true); Frame();
        Require(!InputAdapter.IsActionSetActive("/actions/gameplay", VRInputSource.Any) && !Digital(fire, VRInputSource.RightHand).Active, "Exclusive activation disables the other set.");
    }
    static void SameFrameTrackingLoss()
    {
        Reset();
        left.Velocity.Value = new Vector3(2, 3, 4); left.AngularVelocity.Value = new Vector3(1, 2, 3);
        var moving = InputAdapter.ReadPose("/actions/Valheim/in/PoseL", VRInputSource.LeftHand);
        Require(moving.Valid && moving.Velocity.x == 2, "Tracked velocity reaches gameplay.");
        left.Tracked.Value = 0;
        var lost = InputAdapter.ReadPose("/actions/Valheim/in/PoseL", VRInputSource.LeftHand);
        Require(!lost.Valid && lost.Connected && lost.Velocity.sqrMagnitude == 0 && lost.AngularVelocity.sqrMagnitude == 0, "Same-frame loss must discard cached motion.");
        left.Position.Value = new Vector3(100, 0, 0); left.Velocity.Value = Vector3.zero; left.Tracked.Value = 1;
        var regained = InputAdapter.ReadPose("/actions/Valheim/in/PoseL", VRInputSource.LeftHand);
        Require(regained.Valid && regained.Velocity.sqrMagnitude == 0, "Same-frame regain must not convert a tracking jump to velocity.");
        Frame(); left.Position.Value = new Vector3(101, 0, 0);
        var derived = InputAdapter.ReadPose("/actions/Valheim/in/PoseL", VRInputSource.LeftHand);
        Require(derived.Velocity.x > 80 && derived.Velocity.x < 100, "Ordinary consecutive tracked positions still provide fallback velocity.");
    }
    static void TrackingStateFallback()
    {
        Reset();
        var head = new XRHMD();
        var state = head.Add("trackingState", new IntegerControl { Value = 3 });
        head.Add("devicePosition", new Vector3Control()); head.Add("deviceRotation", new QuaternionControl());
        InputSystem.devices.Add(head); InputSystem.Notify(head, InputDeviceChange.Added);
        Require(InputAdapter.ReadPose("/user/head", VRInputSource.Head).Valid, "Position and rotation tracking validate devices without isTracked.");
        state.Value = 1;
        Require(!InputAdapter.ReadPose("/user/head", VRInputSource.Head).Valid, "Partial tracking state does not validate a pose.");
    }
    static void DeviceReplacement()
    {
        Reset(); left.Trigger.Value = 1; Frame(); Require(Digital(Use).Held, "Original device is sampled.");
        InputSystem.devices.Remove(left.Device); left.Device.added = false;
        InputSystem.Notify(left.Device, InputDeviceChange.Removed); Frame();
        Require(!Digital(Use).Held && Digital(Use).Up && !InputAdapter.IsBound(Use, VRInputSource.LeftHand), "Device removal clears cached controls and releases the action.");
        left = new Controller(CommonUsages.LeftHand); InputSystem.devices.Add(left.Device);
        InputSystem.Notify(left.Device, InputDeviceChange.Added); left.Trigger.Value = 1; Frame();
        Require(Digital(Use).Down && InputAdapter.IsBound(Use, VRInputSource.LeftHand), "Replacement device gets fresh controls and input edges.");
    }
    static void HapticsAndShutdown()
    {
        Reset();
        InputAdapter.Haptic(VRInputSource.Any, .1f, .25f, .5f);
        Frame(); Require(left.Device.Pulses.Count == 0, "Delayed haptic does not fire early.");
        Time.unscaledTime = .2f; Time.frameCount++; InputAdapter.Pump();
        Require(left.Device.Pulses.Count == 1 && right.Device.Pulses.Count == 1 && left.Device.Pulses[0] == (.5f, .25f), "Any haptic routes the requested amplitude and duration to both hands.");
        int callbacks = 0; InputAdapter.ListenDigital(Use, VRInputSource.LeftHand, true, _ => callbacks++);
        InputAdapter.Haptic(VRInputSource.LeftHand, 1, .1f, 1); InputAdapter.Shutdown();
        left.Trigger.Value = 1; Time.unscaledTime = 2; Time.frameCount++; InputAdapter.Pump();
        Require(callbacks == 0 && left.Device.Pulses.Count == 1, "Shutdown clears listeners and delayed pulses.");
        Require(!InputAdapter.IsBound(Use, VRInputSource.LeftHand) && !Digital(Use).Active, "Shutdown clears action binding state.");
    }
    static void SteadyInputAllocations()
    {
        Reset(); left.Trigger.Value = 1; right.Grip.Value = 1; left.Stick.Value = new Vector2(.2f, .3f);
        for (int i = 0; i < 200; i++) AllocationFrame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++) AllocationFrame();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, $"Steady sampling/case-insensitive reads/binding queries allocated {allocated} bytes over 500 frames.");
    }
    static void AllocationFrame()
    {
        Frame(); Digital(Use); Digital("/ACTIONS/VALHEIM/IN/USE", VRInputSource.Any);
        Digital(Grab, VRInputSource.RightHand); InputAdapter.ReadAxis(Walk, VRInputSource.LeftHand);
        InputAdapter.IsBound(Split, VRInputSource.LeftHand); InputAdapter.IsActionSetActive("/actions/Valheim", VRInputSource.Any);
    }
    static void ReplaceControllers(string layout, string stickName = "thumbstick")
    {
        InputSystem.devices.Clear();
        left = new Controller(CommonUsages.LeftHand, layout, stickName);
        right = new Controller(CommonUsages.RightHand, layout, stickName);
        InputSystem.devices.Add(left.Device); InputSystem.devices.Add(right.Device);
        InputSystem.Notify(left.Device, InputDeviceChange.Added);
    }
    static void ControllerProfiles()
    {
        Reset();
        Require(BindingProfiles.Find("knuckles") != null && BindingProfiles.Find("vive_controller") != null && BindingProfiles.Find("holographic_controller") != null, "All upstream controller layouts are discovered from the action manifest.");
        ReplaceControllers("ValveIndexController");
        var force = left.Device.Add("trackpadForce", new AxisControl());
        left.Device.Add("trackpad", new Vector2Control());
        left.Device.Add("primaryButton", new ButtonControl());
        Frame();
        Require(InputAdapter.ProfileForHand(1).Id == "knuckles", "Index selects the upstream Knuckles layout.");
        left.Grip.Value = 1; force.Value = 0; Frame();
        const string menu = "/actions/Valheim/in/ToggleMenu";
        Require(Digital(Grab).Held && !Digital(menu).Held, "Index keeps the stock grip binding and does not use grip closure for its trackpad-force menu binding.");
        force.Value = 1; Frame();
        Require(Digital(menu).Down, "Index trackpad force threshold presses ToggleMenu.");
        force.Value = 0; Frame(); Require(Digital(menu).Up, "Index force release ends ToggleMenu.");
        left.Stick.Value = new Vector2(.2f, .4f); Frame();
        Require(InputAdapter.ReadAxis(Walk, VRInputSource.LeftHand).y == .4f, "Index thumbstick spelling maps to Walk.");
        right.Device.layout = "OculusTouchControllerOpenXR";
        InputSystem.Notify(right.Device, InputDeviceChange.ConfigurationChanged); Frame();
        Require(InputAdapter.ProfileForHand(1).Id == "knuckles" && InputAdapter.ProfileForHand(2).Id == "oculus_touch", "Mixed controllers select a layout for each hand.");
        ReplaceControllers("WMRSpatialController", "joystick");
        left.Device.Add("touchpad", new Vector2Control()); right.Device.Add("touchpad", new Vector2Control());
        Frame(); left.Stick.Value = new Vector2(.1f, 0); Frame();
        Require(InputAdapter.ProfileForHand(1).Id == "holographic_controller" && InputAdapter.ReadAxis(Walk, VRInputSource.LeftHand).sqrMagnitude == 0, "WMR selects its layout and preserves its stock walk deadzone.");
        left.Stick.Value = new Vector2(.6f, 0); Frame();
        Require(Math.Abs(InputAdapter.ReadAxis(Walk, VRInputSource.LeftHand).x - .5f) < .001f, "WMR native joystick alias and deadzone rescale work.");
        ReplaceControllers("KHRSimpleController", "absentStick"); Frame();
        Require(InputAdapter.ProfileForHand(1) == null && !InputAdapter.IsBound(Walk, VRInputSource.LeftHand), "A controller without enough controls is not advertised as a bound Touch controller.");
    }
    static void TrackpadModes()
    {
        Reset(); ReplaceControllers("ViveController", "trackpad");
        var clicked = left.Device.Add("trackpadClicked", new ButtonControl());
        left.Device.Add("trackpadTouched", new ButtonControl { Value = 1 });
        Frame();
        Require(InputAdapter.ProfileForHand(1).Id == "vive_controller", "Vive selects its trackpad layout.");
        left.Stick.Value = new Vector2(.2f, .4f); Frame();
        Require(InputAdapter.ReadAxis(Walk, VRInputSource.LeftHand).y == .4f, "Vive trackpad position maps to Walk.");
        const string north = "/actions/Valheim/in/Jump";
        // Make a focused layout fixture using the real controller-mode parser.
        var profile = InputAdapter.ProfileForHand(1);
        var doc = (JObject)profile.Current.DeepClone();
        ((JArray)GameSet(doc)["sources"]).Add(new JObject {
            ["path"] = "/user/hand/left/input/trackpad", ["mode"] = "dpad",
            ["parameters"] = new JObject { ["sub_mode"] = "click", ["deadzone_pct"] = 25 },
            ["inputs"] = new JObject { ["north"] = new JObject { ["output"] = north } }
        });
        foreach (var source in ((JArray)GameSet(doc)["sources"]).OfType<JObject>().ToArray())
            if ((string)source["mode"] != "dpad") foreach (var input in ((JObject)source["inputs"]).Properties().ToArray())
                if (string.Equals((string)input.Value["output"], north, StringComparison.OrdinalIgnoreCase)) input.Remove();
        profile.Current = doc; InputAdapter.RefreshBindings(); Frame();
        left.Stick.Value = new Vector2(0, .8f); Frame(); Require(!Digital(north).Held, "Click-gated pad direction does not fire on position alone.");
        clicked.Value = 1; Frame(); Require(Digital(north).Down, "Click plus pad direction triggers the rebound action.");
        left.Stick.Value = new Vector2(.8f, 0); Frame(); Require(Digital(north).Up, "Pad direction respects its angular sector.");
        clicked.Value = 0;
        doc = (JObject)profile.Current.DeepClone();
        ((JArray)GameSet(doc)["sources"]).Add(new JObject {
            ["path"] = "/user/hand/left/input/trackpad", ["mode"] = "scroll",
            ["inputs"] = new JObject { ["scroll"] = new JObject { ["output"] = Walk } }
        });
        foreach (var source in ((JArray)GameSet(doc)["sources"]).OfType<JObject>().ToArray())
            if ((string)source["mode"] != "scroll") foreach (var input in ((JObject)source["inputs"]).Properties().ToArray())
                if (string.Equals((string)input.Value["output"], Walk, StringComparison.OrdinalIgnoreCase)) input.Remove();
        profile.Current = doc; InputAdapter.RefreshBindings(); Frame();
        left.Stick.Value = new Vector2(.6f, .3f); Frame();
        var scroll = InputAdapter.ReadAxis(Walk, VRInputSource.LeftHand);
        Require(Math.Abs(scroll.x + .2f) < .001f && Math.Abs(scroll.y - .3f) < .001f, "Trackpad scroll reports movement rather than held position.");
        Require(InputAdapter.ReadAxis(Walk, VRInputSource.Any).x == scroll.x, "Any and individual sources share one scroll sample.");
        Frame(); Require(InputAdapter.ReadAxis(Walk, VRInputSource.LeftHand).sqrMagnitude == 0, "Stationary trackpad does not continue scrolling.");
    }
    static JObject GameSet(JObject doc) => (JObject)((JObject)doc["bindings"]).Properties().First(property => property.Name.Equals("/actions/Valheim", StringComparison.OrdinalIgnoreCase)).Value;
    static JObject Rebound(BindingProfile profile)
    {
        var doc = (JObject)profile.Defaults.DeepClone();
        foreach (var set in ((JObject)doc["bindings"]).Properties())
        foreach (var source in set.Value["sources"] ?? new JArray())
        foreach (var input in ((JObject)source["inputs"]).Properties().ToArray())
            if (string.Equals((string)input.Value["output"], Use, StringComparison.OrdinalIgnoreCase) || ((string)input.Value["output"]).EndsWith("/LeftClick", StringComparison.OrdinalIgnoreCase)) input.Remove();
        ((JArray)GameSet(doc)["sources"]).Add(new JObject {
            ["path"] = "/user/hand/left/input/x", ["mode"] = "button",
            ["inputs"] = new JObject { ["click"] = new JObject { ["output"] = Use } }
        });
        return doc;
    }
    static void SavedLayoutsAndRecovery()
    {
        Reset();
        string root = Path.Combine(Path.GetTempPath(), "vhvr-bindings-" + Guid.NewGuid().ToString("N"));
        BindingProfiles.Configure(root);
        var profile = BindingProfiles.Find("oculus_touch");
        string defaults = profile.Defaults.ToString();
        var button = left.Device.Add("primaryButton", new ButtonControl());
        InputSystem.Notify(left.Device, InputDeviceChange.ConfigurationChanged);
        Frame();
        BindingProfiles.Save(profile, Rebound(profile));
        Require(File.Exists(BindingProfiles.PersonalPath(profile)), "Personal JSON is saved outside shipped defaults.");
        Require(profile.Defaults.ToString() == defaults, "Saving leaves the upstream layout unchanged.");
        InputAdapter.RefreshBindings(); button.Value = 1; Frame();
        Require(Digital(Use).Held && !Digital(Use).Down, "Applying a binding does not invent a press for a held button.");
        button.Value = 0; Frame(); button.Value = 1; Frame(); Require(Digital(Use).Down, "The saved replacement actually drives gameplay input.");
        const string click = "/actions/Valheim/in/LeftClick";
        Frame(); Require(!InputAdapter.IsBound(click, VRInputSource.LeftHand), "A removed pointer action is reported unbound.");
        InputAdapter.EditBindings(true); Frame(); left.Trigger.Value = 1; Frame();
        Require(InputAdapter.IsBound(click, VRInputSource.LeftHand) && Digital(click).Down, "The binding editor restores default pointer controls even for a broken personal layout.");
        InputAdapter.EditBindings(false); Frame(); Require(!InputAdapter.IsBound(click, VRInputSource.LeftHand), "Cancel returns to the previous personal layout.");
        InputAdapter.LoadBindings(); Frame();
        Require(Digital(Use).Held && !Digital(Use).Down, "A saved layout survives a reload without a synthetic press.");
        profile = BindingProfiles.Find("oculus_touch");
        BindingProfiles.Save(profile, Rebound(profile));
        Require(File.Exists(BindingProfiles.PersonalPath(profile) + ".bak"), "Replacing a save keeps the previous personal layout.");
        BindingProfiles.Reset(profile); InputAdapter.RefreshBindings(); Frame();
        left.Trigger.Value = 0; Frame(); left.Trigger.Value = 1; Frame();
        Require(Digital(Use).Down && !File.Exists(BindingProfiles.PersonalPath(profile)), "Reset restores upstream controls and removes the active override.");
        Require(File.Exists(BindingProfiles.PersonalPath(profile) + ".bak"), "Reset keeps a recoverable backup.");
        BindingProfiles.Configure(null);
    }
    static void InvalidSavedLayout()
    {
        Reset();
        string root = Path.Combine(Path.GetTempPath(), "vhvr-bad-bindings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); BindingProfiles.Configure(root);
        var profile = BindingProfiles.Find("oculus_touch");
        string path = BindingProfiles.PersonalPath(profile);
        File.WriteAllText(path, "{broken"); InputAdapter.LoadBindings();
        profile = BindingProfiles.Find("oculus_touch");
        Require(profile.LoadError != null && File.ReadAllText(path) == "{broken", "Corrupt saves fall back to defaults without destroying the user's file.");
        Frame(); left.Trigger.Value = 1; Frame(); Require(Digital(Use).Down, "Fallback defaults remain usable.");
        var wrong = Rebound(profile); wrong["controller_type"] = "knuckles";
        bool rejected = false;
        try { BindingProfiles.Save(profile, wrong); } catch (ArgumentException) { rejected = true; }
        Require(rejected && File.ReadAllText(path) == "{broken", "A mismatched controller file cannot replace the current save.");
        wrong = Rebound(profile); GameSet(wrong)["sources"][0]["parameters"] = new JObject { ["click_activate_threshold"] = .2f, ["click_deactivate_threshold"] = .9f };
        rejected = false; try { BindingProfiles.Save(profile, wrong); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Invalid threshold ordering is rejected before writing.");
        wrong = Rebound(profile); GameSet(wrong)["sources"][0]["parameters"] = new JArray(.5f);
        rejected = false; try { BindingProfiles.Save(profile, wrong); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Malformed parameter arrays cannot replace a personal layout.");
        wrong = Rebound(profile); GameSet(wrong)["sources"][0]["inputs"] = new JObject { ["click"] = 1 };
        File.WriteAllText(path, wrong.ToString()); InputAdapter.LoadBindings();
        Require(BindingProfiles.Find("oculus_touch").LoadError != null, "Malformed input components fall back to defaults instead of failing startup.");
        BindingProfiles.Configure(null);
    }
}
