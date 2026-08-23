using System;

namespace Satisfying.Shared
{
    public enum SightKind : byte
    {
        Iron = 0,
        RedDot = 1,
        Holo = 2
    }

    /// <summary>
    /// An optic. The trade is always the same shape: a clearer sight picture costs you a little time
    /// getting onto it. Server authoritative like every other simulation value, because it changes
    /// how fast you can aim.
    /// </summary>
    [Serializable]
    public class SightTuning
    {
        public string name = "Iron sights";

        [Tune("Sights", 0.4f, 2.5f, Tip = "Multiplier on the weapon's aim-down-sights time.")]
        public float adsTimeMul = 1f;

        [Tune("Sights", 0.2f, 2f, Tip = "Multiplier on the aimed cone of fire.")]
        public float spreadMul = 1f;

        [Tune("Sights", 0.5f, 1.5f, Tip = "Field of view multiplier while aiming - magnification.")]
        public float zoomMul = 1f;

        public SightTuning Clone() { return (SightTuning)MemberwiseClone(); }

        public static SightTuning[] Defaults()
        {
            SightTuning iron = new SightTuning();
            iron.name = "Iron sights";

            SightTuning dot = new SightTuning();
            dot.name = "Red dot";
            dot.adsTimeMul = 1.06f;
            dot.spreadMul = 0.88f;
            dot.zoomMul = 0.97f;

            SightTuning holo = new SightTuning();
            holo.name = "Holographic";
            holo.adsTimeMul = 1.14f;
            holo.spreadMul = 0.8f;
            holo.zoomMul = 0.93f;

            return new SightTuning[] { iron, dot, holo };
        }
    }
}
