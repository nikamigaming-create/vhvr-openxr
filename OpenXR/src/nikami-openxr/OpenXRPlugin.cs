using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using Valve.VR;

namespace Nikami.OpenXR;

[BepInPlugin("nikami.openxr", "Nikami OpenXR", "0.1.0")]
[BepInDependency("org.bepinex.plugins.valheimvrmod")]
[DefaultExecutionOrder(-30000)]
public sealed class OpenXRPlugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    internal static OpenXRLoader Loader;
    internal static bool Ready;
    public static bool SessionStarted { get; private set; }
    public static bool DisplayHealthy { get; private set; }
    public static bool FramesHealthy { get; private set; }
    public static bool DisplayFocused { get; private set; } = true;
    public static int LastRenderPassCount { get; private set; }
    public static int RecoveryCount { get; private set; }
    public static int VRCameraFrames { get; private set; }
    public static float LastVRCameraFrameAge { get; private set; }
    public static event Action<OpenXRSettings> ConfigureRuntime;
    static float nextReport;
    static float sessionStartedAt = -1;
    static float unhealthySince = -1;
    static bool recoveryAttempted;
    static bool sawHealthyFrame;
    static float lastVrCameraFrame = -1;
    static bool cameraExpected;
    static float nextCameraScan;
    static XRDisplaySubsystem observedDisplay;
    bool previousRunInBackground;
    void Awake()
    {
        Log = Logger;
        var arguments = Environment.GetCommandLineArgs();
        if (!Array.Exists(arguments, a => a.Equals("-ModEnabled=true", StringComparison.OrdinalIgnoreCase)) ||
            Array.Exists(arguments, a => a.Equals("-vrbackend=steamvr", StringComparison.OrdinalIgnoreCase)))
        {
            enabled = false;
            return;
        }
        previousRunInBackground = Application.runInBackground;
        Application.runInBackground = true;
        var harmony = new Harmony("nikami.openxr");
        try
        {
            RuntimeAdapter.Install(harmony);
            InputAdapter.Install(harmony);
            NikamiIntegration.Install(harmony);
            Patch(harmony, "ValheimVRMod.VRCore.VRManager:InitializeVR", nameof(Initialize));
            Patch(harmony, "ValheimVRMod.VRCore.VRManager:StartVR", nameof(StartVR));
            // Native OpenXR projection layers already contain the world-space GUI.
            Patch(harmony, "ValheimVRMod.Utilities.VHVRConfig:GetUseOverlayGui", nameof(NoOverlay));
            Camera.onPreRender += CountVRCameraFrame;
            Log.LogInfo("OpenXR adapter installed. Upstream VHVR gameplay assembly is unchanged.");
        }
        catch (Exception error)
        {
            // Do not fall through to SteamVR when an upstream API changes.
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("org.bepinex.plugins.valheimvrmod", out var upstream) && upstream.Instance)
                upstream.Instance.enabled = false;
            harmony.UnpatchSelf();
            Logger.LogError("OpenXR compatibility initialization failed; VR startup stopped. " + error);
            enabled = false;
            Application.runInBackground = previousRunInBackground;
        }
    }

    static void Patch(Harmony h, string target, string prefix)
    {
        var original = AccessTools.Method(target) ?? throw new MissingMethodException(target);
        h.Patch(original, new HarmonyMethod(typeof(OpenXRPlugin), prefix));
    }
    static bool NoOverlay(ref bool __result) { __result = false; return false; }
    static bool Initialize(ref bool __result)
    {
        try
        {
            var settings = OpenXRSettings.Instance;
            settings.renderMode = OpenXRSettings.RenderMode.MultiPass;
            settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
            var profiles = new OpenXRFeature[] {
                ScriptableObject.CreateInstance<OculusTouchControllerProfile>(),
                ScriptableObject.CreateInstance<ValveIndexControllerProfile>(),
                ScriptableObject.CreateInstance<HTCViveControllerProfile>(),
                ScriptableObject.CreateInstance<MicrosoftMotionControllerProfile>(),
                ScriptableObject.CreateInstance<KHRSimpleControllerProfile>()
            };
            foreach (var profile in profiles)
            {
                AccessTools.Field(typeof(OpenXRFeature), "nameUi").SetValue(profile, profile.GetType().Name);
                AccessTools.Field(typeof(OpenXRFeature), "openxrExtensionStrings").SetValue(profile, "");
                AccessTools.Field(typeof(OpenXRFeature), "targetOpenXRApiVersion")?.SetValue(profile, "1.0");
                profile.enabled = true;
                DontDestroyOnLoad(profile);
            }
            AccessTools.Field(typeof(OpenXRSettings), "features").SetValue(settings, profiles);
            ConfigureRuntime?.Invoke(settings);
            var inputSettings = UnityEngine.InputSystem.InputSystem.settings;
            Log.LogInfo("Input device allowlist: " + string.Join(",", inputSettings.supportedDevices));
            if (inputSettings.supportedDevices.Count != 0)
                inputSettings.supportedDevices = inputSettings.supportedDevices.Concat(new[] { "XRHMD", "XRController" }).Distinct().ToArray();
            DontDestroyOnLoad(settings);
            Loader = ScriptableObject.CreateInstance<OpenXRLoader>();
            DontDestroyOnLoad(Loader);
            if (!Loader.Initialize()) throw new InvalidOperationException("Unity OpenXR loader initialization failed; see Player.log.");
            SteamVR_Actions.PreInitialize();
            RuntimeAdapter.InitializeManagedActions();
            InputAdapter.LoadBindings();
            Ready = true;
            Log.LogInfo("Native Unity OpenXR initialized: " + OpenXRRuntime.name + " " + OpenXRRuntime.version);
            __result = true;
        }
        catch (Exception ex)
        {
            Ready = false;
            Loader?.Deinitialize();
            Log.LogError(ex);
            __result = false;
        }
        return false;
    }
    static bool StartVR(ref bool __result)
    {
        __result = Ready && Loader != null && Loader.Start();
        SessionStarted = __result;
        DisplayHealthy = false;
        DisplayFocused = true;
        LastRenderPassCount = 0;
        RecoveryCount = 0;
        sawHealthyFrame = false;
        FramesHealthy = true;
        VRCameraFrames = 0;
        LastVRCameraFrameAge = -1;
        lastVrCameraFrame = -1;
        cameraExpected = false;
        nextCameraScan = 0;
        unhealthySince = -1;
        sessionStartedAt = __result ? Time.unscaledTime : -1;
        recoveryAttempted = false;
        var input = Loader?.GetLoadedSubsystem<XRInputSubsystem>();
        input?.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
        Log.LogInfo("OpenXR session start: " + __result + "; render mode=" + OpenXRSettings.Instance.renderMode);
        return false;
    }
    void Update()
    {
        if (!Ready) return;
        // Run the upstream action/event machinery once per game frame. Only its data
        // provider changes; button edges and action-set activation stay upstream.
        SteamVR_Input.UpdateNonVisualActions();
        SteamVR_Input.UpdatePoseActions();
        if (SessionStarted)
        {
            UpdateWatchdog();
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.F10)) TryRecover("Ctrl+F10");
        }
        if (Time.unscaledTime < nextReport) return;
        nextReport = Time.unscaledTime + 10;
        var display = Loader?.GetLoadedSubsystem<XRDisplaySubsystem>();
        Log.LogInfo($"OpenXR live: display={display?.running}, passes={LastRenderPassCount}, healthy={DisplayHealthy}, "
            + $"framesHealthy={FramesHealthy}, vrFrames={VRCameraFrames}, frameAge={LastVRCameraFrameAge:0.0}, "
            + $"focused={DisplayFocused}, recoveries={RecoveryCount}, devices={UnityEngine.InputSystem.InputSystem.devices.Count}, "
            + $"head={InputTracking.GetLocalPosition(XRNode.Head)}");
    }

    static void CountVRCameraFrame(Camera camera)
    {
        if (camera && camera.name == "VRCamera")
        {
            VRCameraFrames++;
            lastVrCameraFrame = Time.unscaledTime;
            LastVRCameraFrameAge = 0;
        }
    }

    void UpdateWatchdog()
    {
        var display = Loader?.GetLoadedSubsystem<XRDisplaySubsystem>();
        if (!ReferenceEquals(display, observedDisplay))
        {
            if (observedDisplay != null) observedDisplay.displayFocusChanged -= OnDisplayFocusChanged;
            observedDisplay = display;
            if (observedDisplay != null) observedDisplay.displayFocusChanged += OnDisplayFocusChanged;
        }
        bool running = false;
        int passes = 0;
        try
        {
            running = display != null && display.running;
            passes = display == null ? 0 : display.GetRenderPassCount();
        }
        catch (Exception error)
        {
            Log.LogWarning("OpenXR watchdog read failed: " + error.Message);
        }
        LastRenderPassCount = passes;
        DisplayHealthy = running && passes > 0;
        LastVRCameraFrameAge = lastVrCameraFrame < 0 ? -1 : Mathf.Max(0, Time.unscaledTime - lastVrCameraFrame);
        if (Time.unscaledTime >= nextCameraScan)
        {
            nextCameraScan = Time.unscaledTime + .5f;
            cameraExpected = Player.m_localPlayer && Camera.allCameras.Any(camera =>
                camera && camera.name == "VRCamera" && camera.enabled && camera.gameObject.activeInHierarchy);
        }
        FramesHealthy = DisplayHealthy && (!cameraExpected || (lastVrCameraFrame >= 0 && LastVRCameraFrameAge <= 2.5f));
        if (DisplayHealthy && FramesHealthy)
        {
            sawHealthyFrame = true;
            unhealthySince = -1;
            return;
        }
        if (DisplayHealthy && !cameraExpected)
        {
            // Menus can have a live OpenXR display before the gameplay camera
            // exists. Do not call this a black frame or restart the loader.
            unhealthySince = -1;
            return;
        }
        if (unhealthySince < 0) unhealthySince = Time.unscaledTime;
        // The loader can take a few seconds to publish its first display. Give
        // it a grace period, then make one guarded restart attempt per session.
        // Do not restart during normal startup: Unity/OpenXR may publish an
        // idle subsystem for several seconds before the first render pass. A
        // recovery is only automatic after at least one real display frame.
        if (sawHealthyFrame && !recoveryAttempted && Time.unscaledTime - sessionStartedAt >= 8f
            && Time.unscaledTime - unhealthySince >= 2f)
            TryRecover(DisplayHealthy ? "VRCamera stopped submitting frames" : "display stopped or has no render passes");
    }

    void OnDisplayFocusChanged(bool focused)
    {
        DisplayFocused = focused;
        Log.LogInfo("OpenXR display focus: " + focused);
    }

    void TryRecover(string reason)
    {
        if (recoveryAttempted || Loader == null) return;
        recoveryAttempted = true;
        RecoveryCount++;
        Log.LogWarning("OpenXR watchdog recovery (" + reason + "): restarting the managed display session once.");
        try
        {
            Loader.Stop();
            bool started = Loader.Start();
            SessionStarted = started;
            sessionStartedAt = Time.unscaledTime;
            unhealthySince = -1;
            var input = Loader.GetLoadedSubsystem<XRInputSubsystem>();
            input?.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            Log.LogInfo("OpenXR watchdog recovery result: " + started + "; waiting for display frames.");
        }
        catch (Exception error)
        {
            SessionStarted = false;
            Log.LogError("OpenXR watchdog recovery failed: " + error);
        }
    }
    void OnDestroy()
    {
        if (!enabled) return;
        Ready = false;
        SessionStarted = false;
        Camera.onPreRender -= CountVRCameraFrame;
        if (observedDisplay != null) observedDisplay.displayFocusChanged -= OnDisplayFocusChanged;
        observedDisplay = null;
        Loader?.Stop();
        Loader?.Deinitialize();
        Application.runInBackground = previousRunInBackground;
    }
}
