namespace Satisfying.Shared
{
    /// <summary>One on-screen control, in screen points with the origin at the bottom left.</summary>
    public struct TouchButton
    {
        public string Label;
        public float X, Y;          // centre
        public float Radius;
        public bool Held;
        public bool Toggle;         // stays on until pressed again (crouch)
    }

    public enum TouchAction { Fire, Jump, Crouch, LeanLeft, LeanRight, Count }

    /// <summary>
    /// Touch controls, with no engine in sight so the finger bookkeeping can be tested rather than
    /// discovered on a phone. Coordinates are screen points, origin bottom left - the convention
    /// Unity's touch positions already use; the drawing layer flips for IMGUI.
    ///
    /// A finger is claimed by whatever it lands on and keeps that job until it lifts. That is the
    /// whole trick: without ownership, sliding a thumb off the stick starts turning the camera, and
    /// a second finger for the trigger yanks the aim.
    /// </summary>
    public sealed class TouchRig
    {
        public const int NoFinger = -1;

        public float Width { get; private set; }
        public float Height { get; private set; }

        /// <summary>Move axes, -1..1, from the left thumb.</summary>
        public float MoveX, MoveY;

        /// <summary>Look movement since the last read, in points. Read once per frame and cleared.</summary>
        public float LookDeltaX, LookDeltaY;

        /// <summary>Sprint comes from pushing the stick right to its edge, so it needs no button.</summary>
        public bool Sprint { get { return _stickPush > SprintThreshold; } }
        public float StickPush { get { return _stickPush; } }

        public readonly TouchButton[] Buttons = new TouchButton[(int)TouchAction.Count];

        /// <summary>Where the left thumb landed, and where it is now - the stick draws from these.</summary>
        public float StickOriginX, StickOriginY, StickX, StickY;
        public bool StickActive { get { return _stickFinger != NoFinger; } }

        const float SprintThreshold = 0.92f;
        const float DeadZone = 0.14f;

        float _stickRadius = 100f;
        float _stickPush;
        int _stickFinger = NoFinger;
        int _lookFinger = NoFinger;
        float _lookLastX, _lookLastY;
        readonly int[] _buttonFinger = new int[(int)TouchAction.Count];

        public TouchRig()
        {
            for (int i = 0; i < _buttonFinger.Length; i++) _buttonFinger[i] = NoFinger;
            Layout(1280f, 720f);
        }

        /// <summary>
        /// Places the controls for a screen of this size. Everything is a fraction of the short edge,
        /// so a tablet gets the same thumb reach as a phone rather than the same pixel count.
        /// </summary>
        public void Layout(float width, float height)
        {
            Width = width;
            Height = height;
            float unit = height < width ? height : width;

            _stickRadius = unit * 0.16f;

            float pad = unit * 0.09f;
            float big = unit * 0.115f;
            float small = unit * 0.082f;

            Set(TouchAction.Fire, "FIRE", width - pad - big * 0.4f, pad + big * 0.6f, big, false);
            Set(TouchAction.Jump, "JUMP", width - pad - big * 0.3f, pad + big * 2.4f, small, false);
            Set(TouchAction.Crouch, "CRCH", width - pad - big * 2.1f, pad + big * 0.8f, small, true);
            Set(TouchAction.LeanLeft, "Q", width - pad - big * 2.4f, height - pad - small, small, false);
            Set(TouchAction.LeanRight, "E", width - pad - small * 0.4f, height - pad - small, small, false);
        }

        void Set(TouchAction action, string label, float x, float y, float radius, bool toggle)
        {
            int i = (int)action;
            Buttons[i].Label = label;
            Buttons[i].X = x;
            Buttons[i].Y = y;
            Buttons[i].Radius = radius;
            Buttons[i].Toggle = toggle;
        }

        public bool Held(TouchAction action) { return Buttons[(int)action].Held; }

