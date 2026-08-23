using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// Immediate mode UI styling built at runtime. Chosen deliberately: no canvas, no fonts to import,
    /// no prefabs - the whole project stays code-only and opens in any Unity install without a re-import.
    /// </summary>
    public sealed class UiSkin
    {
        public GUIStyle Panel;
        public GUIStyle PanelDim;
        public GUIStyle Header;
        public GUIStyle Title;
        public GUIStyle Label;
        public GUIStyle LabelDim;
        public GUIStyle LabelRight;
        public GUIStyle Small;
        public GUIStyle SmallDim;
        public GUIStyle Value;
        public GUIStyle Button;
        public GUIStyle ButtonPrimary;
        public GUIStyle ButtonSmall;
        public GUIStyle TextField;
        public GUIStyle Slider;
        public GUIStyle SliderThumb;
        public GUIStyle Toggle;

        public Texture2D White;
        public Texture2D Shade;

        public static readonly Color Ink = new Color(0.92f, 0.93f, 0.95f);
        public static readonly Color InkDim = new Color(0.62f, 0.65f, 0.70f);
        public static readonly Color Accent = new Color(0.98f, 0.66f, 0.28f);
        public static readonly Color Good = new Color(0.42f, 0.82f, 0.52f);
        public static readonly Color Bad = new Color(0.90f, 0.35f, 0.32f);
        public static readonly Color PanelColor = new Color(0.07f, 0.075f, 0.09f, 0.93f);

        public static Texture2D Solid(Color color)
        {
            Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, color);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Point;
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        public static UiSkin Build()
        {
            UiSkin s = new UiSkin();
            s.White = Solid(Color.white);
            s.Shade = Solid(new Color(0f, 0f, 0f, 0.55f));

            Texture2D panel = Solid(PanelColor);
            Texture2D panelDim = Solid(new Color(0.10f, 0.11f, 0.13f, 0.80f));
            Texture2D button = Solid(new Color(0.16f, 0.17f, 0.20f, 0.95f));
            Texture2D buttonHover = Solid(new Color(0.24f, 0.26f, 0.30f, 0.98f));
            Texture2D buttonActive = Solid(new Color(0.34f, 0.24f, 0.12f, 1f));
            Texture2D primary = Solid(new Color(0.44f, 0.29f, 0.10f, 0.98f));
            Texture2D primaryHover = Solid(new Color(0.58f, 0.38f, 0.13f, 1f));
            Texture2D field = Solid(new Color(0.05f, 0.055f, 0.07f, 0.98f));
            Texture2D track = Solid(new Color(0.22f, 0.23f, 0.27f, 1f));
            Texture2D thumb = Solid(Accent);

            s.Panel = new GUIStyle();
            s.Panel.normal.background = panel;
            s.Panel.padding = new RectOffset(14, 14, 12, 12);

            s.PanelDim = new GUIStyle();
            s.PanelDim.normal.background = panelDim;
            s.PanelDim.padding = new RectOffset(10, 10, 8, 8);

            s.Label = new GUIStyle();
            s.Label.normal.textColor = Ink;
            s.Label.fontSize = 15;
            s.Label.wordWrap = false;
            s.Label.padding = new RectOffset(2, 2, 3, 3);

            s.LabelDim = new GUIStyle(s.Label);
            s.LabelDim.normal.textColor = InkDim;

            s.LabelRight = new GUIStyle(s.Label);
            s.LabelRight.alignment = TextAnchor.MiddleRight;

            s.Small = new GUIStyle(s.Label);
            s.Small.fontSize = 12;

            s.SmallDim = new GUIStyle(s.Small);
            s.SmallDim.normal.textColor = InkDim;

            s.Value = new GUIStyle(s.Label);
            s.Value.alignment = TextAnchor.MiddleRight;
            s.Value.normal.textColor = Accent;

            s.Header = new GUIStyle(s.Label);
            s.Header.fontSize = 17;
            s.Header.fontStyle = FontStyle.Bold;
            s.Header.normal.textColor = Accent;
            s.Header.padding = new RectOffset(2, 2, 8, 6);

            s.Title = new GUIStyle(s.Label);
            s.Title.fontSize = 34;
            s.Title.fontStyle = FontStyle.Bold;
            s.Title.alignment = TextAnchor.MiddleCenter;

            s.Button = new GUIStyle();
            s.Button.normal.background = button;
            s.Button.hover.background = buttonHover;
            s.Button.active.background = buttonActive;
            s.Button.normal.textColor = Ink;
            s.Button.hover.textColor = Color.white;
            s.Button.active.textColor = Color.white;
            s.Button.alignment = TextAnchor.MiddleCenter;
            s.Button.fontSize = 15;
            s.Button.padding = new RectOffset(10, 10, 8, 8);
            s.Button.margin = new RectOffset(2, 2, 3, 3);

            s.ButtonPrimary = new GUIStyle(s.Button);
            s.ButtonPrimary.normal.background = primary;
            s.ButtonPrimary.hover.background = primaryHover;
            s.ButtonPrimary.fontStyle = FontStyle.Bold;

            s.ButtonSmall = new GUIStyle(s.Button);
            s.ButtonSmall.fontSize = 12;
            s.ButtonSmall.padding = new RectOffset(6, 6, 4, 4);

            s.TextField = new GUIStyle();
            s.TextField.normal.background = field;
            s.TextField.focused.background = field;
            s.TextField.normal.textColor = Ink;
            s.TextField.focused.textColor = Color.white;
            s.TextField.fontSize = 15;
            s.TextField.padding = new RectOffset(8, 8, 7, 7);
            s.TextField.margin = new RectOffset(2, 2, 3, 3);

            s.Slider = new GUIStyle();
            s.Slider.normal.background = track;
            s.Slider.fixedHeight = 6;
            s.Slider.margin = new RectOffset(2, 2, 9, 9);
            s.Slider.border = new RectOffset(0, 0, 0, 0);

            s.SliderThumb = new GUIStyle();
            s.SliderThumb.normal.background = thumb;
            s.SliderThumb.active.background = Solid(Color.white);
            s.SliderThumb.fixedWidth = 12;
            s.SliderThumb.fixedHeight = 14;

            s.Toggle = new GUIStyle(s.Button);
            s.Toggle.alignment = TextAnchor.MiddleLeft;
            s.Toggle.padding = new RectOffset(8, 8, 5, 5);

            return s;
        }

        // ------------------------------------------------------------------ primitives
        public void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, White);
            GUI.color = previous;
        }

        public void Bar(Rect rect, float fill01, Color foreground, Color background)
        {
            Fill(rect, background);
            Rect inner = rect;
            inner.width = rect.width * Mathf.Clamp01(fill01);
            Fill(inner, foreground);
        }

        /// <summary>Centre-anchored bar that fills left or right - used for the lean gauge.</summary>
        public void SignedBar(Rect rect, float value, Color foreground, Color background)
        {
            Fill(rect, background);
            float half = rect.width * 0.5f;
            float amount = Mathf.Clamp(value, -1f, 1f) * half;
            Rect inner = rect;
            if (amount >= 0f) { inner.x = rect.x + half; inner.width = amount; }
            else { inner.x = rect.x + half + amount; inner.width = -amount; }
            Fill(inner, foreground);
            Fill(new Rect(rect.x + half - 1f, rect.y - 2f, 2f, rect.height + 4f), new Color(1f, 1f, 1f, 0.5f));
        }

        /// <summary>A circle drawn as short segments - enough for a reticle, and no texture needed.</summary>
        public void Ring(Vector2 centre, float radius, float thickness, Color color, int segments = 32)
        {
            float step = Mathf.PI * 2f / Mathf.Max(8, segments);
            float half = thickness * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float angle = step * i;
                float x = centre.x + Mathf.Cos(angle) * radius;
                float y = centre.y + Mathf.Sin(angle) * radius;
                Fill(new Rect(x - half, y - half, thickness, thickness), color);
            }
        }

        public void Text(Rect rect, string text, GUIStyle style, Color color)
        {
            Color previous = style.normal.textColor;
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
            style.normal.textColor = previous;
        }
    }
}
