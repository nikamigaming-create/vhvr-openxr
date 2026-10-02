using System.Collections.Generic;
using UnityEngine;

namespace ValheimVRMod.VRCore.Backends
{
    public static class VRShaders
    {
        static readonly Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();
        public static bool Initialize(string path)
        {
            if (VRBackendHost.Active.Kind == VRBackendKind.OpenVR) return OpenVRRigBackend.LoadShaders(path);
            var bundle = AssetBundle.LoadFromFile(path);
            if (!bundle) return false;
            foreach (var shader in bundle.LoadAllAssets<Shader>()) Remember(shader);
            return true;
        }
        public static void Remember(Shader shader) { if (shader) Shaders[shader.name] = shader; }
        public static Shader GetShader(string name) => Shaders.TryGetValue(name, out var shader) ? shader : Shader.Find(name);
    }
}
