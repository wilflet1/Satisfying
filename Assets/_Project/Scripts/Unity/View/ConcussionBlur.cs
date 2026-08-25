using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// What a round off your helmet does to you: you are still alive and you can still shoot, but for
    /// a moment you cannot see well enough to do it properly.
    ///
    /// The blur is a downsample and an upsample and nothing else. Bilinear filtering on the way back
    /// up IS a blur, which means the whole effect needs no shader - and this project has no shader
    /// assets and would like to keep it that way. Strength picks how far down it goes; a second pass
    /// at higher strength widens the kernel so a rifle round smears rather than just softens.
    ///
    /// It sits on the camera because OnRenderImage has to. Everything about how long it lasts and how
    /// bad it is comes from the weapon that fired - see WeaponTuning.concussionTime/Strength.
    /// </summary>
    // ExecuteAlways so the shot sheet can photograph it. The component does nothing at all until
    // something calls Hit, so running outside play mode costs one branch per frame.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ConcussionBlur : MonoBehaviour
    {
        float _strength;        // 0..1, what it was when it landed
        float _timer;
        float _duration;

        /// <summary>
        /// 0 when your vision is clear. Derived rather than accumulated: Update only moves the clock,
        /// which means the effect reads correctly from a single frame and can be photographed by the
        /// shot sheet without play mode running.
        ///
        /// It hangs on hard and then lets go, rather than fading out linearly - which is both what
        /// being hit feels like and what stops it reading as a slow dissolve.
        /// </summary>
        public float Current
        {
            get
            {
                float k = Remaining01();
                return _strength * k * k;
            }
        }

        /// <summary>
        /// A new hit does not queue behind the last one - it takes over if it is worse, and tops the
        /// clock up if it is not. Being shot twice in the head should not be gentler than being shot
        /// once because the second one restarted a weaker effect.
        /// </summary>
        public void Hit(float strength, float duration)
        {
            if (duration <= 0f || strength <= 0f) return;
            _strength = Mathf.Max(_strength * Remaining01(), strength);
            _duration = Mathf.Max(duration, _timer);
            _timer = _duration;
        }

        public void Clear()
        {
            _timer = 0f;
            _strength = 0f;
        }

        float Remaining01()
        {
            return _duration > 0.001f ? Mathf.Clamp01(_timer / _duration) : 0f;
        }

        void Update()
        {
            if (_timer > 0f) _timer = Mathf.Max(0f, _timer - Time.deltaTime);
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            float blur = Current;
            if (blur <= 0.004f || source == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            // 2x at nothing up to 16x at full. Powers of two only: an odd divisor gives the upsample
            // a lopsided kernel and the image visibly slides sideways as the effect wears off.
            int steps = Mathf.Clamp(1 + Mathf.FloorToInt(blur * 4f), 1, 4);
            int divisor = 1 << steps;
            int width = Mathf.Max(2, source.width / divisor);
            int height = Mathf.Max(2, source.height / divisor);

            RenderTexture small = RenderTexture.GetTemporary(width, height, 0, source.format);
            small.filterMode = FilterMode.Bilinear;

            RenderTexture half = RenderTexture.GetTemporary(width * 2, height * 2, 0, source.format);
            half.filterMode = FilterMode.Bilinear;

            // Down in two hops rather than one. A single big downsample point-samples in practice and
            // the result crawls with aliasing every time you turn your head.
            Graphics.Blit(source, half);
            Graphics.Blit(half, small);
            Graphics.Blit(small, half);
            Graphics.Blit(half, destination);

            RenderTexture.ReleaseTemporary(half);
            RenderTexture.ReleaseTemporary(small);
        }
    }
}
