using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace Nikami.OpenXR;

internal static class OpenXRImageQuality
{
    static readonly int Quality = Shader.PropertyToID("_QualitySettings");
    static readonly int ConsoleQuality = Shader.PropertyToID("_ConsoleSettings");

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(FxaaComponent), nameof(FxaaComponent.Render)),
            prefix: new HarmonyMethod(typeof(OpenXRImageQuality), nameof(RenderFxaa)));
        harmony.Patch(AccessTools.Method("ValheimVRMod.VRCore.UI.VRGUI:creatGuiCamera"),
            postfix: new HarmonyMethod(typeof(OpenXRImageQuality), nameof(InitializeGuiCamera)));
        harmony.Patch(AccessTools.Method("ValheimVRMod.Scripts.GesturedLocomotionManager+GesturedWalkRun+HexagonWheel:CreateHexagonWheel"),
            prefix: new HarmonyMethod(typeof(OpenXRImageQuality), nameof(SkipDebugWheel)));
    }

    static bool SkipDebugWheel()
    {
        // VHVR's ENABLE_DEBUG_WALK_RUN_INDICATOR is false, so Update returns
        // without hiding its Awake-created LineRenderer. Its missing default
        // material draws magenta geometry at the origin during startup.
        return false;
    }

    static void InitializeGuiCamera(Camera ____guiCamera)
    {
        // This camera draws a single flat UI texture. Only VRUICamera, which
        // presents that texture on the tracked panel, should render stereo.
        if (____guiCamera) ____guiCamera.stereoTargetEye = StereoTargetEyeMask.None;
    }

    static bool RenderFxaa(FxaaComponent __instance, RenderTexture source, RenderTexture destination)
    {
        var camera = __instance.context?.camera;
        if (!OpenXRPlugin.Ready || !camera || camera.name != "VRCamera")
            return true;
        // Keep the native ExtremeQuality edge thresholds, but avoid its full
        // subpixel blur on thin grass blades. This is the same native shader
        // and blit, after native HDR tonemapping, with no extra render pass.
        var material = __instance.context.materialFactory.Get("Hidden/Post FX/FXAA");
        material.SetVector(Quality, new Vector4(.35f, .063f, .0312f, 0));
        material.SetVector(ConsoleQuality, new Vector4(.5f, 8f, .125f, .04f));
        Graphics.Blit(source, destination, material, 0);
        return false;
    }
}
