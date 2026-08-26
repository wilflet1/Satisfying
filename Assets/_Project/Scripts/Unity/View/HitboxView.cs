using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// The shape the server actually tests, drawn over the body.
    ///
    /// This is the only honest way to look at a character: the model is one thing and the hitbox is
    /// another, and every "I hit him and nothing happened" argument in a shooter is really an argument
    /// about whether those two agree. With real avatars in the game - which come in whatever
    /// proportions their maker chose - it stops being a debug view and becomes the thing you check a
    /// new character against before you ship it.
    ///
    /// Fifteen capsules, built once and moved every frame. A capsule is a cylinder and two spheres
    /// rather than Unity's capsule primitive, because that primitive is a fixed two-to-one shape and
    /// scaling it to a segment's length squashes the end caps - which would draw a hitbox that is not
    /// the hitbox.
    /// </summary>
    public sealed class HitboxView
    {
        sealed class Segment
        {
            public Transform Body;      // the cylinder between the two ends
            public Transform CapA;
            public Transform CapB;
        }

        readonly Transform _root;
        readonly Segment[] _segments = new Segment[PlayerHitbox.SegmentCount];
        readonly Material[] _materials = new Material[8];

        public HitboxView(Transform parent, int layer)
        {
            GameObject root = new GameObject("Hitboxes");
            root.transform.SetParent(parent, false);
            _root = root.transform;

            // One colour per zone, so what you are looking at is which part it would be called.
            _materials[(int)HitZone.Head] = Wire(new Color(1f, 0.25f, 0.2f));
            _materials[(int)HitZone.Neck] = Wire(new Color(1f, 0.55f, 0.15f));
            _materials[(int)HitZone.Chest] = Wire(new Color(0.35f, 0.8f, 1f));
            _materials[(int)HitZone.Stomach] = Wire(new Color(0.4f, 0.95f, 0.6f));
            _materials[(int)HitZone.Arm] = Wire(new Color(0.95f, 0.85f, 0.3f));
            _materials[(int)HitZone.Leg] = Wire(new Color(0.7f, 0.5f, 1f));
            _materials[(int)HitZone.Foot] = Wire(new Color(1f, 0.4f, 0.8f));
            _materials[(int)HitZone.None] = Wire(new Color(0.8f, 0.8f, 0.8f));

            for (int i = 0; i < _segments.Length; i++) _segments[i] = Build(layer);
            SetVisible(false);
        }

        static Material Wire(Color colour)
        {
            // Transparent and additive, so overlapping capsules read as overlapping rather than as one
            // solid lump, and so the body underneath stays visible through them.
            Material m = Palette.Make("hitbox", new Color(colour.r, colour.g, colour.b, 0.22f), 0.1f, 0f, true);
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", colour * 0.9f);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_EMISSION");
            m.renderQueue = 3200;
            return m;
        }

        Segment Build(int layer)
        {
            Segment s = new Segment();
            s.Body = Primitive(PrimitiveType.Cylinder, layer);
            s.CapA = Primitive(PrimitiveType.Sphere, layer);
            s.CapB = Primitive(PrimitiveType.Sphere, layer);
            return s;
        }

        Transform Primitive(PrimitiveType type, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.layer = layer;
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.SetParent(_root, false);
            return go.transform;
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        }

        public bool Visible { get { return _root != null && _root.gameObject.activeSelf; } }

        /// <summary>
        /// Lays the capsules over a player. The hitbox comes from PlayerHitbox.FromState - the same
        /// call the server makes to decide what you hit - so this cannot drift from the truth by being
        /// a second implementation of it. If what you see here is not on the character, the character
        /// is wrong.
        /// </summary>
        public void Render(in PlayerSimState state, MovementTuning move, WeaponTuning weapon)
        {
            PlayerHitbox box = PlayerHitbox.FromState(in state, move, weapon);

            for (int i = 0; i < PlayerHitbox.SegmentCount; i++)
            {
                Vec3 a, b;
                float radius;
                HitZone zone;
                box.Segment(i, out a, out b, out radius, out zone);

                Segment segment = _segments[i];
                Material material = _materials[(int)zone] ?? _materials[(int)HitZone.None];

                Vector3 from = a.ToUnity();
                Vector3 to = b.ToUnity();
                float diameter = radius * 2f;

                segment.CapA.position = from;
                segment.CapA.localScale = Vector3.one * diameter;
                segment.CapB.position = to;
                segment.CapB.localScale = Vector3.one * diameter;

                Vector3 delta = to - from;
                float length = delta.magnitude;
                if (length > 0.0005f)
                {
                    segment.Body.gameObject.SetActive(true);
                    segment.Body.position = from + delta * 0.5f;
                    segment.Body.rotation = Quaternion.LookRotation(delta / length) * Quaternion.Euler(90f, 0f, 0f);
                    // Unity's cylinder is two units tall, so half the length is the Y scale.
                    segment.Body.localScale = new Vector3(diameter, length * 0.5f, diameter);
                }
                else
                {
                    // A sphere segment - the head - has no cylinder.
                    segment.Body.gameObject.SetActive(false);
                }

                Paint(segment.Body, material);
                Paint(segment.CapA, material);
                Paint(segment.CapB, material);
            }
        }

        static void Paint(Transform t, Material material)
        {
            MeshRenderer renderer = t.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != material) renderer.sharedMaterial = material;
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
        }
    }
}
