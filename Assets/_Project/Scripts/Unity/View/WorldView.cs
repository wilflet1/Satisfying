using System.Collections.Generic;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// The changeable world made visible: panes of glass that stop bullets until they do not, and
    /// objects heavy enough to be worth dragging. Shapes come from the shared WorldModel, which both
    /// machines build identically; only the state is replicated.
    /// </summary>
    public sealed class WorldView
    {
        sealed class WindowView
        {
            public GameObject Glass;
            public Collider Barrier;
            public Vector3 Centre;
            public Vector3 Size;
            public bool Broken;
        }

        sealed class PropView
        {
            public GameObject Root;
            public Transform GrabPoint;
            public Vector3 Size;
            public float Mass;
        }

        readonly List<WindowView> _windows = new List<WindowView>();
        readonly List<PropView> _props = new List<PropView>();
        Transform _root;
        Palette _palette;
        Material _glassMaterial;
        CombatFx _fx;
        SoundPlayer _sound;
        AudioBank _audio;

        public int WindowCount { get { return _windows.Count; } }
        public int PropCount { get { return _props.Count; } }

        public void Build(WorldModel model, Palette palette, int worldLayer, Transform parent,
                          CombatFx fx, SoundPlayer sound, AudioBank audio)
        {
            Clear();
            _palette = palette;
            _fx = fx;
            _sound = sound;
            _audio = audio;

            GameObject root = new GameObject("World Objects");
            root.transform.SetParent(parent, false);
            _root = root.transform;

            if (_glassMaterial == null) _glassMaterial = MakeGlass();

            for (int i = 0; i < model.Windows.Count; i++)
            {
                Box bounds = model.Windows[i].Bounds;
                WindowView view = new WindowView();
                view.Centre = bounds.Center.ToUnity();
                view.Size = bounds.Size.ToUnity();
                view.Glass = Blockout.Box(_root, "glass " + i, view.Centre, view.Size, _glassMaterial, true, worldLayer);
                MeshRenderer glassRenderer = view.Glass.GetComponent<MeshRenderer>();
                if (glassRenderer != null)
                    glassRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                view.Barrier = view.Glass.GetComponent<Collider>();
                _windows.Add(view);
            }

            for (int i = 0; i < model.Props.Count; i++)
            {
                PropDef def = model.Props[i];
                PropView view = new PropView();
                view.Size = def.Size.ToUnity();
                view.Mass = def.Mass;

                // Heavier reads darker and more metallic, so weight is legible before you touch it.
                float heavy = Mathf.Clamp01(def.Mass / 160f);
                Material material = Palette.Make("prop " + i,
                    Color.Lerp(new Color(0.72f, 0.55f, 0.33f), new Color(0.34f, 0.36f, 0.40f), heavy),
                    Mathf.Lerp(0.15f, 0.5f, heavy), Mathf.Lerp(0f, 0.6f, heavy));

                GameObject go = new GameObject("prop " + i);
                go.transform.SetParent(_root, false);
                go.layer = worldLayer;
                Blockout.Box(go.transform, "body", new Vector3(0f, view.Size.y * 0.5f, 0f), view.Size, material, true, worldLayer);

                // A strip of banding so you can see it turn as you drag it.
                Blockout.Box(go.transform, "band", new Vector3(0f, view.Size.y * 0.5f, 0f),
                    new Vector3(view.Size.x * 1.02f, view.Size.y * 0.16f, view.Size.z * 1.02f),
                    heavy > 0.5f ? palette.Metal : palette.GunDark, false, worldLayer);

                GameObject grab = new GameObject("grab point");
                grab.transform.SetParent(go.transform, false);
                grab.transform.localPosition = new Vector3(0f, view.Size.y * 0.62f, -view.Size.z * 0.5f);
                view.GrabPoint = grab.transform;

                view.Root = go;
                _props.Add(view);
            }
        }

        static Material MakeGlass()
        {
            // Barely there. A pane you cannot see through is a wall you can shoot, which is the worst
            // of both - the point of glass is that it shows you the room and then stops being there.
            // The tint is cool and very slightly green, which is what float glass actually looks like
            // edge on, and the smoothness is what gives it a highlight to catch.
            Material m = Palette.Make("glass", new Color(0.70f, 0.84f, 0.88f, 0.10f), 0.97f, 0.05f);
            // The Standard shader needs telling that it is transparent; it is opaque by default.
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);      // URP, if anyone switches
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;

            // A pane that casts a solid black shadow gives itself away from across the map and looks
            // like a sheet of steel from outside. It receives shadows; it does not throw one.
            return m;
        }

        public void Clear()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
            _windows.Clear();
            _props.Clear();
        }

        /// <summary>Pushes the replicated state onto the scene: broken glass disappears, props move.</summary>
        public void Apply(WorldState state, float dt)
        {
            if (state == null) return;

            for (int i = 0; i < _windows.Count; i++)
            {
                bool broken = state.IsBroken(i);
                if (broken == _windows[i].Broken) continue;
                SetBroken(i, broken);
            }

            for (int i = 0; i < _props.Count && i < state.Props.Length; i++)
            {
                PropView view = _props[i];
                if (view.Root == null) continue;

                Vector3 target = state.Props[i].Position.ToUnity();
                Quaternion rotation = Quaternion.Euler(0f, state.Props[i].Yaw, 0f);

                // Snap when it is far out (a correction or a round reset), glide otherwise.
                float distance = Vector3.Distance(view.Root.transform.position, target);
                if (distance > 2.5f)
                {
                    view.Root.transform.position = target;
                    view.Root.transform.rotation = rotation;
                }
                else
                {
                    float k = 1f - Mathf.Exp(-18f * dt);
                    view.Root.transform.position = Vector3.Lerp(view.Root.transform.position, target, k);
                    view.Root.transform.rotation = Quaternion.Slerp(view.Root.transform.rotation, rotation, k);
                }
            }
        }

        void SetBroken(int index, bool broken)
        {
            WindowView view = _windows[index];
            view.Broken = broken;

            if (view.Glass != null) view.Glass.SetActive(!broken);
            if (view.Barrier != null) view.Barrier.enabled = !broken;

            if (!broken) return;

            if (_fx != null) _fx.Shatter(view.Centre, view.Size);
            if (_sound != null && _audio != null) _sound.PlayAt(_audio.GlassBreak, view.Centre, 0.9f);
        }

        public Vector3 GrabPoint(int propIndex)
        {
            if (propIndex < 0 || propIndex >= _props.Count || _props[propIndex].GrabPoint == null) return Vector3.zero;
            return _props[propIndex].GrabPoint.position;
        }

        public float PropMass(int propIndex)
        {
            if (propIndex < 0 || propIndex >= _props.Count) return 0f;
            return _props[propIndex].Mass;
        }

        public Vector3 PropCentre(int propIndex)
        {
            if (propIndex < 0 || propIndex >= _props.Count || _props[propIndex].Root == null) return Vector3.zero;
            return _props[propIndex].Root.transform.position + Vector3.up * (_props[propIndex].Size.y * 0.5f);
        }
    }
}
