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
        Hook(h, typeof(Hand), "GetTrackedObjectVelocity", nameof(TrackedVelocity));
        Hook(h, typeof(SteamVR_Input), "UpdateSkeletonActions", nameof(Skip));
        Hook(h, typeof(SteamVR_Action_Pose), "SetTrackingUniverseOrigin", nameof(SetOrigin));
        h.Patch(AccessTools.Method("ValheimVRMod.Utilities.CameraUtils:getCamera"),
            new HarmonyMethod(typeof(RuntimeAdapter), nameof(SelectMainCamera)));
        h.Patch(AccessTools.Method("ValheimVRMod.VRCore.VRPlayer:enableCameras"),
            new HarmonyMethod(typeof(RuntimeAdapter), nameof(RefreshWorldCamera)));
        // VHVR 0.10.3 can enter its body-tracker update with a provider but
        // without the optional waist debug renderer (common on runtimes that
        // expose only HMD + hands).  The null renderer aborts VRPlayer.Update
        // before it can attach the rig to the character, which in turn makes
        // WeaponCollision reject every hit.  Supply a disabled sentinel so
        // the normal attach/hand/weapon path continues; the sentinel is never
        // rendered or used for tracking.
        var vrPlayerUpdate = AccessTools.Method("ValheimVRMod.VRCore.VRPlayer:Update");
        if (vrPlayerUpdate != null)
            h.Patch(vrPlayerUpdate, new HarmonyMethod(typeof(RuntimeAdapter), nameof(EnsureHipRenderer)));
        var shieldParry = AccessTools.Method("ValheimVRMod.Scripts.Block.ShieldBlock:CheckParryMotion");
        if (shieldParry != null)
            h.Patch(shieldParry, new HarmonyMethod(typeof(RuntimeAdapter), nameof(SkipIncompleteShieldParry)));
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
    static void RefreshWorldCamera(object __instance)
    {
        var camera = GameCamera.instance ? GameCamera.instance.GetComponent<Camera>() : null;
        if (!camera || camera == worldCamera) return;
        worldCamera = camera;
        var vr = AccessTools.Field(__instance.GetType(), "_vrCam").GetValue(__instance) as Camera;
        if (!vr) return;
        // Let VHVR's own initialization copy the new scene's effects and
        // visibility mask, then disable the ordinary game camera as usual.
        foreach (var fade in vr.GetComponents<MonoBehaviour>()
                     .Where(c => c && c.GetType().FullName == "ValheimVRMod.Scripts.FadingManager"))
            UnityEngine.Object.Destroy(fade);
        vr.enabled = false;
    }
    static void TrackedVelocity(Hand __instance, ref Vector3 __result)
    {
        if (__result.sqrMagnitude > .0001f || __instance == null)
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
        var vrType = AccessTools.TypeByName("ValheimVRMod.VRCore.VRPlayer");
        if (vrType == null) return;
        var field = vrType == null ? null : AccessTools.Field(vrType, "hipTrackerRenderer");
        var existing = field?.GetValue(null) as MeshRenderer;
        if (field != null && !existing)
        {
            var sentinel = new GameObject("NikamiOpenXRHipTrackerSentinel");
            sentinel.hideFlags = HideFlags.HideAndDontSave;
            var mesh = sentinel.AddComponent<MeshRenderer>();
            mesh.enabled = false;
            field.SetValue(null, mesh);
        }
        EnsureTransform(vrType, "trackedPelvis", "NikamiOpenXRTrackedPelvisSentinel");
        var pelvis = EnsureTransform(vrType, "pelvis", "NikamiOpenXRPelvisSentinel");
        var tracked = GetTransform(vrType, "trackedPelvis");
        if (pelvis && tracked && pelvis.parent != tracked) pelvis.SetParent(tracked, false);
    }
    static Transform EnsureTransform(Type vrType, string fieldName, string objectName)
    {
        var field = vrType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Static |
                                             BindingFlags.Public | BindingFlags.NonPublic);
        var property = vrType.GetProperty(fieldName, BindingFlags.Instance | BindingFlags.Static |
                                               BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null && property == null) return null;
        var current = field != null ? field.GetValue(null) as Transform : property.GetValue(null) as Transform;
        if (current) return current;
        var go = new GameObject(objectName);
        go.hideFlags = HideFlags.HideAndDontSave;
        current = go.transform;
        if (field != null) field.SetValue(null, current);
        else if (property.CanWrite) property.SetValue(null, current);
        return current;
    }
    static Transform GetTransform(Type vrType, string name)
    {
        var field = vrType.GetField(name, BindingFlags.Instance | BindingFlags.Static |
                                           BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null) return field.GetValue(null) as Transform;
        return vrType.GetProperty(name, BindingFlags.Instance | BindingFlags.Static |
                                         BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as Transform;
    }
    static bool SkipIncompleteShieldParry()
    {
        var vrType = AccessTools.TypeByName("ValheimVRMod.VRCore.VRPlayer");
        if (vrType == null) return false;
        var left = AccessTools.Property(vrType, "leftHand")?.GetValue(null);
        var right = AccessTools.Property(vrType, "rightHand")?.GetValue(null);
        if (left == null || right == null) return false;
        var leftEstimator = AccessTools.Property(vrType, "leftHandPhysicsEstimator")?.GetValue(null);
        var rightEstimator = AccessTools.Property(vrType, "rightHandPhysicsEstimator")?.GetValue(null);
        return leftEstimator != null && rightEstimator != null;
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
