using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Satisfying.Game
{
    /// <summary>
    /// Where avatars come from, and where they are kept once they have been got.
    ///
    /// Ready Player Me serves an avatar as a GLB at a URL you get from its creator. There is no way to
    /// embed that creator in a standalone build - it is a web application - so the flow is the one
    /// every game using RPM outside a browser uses: the creator opens in your browser, you make a
    /// character, RPM hands you a .glb link, and you paste it in. GlbLoader does the rest.
    ///
    /// Everything is cached to disk on first fetch, keyed by URL, so a character is downloaded once
    /// and then loads instantly and works offline afterwards. Local .glb files in the same folder are
    /// picked up too, which is how you use an avatar you already have without going near the network.
    /// </summary>
    public sealed class AvatarLibrary
    {
        /// <summary>The RPM creator. Opened in the player's browser by the character panel.</summary>
        public const string CreatorUrl = "https://readyplayer.me/avatar?frameApi";

        /// <summary>What a finished avatar URL looks like, for the panel to check against.</summary>
        public const string ModelHost = "models.readyplayer.me";

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
                string[] files = Directory.GetFiles(CacheDirectory, "*.glb");
                for (int i = 0; i < files.Length; i++) found.Add(files[i]);
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
                    // RPM lets the quality be asked for in the URL. A duellist is never closer than a
                    // couple of metres and there can be six of them, so the low LOD with a single
                    // atlas is the right trade - it is about a fifth of the download and looks the
                    // same at the range anyone sees it.
                    string url = WithQuality(source);
                    using (UnityWebRequest request = UnityWebRequest.Get(url))
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
            Collect(copy.transform, model);
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

            if (name.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name.Length == 0 ? "avatar" : name;
        }

        static string CachePathFor(string url)
        {
            return Path.Combine(CacheDirectory, NameOf(url) + ".glb");
        }

        static string WithQuality(string url)
        {
            if (url.IndexOf("meshLod", StringComparison.OrdinalIgnoreCase) >= 0) return url;
            string join = url.IndexOf('?') >= 0 ? "&" : "?";
            return url + join + "meshLod=1&textureAtlas=1024&textureSizeLimit=1024&morphTargets=none";
        }

        /// <summary>
        /// Turns whatever someone pasted into a .glb URL. RPM hands out several shapes of link - the
        /// model URL, the viewer URL, and a bare id - and asking a player to know the difference is
        /// asking them to fail.
        /// </summary>
        public static string Normalise(string pasted)
        {
            if (string.IsNullOrEmpty(pasted)) return null;
            string text = pasted.Trim();

            if (File.Exists(text)) return text;

            if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                if (text.IndexOf(".glb", StringComparison.OrdinalIgnoreCase) >= 0) return text;
                // A viewer link: the id is the last path segment.
                string id = NameOf(text);
                return "https://" + ModelHost + "/" + id + ".glb";
            }

            // A bare avatar id.
            if (text.Length >= 20 && text.IndexOf(' ') < 0)
                return "https://" + ModelHost + "/" + text + ".glb";

            return null;
        }
    }
}
