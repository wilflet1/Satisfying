using System.Collections.Generic;
using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// The character creator.
    ///
    /// Ready Player Me's creator is a web application, and there is no browser inside a standalone
    /// Unity build to put it in - so this does the only thing that actually works outside one, and
    /// does it in as few steps as it can be done in:
    ///
    ///   1. OPEN CREATOR   launches readyplayer.me in the player's own browser
    ///   2. paste the link it gives you back into the field here
    ///   3. USE THIS ONE   downloads it, loads it, and remembers it
    ///
    /// Anything already downloaded is listed underneath and is one click to switch back to, and any
    /// .glb dropped into the avatars folder by hand shows up in the same list - so an avatar you
    /// already have never needs the network at all.
    ///
    /// The paste box takes whatever RPM hands over: a .glb link, a viewer link, or a bare avatar id.
    /// Asking a player to know the difference between those is asking them to fail.
    /// </summary>
    public sealed class CharacterPanelUI
    {
        public UiSkin Skin;
        public AvatarLibrary Library;
        public NetGame Game;

        /// <summary>Called when the player picks one, with the source string to remember.</summary>
        public System.Action<string> OnChosen;

        string _pasted = "";
        string _status = "";
        bool _busy;
        Vector2 _scroll;
        List<string> _cached = new List<string>();
        float _refreshedAt = -99f;

        public string Chosen;

        public void Draw(Rect rect)
        {
            GUILayout.BeginArea(rect, Skin.Panel);
            GUILayout.Label("CHARACTER", Skin.Header);
            GUILayout.Label("Your avatar is a Ready Player Me character. Pick one and everyone wears it; " +
                            "pick none and every player and bot is dealt one of the characters you have. " +
                            "Everyone is the same size to shoot at either way - the hitbox comes from the " +
                            "game, not the model, and F5 draws it over anyone so you can see that.",
                            Skin.SmallDim);

            GUILayout.Space(10f);

            // ---- step one
            GUILayout.Label("1.  MAKE ONE", Skin.Label);
            if (GUILayout.Button("open the Ready Player Me creator", Skin.ButtonPrimary))
            {
                Application.OpenURL(AvatarLibrary.CreatorUrl);
                _status = "the creator has opened in your browser - copy the link it gives you at the end";
            }
            GUILayout.Label("It opens in your browser. Build a character, and at the end it hands you a link.",
                            Skin.SmallDim);

            GUILayout.Space(10f);

            // ---- step two
            GUILayout.Label("2.  PASTE THE LINK", Skin.Label);
            GUILayout.BeginHorizontal();
            _pasted = GUILayout.TextField(_pasted, 200, Skin.TextField);
            if (GUILayout.Button("use this one", Skin.Button, GUILayout.Width(120f)) && !_busy) Use(_pasted);
            GUILayout.EndHorizontal();
            GUILayout.Label("A .glb link, a readyplayer.me link, or just the avatar id - any of them.",
                            Skin.SmallDim);

            if (_status.Length > 0)
            {
                GUILayout.Space(6f);
                GUILayout.Label(_status, Skin.SmallDim);
            }

            GUILayout.Space(10f);

            // ---- what is already here
            GUILayout.Label("3.  OR PICK ONE YOU ALREADY HAVE", Skin.Label);
            Refresh();

            if (_cached.Count == 0)
            {
                GUILayout.Label("Nothing downloaded yet. Anything you use is kept, and works offline " +
                                "afterwards. You can also drop .glb files straight into:", Skin.SmallDim);
                GUILayout.Label(AvatarLibrary.CacheDirectory, Skin.SmallDim);
            }
            else
            {
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(150f));
                for (int i = 0; i < _cached.Count; i++)
                {
                    bool active = _cached[i] == Chosen;
                    if (GUILayout.Button((active ? "> " : "") + AvatarLibrary.NameOf(_cached[i]),
                                         active ? Skin.ButtonPrimary : Skin.Button) && !_busy)
                        Use(_cached[i]);
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("don't pick - deal everyone a random one", Skin.Button))
            {
                Chosen = null;
                _status = _cached.Count > 0
                    ? "every player and bot will be dealt one of your " + _cached.Count + " characters"
                    : "no characters downloaded yet, so everyone gets a differently kitted blockout";
                if (OnChosen != null) OnChosen(null);
            }
            GUILayout.Label("Dealt by peer id rather than at random, so both machines agree on who is " +
                            "wearing what without sending anything.", Skin.SmallDim);

            GUILayout.EndArea();
        }

        void Refresh()
        {
            // Cheap, but not every frame: this touches the disk.
            if (Time.realtimeSinceStartup - _refreshedAt < 2f) return;
            _refreshedAt = Time.realtimeSinceStartup;
            _cached = Library.Available();
        }

        void Use(string pasted)
        {
            string source = AvatarLibrary.Normalise(pasted);
            if (source == null)
            {
                _status = "that does not look like an avatar link or an id";
                return;
            }

            _busy = true;
            _status = "loading " + AvatarLibrary.NameOf(source) + "...";

            Library.Load(source, delegate(AvatarLibrary.Entry entry)
            {
                _busy = false;
                if (entry.Error != null)
                {
                    _status = "could not load it: " + entry.Error;
                    return;
                }

                AvatarRig probe = Library.Instantiate(entry, null, 0);
                string missing = probe != null ? probe.Missing() : "everything";
                if (probe != null) probe.Destroy();

                if (missing.Length > 0)
                {
                    _status = "loaded, but it is missing bones this game needs: " + missing
                            + "- it will not pose correctly";
                    return;
                }

                Chosen = source;
                _refreshedAt = -99f;
                _status = "using " + entry.Name;
                if (OnChosen != null) OnChosen(source);
            });
        }
    }
}
