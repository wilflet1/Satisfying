using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// The opponent, drawn from interpolated snapshots. The pose is driven by the same replicated values
    /// the server uses for hit registration, so what you shoot at is what the server tests - including
    /// the lean, the stance and the raised weapon of a blind fire.
    /// </summary>
    public sealed class RemotePlayerView
    {
        public readonly int PeerId;
        public readonly Blockout.Character Character;
        public WeaponModel Weapon;

        readonly MovementTuning _move;
        readonly Palette _palette;
        readonly Material _skin;
        readonly int _layer;
        readonly WeaponAnimator _animator = new WeaponAnimator();
        readonly Transform _weaponHolder;
        ArmRig _rightArm;
        ArmRig _leftArm;

        int _weaponIndex = -1;
        float _reloadTimer;
        float _deathTimer;
        float _stepPhase;

        public RemotePlayerView(Transform parent, int peerId, Palette palette, MovementTuning move, int layer)
        {
            PeerId = peerId;
            _move = move;
            _palette = palette;
            _layer = layer;
            _skin = palette.Enemy;

            Character = Blockout.Duellist(parent, "Opponent " + peerId, palette, palette.Enemy, layer);

            GameObject holder = new GameObject("weapon holder");
            holder.layer = layer;
            holder.transform.SetParent(Character.Chest, false);
            holder.transform.localPosition = new Vector3(0.10f, -0.06f, 0.20f);
            _weaponHolder = holder.transform;

            Material armMaterial = Palette.Make("remote arms", new Color(0.36f, 0.30f, 0.27f), 0.12f, 0f);
            _rightArm = ArmRig.Build(Character.Chest, "right arm", new Vector3(0.24f, -0.02f, 0.0f), 1f,
                palette, armMaterial, layer, 0.10f, 0.28f, 0.27f);
            _leftArm = ArmRig.Build(Character.Chest, "left arm", new Vector3(-0.24f, -0.02f, 0.0f), -1f,
                palette, armMaterial, layer, 0.10f, 0.28f, 0.27f);

            SetWeapon(0);
        }

        void SetWeapon(int index)
        {
            if (index == _weaponIndex) return;
            _weaponIndex = index;
            if (Weapon != null && Weapon.Root != null) Object.Destroy(Weapon.Root);
            Weapon = WeaponModels.Build(index, _weaponHolder, _palette, _layer);
            _animator.Bind(Weapon);
        }

        public void Destroy()
        {
            if (Character != null && Character.Root != null) Object.Destroy(Character.Root);
        }

        public void SetVisible(bool visible)
        {
            if (Character.Root.activeSelf != visible) Character.Root.SetActive(visible);
        }

        public void OnShot()
        {
            _animator.OnShot();
        }

        public Vector3 MuzzlePosition()
        {
            return Weapon != null && Weapon.Muzzle != null ? Weapon.Muzzle.position : Character.Chest.position;
        }

        public void Render(in PlayerNetState state, float dt, WeaponTuning weapon, out float footstepImpulse)
        {
            footstepImpulse = 0f;
            SetWeapon(state.WeaponIndex);

            Transform root = Character.Root.transform;
            Vector3 position = state.Position.ToUnity();

            _reloadTimer = state.Reloading ? _reloadTimer + dt : 0f;
            float reloadProgress = state.Reloading && weapon != null && weapon.reloadTime > 0.01f
                ? Mathf.Clamp01(_reloadTimer / weapon.reloadTime)
                : 0f;
            _animator.Update(dt, state.Reloading, reloadProgress, state.Ads);

            if (!state.Alive)
            {
                _deathTimer = Mathf.Min(1f, _deathTimer + dt * 3.5f);
                root.position = position;
                root.rotation = Quaternion.Euler(0f, state.Yaw, 0f);
                Character.Body.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 88f, _deathTimer), 0f, 0f);
                Character.Body.localPosition = new Vector3(0f, Mathf.Lerp(0f, -0.85f, _deathTimer), Mathf.Lerp(0f, 0.35f, _deathTimer));
                Character.Body.localScale = Vector3.one;
                Character.Head.localPosition = Vector3.Lerp(new Vector3(0f, 1.69f, 0f), new Vector3(0f, 0.28f, 0.62f), _deathTimer);
                Character.Head.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 80f, _deathTimer), 0f, 0f);
                Character.Chest.localPosition = Vector3.Lerp(new Vector3(0f, 1.34f, 0f), new Vector3(0f, 0.30f, 0.28f), _deathTimer);
                Character.Chest.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 80f, _deathTimer), 0f, 0f);
                SolveArms();
                return;
            }
            _deathTimer = 0f;

            root.position = position;
            root.rotation = Quaternion.Euler(0f, state.Yaw, 0f);

            float lean = LeanFor(in state);
            float heightFactor = Mathf.Clamp(state.Height / Mathf.Max(0.3f, _move.standHeight), 0.2f, 1.2f);

            if (state.Stance == Stance.Prone)
            {
                Character.Body.localRotation = Quaternion.Euler(82f, 0f, -lean * _move.leanAngle * 0.6f);
                Character.Body.localPosition = new Vector3(lean * _move.leanOffset, -0.72f, 0.28f);
                Character.Body.localScale = Vector3.one;
                Character.LeftLeg.localScale = new Vector3(1f, 0.35f, 1f);
                Character.RightLeg.localScale = new Vector3(1f, 0.35f, 1f);
            }
            else
            {
                Character.Body.localRotation = Quaternion.Euler(0f, 0f, -lean * _move.leanAngle);
                Character.Body.localPosition = new Vector3(lean * _move.leanOffset,
                    (heightFactor - 1f) * _move.standHeight * 0.55f - Mathf.Abs(lean) * _move.leanDrop, 0f);
                Character.Body.localScale = new Vector3(1f, heightFactor, 1f);
                Character.LeftLeg.localScale = new Vector3(1f, heightFactor, 1f);
                Character.RightLeg.localScale = new Vector3(1f, heightFactor, 1f);
            }

            // The head is placed to land exactly where PlayerHitbox puts it, so the thing you aim at and
            // the thing the server tests are the same object. Lean really does move the head.
            float eyeHeight = state.Height + _move.eyeDrop;
            Vector3 leanShift = new Vector3(lean * _move.leanOffset, -Mathf.Abs(lean) * _move.leanDrop, 0f);

            if (state.Stance == Stance.Prone)
            {
                Character.Head.localPosition = new Vector3(0f, eyeHeight + 0.02f, 0.5f) + leanShift;
                Character.Chest.localPosition = new Vector3(0f, eyeHeight * 0.85f, 0.24f) + leanShift * 0.9f;
            }
            else
            {
                Character.Head.localPosition = new Vector3(0f, eyeHeight + 0.05f, 0f) + leanShift;
                Character.Chest.localPosition = new Vector3(0f, eyeHeight * 0.78f, 0f) + leanShift * 0.9f;
            }
            Character.Head.localRotation = Quaternion.Euler(state.Pitch * 0.75f, 0f, -lean * _move.leanAngle * 0.5f);

            // Chest pitches with the aim so the weapon points where they are actually shooting.
            float chestPitch = state.Pitch * 0.85f;
            Vector3 holderPosition = new Vector3(0.10f, -0.06f, 0.20f);
            Vector3 holderEuler = Vector3.zero;

            if (state.BlindFire > 0.01f)
            {
                float dial = Mathf.Clamp(state.BlindAngle, -1f, 1f);
                float elevation = dial >= 0f ? dial * _move.blindFirePitchMax : -dial * _move.blindFirePitchMin;
                holderPosition = Vector3.Lerp(holderPosition, new Vector3(0.08f, 0.42f, 0.16f), state.BlindFire);
                holderEuler = Vector3.Lerp(holderEuler, new Vector3(-elevation - chestPitch, lean * _move.blindFireYaw, -18f), state.BlindFire);
            }
            else if (state.Ads > 0.01f)
            {
                holderPosition = Vector3.Lerp(holderPosition, new Vector3(0f, -0.02f, 0.24f), state.Ads);
            }

            Character.Chest.localRotation = Quaternion.Euler(chestPitch, 0f, -lean * _move.leanAngle);
            _weaponHolder.localPosition = holderPosition;
            _weaponHolder.localRotation = Quaternion.Euler(holderEuler);

            float speed = state.Velocity.Flat.Magnitude;
            if (state.Grounded && speed > 0.4f)
            {
                float previousPhase = _stepPhase;
                _stepPhase += dt * Mathf.Clamp(speed * 1.5f, 1f, 14f);
                float swing = Mathf.Sin(_stepPhase) * Mathf.Clamp01(speed / 5f) * 0.22f;
                Character.LeftLeg.localPosition = new Vector3(-0.11f, 0.35f * (state.Stance == Stance.Stand ? 1f : 0.7f), swing);
                Character.RightLeg.localPosition = new Vector3(0.11f, 0.35f * (state.Stance == Stance.Stand ? 1f : 0.7f), -swing);
                if (Mathf.FloorToInt(previousPhase / Mathf.PI) != Mathf.FloorToInt(_stepPhase / Mathf.PI))
                    footstepImpulse = Mathf.Clamp01(speed / 6f);
            }
            else
            {
                Character.LeftLeg.localPosition = new Vector3(-0.11f, 0.35f, 0f);
                Character.RightLeg.localPosition = new Vector3(0.11f, 0.35f, 0f);
            }

            SolveArms();
        }

        void SolveArms()
        {
            if (Weapon == null || Weapon.Root == null) return;
            Quaternion weaponRotation = Weapon.Root.transform.rotation;
            Vector3 rightPole = Character.Chest.rotation * new Vector3(0.6f, -1f, -0.25f);
            Vector3 leftPole = Character.Chest.rotation * new Vector3(-0.6f, -1f, -0.25f);

            if (Weapon.GripAnchor != null)
                _rightArm.Solve(Weapon.GripAnchor.position, rightPole, weaponRotation * Quaternion.Euler(-8f, 0f, 0f));
            _leftArm.Solve(_animator.SupportHandWorld(), leftPole, weaponRotation * Quaternion.Euler(-14f, 0f, 6f));
        }

        float LeanFor(in PlayerNetState state)
        {
            float mul = 1f;
            if (state.Stance == Stance.Prone) mul = _move.proneLeanMul;
            else if (state.Stance == Stance.Crouch) mul = _move.crouchLeanMul;
            mul *= Mathf.Lerp(1f, _move.adsLeanMul, state.Ads);
            return state.Lean * mul;
        }
    }
}
