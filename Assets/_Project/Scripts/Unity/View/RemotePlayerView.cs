using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// The opponent, drawn from interpolated snapshots.
    ///
    /// Every bone is placed straight out of BodyPose - the same skeleton PlayerHitbox lays its capsules
    /// over - so the model is not an interpretation of the hit registration, it is the same numbers.
    /// Nothing here poses anything by hand; if a limb looks wrong, it is wrong in Shared and it is wrong
    /// for the server too, which is exactly where you want that kind of bug to live.
    /// </summary>
    public sealed class RemotePlayerView
    {
        public readonly int PeerId;
        public readonly Blockout.Character Character;
        public WeaponModel Weapon;

        readonly MovementTuning _move;
        readonly Palette _palette;
        readonly int _layer;
        readonly WeaponAnimator _animator = new WeaponAnimator();
        readonly Transform _weaponHolder;

        int _weaponIndex = -1;
        Vector3? _grabTarget;
        float _reloadTimer;
        float _deathTimer;
        Quaternion _deathFall = Quaternion.identity;
        float _stepPhase;

        readonly bool _firstPerson;

        /// <summary>
        /// firstPerson builds the same character for the player wearing it: look down and your own legs
        /// and chest are there. The head and neck come off (the camera lives inside them) and the arms
        /// and weapon are left to the viewmodel, which already draws them at the right scale.
        /// </summary>
        public RemotePlayerView(Transform parent, int peerId, Palette palette, MovementTuning move, int layer,
                                bool firstPerson = false)
        {
            _firstPerson = firstPerson;
            PeerId = peerId;
            _move = move;
            _palette = palette;
            _layer = layer;

            // Your own body is the friendly colour. Looking down and seeing the shade you have spent
            // the match shooting at is a small thing that reads wrong every single time.
            Character = Blockout.Duellist(parent, "Duellist " + peerId, palette,
                firstPerson ? palette.Ally : palette.Enemy, layer, move.standHeight / 1.82f);

            _weaponHolder = Blockout.Group(Character.Root.transform, "weapon holder", Vector3.zero, layer).transform;

            if (_firstPerson)
            {
                Character.SetFirstPerson();
                Character.SetArmsVisible(false);
                _weaponHolder.gameObject.SetActive(false);
            }
            else
            {
                SetWeapon(0);
            }
        }

        void SetWeapon(int index)
        {
            if (_firstPerson || index == _weaponIndex) return;
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

        public void OnShot() { _animator.OnShot(); }

        public WeaponAnimator.SoundCue ConsumeWeaponCue() { return _animator.ConsumeCue(); }

        /// <summary>Their hand goes onto whatever they are dragging, same as yours does.</summary>
        public void SetGrabTarget(Vector3? worldPoint) { _grabTarget = worldPoint; }

        public Vector3 MuzzlePosition()
        {
            return Weapon != null && Weapon.Muzzle != null ? Weapon.Muzzle.position : Character.Chest.Joint.position;
        }

        public void Render(in PlayerNetState state, float dt, WeaponTuning weapon, out float footstepImpulse)
        {
            footstepImpulse = 0f;
            SetWeapon(state.WeaponIndex);
            if (Weapon != null) Weapon.SetSight(state.SightIndex);

            _reloadTimer = state.Reloading ? _reloadTimer + dt : 0f;
            float reloadProgress = state.Reloading && weapon != null && weapon.reloadTime > 0.01f
                ? Mathf.Clamp01(_reloadTimer / weapon.reloadTime)
                : 0f;
            _animator.Update(dt, state.Reloading, reloadProgress, state.Ads, state.Ammo <= 0);
            if (Weapon != null) Weapon.SetSupportGrip(weapon);

            Transform root = Character.Root.transform;
            root.position = state.Position.ToUnity();
            root.rotation = Quaternion.Euler(0f, state.Yaw, 0f);

            PlayerSimState shown = state.ToDisplayState(_move.staminaMax);
            BodyPose pose = BodyPose.Build(in shown, _move, weapon);

            Stride(in state, ref pose, dt, out footstepImpulse);
            if (!state.Alive) Collapse(ref pose, dt);
            else { _deathTimer = 0f; _deathFall = Quaternion.identity; }

            Place(in pose, in state, weapon);
        }

        /// <summary>
        /// The walk cycle. This is the one thing on the body that is NOT in BodyPose, because the phase
        /// is not replicated - it would cost a byte a tick to make a foot swing agree to the centimetre.
        /// So the amplitude is kept small enough that a stepping leg never travels more than about a
        /// shin's width from the capsule that is actually being shot at.
        /// </summary>
        void Stride(in PlayerNetState state, ref BodyPose pose, float dt, out float footstepImpulse)
        {
            footstepImpulse = 0f;
            float speed = state.Velocity.Flat.Magnitude;
            if (!state.Grounded || state.Sliding || state.Vaulting || speed <= 0.4f || !state.Alive) return;

            float previousPhase = _stepPhase;
            _stepPhase += dt * Mathf.Clamp(speed * 1.5f, 1f, 14f);

            float swing = Mathf.Sin(_stepPhase) * Mathf.Clamp01(speed / 5f) * 0.10f;
            Nudge(ref pose.LeftKnee, swing * 0.5f);
            Nudge(ref pose.LeftAnkle, swing);
            Nudge(ref pose.LeftToe, swing);
            Nudge(ref pose.RightKnee, -swing * 0.5f);
            Nudge(ref pose.RightAnkle, -swing);
            Nudge(ref pose.RightToe, -swing);

            if (Mathf.FloorToInt(previousPhase / Mathf.PI) != Mathf.FloorToInt(_stepPhase / Mathf.PI))
                footstepImpulse = Mathf.Clamp01(speed / 6f);
        }

        static void Nudge(ref Vec3 joint, float forward) { joint.z += forward; }

        /// <summary>
        /// Going down. The whole skeleton is rotated about the hips rather than each bone being animated,
        /// which costs four lines and means a body that folds instead of a body that sinks into the floor.
        /// </summary>
        void Collapse(ref BodyPose pose, float dt)
        {
            _deathTimer = Mathf.Min(1f, _deathTimer + dt * 2.6f);
            float k = Mathf.SmoothStep(0f, 1f, _deathTimer);
            Quaternion fall = Quaternion.Euler(-86f * k, 22f * k, 0f);
            Vector3 pivot = pose.Pelvis.ToUnity();

            Fold(ref pose.Head, fall, pivot);
            Fold(ref pose.NeckBase, fall, pivot);
            Fold(ref pose.Shoulders, fall, pivot);
            Fold(ref pose.ChestTop, fall, pivot);
            Fold(ref pose.ChestBase, fall, pivot);
            Fold(ref pose.LeftShoulder, fall, pivot);
            Fold(ref pose.LeftElbow, fall, pivot);
            Fold(ref pose.LeftHand, fall, pivot);
            Fold(ref pose.RightShoulder, fall, pivot);
            Fold(ref pose.RightElbow, fall, pivot);
            Fold(ref pose.RightHand, fall, pivot);

            // The arms need a second fold of their own, about the shoulder line they hang from.
            // Tipping a man who is holding a rifle out in front of him backwards about his hips swings
            // everything that was in front of him upwards - so the first version of this dropped a
            // corpse that presented its weapon at the sky with one arm pointing straight up.
            Quaternion arms = Quaternion.Euler(104f * k, 0f, 0f);
            Vector3 shoulders = pose.Shoulders.ToUnity();
            Fold(ref pose.LeftShoulder, arms, shoulders);
            Fold(ref pose.LeftElbow, arms, shoulders);
            Fold(ref pose.LeftHand, arms, shoulders);
            Fold(ref pose.RightShoulder, arms, shoulders);
            Fold(ref pose.RightElbow, arms, shoulders);
            Fold(ref pose.RightHand, arms, shoulders);

            // The legs fold the other way, or a corpse ends up doing a bridge. The hips go with them,
            // or the thighs are drawn from where the hips used to be.
            Quaternion legs = Quaternion.Euler(52f * k, 0f, 0f);
            Fold(ref pose.LeftHip, legs, pivot);
            Fold(ref pose.RightHip, legs, pivot);
            Fold(ref pose.LeftKnee, legs, pivot);
            Fold(ref pose.LeftAnkle, legs, pivot);
            Fold(ref pose.LeftToe, legs, pivot);
            Fold(ref pose.RightKnee, legs, pivot);
            Fold(ref pose.RightAnkle, legs, pivot);
            Fold(ref pose.RightToe, legs, pivot);

            // Rotating about the hips alone leaves the hips at standing height with a corpse hanging
            // off them, so the whole body comes down as one.
            float drop = Mathf.Lerp(0f, Mathf.Max(0f, pose.Pelvis.y - 0.17f * pose.Scale), k);
            pose.Translate(new Vec3(0f, -drop, 0f));

            // The gun is hung off the firing hand by a rotation of its own, and that rotation knows
            // nothing about any of the above. Without this the rifle stays perfectly level in the air
            // while the man holding it lies on the floor.
            _deathFall = arms * fall;
        }

        static void Fold(ref Vec3 joint, Quaternion rotation, Vector3 pivot)
        {
            Vector3 turned = pivot + rotation * (joint.ToUnity() - pivot);
            joint = turned.ToSim();
        }

        void Place(in BodyPose pose, in PlayerNetState state, WeaponTuning weapon)
        {
            // The chest is DRAWN to the shoulder line; the hitbox capsule stops a radius short of it so
            // its rounded cap lands there instead of bulging over the collarbones. Same extent, so the
            // model and the capsule still end in the same place.
            Character.Chest.Set(pose.ChestBase.ToUnity(), pose.Shoulders.ToUnity());
            Character.Stomach.Set(pose.Pelvis.ToUnity(), pose.ChestBase.ToUnity());
            Character.Neck.Set(pose.NeckBase.ToUnity(), pose.Head.ToUnity());

            Character.LeftThigh.Set(pose.LeftHip.ToUnity(), pose.LeftKnee.ToUnity());
            Character.LeftShin.Set(pose.LeftKnee.ToUnity(), pose.LeftAnkle.ToUnity());
            Character.LeftFoot.Set(pose.LeftAnkle.ToUnity(), pose.LeftToe.ToUnity());
            Character.RightThigh.Set(pose.RightHip.ToUnity(), pose.RightKnee.ToUnity());
            Character.RightShin.Set(pose.RightKnee.ToUnity(), pose.RightAnkle.ToUnity());
            Character.RightFoot.Set(pose.RightAnkle.ToUnity(), pose.RightToe.ToUnity());

            float lean = LeanFor(in state);
            float roll = -lean * _move.leanAngle;

            Character.Head.localPosition = pose.Head.ToUnity();
            Character.Head.localRotation = Quaternion.Euler(state.Alive ? state.Pitch * 0.8f : 0f, 0f, roll * 0.5f);

            // The weapon is hung off the firing hand, not the chest: the hand comes out of BodyPose and
            // so does the arm hitbox, so lining the grip up with it is what keeps the gun, the arms and
            // the thing the server shoots at in one place.
            Quaternion hold = Quaternion.Euler(state.Alive ? state.Pitch : 0f, 0f, state.Alive ? roll : 0f);
            if (!state.Alive) hold = _deathFall * hold;
            if (state.Alive && state.BlindFire > 0.01f)
            {
                float dial = Mathf.Clamp(state.BlindAngle, -1f, 1f);
                float elevation = dial >= 0f ? dial * _move.blindFirePitchMax : -dial * _move.blindFirePitchMin;
                hold = Quaternion.Euler(state.Pitch - elevation * state.BlindFire,
                                        lean * _move.blindFireYaw * state.BlindFire,
                                        roll - 18f * state.BlindFire);
            }

            Vector3 rightHand = pose.RightHand.ToUnity();
            Vector3 leftHand = pose.LeftHand.ToUnity();

            if (!_firstPerson)
            {
                Vector3 grip = Weapon != null && Weapon.GripAnchor != null ? Weapon.GripAnchor.localPosition : Vector3.zero;
                _weaponHolder.localRotation = hold;
                _weaponHolder.localPosition = rightHand - hold * grip;

                // Reloads and drags pull the support hand off the gun; the arm follows it there.
                if (_grabTarget.HasValue)
                    leftHand = Character.Root.transform.InverseTransformPoint(_grabTarget.Value);
                else if (_animator.SupportHandBlend > 0.001f && Weapon != null && Weapon.Root != null)
                    leftHand = Vector3.Lerp(leftHand,
                        Character.Root.transform.InverseTransformPoint(_animator.SupportHandWorld()),
                        _animator.SupportHandBlend);

                Character.LeftUpperArm.Set(pose.LeftShoulder.ToUnity(), pose.LeftElbow.ToUnity());
                Character.LeftForearm.Set(pose.LeftElbow.ToUnity(), leftHand);
                Character.RightUpperArm.Set(pose.RightShoulder.ToUnity(), pose.RightElbow.ToUnity());
                Character.RightForearm.Set(pose.RightElbow.ToUnity(), rightHand);

                Character.LeftHand.localPosition = leftHand;
                Character.LeftHand.localRotation = hold;
                Character.RightHand.localPosition = rightHand;
                Character.RightHand.localRotation = hold;
            }
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
