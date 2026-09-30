using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Nikami.OpenXR;

// Locally generated shader caches belong to the player's installation. Game
// shader bytecode is never included in the public companion or release package.
internal static class SinglePassRenderer
{
    [Serializable] internal sealed class CacheManifest
    {
        public int cacheSchema;
        public string unityVersion;
        public string gameAssemblySha256;
        // Shader coverage is a build contract, separate from visual checking.
        public int uiShaderCoverage;
        public int stereoShadowCoverage;
        public int executableTextureCoverage;
        public string vhvrAssetsSha256, steamVrShadersSha256;
        public CacheFile[] files;
        public CacheFile[] sourceFiles;
    }
    [Serializable] internal sealed class CacheFile { public string name; public string sha256; }
    static readonly Dictionary<string, Shader> shaders = new(StringComparer.Ordinal);
    static readonly HashSet<int> cameras = new();
    static readonly HashSet<int> assets = new();
    static readonly Dictionary<int, Shader> resolvedShaders = new();
    static readonly List<AssetBundle> bundles = new();
    static readonly Dictionary<BuiltinShaderType, (BuiltinShaderMode mode, Shader shader)> originalBuiltins = new();
    static readonly CameraEvent[] cameraEvents = (CameraEvent[])Enum.GetValues(typeof(CameraEvent));
    internal static bool Active { get; private set; }
    static bool scanMaterials;
    static Camera worldCamera;
    static Harmony patches;

    internal static bool Initialize()
    {
        if (Active) return true;
        try
        {
            return InitializeVerified();
        }
        catch (Exception error)
        {
            // A stale or partial private cache must never disable the whole
            // OpenXR adapter.  Fall back to Unity's multipass path instead;
            // that path keeps UI eye ownership correct even when a cache was
            // copied from another Valheim build.
            Shutdown();
            OpenXRPlugin.Log.LogWarning("OpenXR single-pass cache rejected; falling back to multipass: " + error.Message);
            return false;
        }
    }

