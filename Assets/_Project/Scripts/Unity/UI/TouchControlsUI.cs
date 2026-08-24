using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Draws the thumb controls. The rig owns where they are and what they are doing; this only puts
    /// pixels on them, in the same IMGUI the rest of the game uses so there is nothing to import.
    ///
    /// Note the flip: the rig works bottom-left, matching Unity's touch positions, while IMGUI works
    /// top-left. Converting in exactly one place is what stops the hit testing and the drawing from
    /// quietly disagreeing about where a button is.
    /// </summary>
    public sealed class TouchControlsUI
    {
        public UiSkin Skin;
        public TouchInputSource Input;
        public FeelTuning Feel;

        GUIStyle _centred;

        public void Draw()
        {
            if (Input == null || Skin == null) return;
            if (_centred == null)
            {
                _centred = new GUIStyle(Skin.Small);
                _centred.alignment = TextAnchor.MiddleCenter;
            }

            TouchRig rig = Input.Rig;
            float alpha = Feel != null ? Mathf.Clamp01(Feel.touchControlAlpha) : 0.55f;
            Color ink = new Color(1f, 1f, 1f, alpha);
            Color hot = new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, Mathf.Min(1f, alpha + 0.35f));

            // The stick only exists while a thumb is on it, so it is never in the way of the view.
            if (rig.StickActive)
            {
                float radius = StickRadius(rig);
                Skin.Ring(new Vector2(rig.StickOriginX, Flip(rig.StickOriginY)), radius, 2f, ink, 30);

                float dx = rig.StickX - rig.StickOriginX;
                float dy = rig.StickY - rig.StickOriginY;
                float length = Mathf.Sqrt(dx * dx + dy * dy);
                if (length > radius && length > 0.001f)
                {
                    dx = dx / length * radius;
                    dy = dy / length * radius;
                }
                Disc(rig.StickOriginX + dx, Flip(rig.StickOriginY + dy), radius * 0.4f, rig.Sprint ? hot : ink);
            }

            for (int i = 0; i < rig.Buttons.Length; i++)
            {
                TouchButton b = rig.Buttons[i];
                float y = Flip(b.Y);
                Color colour = b.Held ? hot : ink;

                if (b.Held) Disc(b.X, y, b.Radius, new Color(colour.r, colour.g, colour.b, 0.22f));
                Skin.Ring(new Vector2(b.X, y), b.Radius, b.Held ? 3f : 2f, colour, 30);
                Skin.Text(new Rect(b.X - b.Radius, y - b.Radius, b.Radius * 2f, b.Radius * 2f), b.Label, _centred, colour);
            }
        }

        static float StickRadius(TouchRig rig)
        {
            float unit = rig.Height < rig.Width ? rig.Height : rig.Width;
            return unit * 0.16f;
        }

        /// <summary>Touch space is bottom-left, IMGUI is top-left. This is the only place that knows.</summary>
        static float Flip(float y) { return Screen.height - y; }

        /// <summary>A filled circle out of horizontal slices - the only primitive available is a rect.</summary>
        void Disc(float x, float y, float radius, Color colour)
        {
            int slices = Mathf.Clamp(Mathf.RoundToInt(radius), 6, 40);
            float sliceHeight = radius / slices;
            for (int i = -slices; i <= slices; i++)
            {
                float t = i / (float)slices;
                float halfWidth = Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)) * radius;
                Skin.Fill(new Rect(x - halfWidth, y + t * radius, halfWidth * 2f, sliceHeight + 1f), colour);
            }
        }
    }
}
