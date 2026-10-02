using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using ValheimVRMod.VRCore.Backends;
using Hand = ValheimVRMod.VRCore.Backends.VRHand;

namespace Nikami.OpenXR;

// Native OpenXR camera/presentation corrections and upstream scene safety guards.
internal static class RuntimeAdapter
{
    static Camera worldCamera;
    static Func<Hand> leftHand, rightHand;
    static Func<Component> leftEstimator, rightEstimator;
    static AccessTools.FieldRef<MeshRenderer> hipTrackerRenderer;
    static TransformSlot trackedPelvis, pelvis;
    sealed class RenderFrameStamp { internal int Frame = -1; }
    static readonly ConditionalWeakTable<MonoBehaviour, RenderFrameStamp> RenderFrames = new();
    static int lastVrCameraQualityFrame = -1;
    static readonly List<Behaviour> cameraEffects = new();

    // Resolve metadata once; keep reading the live Unity objects after scene loads.
    sealed class TransformSlot
    {
        readonly Func<Transform> get;
        readonly Action<Transform> set;
        readonly string objectName;
        internal TransformSlot(Type type, string name, string objectName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var field = type.GetField(name, flags);
            var property = field == null ? type.GetProperty(name, flags) : null;
            if (field != null)
            {
                var access = AccessTools.StaticFieldRefAccess<Transform>(field);
                get = () => access();
                set = value => access() = value;
            }
            else if (property != null)
            {
                get = AccessTools.MethodDelegate<Func<Transform>>(property.GetGetMethod(true));
                if (property.CanWrite) set = AccessTools.MethodDelegate<Action<Transform>>(property.GetSetMethod(true));
            }
            this.objectName = objectName;
        }
        internal Transform Ensure()
        {
            if (get == null) return null;
            var current = get();
            if (current) return current;
            if (set == null) return null;
            var go = new GameObject(objectName) { hideFlags = HideFlags.HideAndDontSave };
            current = go.transform;
            set(current);
            return current;
        }
    }

