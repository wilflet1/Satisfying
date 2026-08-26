using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Live tuning. Every [Tune] field in the game is discovered by reflection and gets a slider here,
    /// so adding a knob to the simulation costs one field and no UI work at all.
    ///
    /// Simulation values are host-authoritative: the host edits them and they are pushed to the client,
    /// because client and server have to run identical numbers for prediction to stay quiet. Presentation
    /// values are yours alone and never leave the machine.
    /// </summary>
    public sealed class TuningPanelUI
    {
        /// <summary>
        /// Bumped whenever a default changes in a way a saved file would fight. A stored value beats
        /// a new default silently and forever, which is how the gun carried on lifting after the knob
        /// that lifted it had been set to zero - the player's own prefs were putting it back.
        /// </summary>
        const string FeelPrefsKey = "satisfying.feel.v2";

        public UiSkin Skin;
        public NetGame Game;
        public FeelTuning Feel;
        public ClientNetTuning ClientNet;
        public System.Action OnSimValueChanged;

        readonly List<TuneField> _fields = new List<TuneField>();
        readonly Dictionary<string, bool> _collapsed = new Dictionary<string, bool>();
        readonly HashSet<string> _simCategories = new HashSet<string>();
        Vector2 _scroll;
        string _filter = "";
        string _preset = "my feel";
        string _message = "";
        float _messageTime;

        public void Rebuild()
        {
            _fields.Clear();
            _simCategories.Clear();

            List<TuneField> sim = TuningSerializer.Collect(Game.Tuning);
            for (int i = 0; i < sim.Count; i++)
            {
                _fields.Add(sim[i]);
                _simCategories.Add(sim[i].Category);
            }

            _fields.AddRange(TuningSerializer.Collect(Feel));
            if (ClientNet != null) _fields.AddRange(TuningSerializer.Collect(ClientNet));
        }

        static string PresetDirectory
        {
            get { return Path.Combine(Application.persistentDataPath, "tuning"); }
        }

        public void Draw(Rect rect)
        {
            if (_fields.Count == 0) Rebuild();

            GUILayout.BeginArea(rect, Skin.Panel);
            GUILayout.Label("TUNING", Skin.Header);

            bool host = Game.IsHost;
            GUILayout.Label(host
                ? "You are the host: simulation values apply to both players."
                : "The host owns the simulation values. Yours are read only; feel values are still yours.",
                Skin.SmallDim);

            GUILayout.BeginHorizontal();
            GUILayout.Label("filter", Skin.Small, GUILayout.Width(50f));
            _filter = GUILayout.TextField(_filter, 24, Skin.TextField);
            if (GUILayout.Button("x", Skin.ButtonSmall, GUILayout.Width(26f))) _filter = "";
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll);

            string currentCategory = null;
            bool visible = true;
            bool changedSim = false;

            for (int i = 0; i < _fields.Count; i++)
            {
                TuneField field = _fields[i];
                bool matches = _filter.Length == 0 ||
                               field.Label.ToLowerInvariant().Contains(_filter.ToLowerInvariant()) ||
                               field.Category.ToLowerInvariant().Contains(_filter.ToLowerInvariant());
                if (!matches) continue;

                if (field.Category != currentCategory)
                {
                    currentCategory = field.Category;
                    bool collapsed;
                    if (!_collapsed.TryGetValue(currentCategory, out collapsed)) collapsed = false;

                    GUILayout.Space(6f);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button((collapsed ? "+ " : "- ") + currentCategory.ToUpperInvariant(), Skin.Toggle))
                    {
                        collapsed = !collapsed;
                        _collapsed[currentCategory] = collapsed;
                    }
                    bool isSim = _simCategories.Contains(currentCategory);
                    GUILayout.Label(isSim ? "sim" : "local", Skin.SmallDim, GUILayout.Width(42f));
                    GUILayout.EndHorizontal();
                    visible = !collapsed;
                }

                if (!visible) continue;

                bool editable = !_simCategories.Contains(field.Category) || host;
                float value = field.Get();

                GUILayout.BeginHorizontal();
                GUILayout.Label(field.Label, editable ? Skin.Small : Skin.SmallDim, GUILayout.Width(168f));

                float updated;
                if (editable)
                    updated = GUILayout.HorizontalSlider(value, field.Min, field.Max, Skin.Slider, Skin.SliderThumb);
                else
                {
                    GUILayout.Label("", Skin.Small);
                    updated = value;
                }

                GUILayout.Label(FormatValue(value), Skin.Value, GUILayout.Width(64f));
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(field.Tip) && _filter.Length > 0)
                    GUILayout.Label(field.Tip, Skin.SmallDim);

                if (!editable || Mathf.Approximately(updated, value)) continue;
                field.Set(updated);
                if (_simCategories.Contains(field.Category)) changedSim = true;
            }

            GUILayout.EndScrollView();

            if (changedSim && OnSimValueChanged != null) OnSimValueChanged();

            DrawPresetBar(host);
            GUILayout.EndArea();
        }

        static string FormatValue(float value)
        {
            if (Mathf.Abs(value) >= 100f) return value.ToString("0");
            if (Mathf.Abs(value) >= 10f) return value.ToString("0.0");
            return value.ToString("0.000");
        }

        void DrawPresetBar(bool host)
        {
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("preset", Skin.Small, GUILayout.Width(50f));
            _preset = GUILayout.TextField(_preset, 24, Skin.TextField);
            if (GUILayout.Button("save", Skin.ButtonSmall, GUILayout.Width(52f))) SavePreset(_preset);
            if (GUILayout.Button("load", Skin.ButtonSmall, GUILayout.Width(52f))) LoadPreset(_preset, host);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            // Everything you have moved away from the defaults, as text, on the clipboard. Easier to
            // paste somewhere than to read back off a screenshot.
            if (GUILayout.Button("copy changes", Skin.ButtonSmall))
            {
                string sim = TuningSerializer.ToTextDiff(Game.Tuning, new GameTuning());
                string feel = TuningSerializer.ToTextDiff(Feel, new FeelTuning());
                string all = (sim.Length > 0 ? "# simulation\n" + sim : "") +
                             (feel.Length > 0 ? "# feel\n" + feel : "");
                GUIUtility.systemCopyBuffer = all.Length > 0 ? all : "# nothing changed from the defaults";
                Note(all.Length > 0 ? "changed values copied" : "nothing has been changed yet");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("reset feel", Skin.ButtonSmall))
            {
                FeelTuning defaults = new FeelTuning();
                TuningSerializer.FromText(Feel, TuningSerializer.ToText(defaults));
                Note("presentation values reset");
                Rebuild();
            }
            if (host && GUILayout.Button("reset simulation", Skin.ButtonSmall))
            {
                TuningSerializer.FromText(Game.Tuning, TuningSerializer.ToText(new GameTuning()));
                if (OnSimValueChanged != null) OnSimValueChanged();
                Note("simulation values reset");
                Rebuild();
            }
            GUILayout.EndHorizontal();

            string[] presets = ListPresets();
            if (presets.Length > 0)
            {
                GUILayout.Label("saved", Skin.SmallDim);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < presets.Length && i < 6; i++)
                {
                    if (!GUILayout.Button(presets[i], Skin.ButtonSmall)) continue;
                    _preset = presets[i];
                    LoadPreset(presets[i], host);
                }
                GUILayout.EndHorizontal();
            }

            if (Time.realtimeSinceStartup - _messageTime < 3f && _message.Length > 0)
                GUILayout.Label(_message, Skin.SmallDim);
        }

        void Note(string message)
        {
            _message = message;
            _messageTime = Time.realtimeSinceStartup;
        }

        string[] ListPresets()
        {
            try
            {
                if (!Directory.Exists(PresetDirectory)) return new string[0];
                string[] files = Directory.GetFiles(PresetDirectory, "*.tuning");
                string[] names = new string[files.Length];
                for (int i = 0; i < files.Length; i++) names[i] = Path.GetFileNameWithoutExtension(files[i]);
                return names;
            }
            catch (System.Exception)
            {
                return new string[0];
            }
        }

        public void SavePreset(string name)
        {
            try
            {
                Directory.CreateDirectory(PresetDirectory);
                string text = "# satisfying tuning preset\n[game]\n" + TuningSerializer.ToText(Game.Tuning) +
                              "[feel]\n" + TuningSerializer.ToText(Feel);
                File.WriteAllText(Path.Combine(PresetDirectory, Sanitise(name) + ".tuning"), text);
                Note("saved to " + PresetDirectory);
            }
            catch (System.Exception e)
            {
                Note("could not save: " + e.Message);
            }
        }

        public void LoadPreset(string name, bool applySimulation)
        {
            try
            {
                string path = Path.Combine(PresetDirectory, Sanitise(name) + ".tuning");
                if (!File.Exists(path)) { Note("no preset called " + name); return; }

                string text = File.ReadAllText(path);
                int feelIndex = text.IndexOf("[feel]");
                string gameSection = feelIndex >= 0 ? text.Substring(0, feelIndex) : text;
                string feelSection = feelIndex >= 0 ? text.Substring(feelIndex) : "";

                if (applySimulation)
                {
                    TuningSerializer.FromText(Game.Tuning, gameSection);
                    if (OnSimValueChanged != null) OnSimValueChanged();
                }
                TuningSerializer.FromText(Feel, feelSection);
                Note(applySimulation ? "preset applied" : "preset applied (feel only - you are not the host)");
                Rebuild();
            }
            catch (System.Exception e)
            {
                Note("could not load: " + e.Message);
            }
        }

        static string Sanitise(string name)
        {
            if (string.IsNullOrEmpty(name)) return "preset";
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++) name = name.Replace(invalid[i], '_');
            return name;
        }

        /// <summary>Feel settings are per player and follow you between sessions.</summary>
        public void SaveFeelToPrefs()
        {
            PlayerPrefs.SetString(FeelPrefsKey, TuningSerializer.ToText(Feel));
            PlayerPrefs.Save();
        }

        public void LoadFeelFromPrefs()
        {
            string text = PlayerPrefs.GetString(FeelPrefsKey, "");
            if (text.Length > 0) TuningSerializer.FromText(Feel, text);
        }
    }
}