        // ================================================================== fingers
        public void Begin(int finger, float x, float y)
        {
            // Buttons first: they sit inside the look region and would otherwise be swallowed by it.
            for (int i = 0; i < Buttons.Length; i++)
            {
                if (_buttonFinger[i] != NoFinger) continue;
                if (!Inside(Buttons[i], x, y)) continue;

                _buttonFinger[i] = finger;
                Buttons[i].Held = Buttons[i].Toggle ? !Buttons[i].Held : true;
                return;
            }

            // The stick is wherever the left thumb lands, not a fixed circle to hunt for.
            if (x < Width * 0.5f && _stickFinger == NoFinger)
            {
                _stickFinger = finger;
                StickOriginX = x;
                StickOriginY = y;
                StickX = x;
                StickY = y;
                return;
            }

            if (_lookFinger == NoFinger)
            {
                _lookFinger = finger;
                _lookLastX = x;
                _lookLastY = y;
            }
        }

        public void Move(int finger, float x, float y)
        {
            if (finger == _stickFinger)
            {
                StickX = x;
                StickY = y;
                UpdateStick();
                return;
            }

            if (finger == _lookFinger)
            {
                LookDeltaX += x - _lookLastX;
                LookDeltaY += y - _lookLastY;
                _lookLastX = x;
                _lookLastY = y;
            }

            // A finger on a button that slides off keeps the button held: a thumb drifting a few
            // millimetres mid-firefight must not drop the trigger.
        }

        public void End(int finger)
        {
            if (finger == _stickFinger)
            {
                _stickFinger = NoFinger;
                MoveX = 0f;
                MoveY = 0f;
                _stickPush = 0f;
                return;
            }

            if (finger == _lookFinger)
            {
                _lookFinger = NoFinger;
                return;
            }

            for (int i = 0; i < _buttonFinger.Length; i++)
            {
                if (_buttonFinger[i] != finger) continue;
                _buttonFinger[i] = NoFinger;
                if (!Buttons[i].Toggle) Buttons[i].Held = false;
                return;
            }
        }

        /// <summary>Everything up: a lost focus or an app switch must not leave the player walking.</summary>
        public void ReleaseAll()
        {
            _stickFinger = NoFinger;
            _lookFinger = NoFinger;
            MoveX = 0f;
            MoveY = 0f;
            _stickPush = 0f;
            LookDeltaX = 0f;
            LookDeltaY = 0f;
            for (int i = 0; i < _buttonFinger.Length; i++)
            {
                _buttonFinger[i] = NoFinger;
                if (!Buttons[i].Toggle) Buttons[i].Held = false;
            }
        }

        public void ConsumeLook(out float x, out float y)
        {
            x = LookDeltaX;
            y = LookDeltaY;
            LookDeltaX = 0f;
            LookDeltaY = 0f;
        }

        void UpdateStick()
        {
            float dx = StickX - StickOriginX;
            float dy = StickY - StickOriginY;
            float distance = MathK.Sqrt(dx * dx + dy * dy);

            if (distance < 0.0001f) { MoveX = 0f; MoveY = 0f; _stickPush = 0f; return; }

            float push = distance / _stickRadius;
            if (push > 1f)
            {
                // Drag past the edge and the stick follows, so the thumb never loses the centre.
                float pull = (distance - _stickRadius) / distance;
                StickOriginX += dx * pull;
                StickOriginY += dy * pull;
                push = 1f;
            }

            _stickPush = push;

            float scaled = push < DeadZone ? 0f : (push - DeadZone) / (1f - DeadZone);
            MoveX = dx / distance * scaled;
            MoveY = dy / distance * scaled;
        }

        static bool Inside(TouchButton button, float x, float y)
        {
            float dx = x - button.X;
            float dy = y - button.Y;
            // A little larger than it looks: fingers are not styluses.
            float reach = button.Radius * 1.25f;
            return dx * dx + dy * dy <= reach * reach;
        }
    }
}
