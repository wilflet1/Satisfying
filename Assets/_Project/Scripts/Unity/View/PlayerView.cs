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
        public ConcussionBlur Blur;

        /// <summary>The picture through a magnified optic. Null until one is fitted.</summary>
        public ScopeView Scope;
        public Transform Rig;
        public Transform ViewmodelRoot;
        public WeaponModel Weapon;
        public Transform MuzzleTip { get { return Weapon != null ? Weapon.Muzzle : null; } }

        /// <summary>
        /// World position of the barrel tip, re-projected so a tracer started here lines up with where the
        /// gun actually appears on screen. The viewmodel is drawn by its own camera at a different FOV, so
        /// the muzzle's raw world position does not fall on the same pixel under the world camera - starting
        /// a tracer there makes it visibly miss the barrel. Round-trip through screen space to correct it.
        /// </summary>
        public Vector3 MuzzleWorldPoint(Vector3 fallbackEye, Vector3 aim)
        {
            if (Weapon == null || Weapon.Muzzle == null || WeaponCamera == null || Camera == null)
                return fallbackEye + aim * 0.4f;

            Vector3 sp = WeaponCamera.WorldToScreenPoint(Weapon.Muzzle.position);
            if (sp.z <= 0.02f) return fallbackEye + aim * 0.4f;   // behind the camera: fall back
            return Camera.ScreenToWorldPoint(new Vector3(sp.x, sp.y, sp.z));
        }

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
        Vector3? _grabTarget;

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
            // On the world camera, not the weapon camera: your gun stays sharp when your head rings,
            // which is both how it works and the only thing keeping the effect playable.
            Blur = cameraGo.AddComponent<ConcussionBlur>();

            // The scope renders the world and nothing else: no viewmodel, because the rifle is not in
            // front of its own objective lens and drawing it twice is the expensive mistake here.
            Scope = new ScopeView(Rig, ~(1 << viewmodelLayer));
            _fov = feel.fieldOfView;

            // A second camera draws the gun and hands so they never clip through walls.
            GameObject weaponCameraGo = new GameObject("Weapon Camera");
            weaponCameraGo.transform.SetParent(cameraGo.transform, false);
            WeaponCamera = weaponCameraGo.AddComponent<Camera>();
            WeaponCamera.clearFlags = CameraClearFlags.Depth;
            WeaponCamera.cullingMask = 1 << viewmodelLayer;
            WeaponCamera.nearClipPlane = 0.01f;
            WeaponCamera.farClipPlane = 8f;
            WeaponCamera.fieldOfView = feel.viewmodelFov;
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

        /// <summary>Where the free hand should be while dragging something, or null when empty handed.</summary>
        public void SetGrabTarget(Vector3? worldPoint) { _grabTarget = worldPoint; }

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
                           SightTuning sight, float yaw, float pitch, float dt, bool sprinting)
        {
            SetWeapon(state.Weapon.Index);
            if (Weapon != null) Weapon.SetSight(state.Weapon.Sight);

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
            if (state.Sliding) targetFov += _feel.slideFovAdd;

            // A magnified optic does NOT zoom the main camera. The scope has a camera of its own and
            // the world outside the tube has to stay where it was, or the picture in picture is just
            // an expensive way of drawing the same zoom twice. It squeezes a little, because leaning
            // into a scope does narrow what you take in, and that is all.
            bool scoped = sight != null && sight.IsScope;
            float sightZoom = sight != null ? Mathf.Max(0.1f, sight.zoomMul) : 1f;
            float aimedFov = scoped
                ? _feel.fieldOfView * Mathf.Lerp(1f, _feel.adsFovMul, 0.35f)
                : _feel.fieldOfView * _feel.adsFovMul * sightZoom;
            targetFov = Mathf.Lerp(targetFov, aimedFov, state.Ads);
            _fov = Mathf.Lerp(_fov, targetFov, 1f - Mathf.Exp(-_feel.fovLerpSpeed * dt));
            Camera.fieldOfView = _fov;
            // Barely narrow the viewmodel camera when aiming: zooming it is what makes the gun
            // swell up and swallow the screen. The world camera does the zooming.
            WeaponCamera.fieldOfView = Mathf.Lerp(_feel.viewmodelFov, _feel.viewmodelFov * 0.94f, state.Ads);

            RenderViewmodel(in state, move, weapon, yaw, pitch, dt, sprinting, bobX, bobY);
            RenderScope(in state, sight);
        }

        /// <summary>
        /// Points the scope camera down the optic and renders it, but only over the last of the aim -
        /// the picture is worthless while the rifle is still coming up, and rendering the world a
        /// second time for a frame nobody can use is the one cost worth avoiding here.
        /// </summary>
        void RenderScope(in PlayerSimState state, SightTuning sight)
        {
            if (Scope == null) return;

            if (sight == null || !sight.IsScope || Weapon == null || Weapon.SightAnchor == null)
            {
                Scope.Render(null, _fov, 1f, 0f);
                return;
            }

            // Nothing until the rifle is most of the way up, then in over the last quarter.
            float blend = Mathf.Clamp01((state.Ads - 0.72f) / 0.28f);
            Scope.Render(Weapon.SightAnchor, _feel.fieldOfView, sight.ClampMagnification(Magnification), blend);
        }

        /// <summary>What the player has dialled the optic to. Local, like every other feel value.</summary>
        public float Magnification = 6f;

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
            _animator.Update(dt, reloading, reloadProgress, state.Ads, state.Weapon.Ammo <= 0);
            if (Weapon != null) Weapon.SetSupportGrip(weapon);

            float swayScale = Weapon != null ? Weapon.SwayScale : 1f;
            float smooth = 1f - Mathf.Exp(-_feel.swaySmooth * dt);
            Vector3 targetSwayOffset = new Vector3(-deltaYaw * _feel.swayPosition, -deltaPitch * _feel.swayPosition, 0f) * swayScale;
            Vector3 targetSwayRotation = new Vector3(deltaPitch * _feel.swayRotation, deltaYaw * _feel.swayRotation, -deltaYaw * _feel.swayRotation * 0.5f) * swayScale;
            _swayOffset = Vector3.Lerp(_swayOffset, targetSwayOffset, smooth);
            _swayRotation = Vector3.Lerp(_swayRotation, targetSwayRotation, smooth);

            MathK.Spring(ref _viewmodelKick.x, ref _viewmodelKickVelocity.x, 0f, _feel.recoilStiffness, _feel.recoilDamping, dt);
            MathK.Spring(ref _viewmodelKick.y, ref _viewmodelKickVelocity.y, 0f, _feel.recoilStiffness, _feel.recoilDamping, dt);
            MathK.Spring(ref _viewmodelKick.z, ref _viewmodelKickVelocity.z, 0f, _feel.recoilStiffness, _feel.recoilDamping, dt);

            // Where the gun sits is worked out in Shared, where it can be tested: the rule that the
            // sight lands on the centre of the screen at full ADS is a rule with a test behind it now.
            Vector3 hip = Weapon != null ? Weapon.HipOffset : new Vector3(0.14f, -0.13f, 0.28f);
            Vector3 sightLocal = Weapon != null && Weapon.SightAnchor != null ? Weapon.SightAnchor.localPosition : Vector3.zero;
            ViewmodelPose pose = ViewmodelPose.Build(in state, move, _feel, hip.ToSim(), sightLocal.ToSim(), sprinting);

            Vector3 basePosition = pose.Position.ToUnity();
            Vector3 baseEuler = pose.Euler.ToUnity();

            // Bob is a breathing motion rather than a pose, so it is cut back hard when aiming rather
            // than removed - a perfectly dead sight picture reads as a screenshot.
            float bobMul = 1f - state.Ads * 0.85f;
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

            // Dragging pulls the support hand off the gun and onto the object.
            Vector3 support = _grabTarget.HasValue ? _grabTarget.Value : _animator.SupportHandWorld();
            Quaternion handRotation = _grabTarget.HasValue
                ? Quaternion.LookRotation((support - _leftArm.Shoulder.position).normalized, Vector3.up)
                : weaponRotation * Quaternion.Euler(-14f, 0f, 6f);

            _leftArm.Solve(support, leftPole, handRotation);
            _leftArm.SetVisible(true);
        }
    }
}
