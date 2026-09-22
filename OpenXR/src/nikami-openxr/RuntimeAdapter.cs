using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Valve.VR;
using Valve.VR.InteractionSystem;

namespace Nikami.OpenXR;

// Keep the managed SteamVR interaction objects used by VHVR's shipped prefabs,
// but never initialize their OpenVR compositor/runtime. Unity OpenXR owns those.
internal static class RuntimeAdapter
{
    static readonly SteamVR ManagedRuntime = (SteamVR)FormatterServices.GetUninitializedObject(typeof(SteamVR));
    static readonly CVRInput ManagedInput = (CVRInput)FormatterServices.GetUninitializedObject(typeof(CVRInput));
    static Camera worldCamera;
    static Func<Hand> leftHand, rightHand;
    static Func<Component> leftEstimator, rightEstimator;
    static AccessTools.FieldRef<MeshRenderer> hipTrackerRenderer;
    static TransformSlot trackedPelvis, pelvis;

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
        Hook(h, typeof(SteamVR), "Initialize", nameof(Skip));
        Hook(h, typeof(SteamVR), "get_instance", nameof(GetRuntime));
        Hook(h, typeof(SteamVR), "get_hmd_DisplayFrequency", nameof(Frequency));
        Hook(h, typeof(SteamVR), "SafeDispose", nameof(Skip));
        Hook(h, typeof(Valve.VR.OpenVR), "get_Input", nameof(GetInput));
        // VHVR's pose behaviours are also the bridge that copies SteamVR action
        // poses onto the tracked hands.  Skipping SteamVR_Behaviour or
        // SteamVR_TrackedObject Update/LateUpdate leaves the hand visually at
        // its spawn pose and gives WeaponCollision a zero velocity.  Only the
        // compositor renderer must be suppressed; its lifecycle is the part
        // that would try to submit through OpenVR.
        foreach (var type in new[] { typeof(SteamVR_Render) })
            foreach (var method in new[] { "Awake", "OnEnable", "Start", "Update", "LateUpdate", "FixedUpdate", "OnDisable", "OnDestroy" })
            {
                // AccessTools.DeclaredMethod logs a warning when a SteamVR type
                // has no method with this name.  VHVR 0.10.3 moved several of
                // these lifecycle methods, so enumerate the methods we can
                // actually patch instead of asking Harmony to resolve ghosts.
                var original = type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                               BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.DeclaredOnly)
                    .FirstOrDefault(candidate => candidate.Name == method &&
                                                  candidate.ReturnType == typeof(void) &&
                                                  candidate.GetParameters().Length == 0);
                if (original != null && original.ReturnType == typeof(void)) h.Patch(original, new HarmonyMethod(typeof(RuntimeAdapter), nameof(Skip)));
            }
        Hook(h, typeof(Valve.VR.InteractionSystem.Player), "Start", nameof(StartPlayer));
        // Runtime controller render-model download belongs to OpenVR. VHVR renders
        // the native character hands/held items; its transforms remain intact.
        Hook(h, typeof(Hand), "InitController", nameof(Skip));
        h.Patch(AccessTools.Method(typeof(Hand), "GetTrackedObjectVelocity"),
            postfix: new HarmonyMethod(typeof(RuntimeAdapter), nameof(TrackedVelocity)));
        Hook(h, typeof(SteamVR_Input), "UpdateSkeletonActions", nameof(Skip));
        Hook(h, typeof(SteamVR_Action_Pose), "SetTrackingUniverseOrigin", nameof(SetOrigin));
        h.Patch(AccessTools.Method("ValheimVRMod.Utilities.CameraUtils:getCamera"),
            new HarmonyMethod(typeof(RuntimeAdapter), nameof(SelectMainCamera)));
        h.Patch(AccessTools.Method("ValheimVRMod.VRCore.VRPlayer:enableCameras"),
            new HarmonyMethod(typeof(RuntimeAdapter), nameof(RefreshWorldCamera)));
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
    static void Hook(Harmony h, Type type, string method, string prefix)
    {
        var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method);
        h.Patch(target, new HarmonyMethod(typeof(RuntimeAdapter), prefix));
    }
    internal static void InitializeManagedActions()
    {
        var settings = SteamVR_Settings.instance;
        AccessTools.Property(typeof(SteamVR), "settings").SetValue(null, settings);
        SteamVR.initializedState = SteamVR.InitializedStates.InitializeSuccess;
        settings.autoEnableVR = false;
        settings.lockPhysicsUpdateRateToRenderFrequency = false;
        SteamVR_Input.Initialize();
    }
    static bool Skip() => false;
    static bool SelectMainCamera(string name, ref Camera __result)
    {
        if (name != "Main Camera") return true;
        // Valheim 1.0 retains an inactive EntryPointSceneLoader camera with this
        // same name and a zero culling mask. VHVR's name-only cache can choose
        // it, leaving the desktop camera rendering the world into both eyes.
        var camera = GameCamera.instance ? GameCamera.instance.GetComponent<Camera>() : null;
        if (!camera)
            camera = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == name && c.enabled && c.cullingMask != 0);
        if (!camera) return true;
        __result = camera;
        return false;
    }
    static void RefreshWorldCamera(Camera ____vrCam)
    {
        var camera = GameCamera.instance ? GameCamera.instance.GetComponent<Camera>() : null;
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
    static void TrackedVelocity(Hand __instance, float timeOffset, ref Vector3 __result)
    {
        if (__result.sqrMagnitude > .0001f || __instance == null || timeOffset != 0)
            return;
        int hand = __instance.handType == SteamVR_Input_Sources.LeftHand ? 1 :
                   __instance.handType == SteamVR_Input_Sources.RightHand ? 2 : 0;
        if (hand == 0)
            return;
        var poseAction = hand == 1 ? SteamVR_Actions.valheim_PoseL : SteamVR_Actions.valheim_PoseR;
        var localVelocity = poseAction?.GetVelocity(hand == 1 ? SteamVR_Input_Sources.LeftHand : SteamVR_Input_Sources.RightHand) ?? Vector3.zero;
        if (localVelocity.sqrMagnitude < .0001f && !InputAdapter.TryGetVelocity(hand, out localVelocity))
            return;
        var origin = Valve.VR.InteractionSystem.Player.instance?.trackingOriginTransform;
        __result = origin != null ? origin.TransformVector(localVelocity) : localVelocity;
    }
    static bool GetRuntime(ref SteamVR __result) { __result = ManagedRuntime; return false; }
    static bool GetInput(ref CVRInput __result) { __result = ManagedInput; return false; }
    static bool Frequency(ref float __result) { __result = 90; return false; }
    static bool SetOrigin(ETrackingUniverseOrigin newOrigin)
    {
        AccessTools.Method(typeof(SteamVR_Action_Pose_Base<SteamVR_Action_Pose_Source_Map<SteamVR_Action_Pose_Source>, SteamVR_Action_Pose_Source>), "SetUniverseOrigin").Invoke(null, new object[] { newOrigin });
        return false;
    }
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
    static bool StartPlayer(Valve.VR.InteractionSystem.Player __instance, ref IEnumerator __result)
    {
        AccessTools.Field(typeof(Valve.VR.InteractionSystem.Player), "_instance").SetValue(null, __instance);
        AccessTools.Method(typeof(Valve.VR.InteractionSystem.Player), "ActivateRig").Invoke(__instance, new object[] { __instance.rigSteamVR });
        __result = Empty();
        return false;
    }
    static IEnumerator Empty() { yield break; }
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
