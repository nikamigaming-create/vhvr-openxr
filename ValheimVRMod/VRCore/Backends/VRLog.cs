using UnityEngine;

namespace ValheimVRMod.VRCore.Backends
{
    public static class VRLog
    {
        public static void Info(string text) { Debug.Log(text); }
        public static void Warning(string text) { Debug.LogWarning(text); }
        public static void Error(object error) { Debug.LogError(error); }
    }
}
