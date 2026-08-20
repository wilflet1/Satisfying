using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// Synthesised sound effects. Original, free, and zero bytes on disk - swap these for real samples
    /// later by replacing the clips on the bank.
    /// </summary>
    public sealed class AudioBank
    {
        public AudioClip Shot;
        public AudioClip ShotDistant;
        public AudioClip DryFire;
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
            b.Shot = Synth.Gunshot("shot", 0.30f, 140f, 1f);
            b.ShotDistant = Synth.Gunshot("shot distant", 0.45f, 70f, 0.55f);
            b.DryFire = Synth.Click("dry fire", 0.06f, 2600f, 0.35f);
            b.Reload = Synth.Click("reload", 0.10f, 900f, 0.5f);
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
