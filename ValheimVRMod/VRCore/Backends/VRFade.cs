using UnityEngine;

namespace ValheimVRMod.VRCore.Backends
{
    // Shared camera fade: no runtime compositor or Valve event dispatch.
    public sealed class VRFade : MonoBehaviour
    {
        static Color start, target = Color.clear;
        static float began, duration;
        static Material material;
        public static void Start(Color color, float seconds)
        {
            start = Current(); target = color; began = Time.unscaledTime; duration = Mathf.Max(0, seconds);
        }
        static Color Current() => duration <= 0 ? target : Color.Lerp(start, target, Mathf.Clamp01((Time.unscaledTime - began) / duration));
        void OnPostRender()
        {
            var color = Current();
            if (color.a <= 0) return;
            if (!material)
            {
                var shader = VRShaders.GetShader("Custom/SteamVR_Fade");
                if (!shader) return;
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            material.SetColor("fadeColor", color);
            material.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Vertex3(-1, -1, 0); GL.Vertex3(1, -1, 0); GL.Vertex3(1, 1, 0); GL.Vertex3(-1, 1, 0);
            GL.End();
        }
    }
}
