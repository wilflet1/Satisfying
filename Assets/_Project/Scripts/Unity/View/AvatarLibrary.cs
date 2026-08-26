using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Satisfying.Game
{
    /// <summary>
    /// Where characters come from, and where they are kept once they have been got.
    ///
    /// This used to be pointed at Ready Player Me. Ready Player Me shut down, which is the argument
    /// against ever building a feature around one company's endpoint - so it is built around a FILE
    /// FORMAT now, and any of them will do:
    ///
    ///   VRM     the successor of choice. It is glTF 2.0 with a humanoid bone map bolted on, made by
    ///           the VRM Consortium as an open format rather than a product. VRoid Studio makes them
    ///           for free, VRoid Hub and Booth are full of free ones, and nobody can turn it off.
    ///   GLB     any rigged humanoid: Mixamo, Blender, an old Ready Player Me file you still have.
    ///
    /// The difference that matters is that a VRM SAYS which bone is its left upper arm, in a table
    /// the author filled in, while a plain GLB leaves it to be guessed from node names. Both work;
    /// the first is right by construction.
    ///
    /// Everything is cached to disk on first fetch, keyed by name, so a character is downloaded once
    /// and then loads instantly and works offline afterwards. Files dropped into the same folder by
    /// hand are picked up as well, which is how you use a character you already have without going
    /// near the network at all.
    /// </summary>
    public sealed class AvatarLibrary
    {
        /// <summary>
        /// A free character creator, opened in the player's browser by the character panel. VRoid
        /// Studio is a desktop application, free, and exports VRM - which is what the loader wants.
        /// </summary>
        public const string CreatorUrl = "https://vroid.com/en/studio";

        /// <summary>A library of free ready-made characters, for anyone who does not want to make one.</summary>
        public const string LibraryUrl = "https://hub.vroid.com/en/characters";

        public sealed class Entry
        {
            public string Source;           // the URL or file path it came from
            public string Name;
            public GlbModel Template;       // loaded once; every player gets a copy of it
            public string Error;
        }

        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        readonly MonoBehaviour _host;
        readonly Shader _shader;

        public AvatarLibrary(MonoBehaviour host, Shader shader)
        {
            _host = host;
            _shader = shader;
            Directory.CreateDirectory(CacheDirectory);
        }

        public static string CacheDirectory
        {
            get { return Path.Combine(Application.persistentDataPath, "avatars"); }
        }

        /// <summary>Anything already downloaded, plus any .glb dropped into the folder by hand.</summary>
        public List<string> Available()
        {
            List<string> found = new List<string>();
            try
            {
                // Both extensions: a VRM is a GLB with a bone table in it, and the loader reads either.
                string[] glb = Directory.GetFiles(CacheDirectory, "*.glb");
                for (int i = 0; i < glb.Length; i++) found.Add(glb[i]);
                string[] vrm = Directory.GetFiles(CacheDirectory, "*.vrm");
                for (int i = 0; i < vrm.Length; i++) found.Add(vrm[i]);
            }
            catch (Exception) { }
            return found;
        }

        public Entry Get(string source)
        {
            Entry entry;
            return _entries.TryGetValue(source, out entry) ? entry : null;
        }

        /// <summary>
        /// Loads one, from the cache if it is there and from the network if it is not. The callback
        /// fires either way, with Error set when it did not work - a character that silently fails to
        /// appear is worse than one that says why.
        /// </summary>
        public void Load(string source, Action<Entry> done)
        {
            Entry existing;
            if (_entries.TryGetValue(source, out existing) && existing.Template != null)
            {
                if (done != null) done(existing);
                return;
            }
            _host.StartCoroutine(LoadRoutine(source, done));
        }

        IEnumerator LoadRoutine(string source, Action<Entry> done)
        {
            Entry entry = new Entry();
            entry.Source = source;
            entry.Name = NameOf(source);
            _entries[source] = entry;

            byte[] bytes = null;

            bool isUrl = source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            if (!isUrl)
            {
                try { bytes = File.ReadAllBytes(source); }
                catch (Exception e) { entry.Error = "could not read the file: " + e.Message; }
            }
            else
            {
                string cached = CachePathFor(source);
                if (File.Exists(cached))
                {
                    try { bytes = File.ReadAllBytes(cached); }
                    catch (Exception) { bytes = null; }
                }

                if (bytes == null)
                {
                    using (UnityWebRequest request = UnityWebRequest.Get(source))
                    {
                        request.timeout = 30;
                        yield return request.SendWebRequest();

                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            entry.Error = "download failed: " + request.error;
                        }
                        else
                        {
                            bytes = request.downloadHandler.data;
                            try { File.WriteAllBytes(cached, bytes); }
                            catch (Exception) { }        // a cache we cannot write is not fatal
                        }
                    }
                }
            }

            if (bytes != null && entry.Error == null)
            {
                string error;
                GlbModel model = GlbLoader.Load(bytes, entry.Name, _shader, out error);
                if (model == null) entry.Error = error;
                else
                {
                    // The template is kept inactive off to one side; players are given copies.
                    model.Root.SetActive(false);
                    entry.Template = model;
                }
            }

            if (done != null) done(entry);
        }

        /// <summary>A copy of a loaded avatar, rigged and ready to be posed.</summary>
        public AvatarRig Instantiate(Entry entry, Transform parent, int layer)
        {
            if (entry == null || entry.Template == null) return null;

            GameObject copy = UnityEngine.Object.Instantiate(entry.Template.Root, parent);
            copy.name = entry.Name;
            copy.SetActive(true);
            SetLayer(copy.transform, layer);

            // Rebuild the bone table against the copy rather than the template, or every player would
            // be driving the same skeleton.
            GlbModel model = new GlbModel();
            model.Root = copy;
            model.Flavour = entry.Template.Flavour;
            Collect(copy.transform, model);

            // And carry the DECLARED humanoid map across, by node name. Without this every copy fell
            // back to guessing bones from names - which happens to work for VRoid and would quietly
            // stop working for the first file whose author named things differently, which is the
            // entire problem the map exists to solve.
            foreach (KeyValuePair<string, Transform> kv in entry.Template.Humanoid)
            {
                if (kv.Value == null) continue;
                Transform mine = model.Find(kv.Value.name);
                if (mine != null) model.Humanoid[kv.Key] = mine;
            }
            copy.GetComponentsInChildren(true, model.Skins);

            return new AvatarRig(model);
        }

        static void Collect(Transform t, GlbModel model)
        {
            if (!model.Bones.ContainsKey(t.name)) model.Bones[t.name] = t;
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), model);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        // ------------------------------------------------------------------ naming

        public static string NameOf(string source)
        {
            if (string.IsNullOrEmpty(source)) return "avatar";
            string name = source;

            int query = name.IndexOf('?');
            if (query > 0) name = name.Substring(0, query);

            int slash = name.LastIndexOfAny(new[] { '/', '\\' });
            if (slash >= 0 && slash + 1 < name.Length) name = name.Substring(slash + 1);

            if (name.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name.Length == 0 ? "avatar" : name;
        }

        static string CachePathFor(string url)
        {
            bool vrm = url.IndexOf(".vrm", StringComparison.OrdinalIgnoreCase) >= 0;
            return Path.Combine(CacheDirectory, NameOf(url) + (vrm ? ".vrm" : ".glb"));
        }

        /// <summary>
        /// Turns whatever someone pasted into something loadable: a direct link to a .vrm or .glb, or
        /// a path to one on disk. Anything else is refused with a straight answer rather than being
        /// downloaded and found to be a web page.
        /// </summary>
        public static string Normalise(string pasted)
        {
            if (string.IsNullOrEmpty(pasted)) return null;
            string text = pasted.Trim().Trim('"');

            if (File.Exists(text)) return text;

            bool model = text.IndexOf(".vrm", StringComparison.OrdinalIgnoreCase) >= 0
                      || text.IndexOf(".glb", StringComparison.OrdinalIgnoreCase) >= 0;

            if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase) && model) return text;
            return null;
        }
    }
}
