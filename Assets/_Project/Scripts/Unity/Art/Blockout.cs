using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// Primitive assembly helpers. Everything visible in the game is built from these at runtime, so the
    /// project has no binary assets at all - clone it and press play.
    /// </summary>
    public static class Blockout
    {
        public static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool withCollider, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;

            Collider collider = go.GetComponent<Collider>();
            if (!withCollider && collider != null) Object.Destroy(collider);

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        public static GameObject Sphere(Transform parent, string name, Vector3 center, float diameter, Material material, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.layer = layer;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = Vector3.one * diameter;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        public static GameObject Rotated(GameObject go, Vector3 euler)
        {
            go.transform.localRotation = Quaternion.Euler(euler);
            return go;
        }

        /// <summary>
        /// A blockout duellist. Deliberately simple, but the silhouette has to read at a glance which way
        /// they are facing and which stance they are in, because that is the whole game. Arms and weapon
        /// are added by the view so they can be driven by IK.
        /// </summary>
        public sealed class Character
        {
            public GameObject Root;
            public Transform Body;       // leans and crouches
            public Transform Chest;      // shoulders and weapon hang here, pitches with the aim
            public Transform Head;
            public Transform LeftLeg;
            public Transform RightLeg;
            public Material Skin;
            public Renderer[] Renderers;
        }

        public static Character Duellist(Transform parent, string name, Palette palette, Material skin, int layer)
        {
            Character c = new Character();
            c.Skin = skin;
            c.Root = new GameObject(name);
            if (parent != null) c.Root.transform.SetParent(parent, false);
            c.Root.layer = layer;

            GameObject body = new GameObject("body");
            body.transform.SetParent(c.Root.transform, false);
            body.layer = layer;
            c.Body = body.transform;

            Box(c.Body, "torso", new Vector3(0f, 1.15f, 0f), new Vector3(0.44f, 0.62f, 0.26f), skin, false, layer);
            Box(c.Body, "chest rig", new Vector3(0f, 1.22f, 0.09f), new Vector3(0.36f, 0.34f, 0.12f), palette.WallDark, false, layer);
            Box(c.Body, "hips", new Vector3(0f, 0.82f, 0f), new Vector3(0.36f, 0.24f, 0.24f), palette.WallDark, false, layer);

            // Chest and head hang off the root, NOT the body: the body gets scaled for crouch and prone,
            // and a squashed head would stop matching the hitbox you are actually shooting at.
            GameObject chest = new GameObject("chest");
            chest.transform.SetParent(c.Root.transform, false);
            chest.transform.localPosition = new Vector3(0f, 1.34f, 0f);
            chest.layer = layer;
            c.Chest = chest.transform;

            GameObject head = new GameObject("head");
            head.transform.SetParent(c.Root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.69f, 0f);
            head.layer = layer;
            c.Head = head.transform;
            Box(c.Head, "skull", Vector3.zero, new Vector3(0.21f, 0.24f, 0.23f), skin, false, layer);
            Box(c.Head, "visor", new Vector3(0f, 0.01f, 0.115f), new Vector3(0.15f, 0.07f, 0.03f), palette.GunDark, false, layer);

            GameObject legs = new GameObject("legs");
            legs.transform.SetParent(c.Root.transform, false);
            legs.layer = layer;
            c.LeftLeg = Box(legs.transform, "left leg", new Vector3(-0.11f, 0.35f, 0f), new Vector3(0.16f, 0.72f, 0.18f), palette.WallDark, false, layer).transform;
            c.RightLeg = Box(legs.transform, "right leg", new Vector3(0.11f, 0.35f, 0f), new Vector3(0.16f, 0.72f, 0.18f), palette.WallDark, false, layer).transform;

            c.Renderers = c.Root.GetComponentsInChildren<Renderer>();
            return c;
        }
    }
}
