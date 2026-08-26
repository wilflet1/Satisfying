using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Synthesised sound effects. Original, free, and zero bytes on disk - swap these for real samples
    /// later by replacing the clips on the bank.
    /// </summary>
    public sealed class AudioBank
    {
        /// <summary>One shot per weapon so an M4 and a USP45 never sound like the same gun.</summary>
        public AudioClip[] Shots;
        public AudioClip[] ShotsDistant;
        public AudioClip Shot;
        public AudioClip ShotDistant;
        public AudioClip DryFire;
        public AudioClip MagOut;
        public AudioClip MagIn;
        public AudioClip BoltRelease;
        public AudioClip Reload;
        public AudioClip HitMarker;
        public AudioClip HeadshotMarker;
        public AudioClip Impact;
        public AudioClip Hurt;
        public AudioClip Death;
        /// <summary>
        /// Four of each, because one footstep played over and over is the single most obvious
        /// synthetic thing in a game - the ear locks onto the repeat within about three steps.
        /// </summary>
        public AudioClip[] StepsConcrete;
        public AudioClip[] StepsWood;
        public AudioClip[] StepsMetal;
        public AudioClip Footstep;
        public AudioClip Jump;
        public AudioClip Land;
        public AudioClip StanceChange;
        public AudioClip Lean;
        public AudioClip GlassBreak;
        public AudioClip MeleeSwing;
        public AudioClip MeleeHit;
        public AudioClip Grab;
        public AudioClip Drag;
        public AudioClip UiClick;
        public AudioClip RoundStart;

        /// <summary>
        /// The three noises a grenade makes before it makes the last one. The draw is the ring of the
        /// spoon and the body coming out of a pouch; the pin is a short metallic snap; the bounce is
        /// per surface, because a grenade rattling on floorboards upstairs and one skittering on
        /// concrete outside are two different pieces of information.
        /// </summary>
        public AudioClip GrenadeDraw;
        public AudioClip GrenadePin;
        public AudioClip GrenadeSettle;
        public AudioClip GrenadeThrow;
        public AudioClip[] GrenadeBounce;
        public AudioClip Explosion;
        public AudioClip ExplosionDistant;

        public static AudioBank Build()
        {
            AudioBank b = new AudioBank();
            // M4: sharp crack. MP5: flatter and faster. USP45: slower, deeper thump.
            b.Shots = new AudioClip[3];
            b.Shots[0] = Synth.Gunshot("m4 shot", 0.28f, 155f, 1f);
            b.Shots[1] = Synth.Gunshot("mp5 shot", 0.21f, 215f, 0.9f);
            b.Shots[2] = Synth.Gunshot("usp shot", 0.36f, 105f, 1.05f);

            b.ShotsDistant = new AudioClip[3];
            b.ShotsDistant[0] = Synth.Gunshot("m4 distant", 0.42f, 78f, 0.55f);
            b.ShotsDistant[1] = Synth.Gunshot("mp5 distant", 0.34f, 105f, 0.5f);
            b.ShotsDistant[2] = Synth.Gunshot("usp distant", 0.50f, 60f, 0.6f);

            b.Shot = b.Shots[0];
            b.ShotDistant = b.ShotsDistant[0];

            b.DryFire = Synth.Click("dry fire", 0.06f, 2600f, 0.35f);
            b.MagOut = Synth.Click("mag out", 0.09f, 620f, 0.45f);
            b.MagIn = Synth.Click("mag in", 0.11f, 380f, 0.55f);
            b.BoltRelease = Synth.Click("bolt", 0.08f, 1500f, 0.5f);
            b.Reload = b.MagIn;
            b.HitMarker = Synth.Tone("hit", 0.07f, 1500f, 0.28f);
            b.HeadshotMarker = Synth.Tone("headshot", 0.11f, 2100f, 0.34f);
            b.Impact = Synth.Noise("impact", 0.09f, 0.35f, 3200f);
            b.Hurt = Synth.Tone("hurt", 0.18f, 220f, 0.4f, -0.5f);
            b.Death = Synth.Tone("death", 0.6f, 330f, 0.45f, -0.75f);
            b.StepsConcrete = new AudioClip[4];
            b.StepsWood = new AudioClip[4];
            b.StepsMetal = new AudioClip[4];
            for (int i = 0; i < 4; i++)
            {
                b.StepsConcrete[i] = Synth.Footstep("step concrete " + i, StepSurface.Concrete, (uint)(i * 2654435761u + 17u));
                b.StepsWood[i] = Synth.Footstep("step wood " + i, StepSurface.Wood, (uint)(i * 2246822519u + 101u));
                b.StepsMetal[i] = Synth.Footstep("step metal " + i, StepSurface.Metal, (uint)(i * 1597334677u + 53u));
            }
            b.Footstep = b.StepsConcrete[0];
            b.Jump = Synth.Noise("jump", 0.10f, 0.2f, 700f);
            b.Land = Synth.Noise("land", 0.16f, 0.34f, 500f);
            b.StanceChange = Synth.Noise("stance", 0.16f, 0.18f, 480f);
            b.Lean = Synth.Noise("lean", 0.10f, 0.09f, 1400f);
            b.GlassBreak = Synth.Noise("glass", 0.55f, 0.55f, 7000f);
            b.MeleeSwing = Synth.Noise("swing", 0.16f, 0.28f, 1600f);
            b.MeleeHit = Synth.Gunshot("melee hit", 0.16f, 90f, 0.75f);
            b.Grab = Synth.Click("grab", 0.07f, 500f, 0.4f);
            b.Drag = Synth.Noise("drag", 0.4f, 0.2f, 700f);
            b.UiClick = Synth.Click("ui", 0.04f, 1800f, 0.25f);
            b.RoundStart = Synth.Tone("round", 0.5f, 520f, 0.35f, 0.4f);

            b.GrenadeDraw = Synth.Tingle("grenade draw", 0.55f);
            // A ring pin coming out of a fuse is not a click. It is a hard scrape of steel on steel
            // and then the spoon left loose against the body - two events, close together, both metal.
            b.GrenadePin = Synth.PinPull("grenade pin", 0.42f);
            b.GrenadeSettle = Synth.Tingle("grenade settle", 0.30f);
            b.GrenadeThrow = Synth.Noise("grenade throw", 0.14f, 0.24f, 1200f);

            // One per surface, indexed by SurfaceKind, so a bounce says what it bounced on.
            b.GrenadeBounce = new AudioClip[5];
            b.GrenadeBounce[(int)SurfaceKind.Concrete] = Synth.Bounce("bounce concrete", 0.16f, 320f, 0.55f, 0.9f);
            b.GrenadeBounce[(int)SurfaceKind.Wood] = Synth.Bounce("bounce wood", 0.28f, 190f, 0.5f, 0.35f);
            b.GrenadeBounce[(int)SurfaceKind.Drywall] = Synth.Bounce("bounce drywall", 0.18f, 240f, 0.4f, 0.6f);
            b.GrenadeBounce[(int)SurfaceKind.Metal] = Synth.Bounce("bounce metal", 0.62f, 720f, 0.6f, 0.08f);
            b.GrenadeBounce[(int)SurfaceKind.Glass] = Synth.Bounce("bounce glass", 0.22f, 1900f, 0.4f, 0.3f);

            b.Explosion = Synth.Gunshot("explosion", 0.85f, 46f, 1f);
            b.ExplosionDistant = Synth.Gunshot("explosion distant", 1.1f, 32f, 0.7f);
            return b;
        }

        public AudioClip ShotFor(int weaponIndex)
        {
            if (Shots == null || Shots.Length == 0) return Shot;
            return Shots[weaponIndex < 0 || weaponIndex >= Shots.Length ? 0 : weaponIndex];
        }

        /// <summary>A step on the surface underfoot. The variant is the caller's business - pass a
        /// counter or a random number, anything that is not the same twice running.</summary>
        public AudioClip BounceFor(SurfaceKind surface)
        {
            if (GrenadeBounce == null || GrenadeBounce.Length == 0) return Impact;
            int index = (int)surface;
            if (index < 0 || index >= GrenadeBounce.Length || GrenadeBounce[index] == null) return GrenadeBounce[0];
            return GrenadeBounce[index];
        }

        public AudioClip StepFor(StepSurface surface, int variant)
        {
            AudioClip[] set = surface == StepSurface.Wood ? StepsWood
                            : surface == StepSurface.Metal ? StepsMetal : StepsConcrete;
            if (set == null || set.Length == 0) return Footstep;
            int index = variant % set.Length;
            return set[index < 0 ? index + set.Length : index];
        }

        public AudioClip DistantShotFor(int weaponIndex)
        {
            if (ShotsDistant == null || ShotsDistant.Length == 0) return ShotDistant;
            return ShotsDistant[weaponIndex < 0 || weaponIndex >= ShotsDistant.Length ? 0 : weaponIndex];
        }
    }

    /// <summary>What is underfoot. Outside is poured concrete; inside the building it is boards.</summary>
    public enum StepSurface : byte
    {
        Concrete = 0,
        Wood = 1,
        Metal = 2
    }

    public static class Synth
    {
        public const int SampleRate = 44100;

        static AudioClip FromSamples(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static uint _seed = 0x1BADB002u;

        /// <summary>So a numbered variant is the same sound every run rather than whatever the
        /// generator happened to be up to.</summary>
        public static void Seed(uint seed) { _seed = seed == 0u ? 0x1BADB002u : seed; }

        static float NextNoise()
        {
            unchecked
            {
                _seed ^= _seed << 13;
                _seed ^= _seed >> 17;
                _seed ^= _seed << 5;
                return ((_seed & 0xFFFF) / 32767.5f) - 1f;
            }
        }

        /// <summary>Noise crack plus a low thump, which is most of what a gunshot is.</summary>
        public static AudioClip Gunshot(string name, float duration, float bodyHz, float volume)
        {
            int count = Mathf.Max(16, (int)(duration * SampleRate));
            float[] data = new float[count];
            float lowpass = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float k = i / (float)count;

                float crackEnv = Mathf.Exp(-t * 55f);
                float bodyEnv = Mathf.Exp(-t * 14f);
                float tailEnv = Mathf.Exp(-t * 6f) * 0.35f;

                float noise = NextNoise();
                lowpass = Mathf.Lerp(lowpass, noise, 0.35f);

                float sample = noise * crackEnv * 0.9f;
                sample += lowpass * tailEnv;
                sample += Mathf.Sin(2f * Mathf.PI * bodyHz * t) * bodyEnv * 0.55f;
                sample += Mathf.Sin(2f * Mathf.PI * bodyHz * 0.5f * t) * bodyEnv * 0.3f;

                data[i] = Mathf.Clamp(sample * volume * (1f - k * 0.15f), -1f, 1f);
            }
            return FromSamples(name, data);
        }

        /// <summary>
        /// A footstep, which is not one sound: it is a heel landing, the ball of the foot following it
        /// a few dozen milliseconds later, and the surface ringing in between. The old one was a
        /// single lowpassed noise burst, which is why it read as a click rather than a boot.
        ///
        /// Concrete is a bright slap that stops dead. Wood is quieter at the front and rings after it,
        /// because a floorboard is a plate with air under it - that hollowness is the whole difference
        /// between being inside and being outside, and it is worth more than any amount of level.
        /// </summary>
        public static AudioClip Footstep(string name, StepSurface surface, uint variant)
        {
            Seed(variant);
            bool wood = surface == StepSurface.Wood;
            bool metal = surface == StepSurface.Metal;

            float duration = wood ? 0.34f : metal ? 0.40f : 0.18f;
            int count = Mathf.Max(16, (int)(duration * SampleRate));
            float[] data = new float[count];

            // Every variant is a slightly different boot on a slightly different spot.
            float pitch = 0.88f + (variant % 71u) / 71f * 0.26f;
            float toeDelay = (wood ? 0.036f : metal ? 0.030f : 0.024f)
                           * (0.8f + (variant % 37u) / 37f * 0.5f);
            float toeLevel = 0.42f + (variant % 53u) / 53f * 0.28f;

            float grit = 0f;
            float previous = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float noise = NextNoise();

                grit = Mathf.Lerp(grit, noise, wood ? 0.10f : metal ? 0.30f : 0.26f);
                float bright = noise - previous;
                previous = noise;

                float sample = Strike(t, 0f, 1f, surface, pitch, grit, bright);
                sample += Strike(t, toeDelay, toeLevel, surface, pitch * 1.06f, grit, bright);

                data[i] = Mathf.Clamp(sample * (wood ? 0.36f : metal ? 0.30f : 0.44f), -1f, 1f);
            }
            return FromSamples(name, data);
        }

        /// <summary>
        /// One impact of a footstep: the knock, what the floor does about it, and the scuff.
        ///
        /// The three surfaces are deliberately far apart, because the whole value of them is being
        /// able to tell which one you are hearing through a wall. Concrete is a dead slap with grit
        /// under it and nothing after. Boards are quieter at the front and ring low - a plate with air
        /// beneath. Steel is a bright clang that carries on for a third of a second.
        /// </summary>
        static float Strike(float t, float at, float level, StepSurface surface, float pitch, float grit, float bright)
        {
            float local = t - at;
            if (local < 0f) return 0f;

            if (surface == StepSurface.Wood)
            {
                float knock = Mathf.Exp(-local * 210f);
                float sample = bright * knock * 0.55f;
                float ring = Mathf.Exp(-local * 26f);
                sample += Mathf.Sin(2f * Mathf.PI * 172f * pitch * local) * ring * 0.62f;
                sample += Mathf.Sin(2f * Mathf.PI * 281f * pitch * local) * Mathf.Exp(-local * 36f) * 0.34f;
                sample += Mathf.Sin(2f * Mathf.PI * 437f * pitch * local) * Mathf.Exp(-local * 58f) * 0.15f;
                sample += grit * Mathf.Exp(-local * 44f) * 0.20f;
                return sample * level;
            }

            if (surface == StepSurface.Metal)
            {
                float knock = Mathf.Exp(-local * 260f);
                float sample = bright * knock * 0.9f;
                // Inharmonic partials and very little damping: that is what makes a sheet ring.
                float ring = Mathf.Exp(-local * 7f);
                sample += Mathf.Sin(2f * Mathf.PI * 660f * pitch * local) * ring * 0.40f;
                sample += Mathf.Sin(2f * Mathf.PI * 1180f * pitch * local) * Mathf.Exp(-local * 9f) * 0.28f;
                sample += Mathf.Sin(2f * Mathf.PI * 2290f * pitch * local) * Mathf.Exp(-local * 14f) * 0.16f;
                return sample * level;
            }

            // Concrete does not ring. It thuds once and the rest is the sole dragging on grit.
            float hard = Mathf.Exp(-local * 380f);
            float flat = bright * hard * 1.05f;
            flat += Mathf.Sin(2f * Mathf.PI * 98f * pitch * local) * Mathf.Exp(-local * 70f) * 0.36f;
            flat += grit * Mathf.Exp(-local * 40f) * 0.50f;
            return flat * level;
        }

        /// <summary>
        /// The spoon ringing against the body as it comes out of the pouch: a handful of close, quiet,
        /// slightly detuned partials that keep catching each other. It is the sound that tells the
        /// room you have committed to something, so it is meant to be recognisable and not loud.
        /// </summary>
        /// <summary>
        /// Pulling the pin: a short bright scrape as the ring drags out of the fuse, then the spoon
        /// ringing loose against the body. All metal, and deliberately louder and harder than the
        /// draw - it is the point of no return and it should sound like one.
        /// </summary>
        public static AudioClip PinPull(string name, float duration)
        {
            int count = Mathf.Max(16, (int)(duration * SampleRate));
            float[] data = new float[count];
            Seed(0x9E3Du);
            float previous = 0f;
            float resonance = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float noise = NextNoise();
                float bright = noise - previous;
                previous = noise;

                // The scrape: bright noise gated into a short rasp, with a wobble on it so it is a
                // drag rather than a hiss.
                float rasp = Mathf.Exp(-t * 34f) * (0.6f + 0.4f * Mathf.Sin(t * 320f));
                float sample = bright * rasp * 0.85f;

                // Steel underneath it, ringing on after the scrape stops.
                resonance = Mathf.Lerp(resonance, bright, 0.5f);
                float ring = Mathf.Exp(-t * 11f);
                sample += Mathf.Sin(2f * Mathf.PI * 1840f * t) * ring * 0.30f;
                sample += Mathf.Sin(2f * Mathf.PI * 2970f * t) * Mathf.Exp(-t * 15f) * 0.20f;
                sample += Mathf.Sin(2f * Mathf.PI * 4310f * t) * Mathf.Exp(-t * 22f) * 0.10f;

                // And the spoon knocking against the body once the pin is gone.
                float spoon = t - 0.11f;
                if (spoon > 0f)
                {
                    float knock = Mathf.Exp(-spoon * 40f);
                    sample += Mathf.Sin(2f * Mathf.PI * 1240f * spoon) * knock * 0.34f;
                    sample += Mathf.Sin(2f * Mathf.PI * 2180f * spoon) * knock * 0.20f;
                }

                data[i] = Mathf.Clamp(sample * 0.85f, -1f, 1f);
            }
            return FromSamples(name, data);
        }

        public static AudioClip Tingle(string name, float duration)
        {
            int count = Mathf.Max(16, (int)(duration * SampleRate));
            float[] data = new float[count];
            Seed(0x51EBu);

            float[] at = { 0.00f, 0.11f, 0.19f, 0.33f };
            float[] hz = { 2450f, 3120f, 2760f, 3380f };

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float sample = 0f;
                for (int c = 0; c < at.Length; c++)
                {
                    float local = t - at[c];
                    if (local < 0f) continue;
                    float env = Mathf.Exp(-local * 26f);
                    sample += Mathf.Sin(2f * Mathf.PI * hz[c] * local) * env * 0.30f;
                    sample += Mathf.Sin(2f * Mathf.PI * hz[c] * 1.48f * local) * env * 0.16f;
                }
                sample += NextNoise() * Mathf.Exp(-t * 9f) * 0.06f;
                data[i] = Mathf.Clamp(sample * 0.55f, -1f, 1f);
            }
            return FromSamples(name, data);
        }

        /// <summary>
        /// Something hard landing on something. `damping` is how fast it stops ringing - steel carries
        /// for most of a second, concrete does not ring at all - and that difference is the whole
        /// reason bounces are worth listening to.
        /// </summary>
        public static AudioClip Bounce(string name, float duration, float hz, float volume, float damping)
        {
            int count = Mathf.Max(16, (int)(duration * SampleRate));
            float[] data = new float[count];
            Seed((uint)(hz * 7.3f) + 11u);
            float previous = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float noise = NextNoise();
                float bright = noise - previous;
                previous = noise;

                float sample = bright * Mathf.Exp(-t * 420f) * 0.85f;
                float ring = Mathf.Exp(-t * (6f + damping * 90f));
                sample += Mathf.Sin(2f * Mathf.PI * hz * t) * ring * 0.5f;
                sample += Mathf.Sin(2f * Mathf.PI * hz * 2.41f * t) * ring * 0.22f;
                sample += Mathf.Sin(2f * Mathf.PI * hz * 4.13f * t) * ring * 0.1f;

                data[i] = Mathf.Clamp(sample * volume, -1f, 1f);
            }
            return FromSamples(name, data);
        }

        public static AudioClip Noise(string name, float duration, float volume, float cutoffHz)
        {
            int count = Mathf.Max(16, (int)(duration * SampleRate));
            float[] data = new float[count];
            float lowpass = 0f;
            float alpha = Mathf.Clamp01(cutoffHz / SampleRate * 6f);

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                lowpass = Mathf.Lerp(lowpass, NextNoise(), alpha);
                float env = Mathf.Exp(-t * (3f / Mathf.Max(0.02f, duration)));
                data[i] = Mathf.Clamp(lowpass * env * volume, -1f, 1f);
            }
            return FromSamples(name, data);
        }

        public static AudioClip Tone(string name, float duration, float hz, float volume, float bend = 0f)
        {
            int count = Mathf.Max(16, (int)(duration * SampleRate));
            float[] data = new float[count];
            float phase = 0f;

            for (int i = 0; i < count; i++)
            {
                float k = i / (float)count;
                float t = i / (float)SampleRate;
                float frequency = hz * (1f + bend * k);
                phase += 2f * Mathf.PI * frequency / SampleRate;
                float env = Mathf.Exp(-t * (4f / Mathf.Max(0.02f, duration)));
                float sample = Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 2f) * 0.3f;
                data[i] = Mathf.Clamp(sample * env * volume, -1f, 1f);
            }
            return FromSamples(name, data);
        }

        public static AudioClip Click(string name, float duration, float hz, float volume)
        {
            int count = Mathf.Max(16, (int)(duration * SampleRate));
            float[] data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * 90f);
                float sample = Mathf.Sin(2f * Mathf.PI * hz * t) * 0.5f + NextNoise() * 0.5f;
                data[i] = Mathf.Clamp(sample * env * volume, -1f, 1f);
            }
            return FromSamples(name, data);
        }
    }

    /// <summary>Small pool of AudioSources so overlapping shots do not cut each other off.</summary>
    public sealed class SoundPlayer
    {
        readonly AudioSource[] _sources;
        readonly AudioLowPassFilter[] _filters;
        readonly AudioSource _ui;
        int _next;

        public float MasterVolume = 0.7f;

        /// <summary>Layer mask geometry is tested against for occlusion. Broken glass has no collider.</summary>
        public int OcclusionMask
        {
            get { return _propagation.Mask; }
            set { _propagation.Mask = value; }
        }

        public Transform Listener
        {
            get { return _propagation.Listener; }
            set { _propagation.Listener = value; }
        }

        public FeelTuning Feel
        {
            get { return _propagation.Feel; }
            set { _propagation.Feel = value; }
        }

        readonly SoundPropagation _propagation = new SoundPropagation();

        const float MinDistance = 3f;
        const float MaxDistance = 70f;

        public SoundPlayer(Transform parent, int voices = 12)
        {
            _propagation.Feel = new FeelTuning();
            _propagation.MinDistance = MinDistance;
            _propagation.MaxDistance = MaxDistance;

            _sources = new AudioSource[voices];
            _filters = new AudioLowPassFilter[voices];
            for (int i = 0; i < voices; i++)
            {
                GameObject go = new GameObject("voice " + i);
                go.transform.SetParent(parent, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = MinDistance;
                source.maxDistance = MaxDistance;
                source.dopplerLevel = 0f;
                _sources[i] = source;

                AudioLowPassFilter filter = go.AddComponent<AudioLowPassFilter>();
                filter.cutoffFrequency = 22000f;
                _filters[i] = filter;
            }

            GameObject uiGo = new GameObject("ui voice");
            uiGo.transform.SetParent(parent, false);
            _ui = uiGo.AddComponent<AudioSource>();
            _ui.playOnAwake = false;
            _ui.spatialBlend = 0f;
        }

        public void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            Play(clip, position, volume, pitch, 0f);
        }

        /// <summary>
        /// Distance is handled by the rolloff; carry decides what happens when there is something in
        /// the way. Pass 0 for a sound that should not be occluded at all - your own gun, a UI ding -
        /// and the weapon's soundCarry for anything that has to get through a wall.
        /// </summary>
        public void Play(AudioClip clip, Vector3 position, float volume, float pitch, float carry)
        {
            if (clip == null) return;

            int index = _next;
            _next = (_next + 1) % _sources.Length;
            AudioSource source = _sources[index];
            AudioLowPassFilter filter = _filters[index];

            SoundPath path = carry > 0f
                ? _propagation.Solve(position, carry)
                : Clear(position);

            source.transform.position = path.Position;
            source.spatialBlend = 1f;
            source.pitch = pitch;
            if (filter != null) filter.cutoffFrequency = path.Cutoff;
            source.PlayOneShot(clip, volume * MasterVolume * path.Gain);
        }

        static SoundPath Clear(Vector3 position)
        {
            SoundPath path = new SoundPath();
            path.Position = position;
            path.Gain = 1f;
            path.Cutoff = 22000f;
            return path;
        }

        /// <summary>Exposed so the sound can be reasoned about from outside - the drill HUD reads it.</summary>
        public SoundPath Solve(Vector3 position, float carry) { return _propagation.Solve(position, carry); }

        public void Play2D(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            _ui.pitch = pitch;
            _ui.PlayOneShot(clip, volume * MasterVolume);
        }
    }
}
