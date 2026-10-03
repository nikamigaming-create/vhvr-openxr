using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Valve.Newtonsoft.Json;
using Valve.Newtonsoft.Json.Linq;

namespace Nikami.OpenXR;

// The controller layouts remain upstream data. Personal layouts use the same
// format, in the user's config directory rather than the redistributed payload.
internal sealed class BindingProfile
{
    internal readonly string Id, Name;
    internal readonly JObject Defaults;
    internal JObject Current;
    internal string LoadError;
    internal BindingProfile(string id, JObject defaults)
    {
        Id = id;
        Name = id switch {
            "oculus_touch" => "Oculus / Quest Touch", "knuckles" => "Valve Index",
            "vive_controller" => "HTC Vive", "holographic_controller" => "Windows Mixed Reality",
            "vive_cosmos_controller" => "Vive Cosmos", "frame_controller" => "Frame controller",
            "logitech_stylus" => "Logitech stylus", _ => id
        };
        Defaults = defaults;
        Current = (JObject)defaults.DeepClone();
    }
}

internal static class BindingProfiles
{
    internal static readonly List<BindingProfile> All = new();
    internal static readonly Dictionary<string, string> ActionTypes = new(StringComparer.OrdinalIgnoreCase);
    internal static string UserDirectory { get; private set; }
    internal static void Configure(string directory) => UserDirectory = directory;
    internal static void Clear() { All.Clear(); ActionTypes.Clear(); }

