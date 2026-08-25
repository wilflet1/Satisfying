using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// Every material in the game, generated at runtime. No art assets to import, no shader variants to
    /// strip, and a drop-in replacement point when you bring in free models later (see Art/README.md).
    /// </summary>
    public sealed class Palette
    {
        public Material Ground;
        public Material Wall;
        public Material WallDark;
        public Material Accent;
        public Material Metal;
        public Material Ally;
        public Material Enemy;
        public Material Gun;
        public Material GunDark;
        public Material Glow;
        public Material Hands;
        public Material RemoteArms;
        public Material Blood;

        static Shader FindShader()
        {
            Shader s = Shader.Find("Standard");
            if (s != null) return s;
            s = Shader.Find("Universal Render Pipeline/Lit");
            if (s != null) return s;
            s = Shader.Find("Legacy Shaders/Diffuse");
            if (s != null) return s;
            return Shader.Find("Unlit/Color");
        }

        public static Material Make(string name, Color color, float smoothness, float metallic, bool emissive = false)
        {
            Material m = new Material(FindShader());
            m.name = name;
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (emissive && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 1.6f);
            }
            return m;
        }

        public static Palette Build()
        {
            Palette p = new Palette();
            p.Ground = Make("ground", new Color(0.21f, 0.22f, 0.235f), 0.12f, 0f);
            p.Wall = Make("wall", new Color(0.44f, 0.45f, 0.47f), 0.16f, 0f);
            p.WallDark = Make("wall dark", new Color(0.28f, 0.29f, 0.32f), 0.2f, 0f);
            p.Accent = Make("accent", new Color(0.75f, 0.44f, 0.16f), 0.3f, 0f);
            p.Metal = Make("metal", new Color(0.5f, 0.52f, 0.56f), 0.55f, 0.75f);
            p.Ally = Make("ally", new Color(0.32f, 0.62f, 0.88f), 0.25f, 0f);
            p.Enemy = Make("enemy", new Color(0.86f, 0.32f, 0.28f), 0.25f, 0f);
            p.Gun = Make("gun", new Color(0.18f, 0.19f, 0.21f), 0.45f, 0.6f);
            p.GunDark = Make("gun dark", new Color(0.11f, 0.115f, 0.13f), 0.3f, 0.4f);
            p.Glow = Make("glow", new Color(1f, 0.82f, 0.45f), 0.5f, 0f, true);
            p.Hands = Make("hands", new Color(0.42f, 0.36f, 0.31f), 0.12f, 0f);
            p.RemoteArms = Make("remote arms", new Color(0.36f, 0.30f, 0.27f), 0.12f, 0f);
            // Dark and matte. Bright red reads as paint, and a glossy one reads as jam.
            p.Blood = Make("blood", new Color(0.38f, 0.03f, 0.04f), 0.08f, 0f);
            return p;
        }
    }
}
