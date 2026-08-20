using UnityEngine;

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
        public AudioClip Footstep;
        public AudioClip Jump;
        public AudioClip Land;
        public AudioClip StanceChange;
        public AudioClip Lean;
        public AudioClip UiClick;
        public AudioClip RoundStart;

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
            b.Footstep = Synth.Noise("step", 0.09f, 0.22f, 900f);
            b.Jump = Synth.Noise("jump", 0.10f, 0.2f, 700f);
            b.Land = Synth.Noise("land", 0.16f, 0.34f, 500f);
            b.StanceChange = Synth.Noise("stance", 0.16f, 0.18f, 480f);
            b.Lean = Synth.Noise("lean", 0.10f, 0.09f, 1400f);
            b.UiClick = Synth.Click("ui", 0.04f, 1800f, 0.25f);
            b.RoundStart = Synth.Tone("round", 0.5f, 520f, 0.35f, 0.4f);
            return b;
        }

        public AudioClip ShotFor(int weaponIndex)
        {
            if (Shots == null || Shots.Length == 0) return Shot;
            return Shots[weaponIndex < 0 || weaponIndex >= Shots.Length ? 0 : weaponIndex];
        }

        public AudioClip DistantShotFor(int weaponIndex)
        {
            if (ShotsDistant == null || ShotsDistant.Length == 0) return ShotDistant;
            return ShotsDistant[weaponIndex < 0 || weaponIndex >= ShotsDistant.Length ? 0 : weaponIndex];
        }
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
        readonly AudioSource _ui;
        int _next;

        public float MasterVolume = 0.7f;

        public SoundPlayer(Transform parent, int voices = 12)
        {
            _sources = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                GameObject go = new GameObject("voice " + i);
                go.transform.SetParent(parent, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 3f;
                source.maxDistance = 70f;
                source.dopplerLevel = 0f;
                _sources[i] = source;
            }

            GameObject uiGo = new GameObject("ui voice");
            uiGo.transform.SetParent(parent, false);
            _ui = uiGo.AddComponent<AudioSource>();
            _ui.playOnAwake = false;
            _ui.spatialBlend = 0f;
        }

        public void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            AudioSource source = _sources[_next];
            _next = (_next + 1) % _sources.Length;
            source.transform.position = position;
            source.spatialBlend = 1f;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume * MasterVolume);
        }

        public void Play2D(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            _ui.pitch = pitch;
            _ui.PlayOneShot(clip, volume * MasterVolume);
        }
    }
}
