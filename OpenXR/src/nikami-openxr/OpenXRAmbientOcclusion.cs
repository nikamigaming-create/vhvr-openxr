using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using AmplifyOcclusion;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace Nikami.OpenXR;

internal static class OpenXRAmbientOcclusion
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(AmplifyOcclusionCommon), nameof(AmplifyOcclusionCommon.IsStereoMultiPassEnabled)),
            postfix: new HarmonyMethod(typeof(OpenXRAmbientOcclusion), nameof(ActualStereoMode)));
        harmony.Patch(AccessTools.Method(typeof(AmplifyOcclusionCommon), nameof(AmplifyOcclusionCommon.UpdateGlobalShaderConstants)),
            transpiler: new HarmonyMethod(typeof(OpenXRAmbientOcclusion), nameof(UseCameraTarget)));
    }

    static void ActualStereoMode(Camera aCamera, ref bool __result)
    {
        if (OpenXRPlugin.Ready)
            __result = aCamera && aCamera.stereoEnabled && XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.MultiPass;
    }

    static bool HasEyeTarget(Camera camera) => camera && camera.stereoEnabled
        && XRSettings.eyeTextureWidth > 0 && XRSettings.eyeTextureHeight > 0;

    static IEnumerable<CodeInstruction> UseCameraTarget(IEnumerable<CodeInstruction> instructions)
    {
        var enabled = AccessTools.PropertyGetter(typeof(XRSettings), nameof(XRSettings.enabled));
        var hasTarget = AccessTools.Method(typeof(OpenXRAmbientOcclusion), nameof(HasEyeTarget));
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (!instruction.Calls(enabled))
            {
                yield return instruction;
                continue;
            }
            // XR can be enabled while the session has no eye textures. The
            // waiting desktop camera must use its pixel dimensions, not 0x0
            // XR dimensions, and must keep two mono history buffers.
            var camera = new CodeInstruction(OpCodes.Ldarg_2);
            camera.MoveLabelsFrom(instruction);
            camera.MoveBlocksFrom(instruction);
            yield return camera;
            yield return new CodeInstruction(OpCodes.Call, hasTarget);
            replaced++;
        }
        if (replaced != 1)
            throw new InvalidOperationException("The native occlusion target selection changed; expected one XR enabled check.");
    }
}
