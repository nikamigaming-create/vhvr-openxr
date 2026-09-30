using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.PostProcessing;
using UnityEngine.XR;
using System.Collections.Generic;

namespace Nikami.OpenXR;

// Process the current XR array directly so later overlay cameras preserve it.
internal sealed class OpenXRStereoColor : MonoBehaviour
{
    internal PostProcessingProfile Profile;
    readonly MaterialFactory materials = new();
    readonly RenderTextureFactory textures = new();
    readonly PostProcessingContext context = new();
    readonly ColorGradingComponent grading = new();
    readonly BloomComponent bloom = new();
    readonly ChromaticAberrationComponent chromatic = new();
    readonly VignetteComponent vignette = new();
    readonly UserLutComponent userLut = new();
    readonly GrainComponent grain = new();
    readonly DitheringComponent dithering = new();
    readonly FxaaComponent fxaa = new();
    readonly AntialiasingModel antialiasing = new();
    readonly CommandBuffer stereoState = new() { name = "Nikami stereo color compatibility" };
    Camera camera;
    readonly List<XRDisplaySubsystem> displays = new();
    XRDisplaySubsystem display;
    bool described;

    void Awake()
    {
        camera = GetComponent<Camera>();
        SubsystemManager.GetSubsystems(displays);
        display = displays.Find(d => d.running);
        antialiasing.enabled = true;
        var settings = antialiasing.settings;
        settings.method = AntialiasingModel.Method.Fxaa;
        antialiasing.settings = settings;
    }

    void OnPostRender()
    {
        if (display == null || !display.running || !Profile) return;
        // If the retained profile has no color effects, avoid a full-screen
        // compatibility pass that would only copy each eye through temporary
        // render textures. Bloom and color grading remain eligible here.
        if (!HasActiveEffects(Profile)) return;
        display.GetRenderPass(0, out var pass);
        var destination = pass.renderTarget;
        var descriptor = pass.renderTargetDesc;
        if (!described)
        {
            described = true;
            Debug.Log($"Nikami SPI color direct XR target={descriptor.dimension}/{descriptor.graphicsFormat}/{descriptor.volumeDepth}, keyword={Shader.IsKeywordEnabled("STEREO_INSTANCING_ON")}");
        }
        context.Reset();
        context.camera = camera;
        context.profile = Profile;
        context.materialFactory = materials;
        context.renderTextureFactory = textures;
        grading.Init(context, Profile.colorGrading);
        bloom.Init(context, Profile.bloom);
        chromatic.Init(context, Profile.chromaticAberration);
        vignette.Init(context, Profile.vignette);
        userLut.Init(context, Profile.userLut);
        grain.Init(context, Profile.grain);
        dithering.Init(context, Profile.dithering);
        fxaa.Init(context, antialiasing);
        descriptor.dimension = TextureDimension.Tex2D;
        descriptor.volumeDepth = 1;
        descriptor.depthBufferBits = 0;
        descriptor.msaaSamples = 1;
        descriptor.vrUsage = VRTextureUsage.None;
        var input = RenderTexture.GetTemporary(descriptor);
        var colored = RenderTexture.GetTemporary(descriptor);
        var output = RenderTexture.GetTemporary(descriptor);
        // Pooled textures may retain sampler state from a previous user. FXAA
        // samples between texels and requires the same filtering as the game's
        // original post-processing RenderTextureFactory.
        input.filterMode = colored.filterMode = output.filterMode = FilterMode.Bilinear;
        input.wrapMode = colored.wrapMode = output.wrapMode = TextureWrapMode.Clamp;
        bool previousSrgb = GL.sRGBWrite;
        SetStereo(SinglePassStereoMode.None);
        var uber = materials.Get("Hidden/Post FX/Uber Shader");
        var aa = materials.Get("Hidden/Post FX/FXAA");
        try
        {
            for (int eye = 0; eye < 2; eye++)
            {
                stereoState.Clear();
                stereoState.CopyTexture(destination, eye, 0, input, 0, 0);
                Graphics.ExecuteCommandBuffer(stereoState);
                uber.shaderKeywords = null;
                uber.SetTexture("_AutoExposure", GraphicsUtils.whiteTexture);
                if (bloom.active) bloom.Prepare(input, uber, GraphicsUtils.whiteTexture);
                if (grading.active) grading.Prepare(uber);
                if (chromatic.active) chromatic.Prepare(uber);
                if (vignette.active) vignette.Prepare(uber);
                if (userLut.active) userLut.Prepare(uber);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(input, colored, uber, 0);
                aa.shaderKeywords = null;
                if (grain.active) grain.Prepare(aa);
                if (dithering.active) dithering.Prepare(aa);
                if (Profile.antialiasing.enabled) fxaa.Render(colored, output);
                else Graphics.Blit(colored, output);
                stereoState.Clear();
                stereoState.CopyTexture(output, 0, 0, destination, eye, 0);
                Graphics.ExecuteCommandBuffer(stereoState);
            }
            textures.ReleaseAll();
        }
        finally
        {
            textures.ReleaseAll();
            SetStereo(SinglePassStereoMode.Instancing);
            GL.sRGBWrite = previousSrgb;
            stereoState.Clear();
            stereoState.SetRenderTarget(destination, 0, CubemapFace.Unknown, -1);
            Graphics.ExecuteCommandBuffer(stereoState);
            RenderTexture.ReleaseTemporary(input);
            RenderTexture.ReleaseTemporary(colored);
            RenderTexture.ReleaseTemporary(output);
        }
    }

    static bool HasActiveEffects(PostProcessingProfile profile)
        => profile.colorGrading.enabled || profile.bloom.enabled || profile.chromaticAberration.enabled
        || profile.vignette.enabled || profile.userLut.enabled || profile.grain.enabled
        || profile.dithering.enabled || profile.antialiasing.enabled;

    void SetStereo(SinglePassStereoMode mode)
    {
        stereoState.Clear();
        stereoState.SetSinglePassStereo(mode);
        stereoState.SetInstanceMultiplier(mode == SinglePassStereoMode.None ? 1u : 2u);
        if (mode == SinglePassStereoMode.None) stereoState.DisableShaderKeyword("STEREO_INSTANCING_ON");
        else stereoState.EnableShaderKeyword("STEREO_INSTANCING_ON");
        Graphics.ExecuteCommandBuffer(stereoState);
        if (mode == SinglePassStereoMode.None) Shader.DisableKeyword("STEREO_INSTANCING_ON");
        else Shader.EnableKeyword("STEREO_INSTANCING_ON");
    }

    void OnDestroy()
    {
        grading.OnDisable();
        textures.Dispose();
        materials.Dispose();
        stereoState.Dispose();
    }
}

