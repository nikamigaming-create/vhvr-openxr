using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;
using TMPro;
using System.Linq;
using UnityEngine.UI;
using UnityEngine.InputSystem.Controls;

namespace Nikami.OpenXR;

internal static class NikamiIntegration
{
    static Func<Hand> rightHand;
    static float nextRotation;
    static TMP_FontAsset font;
    static Button placeButton;
    static Action<InventoryGui> beginPlacement;
    static Func<bool> placementActive;
    static AccessTools.FieldRef<GameObject> quickHudRoot;
    static AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> dragItem;
    static readonly bool[] held = new bool[2], down = new bool[2];
    static int inputFrame = -1;
    internal static void Install(Harmony h)
    {
        var gizmo = AccessTools.Method("nikami.BuildingGizmo:Eligible");
        if (gizmo == null) return;
        h.Patch(gizmo, new HarmonyMethod(typeof(NikamiIntegration), nameof(NoDesktopGizmo)));
        rightHand = AccessTools.MethodDelegate<Func<Hand>>(AccessTools.PropertyGetter(AccessTools.TypeByName("ValheimVRMod.VRCore.VRPlayer"), "rightHand"));
        h.Patch(AccessTools.Method("nikami.ItemPlacement:Update"), transpiler: new HarmonyMethod(typeof(NikamiIntegration), nameof(PlacementInput)));
        h.Patch(AccessTools.Method("nikami.ItemPlacement:Begin"), postfix: new HarmonyMethod(typeof(NikamiIntegration), nameof(PlacementStarted)));
        h.Patch(AccessTools.Method("nikami.QuickSlotHud:Create"), postfix: new HarmonyMethod(typeof(NikamiIntegration), nameof(QuickHudFont)));
        h.Patch(AccessTools.Method("nikami.EquipmentPanel:Update"), postfix: new HarmonyMethod(typeof(NikamiIntegration), nameof(InventoryUi)));
        beginPlacement = AccessTools.MethodDelegate<Action<InventoryGui>>(AccessTools.Method("nikami.ItemPlacement:Begin"));
        placementActive = AccessTools.MethodDelegate<Func<bool>>(AccessTools.PropertyGetter(AccessTools.TypeByName("nikami.ItemPlacement"), "Active"));
        quickHudRoot = AccessTools.StaticFieldRefAccess<GameObject>(AccessTools.Field(AccessTools.TypeByName("nikami.QuickSlotHud"), "_root"));
        dragItem = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        foreach (var name in new[] { "GetJoyRightStickX", "GetJoyRightStickY" })
            h.Patch(AccessTools.Method("ValheimVRMod.VRCore.UI.VRControls:" + name), new HarmonyMethod(typeof(NikamiIntegration), nameof(PlacementOwnsStick)));
    }
    static void QuickHudFont()
    {
        // VHVR moves the original HUD children into VR canvases before Nikami's
        // extra labels are created. Find their existing font in the moved UI.
        if (!font) font = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.font && !t.name.StartsWith("Nikami"))?.font;
        if (!font) return;
        var root = quickHudRoot();
        if (root) foreach (var label in root.GetComponentsInChildren<TMP_Text>(true)) if (!label.font) label.font = font;
    }
    static void InventoryUi(InventoryGui gui, InventoryGrid grid)
    {
        if (!font) QuickHudFont();
        if (!font) return;
        foreach (var label in grid.m_gridRoot.GetComponentsInChildren<TMP_Text>(true))
            if (!label.font) label.font = font;
        if (!placeButton)
        {
            var go = new GameObject("NikamiVRPlaceItem", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(grid.m_gridRoot.parent, false);
            go.layer = grid.gameObject.layer;
            var image = go.GetComponent<Image>();
            image.color = new Color(.21f, .17f, .10f, .98f);
            placeButton = go.GetComponent<Button>();
            placeButton.targetGraphic = image;
            placeButton.onClick.AddListener(() => beginPlacement(InventoryGui.instance));
            var textGo = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.layer = go.layer;
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = 17; text.text = "Place item";
            text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            var rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        // A regular canvas button is hit by VHVR's controller ray. IMGUI buttons
        // only exist on the desktop mirror and cannot serve as VR controls.
        var panel = grid.m_gridRoot.parent.Find("NikamiGearPanel") as RectTransform;
        if (panel)
        {
            var rt = placeButton.GetComponent<RectTransform>();
            rt.anchorMin = panel.anchorMin; rt.anchorMax = panel.anchorMax; rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = panel.anchoredPosition + new Vector2(3, -panel.sizeDelta.y - 5);
            rt.sizeDelta = new Vector2(panel.sizeDelta.x - 6, 34);
        }
        placeButton.interactable = dragItem(gui) != null;
    }
    static void PlacementStarted()
    {
        // Opening the preview with a held UI trigger must not also place it.
        held[0] = Trigger() >= .8f;
        held[1] = Secondary();
        inputFrame = -1;
        Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Aim: right hand   Trigger: place   Stick: turn   B: cancel");
    }
    static bool NoDesktopGizmo(ref bool __result) { __result = false; return false; }
    static bool PlacementOwnsStick(ref float __result) { if (!placementActive()) return true; __result = 0; return false; }
    static IEnumerable<CodeInstruction> PlacementInput(IEnumerable<CodeInstruction> source)
    {
        foreach (var code in source)
        {
            if (code.operand is MethodInfo method)
            {
                string replacement = method.DeclaringType == typeof(Input) ? method.Name switch {
                    "GetMouseButtonDown" => nameof(Click), "GetKeyDown" => nameof(Key), "get_mouseScrollDelta" => nameof(Scroll), _ => null
                } : method.DeclaringType == typeof(Camera) && method.Name == "ViewportPointToRay" ? nameof(Aim) : null;
                if (replacement != null) { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(NikamiIntegration), replacement); }
            }
            yield return code;
        }
    }
    static float Trigger() => InputAdapter.Control<AxisControl>(InputAdapter.Device(2), "trigger")?.ReadValue() ?? 0;
    static bool Secondary() => InputAdapter.Control<ButtonControl>(InputAdapter.Device(2), "secondaryButton")?.isPressed ?? false;
    static bool Click(int button)
    {
        // The upstream laser action set deactivates when the inventory closes.
        // Read the OpenXR controls for the lifetime of our independent preview.
        if (inputFrame != Time.frameCount)
        {
            inputFrame = Time.frameCount;
            bool trigger = Trigger() >= (held[0] ? .7f : .8f), cancel = Secondary();
            down[0] = trigger && !held[0]; down[1] = cancel && !held[1];
            held[0] = trigger; held[1] = cancel;
        }
        return button >= 0 && button < 2 && down[button];
    }
    static bool Key(KeyCode key) => key == KeyCode.Escape && Click(1);
    static Vector2 Scroll()
    {
        var d = InputAdapter.Device(2);
        var axis = (InputAdapter.Control<Vector2Control>(d, "thumbstick") ?? InputAdapter.Control<Vector2Control>(d, "primary2DAxis"))?.ReadValue() ?? Vector2.zero;
        if (Time.unscaledTime < nextRotation || Mathf.Abs(axis.x) < .6f) return Vector2.zero;
        nextRotation = Time.unscaledTime + .2f;
        return new Vector2(0, Mathf.Sign(axis.x));
    }
    static Ray Aim(Camera original, Vector3 point)
    {
        var hand = rightHand();
        return hand ? new Ray(hand.transform.position, hand.transform.forward) : original.ViewportPointToRay(point);
    }
}
