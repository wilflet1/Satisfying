using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Satisfying.Game
{
    public enum GameAction
    {
        MoveForward, MoveBack, MoveLeft, MoveRight,
        Jump, Sprint, Crouch, Prone, WalkSlow,
        Fire, Aim, Reload, BlindFire,
        LeanLeft, LeanRight, LeanModifier,
        StepLeft, StepRight,
        Weapon1, Weapon2, Weapon3,
        SpeedUp, SpeedDown,
        Scoreboard, TuningPanel, BindingsPanel, NetGraph, Menu,
        Count
    }

    /// <summary>
    /// A key, optionally gated behind a held modifier. The chord is what makes Alt+A a side step while
    /// plain A still strafes - and a bare binding is suppressed while a chord on the same key is armed,
    /// so the two never fire together.
    /// </summary>
    [Serializable]
    public struct Binding
    {
        public KeyCode Key;
        public KeyCode Modifier;

        public Binding(KeyCode key, KeyCode modifier = KeyCode.None)
        {
            Key = key;
            Modifier = modifier;
        }

        public bool IsBound { get { return Key != KeyCode.None; } }
        public bool HasModifier { get { return Modifier != KeyCode.None; } }

        public override string ToString()
        {
            if (!IsBound) return "-";
            string key = Pretty(Key);
            return HasModifier ? Pretty(Modifier) + " + " + key : key;
        }

        public static string Pretty(KeyCode code)
        {
            switch (code)
            {
                case KeyCode.Mouse0: return "LMB";
                case KeyCode.Mouse1: return "RMB";
                case KeyCode.Mouse2: return "MMB";
                case KeyCode.Mouse3: return "Mouse 4";
                case KeyCode.Mouse4: return "Mouse 5";
                case KeyCode.LeftShift: return "L Shift";
                case KeyCode.RightShift: return "R Shift";
                case KeyCode.LeftControl: return "L Ctrl";
                case KeyCode.RightControl: return "R Ctrl";
                case KeyCode.LeftAlt: return "L Alt";
                case KeyCode.RightAlt: return "R Alt";
                case KeyCode.Alpha1: return "1";
                case KeyCode.Alpha2: return "2";
                case KeyCode.Alpha3: return "3";
                case KeyCode.Alpha4: return "4";
                case KeyCode.None: return "-";
                default: return code.ToString();
            }
        }
    }

    /// <summary>The whole control scheme: rebindable, persisted, and conflict aware.</summary>
    public sealed class InputBindings
    {
        const string PrefsKey = "satisfying.bindings.v1";

        public readonly Binding[] Bindings = new Binding[(int)GameAction.Count];

        /// <summary>Crouch and prone default to toggles the way extraction shooters do.</summary>
        public bool CrouchIsToggle = true;
        public bool ProneIsToggle = true;
        public bool LeanIsToggle = false;
        public bool FreeLeanWithMouse = true;

        public InputBindings()
        {
            ResetToDefaults();
        }

        public Binding this[GameAction action]
        {
            get { return Bindings[(int)action]; }
            set { Bindings[(int)action] = value; }
        }

        public void ResetToDefaults()
        {
            Set(GameAction.MoveForward, KeyCode.W);
            Set(GameAction.MoveBack, KeyCode.S);
            Set(GameAction.MoveLeft, KeyCode.A);
            Set(GameAction.MoveRight, KeyCode.D);
            Set(GameAction.Jump, KeyCode.Space);
            Set(GameAction.Sprint, KeyCode.LeftShift);
            Set(GameAction.Crouch, KeyCode.C);
            Set(GameAction.Prone, KeyCode.X);
            Set(GameAction.WalkSlow, KeyCode.LeftControl);
            Set(GameAction.Fire, KeyCode.Mouse0);
            Set(GameAction.Aim, KeyCode.Mouse1);
            Set(GameAction.Reload, KeyCode.R);
            Set(GameAction.BlindFire, KeyCode.V);
            Set(GameAction.LeanLeft, KeyCode.Q);
            Set(GameAction.LeanRight, KeyCode.E);
            Set(GameAction.LeanModifier, KeyCode.LeftAlt);
            Set(GameAction.StepLeft, KeyCode.A, KeyCode.LeftAlt);
            Set(GameAction.StepRight, KeyCode.D, KeyCode.LeftAlt);
            Set(GameAction.Weapon1, KeyCode.Alpha1);
            Set(GameAction.Weapon2, KeyCode.Alpha2);
            Set(GameAction.Weapon3, KeyCode.Alpha3);
            Set(GameAction.SpeedUp, KeyCode.PageUp);
            Set(GameAction.SpeedDown, KeyCode.PageDown);
            Set(GameAction.Scoreboard, KeyCode.Tab);
            Set(GameAction.TuningPanel, KeyCode.F1);
            Set(GameAction.BindingsPanel, KeyCode.F2);
            Set(GameAction.NetGraph, KeyCode.F3);
            Set(GameAction.Menu, KeyCode.Escape);

            CrouchIsToggle = true;
            ProneIsToggle = true;
            LeanIsToggle = false;
            FreeLeanWithMouse = true;
        }

        void Set(GameAction action, KeyCode key, KeyCode modifier = KeyCode.None)
        {
            Bindings[(int)action] = new Binding(key, modifier);
        }

        // ------------------------------------------------------------------ queries
        /// <summary>True while the action's key is held (and its modifier, if it has one).</summary>
        public bool Held(GameAction action)
        {
            Binding b = this[action];
            if (!b.IsBound) return false;
            if (b.HasModifier)
                return Input.GetKey(b.Modifier) && Input.GetKey(b.Key);
            return Input.GetKey(b.Key) && !SuppressedByChord(action, b);
        }

        /// <summary>True on the frame the action was pressed.</summary>
        public bool Pressed(GameAction action)
        {
            Binding b = this[action];
            if (!b.IsBound) return false;
            if (b.HasModifier)
                return Input.GetKey(b.Modifier) && Input.GetKeyDown(b.Key);
            return Input.GetKeyDown(b.Key) && !SuppressedByChord(action, b);
        }

        public bool Released(GameAction action)
        {
            Binding b = this[action];
            if (!b.IsBound) return false;
            return Input.GetKeyUp(b.Key);
        }

        /// <summary>
        /// A plain binding stands down while a chord that uses the same key has its modifier held,
        /// so Alt+A steps sideways instead of strafing.
        /// </summary>
        bool SuppressedByChord(GameAction action, Binding binding)
        {
            for (int i = 0; i < Bindings.Length; i++)
            {
                if (i == (int)action) continue;
                Binding other = Bindings[i];
                if (!other.HasModifier || other.Key != binding.Key) continue;
                if (Input.GetKey(other.Modifier)) return true;
            }
            return false;
        }

        /// <summary>Actions sharing a key with the same modifier - shown in the rebinding panel.</summary>
        public List<GameAction> Conflicts(GameAction action)
        {
            List<GameAction> list = new List<GameAction>();
            Binding b = this[action];
            if (!b.IsBound) return list;
            for (int i = 0; i < Bindings.Length; i++)
            {
                if (i == (int)action) continue;
                Binding other = Bindings[i];
                if (other.Key == b.Key && other.Modifier == b.Modifier) list.Add((GameAction)i);
            }
            return list;
        }

        public static string Label(GameAction action)
        {
            switch (action)
            {
                case GameAction.MoveForward: return "Move forward";
                case GameAction.MoveBack: return "Move back";
                case GameAction.MoveLeft: return "Strafe left";
                case GameAction.MoveRight: return "Strafe right";
                case GameAction.WalkSlow: return "Walk (hold)";
                case GameAction.LeanLeft: return "Lean left";
                case GameAction.LeanRight: return "Lean right";
                case GameAction.LeanModifier: return "Slow / free lean modifier";
                case GameAction.StepLeft: return "Side step left";
                case GameAction.StepRight: return "Side step right";
                case GameAction.BlindFire: return "Blind fire (hold)";
                case GameAction.SpeedUp: return "Speed dial up";
                case GameAction.SpeedDown: return "Speed dial down";
                case GameAction.TuningPanel: return "Tuning panel";
                case GameAction.BindingsPanel: return "Controls panel";
                case GameAction.NetGraph: return "Net graph";
                case GameAction.Weapon1: return "Weapon 1";
                case GameAction.Weapon2: return "Weapon 2";
                case GameAction.Weapon3: return "Weapon 3";
                default: return action.ToString();
            }
        }

        // ------------------------------------------------------------------ persistence
        public void Save()
        {
            StringBuilder sb = new StringBuilder(256);
            for (int i = 0; i < Bindings.Length; i++)
                sb.Append((int)Bindings[i].Key).Append(',').Append((int)Bindings[i].Modifier).Append(';');
            sb.Append(CrouchIsToggle ? 1 : 0).Append(',');
            sb.Append(ProneIsToggle ? 1 : 0).Append(',');
            sb.Append(LeanIsToggle ? 1 : 0).Append(',');
            sb.Append(FreeLeanWithMouse ? 1 : 0);

            PlayerPrefs.SetString(PrefsKey, sb.ToString());
            PlayerPrefs.Save();
        }

        public void Load()
        {
            string raw = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(raw)) return;

            try
            {
                string[] parts = raw.Split(';');
                int count = Mathf.Min(parts.Length, Bindings.Length);
                for (int i = 0; i < count; i++)
                {
                    string[] pair = parts[i].Split(',');
                    if (pair.Length < 2) continue;
                    int key, modifier;
                    if (!int.TryParse(pair[0], out key) || !int.TryParse(pair[1], out modifier)) continue;
                    Bindings[i] = new Binding((KeyCode)key, (KeyCode)modifier);
                }

                if (parts.Length > Bindings.Length)
                {
                    string[] flags = parts[parts.Length - 1].Split(',');
                    if (flags.Length >= 4)
                    {
                        CrouchIsToggle = flags[0] == "1";
                        ProneIsToggle = flags[1] == "1";
                        LeanIsToggle = flags[2] == "1";
                        FreeLeanWithMouse = flags[3] == "1";
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[input] could not read saved bindings, using defaults: " + e.Message);
                ResetToDefaults();
            }
        }

        /// <summary>Every key the rebinding UI is allowed to capture.</summary>
        public static readonly KeyCode[] Capturable = BuildCapturable();

        static KeyCode[] BuildCapturable()
        {
            List<KeyCode> list = new List<KeyCode>(256);
            foreach (KeyCode code in Enum.GetValues(typeof(KeyCode)))
            {
                if (code == KeyCode.None) continue;
                int value = (int)code;
                // Skip joystick buttons: this build is keyboard and mouse.
                if (value >= (int)KeyCode.JoystickButton0) continue;
                list.Add(code);
            }
            return list.ToArray();
        }
    }
}
