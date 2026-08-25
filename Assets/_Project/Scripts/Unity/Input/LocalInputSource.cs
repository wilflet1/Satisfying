using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Turns the keyboard and mouse into the tick-rate InputCommand stream the simulation eats.
    /// Continuous state (aim, lean, stance, speed dial) is polled every frame; one-shot presses are
    /// latched so a tap between two ticks is never swallowed.
    /// </summary>
    public sealed class LocalInputSource : IPlayerInput
    {
        public InputBindings Bindings;
        public FeelTuning Feel { get; set; }
        public GameTuning Tuning { get; set; }
        /// <summary>Keyboard actions: movement, lean, stance. Off while a modal menu owns the input.</summary>
        public bool Enabled { get; set; }

        public LocalInputSource()
        {
            Enabled = true;
            LookEnabled = true;
            Sights = new byte[4];
            // The bolt gun comes with its glass on. Everything else starts on irons, as it did.
            Sights[3] = (byte)SightKind.Scope;
            Magnification = 6f;
            ScopeMin = 3.5f;
            ScopeMax = 18f;
        }

        /// <summary>
        /// Mouse look and the mouse buttons. Turned off on its own while the tuning panel is open, so
        /// you can drag a slider with the cursor and still strafe and lean with the keyboard - which is
        /// the entire point of tuning movement live.
        /// </summary>
        public bool LookEnabled { get; set; }

        /// <summary>Optic fitted to each weapon, chosen in the gear menu and saved between sessions.</summary>
        public byte[] Sights { get; private set; }
        public byte CurrentWeapon { get { return _weapon; } }

        public float Yaw { get { return _yaw + _recoilYaw; } }
        public float Pitch { get { return _pitch + _recoilPitch; } }
        public float SpeedDial { get { return _speedDial; } }
        public bool WantsSprint { get { return Bindings != null && Bindings.Held(GameAction.Sprint); } }
        public float BlindAngle { get { return _blindAngle; } }
        public bool BlindFiring { get { return _blindFiring; } }
        public Stance StanceRequest { get { return _stance; } }
        public float LeanTarget { get { return _leanTarget; } }

        float _yaw;
        float _pitch;
        float _recoilPitch;
        float _recoilYaw;
        float _speedDial = 1f;
        float _blindAngle;
        bool _blindFiring;
        float _leanTarget;
        bool _leanToggleLeft;
        bool _leanToggleRight;
        Stance _stance = Stance.Stand;
        byte _weapon;

        bool _latchJump;
        bool _latchReload;
        bool _latchStepLeft;
        bool _latchStepRight;
        bool _latchMantle;
        bool _latchMelee;
        bool _latchGrab;
        byte _meleeSeq;
        byte _grabSeq;
        float _smoothX;
        float _smoothY;

        public void ResetView(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = pitch;
            _recoilPitch = 0f;
            _recoilYaw = 0f;
            _leanTarget = 0f;
            _blindAngle = 0f;
            _leanToggleLeft = false;
            _leanToggleRight = false;
            _stance = Stance.Stand;
        }

        /// <summary>Called once per rendered frame, before any ticks are stepped.</summary>
        public void PollFrame(float dt, in PlayerSimState predicted)
        {
            MovementTuning move = Tuning.move;
            WeaponTuning weapon = Tuning.Weapon(_weapon);

            RecoverRecoil(weapon, dt);

            if (!Enabled)
            {
                _leanTarget = 0f;
                _blindFiring = false;
                return;
            }

            float rawX = 0f;
            if (LookEnabled)
            {
                float sensitivity = Feel.sensitivity * Mathf.Lerp(1f, Feel.adsSensitivityMul, predicted.Ads);
                rawX = Input.GetAxisRaw("Mouse X") * sensitivity;
                float rawY = Input.GetAxisRaw("Mouse Y") * sensitivity * (Feel.invertY >= 0.5f ? 1f : -1f);

                if (Feel.smoothing > 0.001f)
                {
                    float k = 1f - Mathf.Exp(-Mathf.Lerp(60f, 6f, Mathf.Clamp01(Feel.smoothing)) * dt);
                    _smoothX = Mathf.Lerp(_smoothX, rawX, k);
                    _smoothY = Mathf.Lerp(_smoothY, rawY, k);
                    rawX = _smoothX;
                    rawY = _smoothY;
                }

                _pitch = Mathf.Clamp(_pitch + rawY, -move.pitchLimit, move.pitchLimit);
            }

            PollLean();
            _yaw += rawX;
            _yaw = Mathf.Repeat(_yaw + 180f, 360f) - 180f;

            PollStance();
            _blindFiring = LookEnabled && Bindings.Held(GameAction.BlindFire);
            PollSpeedDial(move);
            PollWeapons();

            if (Bindings.Pressed(GameAction.Jump)) { _latchJump = true; _latchMantle = true; }
            if (Bindings.Pressed(GameAction.Reload)) _latchReload = true;
            if (Bindings.Pressed(GameAction.Melee)) _latchMelee = true;
            if (Bindings.Pressed(GameAction.Grab)) _latchGrab = true;
            if (Bindings.Pressed(GameAction.StepLeft)) _latchStepLeft = true;
            if (Bindings.Pressed(GameAction.StepRight)) _latchStepRight = true;
        }

        void PollLean()
        {
            bool modifier = Bindings.Held(GameAction.LeanModifier);
            bool leftHeld = Bindings.Held(GameAction.LeanLeft) || (modifier && Input.GetKey(Bindings[GameAction.LeanLeft].Key));
            bool rightHeld = Bindings.Held(GameAction.LeanRight) || (modifier && Input.GetKey(Bindings[GameAction.LeanRight].Key));

            if (Bindings.LeanIsToggle)
            {
                if (Bindings.Pressed(GameAction.LeanLeft) || (modifier && Input.GetKeyDown(Bindings[GameAction.LeanLeft].Key)))
                {
                    _leanToggleLeft = !_leanToggleLeft;
                    _leanToggleRight = false;
                }
                if (Bindings.Pressed(GameAction.LeanRight) || (modifier && Input.GetKeyDown(Bindings[GameAction.LeanRight].Key)))
                {
                    _leanToggleRight = !_leanToggleRight;
                    _leanToggleLeft = false;
                }
                leftHeld = _leanToggleLeft;
                rightHeld = _leanToggleRight;
            }

            float target = 0f;
            if (leftHeld) target -= 1f;
            if (rightHeld) target += 1f;
            _leanTarget = target;
        }

        void PollStance()
        {
            // Sprinting stands you up - but a crouch pressed DURING a sprint is a slide request, so the
            // stand has to be applied before the crouch is read, never after it.
            if (Bindings.Held(GameAction.Sprint) && Bindings.Held(GameAction.MoveForward)) _stance = Stance.Stand;

            if (Bindings.CrouchIsToggle)
            {
                if (Bindings.Pressed(GameAction.Crouch))
                    _stance = _stance == Stance.Crouch ? Stance.Stand : Stance.Crouch;
            }
            else
            {
                if (Bindings.Held(GameAction.Crouch)) { if (_stance != Stance.Prone) _stance = Stance.Crouch; }
                else if (_stance == Stance.Crouch) _stance = Stance.Stand;
            }

            if (Bindings.ProneIsToggle)
            {
                if (Bindings.Pressed(GameAction.Prone))
                    _stance = _stance == Stance.Prone ? Stance.Crouch : Stance.Prone;
            }
            else
            {
                if (Bindings.Held(GameAction.Prone)) _stance = Stance.Prone;
                else if (_stance == Stance.Prone) _stance = Stance.Crouch;
            }

            if (Bindings.Pressed(GameAction.Jump)) _stance = Stance.Stand;
        }

        /// <summary>
        /// Magnification the player has dialled into a variable optic, and whether the wheel is
        /// currently theirs to turn. Read by PlayerView, which is what actually renders the scope.
        /// </summary>
        public float Magnification { get; set; }

        /// <summary>Set by the game each frame: true when a variable optic is up and the wheel should
        /// be turning the power ring rather than the speed dial.</summary>
        public bool ScopeWheel { get; set; }

        public float ScopeMin { get; set; }
        public float ScopeMax { get; set; }

        void PollSpeedDial(MovementTuning move)
        {
            float wheel = LookEnabled ? Input.mouseScrollDelta.y : 0f;

            // Behind a variable optic the wheel is the power ring. Your walking pace is not something
            // you are adjusting with a rifle up, and a scope you cannot zoom is not a variable scope.
            if (ScopeWheel)
            {
                if (Mathf.Abs(wheel) > 0.01f)
                {
                    // A fixed step in magnification is far too coarse at the bottom and far too fine
                    // at the top, so it steps proportionally: one notch is always about 12 per cent.
                    Magnification *= Mathf.Pow(1.12f, Mathf.Sign(wheel));
                    Magnification = Mathf.Clamp(Magnification, ScopeMin, ScopeMax);
                }
                if (Bindings.Pressed(GameAction.SpeedUp)) Magnification = Mathf.Min(ScopeMax, Magnification * 1.12f);
                if (Bindings.Pressed(GameAction.SpeedDown)) Magnification = Mathf.Max(ScopeMin, Magnification / 1.12f);
                return;
            }

            // While the gun is up over cover the wheel aims it instead of setting your walking pace -
            // it is the only way to steer a shot you cannot see.
            if (_blindFiring)
            {
                if (Mathf.Abs(wheel) > 0.01f) _blindAngle += Mathf.Sign(wheel) * move.blindFireAngleStep;
                if (Bindings.Pressed(GameAction.SpeedUp)) _blindAngle += move.blindFireAngleStep;
                if (Bindings.Pressed(GameAction.SpeedDown)) _blindAngle -= move.blindFireAngleStep;
                _blindAngle = Mathf.Clamp(_blindAngle, -1f, 1f);
                return;
            }

            if (Mathf.Abs(wheel) > 0.01f) _speedDial += Mathf.Sign(wheel) * move.speedDialStep;
            if (Bindings.Pressed(GameAction.SpeedUp)) _speedDial += move.speedDialStep;
            if (Bindings.Pressed(GameAction.SpeedDown)) _speedDial -= move.speedDialStep;
            _speedDial = Mathf.Clamp01(_speedDial);
        }

        void PollWeapons()
        {
            if (Bindings.Pressed(GameAction.Weapon1)) _weapon = 0;
            if (Bindings.Pressed(GameAction.Weapon2)) _weapon = 1;
            if (Bindings.Pressed(GameAction.Weapon3)) _weapon = 2;
            if (Bindings.Pressed(GameAction.Weapon4)) _weapon = 3;
        }

        void RecoverRecoil(WeaponTuning weapon, float dt)
        {
            _recoilPitch = MathK.ExpSmooth(_recoilPitch, 0f, weapon.recoilRecoverSpeed, dt);
            _recoilYaw = MathK.ExpSmooth(_recoilYaw, 0f, weapon.recoilRecoverSpeed, dt);
        }

        /// <summary>
        /// Recoil is applied to the shooter's own aim, so it costs nothing in latency. The share the game
        /// pulls back down for you lives in the recovering offset; the rest is baked into your real aim.
        /// </summary>
        public void ApplyRecoil(int playerId, uint shotIndex, WeaponTuning weapon)
        {
            float pitchKick, yawKick;
            ShotSolver.RecoilKick(weapon, playerId, shotIndex, out pitchKick, out yawKick);

            float recovered = Mathf.Clamp01(weapon.recoilRecoverFraction);
            _pitch = Mathf.Clamp(_pitch + pitchKick * (1f - recovered), -Tuning.move.pitchLimit, Tuning.move.pitchLimit);
            _yaw += yawKick * (1f - recovered);
            _recoilPitch += pitchKick * recovered;
            _recoilYaw += yawKick * recovered;
        }

        // ------------------------------------------------------------------ IInputSource
        public InputCommand Sample(uint tick, float dt)
        {
            InputCommand c = InputCommand.Default(tick);
            c.Yaw = Yaw;
            c.Pitch = Pitch;
            c.WeaponIndex = _weapon;
            c.SightIndex = Sights[Mathf.Clamp(_weapon, 0, Sights.Length - 1)];
            c.SpeedDial = _speedDial;
            c.BlindAngle = _blindAngle;
            c.StanceRequest = _stance;

            if (!Enabled)
            {
                c.LeanAxis = 0f;
                return c;
            }

            float x = 0f, y = 0f;
            if (Bindings.Held(GameAction.MoveForward)) y += 1f;
            if (Bindings.Held(GameAction.MoveBack)) y -= 1f;
            if (Bindings.Held(GameAction.MoveRight)) x += 1f;
            if (Bindings.Held(GameAction.MoveLeft)) x -= 1f;
            c.MoveX = x;
            c.MoveY = y;
            c.LeanAxis = _leanTarget;

            Buttons buttons = Buttons.None;
            if (Bindings.Held(GameAction.Jump) || _latchJump) buttons |= Buttons.Jump;
            if (_latchMantle) buttons |= Buttons.Mantle;
            if (Bindings.Held(GameAction.Sprint)) buttons |= Buttons.Sprint;
            // Firing and aiming follow the cursor: clicking a slider must never fire the gun.
            if (LookEnabled && Bindings.Held(GameAction.Aim)) buttons |= Buttons.Ads;
            if (LookEnabled && Bindings.Held(GameAction.Fire)) buttons |= Buttons.Fire;
            if (Bindings.Held(GameAction.Reload) || _latchReload) buttons |= Buttons.Reload;
            if (Bindings.Held(GameAction.WalkSlow)) buttons |= Buttons.WalkToggle;
            if (_blindFiring) buttons |= Buttons.BlindFire;
            if (LookEnabled && (Bindings.Held(GameAction.Melee) || _latchMelee)) buttons |= Buttons.Melee;
            if (Bindings.Held(GameAction.Grab) || _latchGrab) buttons |= Buttons.Grab;
            // The press itself travels as a counter, not as the button edge, so a lost packet cannot
            // swallow a grab or leave the server thinking you never let go.
            if (_latchMelee && LookEnabled) _meleeSeq = (byte)((_meleeSeq + 1) & 7);
            if (_latchGrab) _grabSeq = (byte)((_grabSeq + 1) & 7);
            c.MeleeSeq = _meleeSeq;
            c.GrabSeq = _grabSeq;
            if (Bindings.Held(GameAction.LeanModifier)) buttons |= Buttons.SlowLean;
            if (Bindings.Held(GameAction.StepLeft) || _latchStepLeft) buttons |= Buttons.StepLeft;
            if (Bindings.Held(GameAction.StepRight) || _latchStepRight) buttons |= Buttons.StepRight;
            c.Buttons = buttons;

            _latchJump = false;
            _latchMantle = false;
            _latchMelee = false;
            _latchGrab = false;
            _latchReload = false;
            _latchStepLeft = false;
            _latchStepRight = false;
            return c;
        }
    }
}
