using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// Procedural weapon animation driven by simulation state rather than clips: the bolt cycles when a
    /// round actually leaves, and the magazine drops on the same timeline the server is counting down.
    /// That means the animation can never drift out of sync with what the game thinks is happening.
    /// </summary>
    public sealed class WeaponAnimator
    {
        public WeaponModel Model;

        float _bolt;                 // 1 = fully back
        float _boltHold;
        Vector3 _magazineHome;
        Quaternion _magazineHomeRotation;
        bool _magazineHidden;

        public enum SoundCue { None, MagOut, MagIn, Bolt }

        float _lastProgress;
        SoundCue _pendingCue;

        /// <summary>Reload sounds are emitted when the animation actually reaches that beat.</summary>
        public SoundCue ConsumeCue()
        {
            SoundCue cue = _pendingCue;
            _pendingCue = SoundCue.None;
            return cue;
        }

        public Vector3 PoseOffset { get; private set; }
        public Vector3 PoseEuler { get; private set; }
        public Vector3 SupportHandLocal { get; private set; }
        public float SupportHandBlend { get; private set; }

        public void Bind(WeaponModel model)
        {
            Model = model;
            if (model == null) return;
            _magazineHome = model.Magazine != null ? model.Magazine.localPosition : Vector3.zero;
            _magazineHomeRotation = model.Magazine != null ? model.Magazine.localRotation : Quaternion.identity;
            _bolt = 0f;
            _magazineHidden = false;
            SetMagazineVisible(true);
        }

        public void OnShot()
        {
            _bolt = 1f;
            _boltHold = 0.012f;
        }

        void SetMagazineVisible(bool visible)
        {
            if (Model == null || Model.Magazine == null) return;
            if (_magazineHidden == !visible) return;
            _magazineHidden = !visible;
            Model.Magazine.gameObject.SetActive(visible);
        }

        /// <summary>
        /// progress goes 0 -> 1 across the weapon's reload time. Phases are laid out so the support hand
        /// is always somewhere believable: mag well, down for a fresh magazine, back up, then the charge.
        ///
        /// empty holds the bolt back on the guns that do that, which is the only "out of ammo" tell that
        /// works without looking at the HUD - including on the opponent, since ammo is replicated.
        /// </summary>
        public void Update(float dt, bool reloading, float progress, float adsBlend, bool empty = false)
        {
            if (Model == null) return;

            _boltHold -= dt;
            if (_boltHold <= 0f) _bolt = Mathf.MoveTowards(_bolt, 0f, dt / 0.05f);
            if (empty && !reloading && Model.HoldsOpenWhenEmpty) _bolt = 1f;
            if (Model.Bolt != null) Model.Bolt.localPosition = Model.BoltTravel * _bolt;

            if (!reloading)
            {
                _lastProgress = 0f;
                SetMagazineVisible(true);
                if (Model.Magazine != null)
                {
                    Model.Magazine.localPosition = _magazineHome;
                    Model.Magazine.localRotation = _magazineHomeRotation;
                }
                PoseOffset = Vector3.zero;
                PoseEuler = Vector3.zero;
                SupportHandBlend = 0f;
                SupportHandLocal = Model.ForegripAnchor != null ? Model.ForegripAnchor.localPosition : Vector3.zero;
                return;
            }

            float p = Mathf.Clamp01(progress);
            if (Crossed(p, 0.16f)) _pendingCue = SoundCue.MagOut;
            else if (Crossed(p, 0.60f)) _pendingCue = SoundCue.MagIn;
            else if (Crossed(p, 0.88f)) _pendingCue = SoundCue.Bolt;
            _lastProgress = p;

            // Tilt the weapon inboard so the magazine well is actually visible while you work.
            float tilt = Mathf.Sin(Mathf.Clamp01(p * 1.4f) * Mathf.PI) ;
            PoseOffset = new Vector3(-0.045f, -0.055f, -0.05f) * tilt * (1f - adsBlend * 0.5f);
            PoseEuler = new Vector3(6f, 26f, 14f) * tilt * (1f - adsBlend * 0.5f);

            Vector3 magAnchor = Model.MagAnchor != null ? Model.MagAnchor.localPosition : Vector3.zero;
            Vector3 foregrip = Model.ForegripAnchor != null ? Model.ForegripAnchor.localPosition : Vector3.zero;
            Vector3 fetch = magAnchor + new Vector3(0.02f, -0.28f, -0.10f);

            SupportHandBlend = 1f;

            if (p < 0.16f)
            {
                // reaching for the release
                SupportHandLocal = Vector3.Lerp(foregrip, magAnchor, p / 0.16f);
                SetMagazineVisible(true);
                SetMagazinePose(0f);
            }
            else if (p < 0.34f)
            {
                // magazine falls away
                float k = (p - 0.16f) / 0.18f;
                SupportHandLocal = Vector3.Lerp(magAnchor, fetch, k * 0.6f);
                SetMagazineVisible(true);
                SetMagazinePose(k);
            }
            else if (p < 0.56f)
            {
                // hand goes down for a fresh magazine
                float k = (p - 0.34f) / 0.22f;
                SupportHandLocal = Vector3.Lerp(Vector3.Lerp(magAnchor, fetch, 0.6f), fetch, k);
                SetMagazineVisible(false);
            }
            else if (p < 0.82f)
            {
                // seat the new magazine
                float k = (p - 0.56f) / 0.26f;
                SupportHandLocal = Vector3.Lerp(fetch, magAnchor, k);
                SetMagazineVisible(true);
                SetMagazinePose(1f - k);
            }
            else
            {
                // charge it and return to the foregrip
                float k = (p - 0.82f) / 0.18f;
                SetMagazineVisible(true);
                SetMagazinePose(0f);
                SupportHandLocal = Vector3.Lerp(magAnchor, foregrip, k);
                if (k > 0.25f && k < 0.5f) _bolt = Mathf.Max(_bolt, 1f - (k - 0.25f) / 0.25f);
                SupportHandBlend = 1f - Mathf.Clamp01((k - 0.6f) / 0.4f);
            }
        }

        bool Crossed(float progress, float threshold)
        {
            return _lastProgress < threshold && progress >= threshold;
        }

        void SetMagazinePose(float outAmount)
        {
            if (Model.Magazine == null) return;
            float k = Mathf.Clamp01(outAmount);
            Model.Magazine.localPosition = _magazineHome + Model.MagazineEject * (k * k);
            Model.Magazine.localRotation = _magazineHomeRotation * Quaternion.Euler(Model.MagazineEjectTilt * k);
        }

        /// <summary>Where the support hand should be, in world space.</summary>
        public Vector3 SupportHandWorld()
        {
            if (Model == null || Model.Root == null) return Vector3.zero;
            Vector3 local = SupportHandBlend > 0f
                ? SupportHandLocal
                : (Model.ForegripAnchor != null ? Model.ForegripAnchor.localPosition : Vector3.zero);
            return Model.Root.transform.TransformPoint(local);
        }
    }
}
