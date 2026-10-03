using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using ValheimVRMod.VRCore.UI;
using Valve.Newtonsoft.Json.Linq;

namespace Nikami.OpenXR;

// uGUI stays on VHVR's existing canvas, so the same VR pointer that operates
// the game menus operates this editor. No desktop-only IMGUI or Valve input.
internal sealed class BindingEditor : MonoBehaviour
{
    const float RowHeight = 36;
    static BindingEditor live;
    BindingProfile profile;
    JObject draft;
    string action;
    int hand = 1, choiceIndex;
    bool dirty;
    float press, release;
    RectTransform body, mappingList;
    Button buttonTemplate;
    TMP_Text textTemplate, profileLabel, status, actionLabel, choiceLabel, thresholdLabel, comboLabel;
    Button comboInputButton, comboBindButton;
    readonly List<Choice> choices = new();
    readonly JArray combo = new();

    sealed class Choice
    {
        internal JObject Source;
        internal string Component;
        internal string Name => ((string)Source["path"]).Split('/').Last() + " / " + Component +
            ((string)Source["parameters"]?["force_input"] == "force" ? " (force)" : "");
    }

    internal static bool Open(string actionSet)
    {
        if (live != null) { live.transform.SetAsLastSibling(); return true; }
        if (BindingProfiles.All.Count == 0) return false;
        var root = ConfigSettings.CreateControllerBindingDialog();
        if (root == null) return false;
        try
        {
            var editor = root.AddComponent<BindingEditor>();
            live = editor;
            editor.Initialize(actionSet);
            // Even a personal layout with its menu/pointer buttons unbound
            // can be recovered here. Defaults drive the menu until it closes.
            InputAdapter.EditBindings(true);
            return true;
        }
        catch (Exception error)
        {
            OpenXRPlugin.Log.LogError("Could not open controller bindings: " + error);
            ConfigSettings.Close(false);
            return false;
        }
    }
    internal static void Close()
    {
        if (live != null) ConfigSettings.Close(false);
    }
    void OnDestroy()
    {
        if (live != this) return;
        live = null;
        InputAdapter.EditBindings(false);
    }
    void Initialize(string actionSet)
    {
        var panel = transform.Find("Panel");
        body = (RectTransform)panel.Find("ControllerBindingsBody");
        textTemplate = panel.Find("Title").GetComponent<TMP_Text>();
        buttonTemplate = panel.Find("Back").GetComponent<Button>();
        Wire(buttonTemplate, "Cancel", () => ConfigSettings.Close(false));
        Wire(panel.Find("Ok").GetComponent<Button>(), "Save and close", Save);
        MakeButton(panel, "Reset defaults", .38f, .01f, .62f, .08f, ResetDraft);
        MakeButton(body, "<", 0, .90f, .08f, 1, () => ChangeProfile(-1));
        profileLabel = Label(body, "", .09f, .90f, .9f, 1);
        MakeButton(body, ">", .92f, .90f, 1, 1, () => ChangeProfile(1));
        status = Label(body, "", 0, .79f, 1, .9f, 16);
        var actionList = ScrollList(body, "Actions", 0, 0, .32f, .78f);
        var names = BindingProfiles.ActionTypes.Where(pair => pair.Key.StartsWith(actionSet + "/in/", StringComparison.OrdinalIgnoreCase) &&
            (pair.Value == "boolean" || pair.Value == "vector1" || pair.Value == "vector2")).Select(pair => pair.Key).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var name in names)
        {
            string selected = name;
            ListButton(actionList, ShortName(name), () => SelectAction(selected));
        }
        var detail = Rect(body, "Mapping details", .35f, 0, 1, .78f);
        actionLabel = Label(detail, "", 0, .90f, 1, 1, 23);
        mappingList = ScrollList(detail, "Current mappings", 0, .51f, 1, .90f);
        MakeButton(detail, "Left / Right", 0, .40f, .28f, .49f, () => { hand = hand == 1 ? 2 : 1; UpdateChoice(); });
        MakeButton(detail, "<", .29f, .40f, .38f, .49f, () => StepChoice(-1));
        choiceLabel = Label(detail, "", .39f, .40f, .90f, .49f, 17);
        MakeButton(detail, ">", .91f, .40f, 1, .49f, () => StepChoice(1));
        thresholdLabel = Label(detail, "", 0, .27f, 1, .40f, 16);
        MakeButton(detail, "Press -", 0, .18f, .24f, .27f, () => Threshold(-.05f, true));
        MakeButton(detail, "Press +", .25f, .18f, .49f, .27f, () => Threshold(.05f, true));
        MakeButton(detail, "Release -", .50f, .18f, .74f, .27f, () => Threshold(-.05f, false));
        MakeButton(detail, "Release +", .75f, .18f, 1, .27f, () => Threshold(.05f, false));
        MakeButton(detail, "Bind input", 0, .08f, .32f, .17f, AddSource);
        comboInputButton = MakeButton(detail, "Add to combo", .34f, .08f, .66f, .17f, AddComboInput);
        comboBindButton = MakeButton(detail, "Bind combo", .68f, .08f, 1, .17f, AddCombo);
        comboLabel = Label(detail, "", 0, 0, .80f, .08f, 15);
        MakeButton(detail, "Clear", .82f, 0, 1, .08f, () => { combo.Clear(); UpdateCombo(); });
        profile = InputAdapter.ProfileForHand(1) ?? InputAdapter.ProfileForHand(2) ?? BindingProfiles.All[0];
        LoadDraft();
        SelectAction(names.FirstOrDefault(path => path.StartsWith(actionSet + "/in/", StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault());
    }
    static string ShortName(string path) => path.Substring(path.LastIndexOf('/') + 1);
    void LoadDraft()
    {
        draft = (JObject)profile.Current.DeepClone();
        draft["controller_type"] = profile.Id;
        dirty = false;
        profileLabel.text = profile.Name;
        status.text = profile.LoadError ?? "Personal changes save for this controller. Menu controls use defaults while this screen is open.";
    }
    void ChangeProfile(int direction)
    {
        if (dirty) { status.text = "Save or cancel your changes before switching controller layouts."; return; }
        int index = BindingProfiles.All.IndexOf(profile);
        profile = BindingProfiles.All[(index + direction + BindingProfiles.All.Count) % BindingProfiles.All.Count];
        LoadDraft();
        SelectAction(action);
    }
    void SelectAction(string selected)
    {
        action = selected;
        choices.Clear(); combo.Clear(); choiceIndex = 0;
        if (action == null) return;
        string actionType = BindingProfiles.ActionTypes[action];
        bool axis = actionType != "boolean";
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var set in ((JObject)profile.Defaults["bindings"]).Properties())
        foreach (JObject source in set.Value["sources"] ?? new JArray())
        foreach (var input in ((JObject)source["inputs"]).Properties())
        {
            bool compatible = actionType == "vector1" ? input.Name == "pull" :
                actionType == "vector2" ? input.Name == "position" || input.Name == "scroll" :
                input.Name != "position" && input.Name != "scroll" && input.Name != "pull";
            if (!compatible) continue;
            string control = ((string)source["path"]).Split('/').Last();
            string key = control + ":" + source["mode"] + ":" + input.Name + ":" + source["parameters"];
            if (keys.Add(key)) choices.Add(new Choice { Source = source, Component = input.Name });
        }
        actionLabel.text = ShortName(action) + (axis ? " (axis)" : " (button)");
        RefreshMappings(); ResetThresholds(); UpdateChoice(); UpdateCombo();
    }
    void StepChoice(int direction)
    {
        if (choices.Count == 0) return;
        choiceIndex = (choiceIndex + direction + choices.Count) % choices.Count;
        ResetThresholds(); UpdateChoice();
    }
    void ResetThresholds()
    {
        var parameters = choices.Count > 0 ? choices[choiceIndex].Source["parameters"] : null;
        press = (float?)parameters?["click_activate_threshold"] ?? .55f;
        release = (float?)parameters?["click_deactivate_threshold"] ?? Math.Min(.45f, press);
    }
    void Threshold(float step, bool activation)
    {
        if (activation) { press = Mathf.Clamp(press + step, .05f, 1); release = Mathf.Min(release, press); }
        else release = Mathf.Clamp(release + step, 0, press);
        UpdateChoice();
    }
    void UpdateChoice()
    {
        choiceLabel.text = (hand == 1 ? "Left " : "Right ") + (choices.Count > 0 ? choices[choiceIndex].Name : "No compatible inputs");
        thresholdLabel.text = $"Press {press:0.00} / release {release:0.00}. Select an existing mapping to remove it.";
        comboInputButton.interactable = choices.Count > 0 && (choices[choiceIndex].Component == "click" || choices[choiceIndex].Component == "touch");
    }
    string ChoicePath()
    {
        string original = (string)choices[choiceIndex].Source["path"];
        return "/user/hand/" + (hand == 1 ? "left" : "right") + "/input/" + original.Split('/').Last();
    }
    JObject ActionSet()
    {
        string set = action.Substring(0, action.IndexOf("/in/", StringComparison.OrdinalIgnoreCase));
        var bindings = (JObject)draft["bindings"];
        var value = bindings.Properties().FirstOrDefault(property => property.Name.Equals(set, StringComparison.OrdinalIgnoreCase))?.Value as JObject;
        if (value == null) bindings[set] = value = new JObject();
        if (!(value["sources"] is JArray)) value["sources"] = new JArray();
        if (!(value["chords"] is JArray)) value["chords"] = new JArray();
        return value;
    }
    void AddSource()
    {
        if (choices.Count == 0 || action == null) return;
        var choice = choices[choiceIndex];
        var source = (JObject)choice.Source.DeepClone();
        source["path"] = ChoicePath();
        source["inputs"] = new JObject { [choice.Component] = new JObject { ["output"] = action } };
        if (choice.Component == "click")
        {
            if (!(source["parameters"] is JObject)) source["parameters"] = new JObject();
            source["parameters"]["click_activate_threshold"] = press;
            source["parameters"]["click_deactivate_threshold"] = release;
        }
        ((JArray)ActionSet()["sources"]).Add(source);
        dirty = true; RefreshMappings();
    }
    void AddComboInput()
    {
        if (!comboInputButton.interactable) return;
        var input = new JArray(ChoicePath(), choices[choiceIndex].Component);
        if (!combo.Any(existing => JToken.DeepEquals(existing, input))) combo.Add(input);
        UpdateCombo();
    }
    void AddCombo()
    {
        if (combo.Count == 0 || action == null) return;
        ((JArray)ActionSet()["chords"]).Add(new JObject { ["output"] = action, ["inputs"] = combo.DeepClone() });
        dirty = true; combo.Clear(); RefreshMappings(); UpdateCombo();
    }
    void UpdateCombo()
    {
        comboLabel.text = combo.Count == 0 ? "Combo: choose inputs from either hand." : "Combo: " + string.Join(" + ", combo.Select(input => ((string)input[0]).Replace("/user/hand/", "").Replace("/input/", " ")));
        comboBindButton.interactable = combo.Count > 0;
    }
    void RefreshMappings()
    {
        foreach (Transform child in mappingList) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        foreach (var set in ((JObject)draft["bindings"]).Properties())
        {
            foreach (JObject source in set.Value["sources"] ?? new JArray())
            foreach (var input in ((JObject)source["inputs"]).Properties())
            {
                if (!string.Equals((string)input.Value["output"], action, StringComparison.OrdinalIgnoreCase)) continue;
                var capturedSource = source; var capturedInput = input;
                string name = ((string)source["path"]).Replace("/user/hand/", "").Replace("/input/", " ") + " / " + input.Name;
                ListButton(mappingList, name + "  [Remove]", () => {
                    capturedInput.Remove();
                    if (!((JObject)capturedSource["inputs"]).HasValues) capturedSource.Remove();
                    dirty = true; RefreshMappings();
                });
            }
            foreach (JObject chord in set.Value["chords"] ?? new JArray())
            {
                if (!string.Equals((string)chord["output"], action, StringComparison.OrdinalIgnoreCase)) continue;
                var captured = chord;
                string name = "Combo: " + string.Join(" + ", ((JArray)chord["inputs"]).Select(input => ((string)input[0]).Replace("/user/hand/", "").Replace("/input/", " ")));
                ListButton(mappingList, name + "  [Remove]", () => { captured.Remove(); dirty = true; RefreshMappings(); });
            }
        }
    }
    void ResetDraft()
    {
        draft = (JObject)profile.Defaults.DeepClone();
        draft["controller_type"] = profile.Id;
        dirty = true;
        status.text = "Upstream defaults selected. Save to apply, or Cancel to keep your personal layout.";
        SelectAction(action);
    }
    void Save()
    {
        try
        {
            var defaults = (JObject)profile.Defaults.DeepClone();
            defaults["controller_type"] = profile.Id;
            if (JToken.DeepEquals(draft, defaults)) BindingProfiles.Reset(profile);
            else BindingProfiles.Save(profile, draft);
            InputAdapter.RefreshBindings();
            ConfigSettings.Close(true);
        }
        catch (Exception error)
        {
            status.text = "Could not save. Your active bindings are unchanged. " + error.Message;
            OpenXRPlugin.Log.LogWarning(status.text);
        }
    }
    static RectTransform Rect(Transform parent, string name, float left, float bottom, float right, float top)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = parent.gameObject.layer;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(left, bottom); rect.anchorMax = new Vector2(right, top);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
    TMP_Text Label(Transform parent, string text, float left, float bottom, float right, float top, float size = 22)
    {
        var rect = Rect(parent, "Label", left, bottom, right, top);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = textTemplate.font; label.fontSharedMaterial = textTemplate.fontSharedMaterial;
        label.fontSize = size; label.enableAutoSizing = true; label.fontSizeMin = Math.Min(14, size); label.fontSizeMax = size;
        label.alignment = TextAlignmentOptions.MidlineLeft; label.raycastTarget = false; label.text = text;
        return label;
    }
    static void Wire(Button button, string label, UnityAction click)
    {
        button.onClick = new Button.ButtonClickedEvent(); button.onClick.AddListener(click);
        var text = button.GetComponentInChildren<TMP_Text>(); if (text != null) text.text = label;
        Destroy(button.GetComponent<UIGamePad>());
        var hint = button.transform.Find("KeyHint"); if (hint != null) Destroy(hint.gameObject);
    }
    Button MakeButton(Transform parent, string text, float left, float bottom, float right, float top, UnityAction click)
    {
        var button = Instantiate(buttonTemplate, parent);
        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(left, bottom); rect.anchorMax = new Vector2(right, top);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Wire(button, text, click);
        var label = button.GetComponentInChildren<TMP_Text>();
        if (label != null) { label.enableAutoSizing = true; label.fontSizeMin = 13; label.fontSizeMax = 20; }
        return button;
    }
    RectTransform ScrollList(Transform parent, string name, float left, float bottom, float right, float top)
    {
        var frame = Rect(parent, name, left, bottom, right, top);
        frame.gameObject.AddComponent<Image>().color = new Color(.06f, .06f, .06f, .85f);
        var viewport = Rect(frame, "Viewport", 0, 0, 1, 1);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect(viewport, "Content", 0, 1, 1, 1);
        content.pivot = new Vector2(.5f, 1);
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
        layout.spacing = 3;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = frame.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = RowHeight;
        return content;
    }
    void ListButton(Transform parent, string label, UnityAction click)
    {
        var button = MakeButton(parent, label, 0, 0, 1, 1, click);
        button.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;
    }
}
