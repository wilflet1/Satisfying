using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Fit an optic to each weapon. The choice rides in the input stream, so the server applies the
    /// same aim time and cone of fire the client predicted - an attachment is a simulation value, not
    /// a decoration.
    /// </summary>
    public sealed class GearPanelUI
    {
        public UiSkin Skin;
        public NetGame Game;
        public IPlayerInput Input;

        const string PrefsKey = "satisfying.loadout.v1";
        Vector2 _scroll;

        public void Load()
        {
            string raw = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(raw)) return;
            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length && i < Input.Sights.Length; i++)
            {
                int value;
                if (int.TryParse(parts[i], out value)) Input.Sights[i] = (byte)Mathf.Clamp(value, 0, 2);
            }
        }

        public void Save()
        {
            string raw = "";
            for (int i = 0; i < Input.Sights.Length; i++) raw += (i > 0 ? "," : "") + Input.Sights[i];
            PlayerPrefs.SetString(PrefsKey, raw);
            PlayerPrefs.Save();
        }

        public void Draw(Rect rect)
        {
            GUILayout.BeginArea(rect, Skin.Panel);
            GUILayout.Label("GEAR", Skin.Header);
            GUILayout.Label("An optic trades a little time getting on target for a clearer sight picture.", Skin.SmallDim);

            _scroll = GUILayout.BeginScrollView(_scroll);

            GameTuning tuning = Game.Tuning;
            for (int weapon = 0; weapon < Input.Sights.Length; weapon++)
            {
                WeaponTuning w = tuning.Weapon(weapon);
                GUILayout.Space(8f);

                bool held = Input.CurrentWeapon == weapon;
                GUILayout.Label((held ? "> " : "") + w.name.ToUpperInvariant() + "   (key " + (weapon + 1) + ")",
                    held ? Skin.Value : Skin.Label);

                GUILayout.BeginHorizontal();
                for (int sight = 0; sight < 3; sight++)
                {
                    SightTuning s = tuning.Sight(sight);
                    bool active = Input.Sights[weapon] == sight;
                    if (GUILayout.Button((active ? "> " : "") + s.name, active ? Skin.ButtonPrimary : Skin.Button))
                    {
                        Input.Sights[weapon] = (byte)sight;
                        Save();
                    }
                }
                GUILayout.EndHorizontal();

                SightTuning fitted = tuning.Sight(Input.Sights[weapon]);
                float adsMs = w.adsTime * fitted.adsTimeMul * 1000f;
                GUILayout.Label(string.Format("   aim in {0:0} ms    aimed spread x{1:0.00}    zoom x{2:0.00}",
                    adsMs, w.spreadAdsMul * fitted.spreadMul, 1f / Mathf.Max(0.01f, fitted.zoomMul)), Skin.SmallDim);
            }

            GUILayout.Space(10f);
            GUILayout.Label("Iron sights are a notch and a post: line the post up in the gap. " +
                            "The red dot and the holo put a reticle on the glass instead.", Skin.SmallDim);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>The reticle an optic paints, drawn at the screen centre because that is where it points.</summary>
        public static void DrawReticle(UiSkin skin, SightKind kind, float centreX, float centreY, float blend)
        {
            if (blend <= 0.01f) return;

            Color hot = new Color(1f, 0.22f, 0.16f, blend);
            Color soft = new Color(1f, 0.35f, 0.25f, blend * 0.35f);

            if (kind == SightKind.RedDot)
            {
                skin.Fill(new Rect(centreX - 8f, centreY - 8f, 16f, 16f), soft * 0.5f);
                skin.Fill(new Rect(centreX - 2.5f, centreY - 2.5f, 5f, 5f), hot);
                return;
            }

            if (kind != SightKind.Holo) return;

            // Ring, centre dot and four ticks - the classic holographic picture.
            skin.Ring(new Vector2(centreX, centreY), 26f, 2f, hot, 40);
            skin.Fill(new Rect(centreX - 2f, centreY - 2f, 4f, 4f), hot);
            skin.Fill(new Rect(centreX - 1f, centreY - 34f, 2f, 8f), hot);
            skin.Fill(new Rect(centreX - 1f, centreY + 26f, 2f, 8f), hot);
            skin.Fill(new Rect(centreX - 34f, centreY - 1f, 8f, 2f), hot);
            skin.Fill(new Rect(centreX + 26f, centreY - 1f, 8f, 2f), hot);
            skin.Fill(new Rect(centreX - 30f, centreY - 30f, 60f, 60f), soft * 0.25f);
        }
    }
}