    internal static void Install(Harmony h)
    {
        OpenXRImageQuality.Install(h);
        OpenXRAmbientOcclusion.Install(h);
        OpenXREquipmentQuality.Install(h);
        OpenXRInputFocus.Install();
        // VHVR's render callbacks update gameplay transforms, but Unity invokes
        // them once for every active camera. In multipass those cameras consume
        // the same game-frame pose; repeating the writes for each eye only adds
        // main-thread work and can make the hand/weapon path hitch. Gate each
        // live component instance once per frame while preserving the first
        // callback's ordering and all physics/input updates.
        foreach (var target in new[] {
            AccessTools.Method("ValheimVRMod.Scripts.WeaponWield:OnRenderObject"),
            AccessTools.Method("ValheimVRMod.Scripts.Block.Block:OnRenderObject"),
            AccessTools.Method("ValheimVRMod.Scripts.Block.ShieldBlock:OnRenderObject"),
            AccessTools.Method("ValheimVRMod.Scripts.WeaponCollision:OnRenderObject"),
            AccessTools.Method("ValheimVRMod.Scripts.FistCollision:OnRenderObject"),
            AccessTools.Method("ValheimVRMod.Scripts.HandGesture:OnRenderObject"),
            AccessTools.Method("ValheimVRMod.VRCore.UI.VRGUI:OnRenderObject"),
            AccessTools.Method("ValheimVRMod.Utilities.PhysicsEstimator:OnRenderObject")
        })
            if (target != null) h.Patch(target, prefix: new HarmonyMethod(typeof(RuntimeAdapter), nameof(RenderOncePerFrame)));
        h.Patch(AccessTools.Method("ValheimVRMod.Utilities.CameraUtils:getCamera"),
            new HarmonyMethod(typeof(RuntimeAdapter), nameof(SelectMainCamera)));
        h.Patch(AccessTools.Method("ValheimVRMod.VRCore.VRPlayer:enableCameras"),
            new HarmonyMethod(typeof(RuntimeAdapter), nameof(RefreshWorldCamera)));
        h.Patch(AccessTools.Method("ValheimVRMod.Utilities.CameraUtils:copyCamera"),
            postfix: new HarmonyMethod(typeof(RuntimeAdapter), nameof(InitializeWorldColor)));
        // VHVR's legacy post stack is authored for the mono/OpenVR camera.
        // In native OpenXR multipass the depth-of-field and motion-blur
        // models can reapply after the graphics menu changes settings,
        // producing sudden focus spikes or one-eye softness. Keep the rest of
        // the post stack (bloom, color grading and occlusion) for fidelity.
        Camera.onPreCull += ConfigureOpenXrCamera;
        // Unity can invoke the legacy post component before Camera.onPreCull.
        // Guard its failed-runtime path at the component boundary so a VR
        // camera left behind after an unavailable OpenXR runtime cannot throw
        // a NullReferenceException every frame.
        var postProcessingPreCull = AccessTools.Method(
            typeof(UnityEngine.PostProcessing.PostProcessingBehaviour), "OnPreCull");
        if (postProcessingPreCull != null)
            h.Patch(postProcessingPreCull,
                prefix: new HarmonyMethod(typeof(RuntimeAdapter), nameof(PreparePostProcessing)));
        // VHVR keeps its VR camera across scene loads, but its underwater light
        // blocker is an unparented scene object. Loading the world destroys that
        // blocker and makes every subsequent physics tick throw. Give the object
        // the camera's lifetime without parenting it to the moving head.
        h.Patch(AccessTools.Method("ValheimVRMod.Scripts.UnderwaterEffectsUpdater:Init"),
            postfix: new HarmonyMethod(typeof(RuntimeAdapter), nameof(PreserveUnderwaterResources)));
        // The GUI panel is recreated across scenes while its camera survives
        // under the persistent head. Reuse that camera instead of adding a
        // second identical stereo camera on every transition.
        h.Patch(AccessTools.Method("ValheimVRMod.VRCore.UI.VRGUI:createUiPanelCamera"),
            new HarmonyMethod(typeof(RuntimeAdapter), nameof(CreatePanelCameraOnce)));
        // VHVR 0.10.3 can enter its body-tracker update with a provider but
        // without the optional waist debug renderer (common on runtimes that
        // expose only HMD + hands).  The null renderer aborts VRPlayer.Update
        // before it can attach the rig to the character, which in turn makes
        // WeaponCollision reject every hit.  Supply a disabled sentinel so
        // the normal attach/hand/weapon path continues; the sentinel is never
        // rendered or used for tracking.
        var vrPlayerUpdate = AccessTools.Method("ValheimVRMod.VRCore.VRPlayer:Update");
        if (vrPlayerUpdate != null)
        {
            var playerType = vrPlayerUpdate.DeclaringType;
            var hipField = AccessTools.Field(playerType, "hipTrackerRenderer");
            if (hipField != null) hipTrackerRenderer = AccessTools.StaticFieldRefAccess<MeshRenderer>(hipField);
            trackedPelvis = new TransformSlot(playerType, "trackedPelvis", "NikamiOpenXRTrackedPelvisSentinel");
            pelvis = new TransformSlot(playerType, "pelvis", "NikamiOpenXRPelvisSentinel");
            h.Patch(vrPlayerUpdate, new HarmonyMethod(typeof(RuntimeAdapter), nameof(EnsureHipRenderer)));
        }
        var shieldParry = AccessTools.Method("ValheimVRMod.Scripts.Block.ShieldBlock:CheckParryMotion");
        if (shieldParry != null)
        {
            var playerType = AccessTools.TypeByName("ValheimVRMod.VRCore.VRPlayer");
            leftHand = AccessTools.MethodDelegate<Func<Hand>>(AccessTools.PropertyGetter(playerType, "leftHand"));
            rightHand = AccessTools.MethodDelegate<Func<Hand>>(AccessTools.PropertyGetter(playerType, "rightHand"));
            leftEstimator = AccessTools.MethodDelegate<Func<Component>>(AccessTools.PropertyGetter(playerType, "leftHandPhysicsEstimator"));
            rightEstimator = AccessTools.MethodDelegate<Func<Component>>(AccessTools.PropertyGetter(playerType, "rightHandPhysicsEstimator"));
            h.Patch(shieldParry, new HarmonyMethod(typeof(RuntimeAdapter), nameof(SkipIncompleteShieldParry)));
        }
        h.Patch(AccessTools.Method("ValheimVRMod.Scripts.LocalWeaponWield:OnDestroy"), transpiler: new HarmonyMethod(typeof(RuntimeAdapter), nameof(SafeWeaponCleanup)));
    }
    static bool RenderOncePerFrame(MonoBehaviour __instance)
    {
        if (!__instance) return false;
        var stamp = RenderFrames.GetOrCreateValue(__instance);
        int frame = Time.frameCount;
        if (stamp.Frame == frame) return false;
        stamp.Frame = frame;
        return true;
    }
    static bool SelectMainCamera(string name, ref Camera __result)
    {
        if (name != "Main Camera") return true;
        // Valheim 1.0 retains an inactive EntryPointSceneLoader camera with this
        // same name and a zero culling mask. VHVR's name-only cache can choose
        // it, leaving the desktop camera rendering the world into both eyes.
        var camera = FindWorldCamera();
        if (!camera) return true;
        __result = camera;
        return false;
    }
    static Camera FindWorldCamera()
    {
        var camera = GameCamera.instance ? GameCamera.instance.GetComponent<Camera>() : null;
        if (camera && camera.cullingMask != 0) return camera;
        if (worldCamera && worldCamera.cullingMask != 0) return worldCamera;
        // The menu has no GameCamera singleton. Revisit the early empty
        // EntryPointSceneLoader camera when the real menu camera appears.
        return UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
            .FirstOrDefault(c => c.name == "Main Camera" && c.enabled && c.cullingMask != 0);
    }
    static void RefreshWorldCamera(Camera ____vrCam)
    {
        var camera = FindWorldCamera();
        if (!camera || camera == worldCamera) return;
        worldCamera = camera;
        var vr = ____vrCam;
        if (!vr) return;
        // Let VHVR's own initialization copy the new scene's effects and
        // visibility mask, then disable the ordinary game camera as usual.
        foreach (var fade in vr.GetComponents<MonoBehaviour>()
                     .Where(c => c && c.GetType().FullName == "ValheimVRMod.Scripts.FadingManager"))
            UnityEngine.Object.Destroy(fade);
        vr.enabled = false;
    }
    static void ConfigureOpenXrCamera(Camera camera)
    {
        if (!camera || camera.name != "VRCamera" || Time.frameCount == lastVrCameraQualityFrame)
            return;
        lastVrCameraQualityFrame = Time.frameCount;
        camera.GetComponents(cameraEffects);
        foreach (var effect in cameraEffects)
        {
            if (!effect) continue;
            string name = effect.GetType().Name;
            if (name == "PostProcessingBehaviour")
            {
                // If the OpenXR runtime did not come up, VHVR can still leave
                // its camera alive while its post-processing component graph is
                // only partially initialized. Do not let that graph throw on
                // every camera callback while the adapter is failed closed.
                if (!OpenXRPlugin.Ready)
                {
                    effect.enabled = false;
                    continue;
                }
                var profile = ((UnityEngine.PostProcessing.PostProcessingBehaviour)effect).profile;
                if (profile == null || profile.depthOfField == null || profile.motionBlur == null)
                {
                    effect.enabled = false;
                    continue;
                }
                // VHVR's AmplifyOcclusionEffect is the stereo-friendly SSAO
                // replacement. Leave that effect enabled, but suppress the
                // vanilla AO model so the two passes never stack.
                if (profile.ambientOcclusion != null)
                    profile.ambientOcclusion.enabled = false;
                profile.depthOfField.enabled = false;
                profile.motionBlur.enabled = false;
                PrepareWorldColor(camera, profile);
                continue;
            }
            if (name == "DepthOfField" || name == "MotionBlur")
                effect.enabled = false;
        }
    }
    static bool PreparePostProcessing(UnityEngine.PostProcessing.PostProcessingBehaviour __instance)
    {
        if (!OpenXRPlugin.Ready)
        {
            __instance.enabled = false;
            return false;
        }
        var camera = __instance.GetComponent<Camera>();
        if (!camera || camera.name != "VRCamera")
            return true;
        var profile = __instance.profile;
        if (profile == null)
            return false;
        if (profile.ambientOcclusion != null)
            profile.ambientOcclusion.enabled = false;
        if (profile.depthOfField != null)
            profile.depthOfField.enabled = false;
        if (profile.motionBlur != null)
            profile.motionBlur.enabled = false;
        PrepareWorldColor(camera, profile);
        return true;
    }
    static void PrepareWorldColor(Camera camera, UnityEngine.PostProcessing.PostProcessingProfile profile)
    {
        if (SinglePassRenderer.Active) return;
        // Keep highlights until the native ACES/color-grading stage. VHVR's
        // legacy CopyCamera forces LDR even when the source camera uses HDR.
        if (!camera.allowHDR) camera.allowHDR = true;
        // Apply the same policy while the physical session is idle: VRCamera
        // still renders the desktop view then. Gating on stereoEnabled left
        // that view on VHVR's old TAA, whose jitter is applied after rendering.
        // Use the native spatial AA shader with the existing color operators.
        var settings = profile.antialiasing.settings;
        if (settings.method != UnityEngine.PostProcessing.AntialiasingModel.Method.Fxaa)
        {
            settings.method = UnityEngine.PostProcessing.AntialiasingModel.Method.Fxaa;
            profile.antialiasing.settings = settings;
        }
    }
    static void InitializeWorldColor(Camera to)
    {
        // CopyCamera explicitly disables HDR. Correct it before the camera is
        // enabled, rather than changing its render target on the first cull.
        if (to && to.name == "VRCamera" && !SinglePassRenderer.Active)
            to.allowHDR = true;
    }
    static void PreserveUnderwaterResources(Component __instance, GameObject ___underwaterLightBlocker)
    {
        if (!___underwaterLightBlocker) return;
        UnityEngine.Object.DontDestroyOnLoad(___underwaterLightBlocker);
        var owner = __instance.GetComponent<OpenXRSceneResources>() ??
            __instance.gameObject.AddComponent<OpenXRSceneResources>();
        owner.Own(___underwaterLightBlocker);
        var renderer = ___underwaterLightBlocker.GetComponent<Renderer>();
        if (renderer && renderer.sharedMaterial) owner.Own(renderer.sharedMaterial);
    }
    static bool CreatePanelCameraOnce(Camera ____uiPanelCamera) => !____uiPanelCamera;
    static void EnsureHipRenderer()
    {
        var existing = hipTrackerRenderer != null ? hipTrackerRenderer() : null;
        if (hipTrackerRenderer != null && !existing)
        {
            var sentinel = new GameObject("NikamiOpenXRHipTrackerSentinel");
            sentinel.hideFlags = HideFlags.HideAndDontSave;
            var mesh = sentinel.AddComponent<MeshRenderer>();
            mesh.enabled = false;
            hipTrackerRenderer() = mesh;
        }
        var tracked = trackedPelvis.Ensure();
        var currentPelvis = pelvis.Ensure();
        if (currentPelvis && tracked && currentPelvis.parent != tracked) currentPelvis.SetParent(tracked, false);
    }
    static bool SkipIncompleteShieldParry()
    {
        // Resolve upstream members once at installation, not during physics.
        // Still call the live getters so scene changes and lazy initialization work.
        return leftHand() && rightHand() && leftEstimator() && rightEstimator();
    }
    static IEnumerable<CodeInstruction> SafeWeaponCleanup(IEnumerable<CodeInstruction> source)
    {
        foreach (var instruction in source)
        {
            if (instruction.Calls(AccessTools.PropertyGetter(typeof(Component), "gameObject")))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(RuntimeAdapter), nameof(LiveObject));
            }
            yield return instruction;
        }
    }
    static GameObject LiveObject(Component component) => component ? component.gameObject : null;
}

// Resources which must follow the persistent VR camera's lifetime but must not
// inherit its head transform. Only objects created by VHVR's Init are registered.
internal sealed class OpenXRSceneResources : MonoBehaviour
{
    readonly HashSet<UnityEngine.Object> owned = new();
    internal void Own(UnityEngine.Object resource) => owned.Add(resource);
    void OnDestroy()
    {
        foreach (var resource in owned)
            if (resource) Destroy(resource);
        owned.Clear();
    }
}
