using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// The look: a dark vignette, cool corners, and a lift out of the deepest blacks.
    ///
    /// There is no post-processing stack here and no shader to write one with, so this is done the
    /// way everything else in this project is done - a texture generated once at startup and drawn
    /// over the frame. That sounds like a compromise and mostly is not: a vignette IS a texture
    /// multiplied over the image, and drawing it as a single alpha-blended quad costs one draw call
    /// and no render targets.
    ///
    /// Two layers, because one never looks right:
    ///
    ///   burn   a dark, slightly cool falloff from the corners in. This is the "bodycam" part - the
    ///          thing that makes a bright flat render read as a lens rather than a screenshot.
    ///   lift   a very faint warm haze in the middle, which stops the darkening reading as a black
    ///          frame stuck on the screen and gives the picture somewhere to sit.
    ///
    /// It is drawn by the HUD, last, over everything including the scope.
    /// </summary>
    public sealed class ScreenGrade
    {
        Texture2D _burn;
        Texture2D _lift;
        int _size;

        /// <summary>0 to 1. Tuned from FeelTuning so it can be turned off by anyone who hates it.</summary>
        public float Strength = 1f;

        void Ensure()
        {
            if (_burn != null) return;

            // 256 is plenty: it is a smooth radial gradient stretched over the whole screen, and the
            // bilinear filter does the rest. Anything larger is memory nobody can see.
            _size = 256;
            _burn = new Texture2D(_size, _size, TextureFormat.RGBA32, false);
            _lift = new Texture2D(_size, _size, TextureFormat.RGBA32, false);
            _burn.wrapMode = TextureWrapMode.Clamp;
            _lift.wrapMode = TextureWrapMode.Clamp;
            _burn.filterMode = FilterMode.Bilinear;
            _lift.filterMode = FilterMode.Bilinear;

            for (int py = 0; py < _size; py++)
            {
                float ny = py / (float)(_size - 1) * 2f - 1f;
                for (int px = 0; px < _size; px++)
                {
                    float nx = px / (float)(_size - 1) * 2f - 1f;

                    // Elliptical rather than circular, so it hugs a wide screen instead of cutting
                    // the top and bottom off it.
                    float d = Mathf.Sqrt(nx * nx * 0.72f + ny * ny);

                    // Nothing at all through the middle, then a smooth ramp to the corners. The eye
                    // notices a vignette that starts too early far more than one that is too strong.
                    float k = Mathf.Clamp01((d - 0.55f) / 0.75f);
                    k = k * k * (3f - 2f * k);                  // smoothstep

                    // Cool in the shadows: the corners go blue-black rather than grey.
                    _burn.SetPixel(px, py, new Color(0.03f, 0.04f, 0.07f, k * 0.72f));

                    float centre = Mathf.Clamp01(1f - d * 1.35f);
                    centre *= centre;
                    _lift.SetPixel(px, py, new Color(0.62f, 0.60f, 0.55f, centre * 0.045f));
                }
            }

            _burn.Apply();
            _lift.Apply();
        }

        public void Draw(float width, float height)
        {
            if (Strength <= 0.01f) return;
            Ensure();

            Color previous = GUI.color;
            Rect full = new Rect(0f, 0f, width, height);

            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Strength));
            GUI.DrawTexture(full, _burn, ScaleMode.StretchToFill, true);
            GUI.DrawTexture(full, _lift, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }
    }
}
