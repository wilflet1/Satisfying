using System.Collections.Generic;
using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// The character panel.
    ///
    /// It used to be built around Ready Player Me, which has since shut down - so it is built around
    /// a file format instead. VRM is glTF with a humanoid bone table in it, made by a consortium
    /// rather than a company; VRoid Studio makes them for free, VRoid Hub is full of free ones, and
    /// no API key is involved at any point. Plain rigged .glb works too, from Mixamo or Blender or
    /// anywhere else.
    ///
    ///   MAKE ONE        opens VRoid Studio's page in the browser
    ///   BROWSE FREE     opens VRoid Hub, for anyone who would rather download than model
    ///   paste a link    to a .vrm or .glb, or a path to one on this machine
    ///
    /// Anything loaded is kept on disk and listed underneath, and files dropped into the folder by
    /// hand appear in the same list - so a character you already have never touches the network.
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
            GUILayout.Label("Characters are VRM or glTF files - the open formats, not a service. Pick one " +
                            "and everyone wears it; pick none and every player and bot is dealt one of the " +
                            "characters you have. Everyone is the same size to shoot at either way: the " +
                            "hitbox comes from the game, not the model, and F5 draws it over anyone so you " +
                            "can see that for yourself.", Skin.SmallDim);

            GUILayout.Space(10f);

            // ---- step one
            GUILayout.Label("1.  GET A CHARACTER", Skin.Label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("make one (VRoid Studio)", Skin.ButtonPrimary))
            {
                Application.OpenURL(AvatarLibrary.CreatorUrl);
                _status = "VRoid Studio is free. Make a character, export it as .vrm, then load the file below.";
            }
            if (GUILayout.Button("browse free ones (VRoid Hub)", Skin.Button))
            {
                Application.OpenURL(AvatarLibrary.LibraryUrl);
                _status = "download any .vrm you like, then load the file below.";
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Both open in your browser. A .vrm is best - it tells the game which bone is " +
                            "which - but any rigged .glb works.", Skin.SmallDim);

            GUILayout.Space(10f);

            // ---- step two
            GUILayout.Label("2.  LOAD IT", Skin.Label);
            GUILayout.BeginHorizontal();
            _pasted = GUILayout.TextField(_pasted, 200, Skin.TextField);
            if (GUILayout.Button("use this one", Skin.Button, GUILayout.Width(120f)) && !_busy) Use(_pasted);
            GUILayout.EndHorizontal();
            GUILayout.Label("A link to a .vrm or .glb, or the path to one on this machine. Load as many " +
                            "as you like, one after another - each is kept. Do ten, press the button at " +
                            "the bottom, and every player and bot is dealt one of them.", Skin.SmallDim);

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
                GUILayout.Label("Nothing here yet. Anything you load is kept and works offline afterwards. " +
                                "You can also drop .vrm or .glb files straight into:", Skin.SmallDim);
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
                _status = "that is not a .vrm or .glb link, and there is no file there either";
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
