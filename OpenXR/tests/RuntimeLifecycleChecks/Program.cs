#nullable enable
using Nikami.OpenXR;
using UnityEngine;
using UnityEngine.EventSystems;

static class Program
{
    static int checks;
    static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
    }
    static void Main()
    {
        var gate = new RenderFrameGate();
        var derived = typeof(Program).GetMethod(nameof(DerivedCallback), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var baseMethod = typeof(Program).GetMethod(nameof(BaseCallback), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var shield = new object();
        var otherShield = new object();
        Require(gate.TryEnter(shield, derived, 10), "derived callback runs");
        Require(gate.TryEnter(shield, baseMethod, 10), "nested base callback still runs");
        Require(!gate.TryEnter(shield, derived, 10), "second camera skips derived callback");
        Require(!gate.TryEnter(shield, baseMethod, 10), "second camera skips base callback");
        Require(gate.TryEnter(otherShield, derived, 10), "another component publishes independently");
        Require(gate.TryEnter(shield, derived, 11) && gate.TryEnter(shield, baseMethod, 11), "next frame publishes both callbacks");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 12; frame < 512; frame++)
        {
            gate.TryEnter(shield, derived, frame);
            gate.TryEnter(shield, baseMethod, frame);
            gate.TryEnter(shield, derived, frame);
        }
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "warmed frame gate does not allocate");
        gate.Clear();
        Require(gate.TryEnter(shield, derived, 511), "shutdown clears frame history");

        OpenXRInputFocus.Shutdown();
        OpenXRInputFocus.Install();
        OpenXRInputFocus.Install();
        Require(Application.FocusSubscribers == 1 && Application.QuitSubscribers == 1, "focus installation is idempotent");
        OpenXRPlugin.Ready = true;
        Chat.instance = new Chat();
        var selected = new GameObject();
        selected.transform.Parent = Chat.instance.m_input.transform;
        EventSystem.current = new EventSystem { currentSelectedGameObject = selected };
        Application.Focus(false);
        Application.Focus(true);
        Require(EventSystem.current.currentSelectedGameObject == null, "focus return releases selected chat child");
        Require(!Chat.instance.m_input.gameObject.Active && !Chat.instance.m_wasFocused, "focus return releases native chat capture");
        Require(Chat.instance.m_input.Text == "unsent draft", "focus return keeps the draft");
        OpenXRInputFocus.Shutdown();
        OpenXRInputFocus.Shutdown();
        Require(Application.FocusSubscribers == 0 && Application.QuitSubscribers == 0, "rollback removes all owned application callbacks");
        OpenXRInputFocus.Install();
        Application.Quit();
        Require(Application.FocusSubscribers == 0 && Application.QuitSubscribers == 0, "quit restores callback ownership");
        Console.WriteLine($"PASS: {checks} production frame/focus checks. Engine events and UI are managed boundary doubles.");
    }
    static void DerivedCallback() { }
    static void BaseCallback() { }
}
