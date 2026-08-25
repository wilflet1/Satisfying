using System;

namespace Satisfying.Shared
{
    public enum SightKind : byte
    {
        Iron = 0,
        RedDot = 1,
        Holo = 2,
        /// <summary>A magnified optic with a picture of its own. Two bits of sight index, four sights.</summary>
        Scope = 3
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

        [Tune("Sights", 0.5f, 1.5f, Tip = "Field of view multiplier while aiming. This is the small squeeze an unmagnified optic gives you, not magnification - see below for that.")]
        public float zoomMul = 1f;

        [Tune("Sights", 1f, 25f, Tip = "Optical magnification. Anything above 1.5 gets a scope picture of its own instead of just narrowing the view.")]
        public float magnification = 1f;

        [Tune("Sights", 1f, 30f, Tip = "Top of the magnification range. Above the bottom of it the optic is variable and the wheel changes it while you are aiming.")]
        public float magnificationMax = 1f;

        /// <summary>Whether this optic is drawn as a scope picture rather than a piece of the gun.</summary>
        public bool IsScope { get { return magnification >= 1.5f; } }

        /// <summary>Whether the wheel does anything to it.</summary>
        public bool IsVariable { get { return magnificationMax > magnification + 0.05f; } }

        /// <summary>Keeps a requested magnification inside what this optic actually has.</summary>
        public float ClampMagnification(float wanted)
        {
            float low = MathK.Max(1f, magnification);
            float high = MathK.Max(low, magnificationMax);
            return MathK.Clamp(wanted, low, high);
        }

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

            // A 3.5-18 first focal plane precision optic. The bottom of the range is for holding a
            // room; the top is for a shot you have time to set up.
            SightTuning scope = new SightTuning();
            scope.name = "Scope 3.5-18";
            scope.adsTimeMul = 1.45f;
            scope.spreadMul = 0.7f;
            scope.zoomMul = 1f;
            scope.magnification = 3.5f;
            scope.magnificationMax = 18f;

            return new SightTuning[] { iron, dot, holo, scope };
        }
    }
}
