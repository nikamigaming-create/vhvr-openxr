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
using ValheimVRMod.VRCore.Backends;

namespace Nikami.OpenXR;

[BepInPlugin("nikami.openxr", "VHVR Backends", "0.2.0")]
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
    // Render-pass queries cross the managed/native XR boundary. They are only
    // needed for the watchdog, not once per submitted frame; polling them at
    // 10 Hz keeps the health signal responsive without adding a native call to
    // the VR game loop.
    static float nextDisplayProbe;
    static XRDisplaySubsystem probedDisplay;
    static bool probedDisplayRunning;
    static int probedRenderPasses;
    static XRDisplaySubsystem displaySubsystem;
    static XRDisplaySubsystem observedDisplay;
    bool previousRunInBackground;
    bool usingOpenVR;
    readonly List<XRDisplaySubsystem> nativeDisplays = new();
    void Awake()
    {
        Log = Logger;
        var arguments = Environment.GetCommandLineArgs();
        if (!Array.Exists(arguments, a => a.Equals("-ModEnabled=true", StringComparison.OrdinalIgnoreCase)) ||
            Array.Exists(arguments, a => a.Equals("-flatScreenMode=true", StringComparison.OrdinalIgnoreCase)))
        {
            enabled = false;
            return;
        }
        var harmony = new Harmony("nikami.openxr");
        try
        {
            var configured = Config.Bind("Runtime", "Backend", "openxr", "VR backend: openxr or openvr (steamvr alias). Restart required; -vrbackend overrides this setting.");
            var choice = VRBackendHost.Choose(configured.Value, arguments);
            if (choice == VRBackendKind.OpenVR)
            {
                VRBackendHost.Select(new OpenVRBackend());
                usingOpenVR = true;
                previousRunInBackground = Application.runInBackground;
                OpenXRPhysicalHands.Install(harmony);
                Camera.onPreRender += CountVRCameraFrame;
                Log.LogInfo("Selected original OpenVR/SteamVR backend with shared hand/physics gameplay. OpenXR native runtime and SDK compatibility patches remain inactive.");
                return;
            }
            VRBackendHost.Select(new OpenXRBackend());
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            RuntimeAdapter.Install(harmony);
            InputAdapter.Install(harmony);
            NikamiIntegration.Install(harmony);
            // Native OpenXR projection layers already contain the world-space GUI.
            Patch(harmony, "ValheimVRMod.Utilities.VHVRConfig:GetUseOverlayGui", nameof(NoOverlay));
            Camera.onPreRender += CountVRCameraFrame;
            Log.LogInfo("Selected OpenXR backend through the shared VHVR runtime/input contract.");
        }
        catch (Exception error)
        {
            // Do not fall through to SteamVR when an upstream API changes.
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("org.bepinex.plugins.valheimvrmod", out var upstream) && upstream.Instance)
                upstream.Instance.enabled = false;
            harmony.UnpatchSelf();
            InputAdapter.Shutdown();
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
    internal static bool InitializeBackend()
    {
        bool result = false;
        Initialize(ref result);
        return result;
    }
    internal static bool StartBackend()
    {
        bool result = false;
        StartVR(ref result);
        return result;
    }
    static bool Initialize(ref bool __result)
    {
        try
        {
            var settings = OpenXRSettings.Instance;
            // The player path retains Valheim's deferred lighting. The forward
            // SPI experiment changed lighting and failed physical acceptance.
            settings.renderMode = OpenXRSettings.RenderMode.MultiPass;
            settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
            // Wait before input polling so the render loop does not miss the
            // current display interval while blocking after game simulation.
            settings.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
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
        nextDisplayProbe = 0;
        probedDisplay = null;
        probedDisplayRunning = false;
        probedRenderPasses = 0;
        displaySubsystem = Loader?.GetLoadedSubsystem<XRDisplaySubsystem>();
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
        if (usingOpenVR)
        {
            Ready = VRBackendHost.IsReady;
            SessionStarted = VRBackendHost.IsRunning;
            nativeDisplays.Clear();
            SubsystemManager.GetSubsystems(nativeDisplays);
            displaySubsystem = nativeDisplays.FirstOrDefault(display => display.running);
            DisplayHealthy = SessionStarted && displaySubsystem != null;
            if (displaySubsystem != null && observedDisplay != displaySubsystem)
            {
                if (observedDisplay != null) observedDisplay.displayFocusChanged -= OnDisplayFocusChanged;
                observedDisplay = displaySubsystem;
                observedDisplay.displayFocusChanged += OnDisplayFocusChanged;
            }
            VRBackendHost.UpdateDisplayState(VRBackendHost.Active, DisplayHealthy, DisplayFocused);
            return;
        }
        if (!Ready) return;
        // Update controller actions before game/UI code asks for them. The
        // upstream behaviour may update again in its own phase, but its phase
        // can run after Valheim has already sampled this frame's inputs.
        SteamVR_Input.UpdateNonVisualActions();
        SteamVR_Input.UpdatePoseActions();
        if (SessionStarted)
        {
            UpdateWatchdog();
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.F10)) TryRecover("Ctrl+F10");
        }
        VRBackendHost.UpdateDisplayState(VRBackendHost.Active, DisplayHealthy, DisplayFocused);
        if (Time.unscaledTime < nextReport) return;
        nextReport = Time.unscaledTime + 10;
        var display = displaySubsystem ?? Loader?.GetLoadedSubsystem<XRDisplaySubsystem>();
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
        var display = displaySubsystem ?? Loader?.GetLoadedSubsystem<XRDisplaySubsystem>();
        if (displaySubsystem == null && display != null) displaySubsystem = display;
        if (!ReferenceEquals(display, observedDisplay))
        {
            if (observedDisplay != null) observedDisplay.displayFocusChanged -= OnDisplayFocusChanged;
            observedDisplay = display;
            if (observedDisplay != null) observedDisplay.displayFocusChanged += OnDisplayFocusChanged;
        }
        float now = Time.unscaledTime;
        if (!ReferenceEquals(display, probedDisplay))
        {
            probedDisplay = display;
            nextDisplayProbe = 0;
            probedDisplayRunning = false;
            probedRenderPasses = 0;
        }
        if (now >= nextDisplayProbe)
        {
            nextDisplayProbe = now + .1f;
            try
            {
                probedDisplayRunning = display != null && display.running;
                probedRenderPasses = display == null ? 0 : display.GetRenderPassCount();
            }
            catch (Exception error)
            {
                probedDisplayRunning = false;
                probedRenderPasses = 0;
                Log.LogWarning("OpenXR watchdog read failed: " + error.Message);
            }
        }
        LastRenderPassCount = probedRenderPasses;
        DisplayHealthy = probedDisplayRunning && probedRenderPasses > 0;
        LastVRCameraFrameAge = lastVrCameraFrame < 0 ? -1 : Mathf.Max(0, now - lastVrCameraFrame);
        if (now >= nextCameraScan)
        {
            nextCameraScan = now + .5f;
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
        if (sawHealthyFrame && !recoveryAttempted && now - sessionStartedAt >= 8f
            && now - unhealthySince >= 2f)
            TryRecover(DisplayHealthy ? "VRCamera stopped submitting frames" : "display stopped or has no render passes");
    }

    void OnDisplayFocusChanged(bool focused)
    {
        DisplayFocused = focused;
        VRBackendHost.UpdateDisplayState(VRBackendHost.Active, DisplayHealthy, focused);
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
            displaySubsystem = Loader.GetLoadedSubsystem<XRDisplaySubsystem>();
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
        probedDisplay = null;
        probedDisplayRunning = false;
        probedRenderPasses = 0;
        displaySubsystem = null;
        InputAdapter.Shutdown();
        SinglePassRenderer.Shutdown();
        VRBackendHost.Stop();
        Application.runInBackground = previousRunInBackground;
    }
}