    internal static void Load(string directory)
    {
        Clear();
        var manifestPath = Path.Combine(directory, "actions.json");
        if (File.Exists(manifestPath))
        {
            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            foreach (var action in manifest["actions"] ?? new JArray())
                ActionTypes[(string)action["name"]] = (string)action["type"];
            foreach (var entry in manifest["default_bindings"] ?? new JArray())
            {
                string id = (string)entry["controller_type"], file = (string)entry["binding_url"];
                if (!SafeId(id) || string.IsNullOrEmpty(file) || Path.GetFileName(file) != file) continue;
                var path = Path.Combine(directory, file);
                if (!File.Exists(path)) continue;
                var root = JObject.Parse(File.ReadAllText(path));
                // HMD and body-tracker bindings are not hand-controller layouts.
                if (!(root["bindings"] is JObject sets) || !sets.Properties().Any(set =>
                    (set.Value["sources"] as JArray)?.Count > 0 || (set.Value["chords"] as JArray)?.Count > 0)) continue;
                All.Add(new BindingProfile(id, root));
            }
        }
        // Also permits isolated input fixtures and source trees without a manifest.
        if (All.Count == 0)
        {
            var root = JObject.Parse(File.ReadAllText(Path.Combine(directory, "bindings_oculus_touch.json")));
            All.Add(new BindingProfile("oculus_touch", root));
        }
        foreach (var profile in All)
        foreach (var action in Outputs(profile.Defaults))
            if (!ActionTypes.ContainsKey(action)) ActionTypes[action] = "boolean";
        foreach (var profile in All)
        {
            var path = PersonalPath(profile);
            if (path == null || !File.Exists(path)) continue;
            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                Validate(profile, root);
                profile.Current = root;
            }
            catch (Exception error) when (error is IOException || error is JsonException || error is ArgumentException || error is FormatException || error is InvalidCastException || error is UnauthorizedAccessException)
            {
                profile.LoadError = "Could not load personal layout; using upstream defaults. " + error.Message;
                OpenXRPlugin.Log.LogWarning(profile.Name + ": " + profile.LoadError);
            }
        }
    }

    internal static BindingProfile Find(string id) => All.Find(profile => profile.Id == id);
    internal static BindingProfile Detect(string layout, bool stick, bool pad, bool faceButtons)
    {
        string name = (layout ?? "").ToLowerInvariant();
        string id = name.Contains("index") || name.Contains("knuckles") ? "knuckles" :
            name.Contains("cosmos") ? "vive_cosmos_controller" :
            name.Contains("wmr") || name.Contains("microsoft") || name.Contains("holographic") ? "holographic_controller" :
            name.Contains("vive") ? "vive_controller" : name.Contains("frame") ? "frame_controller" :
            name.Contains("oculus") || name.Contains("touch") || name.Contains("quest") ? "oculus_touch" :
            pad && stick ? (faceButtons ? "knuckles" : "holographic_controller") :
            pad ? "vive_controller" : stick && faceButtons ? "oculus_touch" : null;
        return id == null ? null : Find(id);
    }

    internal static string PersonalPath(BindingProfile profile) => string.IsNullOrEmpty(UserDirectory) ? null : Path.Combine(UserDirectory, profile.Id + ".json");
    internal static void Save(BindingProfile profile, JObject document)
    {
        Validate(profile, document);
        string path = PersonalPath(profile) ?? throw new InvalidOperationException("Personal binding directory is unavailable.");
        Directory.CreateDirectory(UserDirectory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, document.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
            else File.Move(temporary, path);
            profile.Current = (JObject)document.DeepClone();
            profile.LoadError = null;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static void Reset(BindingProfile profile)
    {
        string path = PersonalPath(profile);
        if (path != null && File.Exists(path))
        {
            File.Copy(path, path + ".bak", true);
            File.Delete(path);
        }
        profile.Current = (JObject)profile.Defaults.DeepClone();
        profile.LoadError = null;
    }

    static bool SafeId(string id) => !string.IsNullOrEmpty(id) && id.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
    internal static IEnumerable<string> Outputs(JObject document)
    {
        foreach (var set in ((JObject)document["bindings"]).Properties())
        {
            foreach (var source in set.Value["sources"] ?? new JArray())
            foreach (var input in ((JObject)source["inputs"]).Properties())
                yield return (string)input.Value["output"];
            foreach (var chord in set.Value["chords"] ?? new JArray()) yield return (string)chord["output"];
        }
    }
    internal static void Validate(BindingProfile profile, JObject root)
    {
        if (!SafeId(profile.Id) || (string)root["controller_type"] != profile.Id || !(root["bindings"] is JObject sets))
            throw new ArgumentException("Layout must match the selected controller and contain action sets.");
        foreach (var set in sets.Properties())
        {
            if (!set.Name.StartsWith("/actions/", StringComparison.OrdinalIgnoreCase) || !(set.Value is JObject))
                throw new ArgumentException("Invalid action set.");
            if ((set.Value["sources"] != null && !(set.Value["sources"] is JArray)) ||
                (set.Value["chords"] != null && !(set.Value["chords"] is JArray))) throw new ArgumentException("Sources and combos must be arrays.");
            foreach (var source in set.Value["sources"] ?? new JArray())
            {
                if (!(source is JObject)) throw new ArgumentException("Invalid controller source.");
                ValidatePath((string)source["path"]);
                string mode = (string)source["mode"];
                if (mode != "button" && mode != "trigger" && mode != "joystick" && mode != "trackpad" && mode != "dpad" && mode != "scroll")
                    throw new ArgumentException("Unsupported controller mode: " + mode);
                if (!(source["inputs"] is JObject inputs)) throw new ArgumentException("Source has no input components.");
                foreach (var input in inputs.Properties())
                {
                    if (!(input.Value is JObject)) throw new ArgumentException("Input components must name an output action.");
                    ValidateOutput(set.Name, (string)input.Value["output"]);
                }
                if (source["parameters"] != null && !(source["parameters"] is JObject)) throw new ArgumentException("Controller parameters must be an object.");
                float press = (float?)source["parameters"]?["click_activate_threshold"] ?? .55f;
                float release = (float?)source["parameters"]?["click_deactivate_threshold"] ?? Math.Min(.45f, press);
                if (float.IsNaN(press) || float.IsNaN(release) || press < 0 || press > 1 || release < 0 || release > press)
                    throw new ArgumentException("Release threshold must be between zero and the press threshold.");
                float deadzone = (float?)source["parameters"]?["deadzone_pct"] ?? 0;
                if (float.IsNaN(deadzone) || deadzone < 0 || deadzone >= 100) throw new ArgumentException("Deadzone must be between zero and 100 percent.");
            }
            foreach (var chord in set.Value["chords"] ?? new JArray())
            {
                if (!(chord is JObject)) throw new ArgumentException("Invalid combo.");
                ValidateOutput(set.Name, (string)chord["output"]);
                if (!(chord["inputs"] is JArray inputs) || inputs.Count == 0) throw new ArgumentException("A combo needs at least one input.");
                foreach (var input in inputs)
                {
                    if (!(input is JArray pair) || pair.Count != 2) throw new ArgumentException("Invalid combo input.");
                    ValidatePath((string)pair[0]);
                }
            }
        }
    }
    static void ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || !(path.StartsWith("/user/hand/left/input/", StringComparison.Ordinal) || path.StartsWith("/user/hand/right/input/", StringComparison.Ordinal)))
            throw new ArgumentException("Input must belong to a left or right controller.");
    }
    static void ValidateOutput(string set, string output)
    {
        if (string.IsNullOrEmpty(output) || !output.StartsWith(set + "/in/", StringComparison.OrdinalIgnoreCase) || !ActionTypes.ContainsKey(output))
            throw new ArgumentException("Unknown action or action-set mismatch: " + output);
    }
}
