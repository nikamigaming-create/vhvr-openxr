using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Nikami.OpenXR;

internal static class OpenXREquipmentQuality
{
    readonly struct Sampling
    {
        internal readonly FilterMode Filter;
        internal readonly int Anisotropy;
        internal Sampling(Texture texture) { Filter = texture.filterMode; Anisotropy = texture.anisoLevel; }
    }
    static readonly Dictionary<Texture, Sampling> Originals = new();
    static readonly List<Renderer> Renderers = new();
    static readonly List<Material> Materials = new();
    static readonly string[] Maps = { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_SpecGlossMap" };

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(VisEquipment), "AttachItem"),
            postfix: new HarmonyMethod(typeof(OpenXREquipmentQuality), nameof(AttachedItem)));
        Application.quitting += Restore;
    }

    static void AttachedItem(VisEquipment __instance, GameObject __result, int itemHash, Transform joint, bool enableEquipEffects, bool backAttach)
    {
        if (!OpenXRPlugin.Ready || !__result || !enableEquipEffects || backAttach
            || !__instance.GetComponentInParent<Player>()) return;
        // The native weapons atlas point-samples its color, normal and metal
        // maps. At hand distance those discrete samples produce large steps
        // in the highlights. Smooth the samples without a sharpening pass,
        // mip bias, material replacement, or per-frame renderer scan.
        __result.GetComponentsInChildren(true, Renderers);
        int before = Originals.Count;
        foreach (var renderer in Renderers)
        {
            renderer.GetSharedMaterials(Materials);
            foreach (var material in Materials)
            {
                if (!material || !material.shader || material.shader.name != "Custom/Creature") continue;
                foreach (string map in Maps)
                {
                    if (!material.HasProperty(map)) continue;
                    var texture = material.GetTexture(map);
                    if (!texture || Originals.ContainsKey(texture)) continue;
                    Originals.Add(texture, new Sampling(texture));
                    texture.filterMode = FilterMode.Trilinear;
                    texture.anisoLevel = Mathf.Max(texture.anisoLevel, 8);
                }
            }
        }
        Renderers.Clear(); Materials.Clear();
        if (Originals.Count != before)
            OpenXRPlugin.Log.LogInfo($"OpenXR held-item sampling: {Originals.Count - before} native textures use trilinear filtering and 8x anisotropy.");
    }

    static void Restore()
    {
        foreach (var pair in Originals)
            if (pair.Key)
            {
                pair.Key.filterMode = pair.Value.Filter;
                pair.Key.anisoLevel = pair.Value.Anisotropy;
            }
        Originals.Clear();
    }
}
