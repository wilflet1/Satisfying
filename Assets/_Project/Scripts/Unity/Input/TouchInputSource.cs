using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// The phone's answer to LocalInputSource: it produces the same InputCommand from thumbs instead
    /// of a keyboard, so everything downstream - prediction, the wire, the server - cannot tell the
    /// difference. The finger bookkeeping lives in TouchRig, which is engine free and tested; this
    /// is the part that reads Unity's touches and draws the controls.
    /// </summary>
    public sealed class TouchInputSource : IPlayerInput
    {
        public readonly TouchRig Rig = new TouchRig();
        public FeelTuning Feel { get; set; }
        public GameTuning Tuning { get; set; }

        /// <summary>Set by the view so the aim we send matches the camera the player is looking through.</summary>
        public float Yaw;
        public float Pitch;

        public bool Enabled { get; set; }
        public bool LookEnabled { get; set; }
        public byte CurrentWeapon { get; private set; }
        public byte[] Sights { get; private set; }

        public TouchInputSource()
        {
            Enabled = true;
            LookEnabled = true;
            Sights = new byte[3];
        }

        /// <summary>The aim the view is showing, pushed back so recoil and respawns are not fought.</summary>
        public void ResetView(float yaw, float pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
        }

        /// <summary>
        /// Recoil moves the aim we send, not just the camera - otherwise the shot pattern on screen
        /// and the one the server computes would drift apart.
        /// </summary>
        public void ApplyRecoil(int playerId, uint shotIndex, WeaponTuning weapon)
        {
            if (weapon == null) return;
            DeterministicRandom rng = DeterministicRandom.ForShot(playerId, shotIndex, 0);
            Pitch = Mathf.Clamp(Pitch - weapon.recoilVertical, -89f, 89f);
            Yaw = Mathf.Repeat(Yaw + rng.NextSigned() * weapon.recoilHorizontal, 360f);
        }

        float _speedDial = 1f;
        byte _grabSeq;
        byte _meleeSeq;

        // A phone has no wheel. The optic sits where the game puts it and the pinch gesture is a job
        // for another day; everything below still has to exist so both inputs are the same shape.
        public float Magnification { get; set; }
        public bool ScopeWheel { get; set; }
        public float ScopeMin { get; set; }
        public float ScopeMax { get; set; }

        public float SpeedDial { get { return _speedDial; } }
        public bool WantsSprint { get { return Rig.Sprint && Rig.MoveY > 0.4f; } }

        /// <summary>Called once per frame before the tick loop, exactly like the desktop source.</summary>
        public void PollFrame(float dt, in PlayerSimState predicted)
        {
            Rig.Layout(Screen.width, Screen.height);

            if (!LookEnabled || !Application.isFocused)
            {
                Rig.ReleaseAll();
                return;
            }

            int count = Input.touchCount;
            for (int i = 0; i < count; i++)
            {
                Touch touch = Input.GetTouch(i);
                Vector2 p = touch.position;

                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        Rig.Begin(touch.fingerId, p.x, p.y);
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        Rig.Move(touch.fingerId, p.x, p.y);
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        Rig.End(touch.fingerId);
                        break;
                }
            }

            // Turn the drag into an aim. Sensitivity is per-inch rather than per-pixel, or the same
            // swipe would turn twice as far on a denser screen.
            float lookX, lookY;
            Rig.ConsumeLook(out lookX, out lookY);

            float dpi = Screen.dpi > 1f ? Screen.dpi : 160f;
            float degreesPerInch = Feel != null ? Feel.touchLookSensitivity : 260f;
            float scale = degreesPerInch / dpi;

            Yaw = Mathf.Repeat(Yaw + lookX * scale, 360f);
            Pitch = Mathf.Clamp(Pitch - lookY * scale * (Feel != null && Feel.invertY > 0.5f ? -1f : 1f),
                -89f, 89f);
        }

        public InputCommand Sample(uint tick, float dt)
        {
            InputCommand c = InputCommand.Default(tick);
            c.Yaw = Yaw;
            c.Pitch = Pitch;
            c.MoveX = Rig.MoveX;
            c.MoveY = Rig.MoveY;
            c.SpeedDial = _speedDial;
            c.WeaponIndex = CurrentWeapon;
            c.SightIndex = Sights[Mathf.Clamp(CurrentWeapon, 0, Sights.Length - 1)];
            c.StanceRequest = Rig.Held(TouchAction.Crouch) ? Stance.Crouch : Stance.Stand;

            Buttons buttons = Buttons.None;
            if (Rig.Held(TouchAction.Fire)) buttons |= Buttons.Fire;
            if (Rig.Held(TouchAction.Jump)) buttons |= Buttons.Jump | Buttons.Mantle;
            if (Rig.Sprint && Rig.MoveY > 0.4f) buttons |= Buttons.Sprint;

            float lean = 0f;
            if (Rig.Held(TouchAction.LeanLeft)) lean -= 1f;
            if (Rig.Held(TouchAction.LeanRight)) lean += 1f;
            c.LeanAxis = lean;

            c.Buttons = buttons;
            c.GrabSeq = _grabSeq;
            c.MeleeSeq = _meleeSeq;
            return c;
        }

        /// <summary>The HUD's own buttons, for the actions that do not deserve a permanent thumb.</summary>
        public void PressGrab() { _grabSeq = (byte)((_grabSeq + 1) & 7); }
        public void PressMelee() { _meleeSeq = (byte)((_meleeSeq + 1) & 7); }
        public void NextWeapon() { CurrentWeapon = (byte)((CurrentWeapon + 1) % 3); }
    }
}
