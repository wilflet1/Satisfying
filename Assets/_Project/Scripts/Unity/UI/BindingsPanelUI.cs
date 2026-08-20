using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Rebinding, including chords. Press the button then hit the key you want; hold a modifier while
    /// you press it to make it a chord like Alt+A.
    /// </summary>
    public sealed class BindingsPanelUI
    {
        public UiSkin Skin;
        public InputBindings Bindings;
        public FeelTuning Feel;

        int _capturing = -1;
        Vector2 _scroll;
        string _message = "";

        public bool Capturing { get { return _capturing >= 0; } }

        public void Draw(Rect rect)
        {
            GUILayout.BeginArea(rect, Skin.Panel);
            GUILayout.Label("CONTROLS", Skin.Header);
            GUILayout.Label("Click a binding, then press the key. Hold Alt (or Ctrl/Shift) while pressing to make a chord.", Skin.SmallDim);

            if (_capturing >= 0)
            {
                GUILayout.Label("listening for a key... Escape cancels", Skin.Value);
                CaptureKey();
            }
            else if (_message.Length > 0)
            {
                GUILayout.Label(_message, Skin.SmallDim);
            }

            _scroll = GUILayout.BeginScrollView(_scroll);

            for (int i = 0; i < (int)GameAction.Count; i++)
            {
                GameAction action = (GameAction)i;
                Binding binding = Bindings[action];

                GUILayout.BeginHorizontal();
                GUILayout.Label(InputBindings.Label(action), Skin.Small, GUILayout.Width(210f));

                bool conflicted = Bindings.Conflicts(action).Count > 0;
                GUIStyle style = conflicted ? Skin.ButtonSmall : Skin.Button;
                Color previous = GUI.color;
                if (conflicted) GUI.color = new Color(1f, 0.6f, 0.55f);
                if (GUILayout.Button(binding.ToString(), style, GUILayout.Width(150f)))
                {
                    _capturing = i;
                    _message = "";
                }
                GUI.color = previous;

                if (GUILayout.Button("clear", Skin.ButtonSmall, GUILayout.Width(52f)))
                    Bindings[action] = new Binding(KeyCode.None);

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10f);
            GUILayout.Label("BEHAVIOUR", Skin.Header);
            Bindings.CrouchIsToggle = ToggleRow("crouch is a toggle", Bindings.CrouchIsToggle);
            Bindings.ProneIsToggle = ToggleRow("prone is a toggle", Bindings.ProneIsToggle);
            Bindings.LeanIsToggle = ToggleRow("lean is a toggle", Bindings.LeanIsToggle);
            Bindings.FreeLeanWithMouse = ToggleRow("modifier + lean key + mouse = free lean", Bindings.FreeLeanWithMouse);

            GUILayout.Space(8f);
            GUILayout.Label("MOUSE", Skin.Header);
            Feel.sensitivity = SliderRow("sensitivity", Feel.sensitivity, 0.02f, 1.2f);
            Feel.adsSensitivityMul = SliderRow("aim multiplier", Feel.adsSensitivityMul, 0.1f, 2f);
            Feel.smoothing = SliderRow("smoothing (adds latency)", Feel.smoothing, 0f, 1f);
            bool invert = Feel.invertY >= 0.5f;
            invert = ToggleRow("invert vertical", invert);
            Feel.invertY = invert ? 1f : 0f;

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("reset to defaults", Skin.ButtonSmall))
            {
                Bindings.ResetToDefaults();
                Bindings.Save();
                _message = "defaults restored";
            }
            if (GUILayout.Button("save", Skin.ButtonSmall))
            {
                Bindings.Save();
                _message = "controls saved";
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        bool ToggleRow(string label, bool value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Skin.Small, GUILayout.Width(260f));
            if (GUILayout.Button(value ? "on" : "off", Skin.ButtonSmall, GUILayout.Width(60f)))
            {
                value = !value;
                Bindings.Save();
            }
            GUILayout.EndHorizontal();
            return value;
        }

        float SliderRow(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Skin.Small, GUILayout.Width(210f));
            float result = GUILayout.HorizontalSlider(value, min, max, Skin.Slider, Skin.SliderThumb);
            GUILayout.Label(result.ToString("0.000"), Skin.Value, GUILayout.Width(60f));
            GUILayout.EndHorizontal();
            return result;
        }

        void CaptureKey()
        {
            Event e = Event.current;
            if (e == null) return;

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    _capturing = -1;
                    _message = "cancelled";
                    e.Use();
                    return;
                }
                if (e.keyCode != KeyCode.None)
                {
                    Assign(e.keyCode);
                    e.Use();
                }
                return;
            }

            if (e.type == EventType.MouseDown)
            {
                Assign(KeyCode.Mouse0 + Mathf.Clamp(e.button, 0, 6));
                e.Use();
            }
        }

        void Assign(KeyCode key)
        {
            KeyCode modifier = KeyCode.None;
            if (!IsModifier(key))
            {
                if (Input.GetKey(KeyCode.LeftAlt)) modifier = KeyCode.LeftAlt;
                else if (Input.GetKey(KeyCode.RightAlt)) modifier = KeyCode.RightAlt;
                else if (Input.GetKey(KeyCode.LeftControl)) modifier = KeyCode.LeftControl;
                else if (Input.GetKey(KeyCode.RightControl)) modifier = KeyCode.RightControl;
                else if (Input.GetKey(KeyCode.LeftShift)) modifier = KeyCode.LeftShift;
                else if (Input.GetKey(KeyCode.RightShift)) modifier = KeyCode.RightShift;
            }

            GameAction action = (GameAction)_capturing;
            Bindings[action] = new Binding(key, modifier);
            Bindings.Save();
            _message = InputBindings.Label(action) + " -> " + Bindings[action];
            _capturing = -1;
        }

        static bool IsModifier(KeyCode key)
        {
            return key == KeyCode.LeftAlt || key == KeyCode.RightAlt ||
                   key == KeyCode.LeftControl || key == KeyCode.RightControl ||
                   key == KeyCode.LeftShift || key == KeyCode.RightShift;
        }
    }
}
