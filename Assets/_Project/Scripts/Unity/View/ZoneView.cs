using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// The hill, made visible from across the map.
    ///
    /// The first version of king of the hill said which room it was in a line of text under the score,
    /// and it was possible to play a whole match without noticing the mode was on. A room you are
    /// meant to fight over has to be a thing in the world, not a caption - so the live room gets a
    /// floor plate, a cage of edges, and a column of light standing in it that you can see through a
    /// window from the far side of the plot.
    ///
    /// It is all generated primitives on the FX layer with no colliders, so it costs nothing and you
    /// can walk straight through it. The colour is the state: open, yours, theirs, contested.
    /// </summary>
    public sealed class ZoneView
    {
        readonly Transform _root;
        readonly Palette _palette;

        readonly GameObject _plate;
        readonly GameObject _column;
        readonly GameObject[] _posts = new GameObject[4];
        readonly Material _material;

        Box _bounds;
        bool _hasBounds;

        public ZoneView(Transform parent, Palette palette, int layer)
        {
            _palette = palette;

            GameObject root = new GameObject("Hill");
            root.transform.SetParent(parent, false);
            _root = root.transform;

            // One material, tinted per frame. Every piece shares it, so the whole marker changes
            // colour in one assignment when the room changes hands.
            _material = Palette.Make("hill", new Color(1f, 1f, 1f, 0.16f), 0.4f, 0f, true);
            if (_material == null) return;
            Transparent(_material);

            _plate = Piece("plate", layer);
            _column = Piece("column", layer);
            for (int i = 0; i < _posts.Length; i++) _posts[i] = Piece("post " + i, layer);

            SetVisible(false);
        }

        GameObject Piece(string name, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = _material;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.SetParent(_root, false);
            return go;
        }

        static void Transparent(Material m)
        {
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_EMISSION");
            m.renderQueue = 3100;
        }

        public void SetVisible(bool visible)
        {
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        }

        /// <summary>Lays the marker over a room. Only done when the hill moves.</summary>
        public void SetZone(in ZoneDef zone)
        {
            _bounds = zone.Bounds;
            _hasBounds = true;

            Vector3 centre = _bounds.Center.ToUnity();
            Vector3 size = _bounds.Size.ToUnity();
            float floor = centre.y - size.y * 0.5f;

            // A plate just off the floor, so it reads as marking the ground rather than floating.
            _plate.transform.position = new Vector3(centre.x, floor + 0.02f, centre.z);
            _plate.transform.localScale = new Vector3(size.x, 0.04f, size.z);

            // A column of light up through the ceiling and well past it - this is the part you see
            // from outside the building.
            _column.transform.position = new Vector3(centre.x, floor + 9f, centre.z);
            _column.transform.localScale = new Vector3(size.x * 0.32f, 18f, size.z * 0.32f);

            // Corner posts, so it reads as a volume with edges from inside the room.
            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 3) ? -0.5f : 0.5f;
                float sz = (i < 2) ? -0.5f : 0.5f;
                _posts[i].transform.position = new Vector3(centre.x + size.x * sx, floor + size.y * 0.5f,
                                                           centre.z + size.z * sz);
                _posts[i].transform.localScale = new Vector3(0.09f, size.y, 0.09f);
            }
        }

        /// <summary>
        /// Colour and pulse. It breathes slowly while nobody is in it and fast while it is contested,
        /// which is a thing you can catch out of the corner of your eye.
        /// </summary>
        public void Render(int holder, int localPeer, float secondsLeft, float warnSeconds)
        {
            if (!_hasBounds) return;

            Color colour;
            float rate;
            if (holder == KothState.Contested) { colour = new Color(1f, 0.72f, 0.16f); rate = 7f; }
            else if (holder == localPeer && holder >= 0) { colour = new Color(0.35f, 0.95f, 0.5f); rate = 1.6f; }
            else if (holder >= 0) { colour = new Color(0.95f, 0.3f, 0.26f); rate = 4f; }
            else { colour = new Color(0.55f, 0.72f, 1f); rate = 1.1f; }

            // And it flashes as the room is about to move, so leaving in time is possible.
            if (secondsLeft <= warnSeconds) rate = 10f;

            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * rate);
            Color tint = new Color(colour.r, colour.g, colour.b, Mathf.Lerp(0.05f, 0.16f, pulse));

            if (_material.HasProperty("_Color")) _material.SetColor("_Color", tint);
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", tint);
            if (_material.HasProperty("_EmissionColor"))
                _material.SetColor("_EmissionColor", colour * Mathf.Lerp(0.5f, 2.2f, pulse));
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
        }
    }
}