    static bool InitializeVerified()
    {
        string directory = Path.Combine(Path.GetDirectoryName(typeof(OpenXRPlugin).Assembly.Location), "single-pass");
        string path = Path.Combine(directory, "manifest.json");
        if (!File.Exists(path)) return false;
        var manifest = Valve.Newtonsoft.Json.JsonConvert.DeserializeObject<CacheManifest>(File.ReadAllText(path));
        if (manifest == null || manifest.cacheSchema != 2 || manifest.unityVersion != Application.unityVersion || manifest.files == null || manifest.files.Length == 0)
            throw new InvalidDataException($"The local single-pass shader cache does not match this Unity player: cache={manifest?.unityVersion}, player={Application.unityVersion}, files={manifest?.files?.Length}.");
        if (manifest.uiShaderCoverage != 1
            || !HashMatches(Path.Combine(Application.streamingAssetsPath, "vhvr_custom"), manifest.vhvrAssetsSha256)
            || !HashMatches(Path.Combine(Application.streamingAssetsPath, "steamvr_shaders"), manifest.steamVrShadersSha256))
            throw new InvalidDataException("the cache does not cover this installation's panel and text shaders");
        if (manifest.stereoShadowCoverage != 1 || manifest.executableTextureCoverage != 1)
            throw new InvalidDataException("rebuild and audit the cache with stereo depth passes, per-eye shadow rays and executable texture bindings");
        if (!HashMatches(Path.Combine(Application.dataPath, "Managed", "assembly_valheim.dll"), manifest.gameAssemblySha256))
            throw new InvalidDataException("Valheim changed; rebuild the local single-pass shader cache before launching VR.");
        if (manifest.sourceFiles == null || manifest.sourceFiles.Length != 7)
            throw new InvalidDataException("the cache does not identify every owned shader source");
        string sourceRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
        var sourceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in manifest.sourceFiles)
        {
            if (source == null || string.IsNullOrEmpty(source.name) || Path.IsPathRooted(source.name) || !sourceNames.Add(source.name))
                throw new InvalidDataException("invalid or duplicate shader source entry");
            string sourcePath = Path.GetFullPath(Path.Combine(sourceRoot, source.name));
            if (!sourcePath.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase) || !HashMatches(sourcePath, source.sha256))
                throw new InvalidDataException("owned shader source changed: " + source.name);
        }
        foreach (var file in manifest.files)
        {
            if (file == null || string.IsNullOrEmpty(file.name) || Path.GetFileName(file.name) != file.name || !file.name.StartsWith("nikami-spi-", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid local single-pass shader cache entry.");
            string bundlePath = Path.Combine(directory, file.name);
            if (!HashMatches(bundlePath, file.sha256)) throw new InvalidDataException("Single-pass shader cache verification failed: " + file.name);
            var bundle = AssetBundle.LoadFromFile(bundlePath);
            if (!bundle) throw new InvalidDataException("Cannot load single-pass shaders: " + file.name);
            bundles.Add(bundle);
            foreach (var shader in bundle.LoadAllAssets<Shader>())
            {
                const string prefix = "Nikami/OpenXR/";
                if (!shader.name.StartsWith(prefix, StringComparison.Ordinal) || !shader.isSupported)
                    throw new InvalidDataException("Unsupported cached single-pass shader: " + shader.name);
                shaders.Add(shader.name.Substring(prefix.Length), shader);
            }
        }
        foreach (string required in new[] { "Custom/Heightmap", "Custom/SkyboxProcedural", "Custom/Player", "Custom/StaticRock", "Standard", "Custom/Water", "Hidden/Internal-ScreenSpaceShadows",
            "Hidden/Internal-DepthNormalsTexture", "Legacy Shaders/Diffuse", "Legacy Shaders/VertexLit",
            "UI/Default", "Sprites/Default", "TextMeshPro/Distance Field", "TextMeshPro/Mobile/Distance Field", "TextMeshPro/Sprite" })
            if (!shaders.ContainsKey(required)) throw new InvalidDataException("Single-pass shader cache is incomplete: " + required);
        // This legacy VHVR shader contains only a fallback to UI/Default.
        shaders.Add("UI/Unlit/Transparent", shaders["UI/Default"]);
        SetBuiltin(BuiltinShaderType.ScreenSpaceShadows, "Hidden/Internal-ScreenSpaceShadows");
        SetBuiltin(BuiltinShaderType.DepthNormals, "Hidden/Internal-DepthNormalsTexture");
        patches = new Harmony("nikami.openxr.singlepass");
        var result = new HarmonyMethod(typeof(SinglePassRenderer), nameof(LoadedAsset));
        patches.Patch(AccessTools.Method(typeof(AssetBundle), nameof(AssetBundle.LoadAsset), new[] { typeof(string), typeof(Type) }), postfix: result);
        patches.Patch(AccessTools.Method(typeof(AssetBundleRequest), "GetResult"), postfix: result);
        patches.Patch(AccessTools.PropertyGetter(typeof(AssetBundleRequest), nameof(AssetBundleRequest.allAssets)),
            postfix: new HarmonyMethod(typeof(SinglePassRenderer), nameof(LoadedAssets)));
        patches.Patch(AccessTools.Method(typeof(AssetBundle), nameof(AssetBundle.LoadAllAssets), new[] { typeof(Type) }),
            postfix: new HarmonyMethod(typeof(SinglePassRenderer), nameof(LoadedAssets)));
        patches.Patch(AccessTools.Method("ValheimVRMod.VRCore.VRPlayer:enableVrCamera"),
            postfix: new HarmonyMethod(typeof(SinglePassRenderer), nameof(CameraEffectsChanged)));
        patches.Patch(AccessTools.Method(typeof(CameraEffects), "ApplySettings"),
            postfix: new HarmonyMethod(typeof(SinglePassRenderer), nameof(CameraEffectsChanged)));
        // Unity can retain camera command buffers after a legacy effect has
        // been disabled. Guard callbacks too, and detach existing buffers once.
        foreach (var type in new[] { typeof(PostProcessingBehaviour), typeof(AmplifyOcclusionEffect),
                     typeof(UnityStandardAssets.ImageEffects.SunShafts), typeof(UnityStandardAssets.ImageEffects.DepthOfField) })
            foreach (string method in new[] { "OnPreCull", "OnPreRender", "OnPostRender" })
            {
                var target = type.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (target != null) patches.Patch(target, prefix: new HarmonyMethod(typeof(SinglePassRenderer), nameof(AllowLegacyCallback)));
            }
        Active = true;
        scanMaterials = true;
        Camera.onPreCull += ConfigureCamera;
        SceneManager.sceneLoaded += SceneLoaded;
        OpenXRPlugin.Log.LogInfo("OpenXR single-pass shader cache verified; forward stereo and array-safe color processing enabled. Legacy SSAO/TAA/DoF are excluded from this path.");
        return true;
    }

    static bool HashMatches(string path, string expected)
    {
        if (!File.Exists(path) || string.IsNullOrEmpty(expected)) return false;
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase);
    }
    static void SetBuiltin(BuiltinShaderType type, string name)
    {
        if (!shaders.TryGetValue(name, out var shader)) return;
        if (!originalBuiltins.ContainsKey(type)) originalBuiltins.Add(type, (GraphicsSettings.GetShaderMode(type), GraphicsSettings.GetCustomShader(type)));
        GraphicsSettings.SetCustomShader(type, shader);
        GraphicsSettings.SetShaderMode(type, BuiltinShaderMode.UseCustom);
    }
    static void SceneLoaded(Scene scene, LoadSceneMode mode) { cameras.Clear(); assets.Clear(); scanMaterials = true; }
    static void CameraEffectsChanged() { cameras.Clear(); scanMaterials = true; }
    static bool AllowLegacyCallback(Behaviour __instance)
    {
        if (!Active) return true;
        var camera = __instance.GetComponent<Camera>();
        return !camera || !camera.stereoEnabled;
    }
    static void LoadedAsset(ref Object __result)
    {
        if (Active && __result is Shader original && ResolveShader(original) is Shader replacement) __result = replacement;
        else ApplyAsset(__result);
    }
    static void LoadedAssets(Object[] __result)
    {
        if (__result == null) return;
        for (int i = 0; i < __result.Length; i++) LoadedAsset(ref __result[i]);
    }
    static void ApplyAsset(Object asset)
    {
        if (!Active || !asset || !assets.Add(asset.GetInstanceID())) return;
        if (asset is Material material) ApplyMaterial(material);
        var go = asset as GameObject;
        if (!go && asset is Component component) go = component.gameObject;
        if (go)
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                foreach (var item in renderer.sharedMaterials) ApplyMaterial(item);
    }
    static void ApplyMaterial(Material material)
    {
        if (!material || !material.shader) return;
        var replacement = ResolveShader(material.shader);
        if (replacement) material.shader = replacement;
    }
    static Shader ResolveShader(Shader original)
    {
        if (!original) return null;
        int id = original.GetInstanceID();
        if (!resolvedShaders.TryGetValue(id, out var replacement))
        {
            shaders.TryGetValue(original.name, out replacement);
            // Every name can collide with a shader embedded by another mod.
            // The explicit legacy UI fallback alias has no own properties.
            if (replacement && original.name != "UI/Unlit/Transparent" && !SameShaderInterface(original, replacement)) replacement = null;
            resolvedShaders[id] = replacement;
        }
        return replacement;
    }
    static bool SameShaderInterface(Shader original, Shader replacement)
    {
        int count = original.GetPropertyCount();
        if (count != replacement.GetPropertyCount()) return false;
        for (int i = 0; i < count; i++)
            if (original.GetPropertyNameId(i) != replacement.GetPropertyNameId(i) || original.GetPropertyType(i) != replacement.GetPropertyType(i)) return false;
        var names = original.keywordSpace.keywordNames;
        var other = new HashSet<string>(replacement.keywordSpace.keywordNames, StringComparer.Ordinal);
        return names.Length == other.Count && Array.TrueForAll(names, other.Contains);
    }
    static void ConfigureCamera(Camera camera)
    {
        if (!Active || !camera.stereoEnabled) return;
        if (camera == worldCamera) RequireWorldDepth(camera);
        if (!cameras.Add(camera.GetInstanceID())) return;
        if (scanMaterials)
        {
            scanMaterials = false;
            foreach (var material in Resources.FindObjectsOfTypeAll<Material>()) ApplyMaterial(material);
        }
        camera.renderingPath = RenderingPath.Forward;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.ResetStereoProjectionMatrices();
        foreach (var effect in camera.GetComponents<Behaviour>())
            if (effect is PostProcessingBehaviour || effect is AmplifyOcclusionEffect ||
                effect is UnityStandardAssets.ImageEffects.SunShafts || effect is UnityStandardAssets.ImageEffects.DepthOfField)
                effect.enabled = false;
        foreach (var evt in cameraEvents)
            foreach (var buffer in camera.GetCommandBuffers(evt))
                if (buffer.name.StartsWith("AmplifyOcclusion_", StringComparison.Ordinal)) camera.RemoveCommandBuffer(evt, buffer);
        if (camera.name == "VRCamera")
        {
            // Legacy AO requests a full-scene depth/normal render. None of
            // the retained color effects consumes it. Keep native depth for
            // water, soft particles and screen-space shadows.
            worldCamera = camera;
            RequireWorldDepth(camera);
            var original = camera.GetComponent<PostProcessingBehaviour>();
            if (original && original.profile)
            {
                var color = camera.GetComponent<OpenXRStereoColor>() ?? camera.gameObject.AddComponent<OpenXRStereoColor>();
                color.Profile = original.profile;
            }
        }
        else camera.depthTextureMode = DepthTextureMode.None;
    }
    static void RequireWorldDepth(Camera camera)
    {
        var mode = (camera.depthTextureMode & ~DepthTextureMode.DepthNormals) | DepthTextureMode.Depth;
        if (camera.depthTextureMode != mode) camera.depthTextureMode = mode;
    }
    internal static void Shutdown()
    {
        worldCamera = null;
        Camera.onPreCull -= ConfigureCamera;
        SceneManager.sceneLoaded -= SceneLoaded;
        patches?.UnpatchSelf();
        patches = null;
        foreach (var original in originalBuiltins)
        {
            GraphicsSettings.SetCustomShader(original.Key, original.Value.shader);
            GraphicsSettings.SetShaderMode(original.Key, original.Value.mode);
        }
        originalBuiltins.Clear();
        foreach (var bundle in bundles)
            if (bundle) bundle.Unload(false);
        bundles.Clear();
        Active = false;
        scanMaterials = false;
        cameras.Clear(); assets.Clear(); resolvedShaders.Clear(); shaders.Clear();
    }
}
