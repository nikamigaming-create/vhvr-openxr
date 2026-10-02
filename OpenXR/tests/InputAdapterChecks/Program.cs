using Nikami.OpenXR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using ValheimVRMod.VRCore.Backends;

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
        internal Controller(string hand)
        {
            Device.usages.Add(hand);
            Trigger = Device.Add("trigger", new AxisControl());
            Grip = Device.Add("grip", new AxisControl());
            Stick = Device.Add("thumbstick", new Vector2Control());
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
            SameFrameTrackingLoss, TrackingStateFallback, DeviceReplacement, HapticsAndShutdown, SteadyInputAllocations };
        int failures = 0;
        foreach (var scenario in scenarios)
        {
            try { scenario(); Console.WriteLine("PASS: " + scenario.Method.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL: " + scenario.Method.Name + ": " + error.Message); }
        }
        InputAdapter.Shutdown();
        Console.WriteLine($"{checks} checks; {failures} failed scenarios. Production InputAdapter + contract; fake clock/Unity controls, no native hardware.");
        return failures == 0 ? 0 : 1;
    }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
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
}
