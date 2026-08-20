using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// The local camera rig, viewmodel and arms. Everything here is presentation only - it reads the
    /// predicted simulation state and never writes to it, so no amount of camera polish can desync you
    /// from the server.
    /// </summary>
    public sealed class PlayerView
    {
        public Camera Camera;
        public Camera WeaponCamera;
        public Transform Rig;
        public Transform ViewmodelRoot;
        public WeaponModel Weapon;
        public Transform MuzzleTip { get { return Weapon != null ? Weapon.Muzzle : null; } }

        readonly FeelTuning _feel;
        readonly Palette _palette;
        readonly int _layer;
        readonly WeaponAnimator _animator = new WeaponAnimator();
        ArmRig _rightArm;
        ArmRig _leftArm;
        int _weaponIndex = -1;

        float _bobPhase;
        float _landDip;
        float _punch;
        float _punchVelocity;
        float _viewRoll;
        float _fov;
        Vector3 _swayOffset;
        Vector3 _swayRotation;
        Vector3 _viewmodelKick;
        Vector3 _viewmodelKickVelocity;
        float _lastYaw;
        float _lastPitch;

        public PlayerView(Transform parent, FeelTuning feel, Palette palette, int viewmodelLayer)
        {
            _feel = feel;
            _palette = palette;
            _layer = viewmodelLayer;

            GameObject rig = new GameObject("View Rig");
            rig.transform.SetParent(parent, false);
            Rig = rig.transform;

            GameObject cameraGo = new GameObject("Main Camera");
            cameraGo.transform.SetParent(Rig, false);
            cameraGo.tag = "MainCamera";
            Camera = cameraGo.AddComponent<Camera>();
            Camera.nearClipPlane = 0.035f;
            Camera.farClipPlane = 500f;
            Camera.fieldOfView = feel.fieldOfView;
            Camera.cullingMask = ~(1 << viewmodelLayer);
            cameraGo.AddComponent<AudioListener>();
            _fov = feel.fieldOfView;

            // A second camera draws the gun and hands so they never clip through walls.
            GameObject weaponCameraGo = new GameObject("Weapon Camera");
            weaponCameraGo.transform.SetParent(cameraGo.transform, false);
            WeaponCamera = weaponCameraGo.AddComponent<Camera>();
            WeaponCamera.clearFlags = CameraClearFlags.Depth;
            WeaponCamera.cullingMask = 1 << viewmodelLayer;
            WeaponCamera.nearClipPlane = 0.01f;
            WeaponCamera.farClipPlane = 8f;
            WeaponCamera.fieldOfView = 55f;
            WeaponCamera.depth = Camera.depth + 1;

            GameObject viewmodelRoot = new GameObject("Viewmodel");
            viewmodelRoot.transform.SetParent(cameraGo.transform, false);
            viewmodelRoot.layer = viewmodelLayer;
            ViewmodelRoot = viewmodelRoot.transform;

            // Arms hang off the camera, not the weapon, and reach for it with IK.
            _rightArm = ArmRig.Build(cameraGo.transform, "right arm", new Vector3(0.19f, -0.26f, -0.24f), 1f,
                palette, palette.Hands, viewmodelLayer, 0.072f, 0.26f, 0.25f);
            _leftArm = ArmRig.Build(cameraGo.transform, "left arm", new Vector3(-0.19f, -0.26f, -0.24f), -1f,
                palette, palette.Hands, viewmodelLayer, 0.070f, 0.26f, 0.25f);

            SetWeapon(0);
        }

        public void SetWeapon(int index)
        {
            if (index == _weaponIndex) return;
            _weaponIndex = index;
            if (Weapon != null && Weapon.Root != null) Object.Destroy(Weapon.Root);
            Weapon = WeaponModels.Build(index, ViewmodelRoot, _palette, _layer);
            _animator.Bind(Weapon);
        }

        public WeaponAnimator.SoundCue ConsumeWeaponCue() { return _animator.ConsumeCue(); }

        public void OnLanded(float impactSpeed)
        {
            _landDip = Mathf.Min(_feel.landDipMax, impactSpeed * _feel.landDipPerSpeed);
        }

        public void OnShot(WeaponTuning weapon)
        {
            _punch += weapon.recoilVertical * 0.35f;
            _viewmodelKick += new Vector3(0f, weapon.recoilVertical * 0.004f, -_feel.recoilKickBack);
            _animator.OnShot();
        }

        /// <summary>
        /// Called every rendered frame with the interpolated local state. The aim angles come from the
        /// input layer, not from the network - your own view never waits for a packet.
        /// </summary>
        public void Render(in PlayerSimState state, Vector3 renderPosition, MovementTuning move, WeaponTuning weapon,
                           float yaw, float pitch, float dt, bool sprinting)
        {
            SetWeapon(state.Weapon.Index);

            float lean = state.EffectiveLean(move);
            float speed = state.Velocity.Flat.Magnitude;

            _landDip = Mathf.Max(0f, _landDip - _landDip * _feel.landRecoverSpeed * dt);
            MathK.Spring(ref _punch, ref _punchVelocity, 0f, _feel.recoilStiffness, _feel.recoilDamping, dt);

            float bobSpeed = Mathf.Clamp01(speed / Mathf.Max(0.5f, move.walkSpeed));
            float bobScale = bobSpeed * (sprinting ? _feel.bobSprintMul : 1f) * Mathf.Lerp(1f, _feel.bobAdsMul, state.Ads);
            if (state.Grounded) _bobPhase += dt * _feel.bobFrequency * Mathf.Max(0.15f, bobSpeed);

            float bobY = Mathf.Sin(_bobPhase * 2f) * _feel.bobAmplitude * bobScale;
            float bobX = Mathf.Cos(_bobPhase) * _feel.bobSideAmount * bobScale;
            float bobRoll = Mathf.Cos(_bobPhase) * _feel.bobRoll * bobScale;

            Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
            Vector3 right = flat * Vector3.right;

            float strafeRoll = 0f;
            Vector3 flatVelocity = state.Velocity.Flat.ToUnity();
            if (flatVelocity.sqrMagnitude > 0.01f)
                strafeRoll = -Vector3.Dot(flatVelocity.normalized, right) * Mathf.Clamp01(speed / Mathf.Max(1f, move.walkSpeed)) * _feel.strafeRoll;

            float targetRoll = state.ViewRoll(move) * _feel.leanRollExtra + strafeRoll + bobRoll;
            _viewRoll = Mathf.Lerp(_viewRoll, targetRoll, 1f - Mathf.Exp(-_feel.leanSmooth * dt));

            Rig.rotation = Quaternion.Euler(pitch - _punch, yaw, _viewRoll);

            Vector3 eye = renderPosition + Vector3.up * state.EyeHeight(move);
            eye += right * (lean * move.leanOffset);
            eye += Vector3.down * (Mathf.Abs(lean) * move.leanDrop);
            eye += Rig.rotation * new Vector3(bobX, bobY, 0f);
            eye += Vector3.down * _landDip;
            Rig.position = eye;

            float targetFov = _feel.fieldOfView;
            if (sprinting) targetFov += _feel.sprintFovAdd;
            targetFov = Mathf.Lerp(targetFov, _feel.fieldOfView * _feel.adsFovMul, state.Ads);
            _fov = Mathf.Lerp(_fov, targetFov, 1f - Mathf.Exp(-_feel.fovLerpSpeed * dt));
            Camera.fieldOfView = _fov;
            WeaponCamera.fieldOfView = Mathf.Lerp(55f, 42f, state.Ads);

            RenderViewmodel(in state, move, weapon, yaw, pitch, dt, sprinting, bobX, bobY);
        }

        void RenderViewmodel(in PlayerSimState state, MovementTuning move, WeaponTuning weapon,
                             float yaw, float pitch, float dt, bool sprinting, float bobX, float bobY)
        {
            float deltaYaw = Mathf.DeltaAngle(_lastYaw, yaw);
            float deltaPitch = pitch - _lastPitch;
            _lastYaw = yaw;
            _lastPitch = pitch;

            bool reloading = state.Weapon.Reloading;
            float reloadProgress = reloading && weapon.reloadTime > 0.01f
                ? 1f - Mathf.Clamp01(state.Weapon.ReloadTimer / weapon.reloadTime)
                : 0f;
            _animator.Update(dt, reloading, reloadProgress, state.Ads);

            float swayScale = Weapon != null ? Weapon.SwayScale : 1f;
            float smooth = 1f - Mathf.Exp(-_feel.swaySmooth * dt);
            Vector3 targetSwayOffset = new Vector3(-deltaYaw * _feel.swayPosition, -deltaPitch * _feel.swayPosition, 0f) * swayScale;
            Vector3 targetSwayRotation = new Vector3(deltaPitch * _feel.swayRotation, deltaYaw * _feel.swayRotation, -deltaYaw * _feel.swayRotation * 0.5f) * swayScale;
            _swayOffset = Vector3.Lerp(_swayOffset, targetSwayOffset, smooth);
            _swayRotation = Vector3.Lerp(_swayRotation, targetSwayRotation, smooth);

            MathK.Spring(ref _viewmodelKick.x, ref _viewmodelKickVelocity.x, 0f, _feel.recoilStiffness, _feel.recoilDamping, dt);
            MathK.Spring(ref _viewmodelKick.y, ref _viewmodelKickVelocity.y, 0f, _feel.recoilStiffness, _feel.recoilDamping, dt);
            MathK.Spring(ref _viewmodelKick.z, ref _viewmodelKickVelocity.z, 0f, _feel.recoilStiffness, _feel.recoilDamping, dt);

            Vector3 feelNudge = new Vector3(_feel.viewmodelX, _feel.viewmodelY, _feel.viewmodelZ);
            Vector3 hip = (Weapon != null ? Weapon.HipOffset : new Vector3(0.14f, -0.13f, 0.28f)) + feelNudge;

            // Aiming lines the weapon's own sight up with the exact centre of the screen, whatever gun it is.
            Vector3 sightLocal = Weapon != null && Weapon.SightAnchor != null ? Weapon.SightAnchor.localPosition : Vector3.zero;
            Vector3 ads = -sightLocal + new Vector3(0f, 0f, _feel.adsSightDistance) + feelNudge * 0.25f;

            Vector3 basePosition = Vector3.Lerp(hip, ads, state.Ads);
            Vector3 baseEuler = Vector3.zero;

            if (state.Stance == Stance.Prone) basePosition += new Vector3(0f, 0.03f, -0.06f);
            else if (state.Stance == Stance.Crouch) basePosition += new Vector3(0f, 0.012f, 0f);

            float sprintBlend = sprinting ? 1f : 0f;
            basePosition += new Vector3(0.05f, -0.05f, -0.06f) * sprintBlend;
            baseEuler += new Vector3(6f, -18f, _feel.sprintTilt) * sprintBlend;

            // Blind fire: the gun goes up over the cover and swings with the lean, the camera does not move.
            if (state.BlindFire > 0.001f)
            {
                float dial = Mathf.Clamp(state.BlindAngle, -1f, 1f);
                float elevation = dial >= 0f ? dial * move.blindFirePitchMax : -dial * move.blindFirePitchMin;
                Vector3 blindPosition = new Vector3(hip.x * 0.35f, 0.30f, hip.z * 0.75f);
                Vector3 blindEuler = new Vector3(-elevation, state.EffectiveLean(move) * move.blindFireYaw, -22f * Mathf.Sign(hip.x));
                basePosition = Vector3.Lerp(basePosition, blindPosition, state.BlindFire);
                baseEuler = Vector3.Lerp(baseEuler, blindEuler, state.BlindFire);
            }

            float bobMul = 1f - state.Ads * 0.75f;
            Vector3 bobOffset = new Vector3(bobX, bobY, 0f) * 1.6f * bobMul;

            ViewmodelRoot.localPosition = basePosition + _animator.PoseOffset + _swayOffset * (1f - state.Ads * 0.6f) + _viewmodelKick + bobOffset;
            ViewmodelRoot.localRotation = Quaternion.Euler(baseEuler + _animator.PoseEuler + _swayRotation * (1f - state.Ads * 0.6f));

            SolveArms(state.Ads);
        }

        void SolveArms(float adsBlend)
        {
            if (Weapon == null || Weapon.Root == null) return;

            Transform camera = Camera.transform;
            Quaternion weaponRotation = Weapon.Root.transform.rotation;

            Vector3 rightPole = camera.rotation * new Vector3(0.55f, -1f, -0.2f);
            Vector3 leftPole = camera.rotation * new Vector3(-0.55f, -1f, -0.2f);

            if (Weapon.GripAnchor != null)
                _rightArm.Solve(Weapon.GripAnchor.position, rightPole, weaponRotation * Quaternion.Euler(-8f, 0f, 0f));

            Vector3 support = _animator.SupportHandWorld();
            _leftArm.Solve(support, leftPole, weaponRotation * Quaternion.Euler(-14f, 0f, 6f));

            // The support hand lets go while a fresh magazine is fetched.
            _leftArm.SetVisible(true);
        }
    }
}
