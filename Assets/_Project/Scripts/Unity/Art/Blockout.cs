using UnityEngine;
using Satisfying.Shared;

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

        public static GameObject Group(Transform parent, string name, Vector3 position, int layer)
        {
            GameObject go = new GameObject(name);
            go.layer = layer;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go;
        }

        /// <summary>
        /// One limb: a joint you place and aim, and a mesh sized to span it.
        ///
        /// The mesh is a CHILD of the joint and nothing else ever touches its scale. That is not a style
        /// preference - the old code posed the body by writing localScale straight onto the leg boxes,
        /// which are the same transforms whose scale IS their size, so a crouch turned each leg into a
        /// one-metre slab and going prone left you standing inside one. It is why you could not see
        /// anything down the sights lying down.
        /// </summary>
        public sealed class Bone
        {
            public Transform Joint;
            public Transform Mesh;
            public float Width = 0.1f;
            public float Depth = 0.1f;
            public float Fill = 0.94f;      // mesh is a shade shorter than the bone, so joints read as joints

            public static Bone Build(Transform parent, string name, float width, float depth, Material material, int layer)
            {
                Bone b = new Bone();
                b.Width = width;
                b.Depth = depth;
                b.Joint = Group(parent, name, Vector3.zero, layer).transform;
                b.Mesh = Box(b.Joint, name + " mesh", Vector3.zero, Vector3.one, material, false, layer).transform;
                return b;
            }

            /// <summary>Both ends, in the parent's space. Everything else follows from them.</summary>
            public void Set(Vector3 a, Vector3 b)
            {
                Vector3 delta = b - a;
                float length = delta.magnitude;
                Joint.localPosition = a;

                if (length > 0.0005f)
                {
                    // Roll is pinned to the body's own right, not to a world axis. The obvious version -
                    // LookRotation(dir, Vector3.up) with a fallback when the bone is vertical - snaps the
                    // roll ninety degrees the instant a bone crosses out of vertical, which for a torso
                    // 0.395 m wide and 0.255 m deep is the chest visibly turning sideways as you go prone.
                    Vector3 dir = delta / length;
                    Vector3 right = Vector3.Cross(Vector3.up, dir);
                    right = right.sqrMagnitude < 1e-6f ? Vector3.right : right.normalized;
                    Joint.localRotation = Quaternion.LookRotation(dir, Vector3.Cross(dir, right));
                }

                Mesh.localPosition = new Vector3(0f, 0f, length * 0.5f);
                Mesh.localScale = new Vector3(Width, Depth, Mathf.Max(0.02f, length * Fill));
            }

            /// <summary>
            /// A fixed-size fitting on this bone - a plate carrier, a belt, a knee pad. The three
            /// placement numbers are in the bone's own frame: across the body, out of its front, and
            /// along its length from the joint. Size reads the same way, so nothing here has to know
            /// which direction the bone happens to be pointing.
            /// </summary>
            public GameObject Fitting(string name, Vector3 at, Vector3 size, Material material, int layer)
            {
                return Box(Joint, name, new Vector3(at.x, -at.y, at.z), new Vector3(size.x, size.y, size.z),
                    material, false, layer);
            }

            public void SetVisible(bool visible)
            {
                if (Joint != null && Joint.gameObject.activeSelf != visible) Joint.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// A duellist, built as a skeleton rather than a stack of boxes. Every bone below is one of the
        /// capsules in PlayerHitbox, placed from the same BodyPose, so the silhouette you aim at really
        /// is the shape the server tests - feet, shins, thighs, stomach, chest, neck, head and arms.
        /// </summary>
        public sealed class Character
        {
            public GameObject Root;

            public Transform Head;          // the skull group, rotated by the aim
            public Bone Neck;
            public Bone Chest;
            public Bone Stomach;

            public Bone LeftUpperArm, LeftForearm;
            public Bone RightUpperArm, RightForearm;
            public Transform LeftHand, RightHand;

            public Bone LeftThigh, LeftShin, LeftFoot;
            public Bone RightThigh, RightShin, RightFoot;

            public Material Skin;

            /// <summary>The bits that live inside your own camera. Wearing your own skull is not a look.</summary>
            public void SetFirstPerson()
            {
                Head.gameObject.SetActive(false);
                Neck.SetVisible(false);
            }

            public void SetArmsVisible(bool visible)
            {
                LeftUpperArm.SetVisible(visible);
                LeftForearm.SetVisible(visible);
                RightUpperArm.SetVisible(visible);
                RightForearm.SetVisible(visible);
                LeftHand.gameObject.SetActive(visible);
                RightHand.gameObject.SetActive(visible);
            }
        }

        public static Character Duellist(Transform parent, string name, Palette palette, Material skin, int layer,
                                         float scale = 1f)
        {
            Character c = new Character();
            c.Skin = skin;
            c.Root = new GameObject(name);
            if (parent != null) c.Root.transform.SetParent(parent, false);
            c.Root.layer = layer;
            Transform t = c.Root.transform;

            Material kit = palette.WallDark;

            // ---- torso. Wider than it is deep, which is most of what makes a person read as a person.
            c.Chest = Bone.Build(t, "chest", 0.395f * scale, 0.255f * scale, skin, layer);
            c.Stomach = Bone.Build(t, "stomach", 0.330f * scale, 0.225f * scale, kit, layer);
            c.Neck = Bone.Build(t, "neck", 0.115f * scale, 0.115f * scale, skin, layer);

            // Plate carrier and belt ride on the joints, not on the stretched meshes, so they keep
            // their proportions whatever the bone underneath is doing. Across, out of the front, and
            // up the bone - see Bone.Fitting.
            c.Chest.Fitting("plate carrier", new Vector3(0f, 0.008f, 0.155f) * scale,
                new Vector3(0.410f, 0.280f, 0.200f) * scale, kit, layer);
            c.Chest.Fitting("shoulder strap left", new Vector3(-0.130f, 0.100f, 0.190f) * scale,
                new Vector3(0.080f, 0.090f, 0.200f) * scale, kit, layer);
            c.Chest.Fitting("shoulder strap right", new Vector3(0.130f, 0.100f, 0.190f) * scale,
                new Vector3(0.080f, 0.090f, 0.200f) * scale, kit, layer);
            c.Chest.Fitting("mag pouch", new Vector3(0f, 0.140f, 0.060f) * scale,
                new Vector3(0.190f, 0.070f, 0.110f) * scale, palette.Accent, layer);
            c.Stomach.Fitting("belt", new Vector3(0f, 0f, 0.038f) * scale,
                new Vector3(0.355f, 0.250f, 0.070f) * scale, palette.Gun, layer);

            // ---- head. Its own group because it pitches with the aim while the neck only leans.
            c.Head = Group(t, "head", Vector3.zero, layer).transform;
            Box(c.Head, "skull", new Vector3(0f, 0.005f, 0f), new Vector3(0.185f, 0.215f, 0.205f) * scale, skin, false, layer);
            Box(c.Head, "jaw", new Vector3(0f, -0.075f, 0.028f) * scale, new Vector3(0.150f, 0.085f, 0.165f) * scale, skin, false, layer);
            Box(c.Head, "helmet", new Vector3(0f, 0.070f, -0.008f) * scale, new Vector3(0.215f, 0.115f, 0.235f) * scale, kit, false, layer);
            Box(c.Head, "visor", new Vector3(0f, 0.012f, 0.098f) * scale, new Vector3(0.155f, 0.062f, 0.030f) * scale, palette.GunDark, false, layer);
            Box(c.Head, "ear left", new Vector3(-0.105f, 0.012f, -0.010f) * scale, new Vector3(0.040f, 0.095f, 0.095f) * scale, palette.GunDark, false, layer);
            Box(c.Head, "ear right", new Vector3(0.105f, 0.012f, -0.010f) * scale, new Vector3(0.040f, 0.095f, 0.095f) * scale, palette.GunDark, false, layer);

            // ---- arms
            c.LeftUpperArm = Bone.Build(t, "left upper arm", 0.115f * scale, 0.115f * scale, skin, layer);
            c.LeftForearm = Bone.Build(t, "left forearm", 0.098f * scale, 0.098f * scale, skin, layer);
            c.RightUpperArm = Bone.Build(t, "right upper arm", 0.115f * scale, 0.115f * scale, skin, layer);
            c.RightForearm = Bone.Build(t, "right forearm", 0.098f * scale, 0.098f * scale, skin, layer);
            c.LeftHand = Hand(t, "left hand", palette, layer, scale, -1f);
            c.RightHand = Hand(t, "right hand", palette, layer, scale, 1f);

            // ---- legs
            c.LeftThigh = Bone.Build(t, "left thigh", 0.155f * scale, 0.165f * scale, kit, layer);
            c.RightThigh = Bone.Build(t, "right thigh", 0.155f * scale, 0.165f * scale, kit, layer);
            c.LeftShin = Bone.Build(t, "left shin", 0.125f * scale, 0.135f * scale, kit, layer);
            c.RightShin = Bone.Build(t, "right shin", 0.125f * scale, 0.135f * scale, kit, layer);
            c.LeftFoot = Bone.Build(t, "left boot", 0.108f * scale, 0.095f * scale, palette.Gun, layer);
            c.RightFoot = Bone.Build(t, "right boot", 0.108f * scale, 0.095f * scale, palette.Gun, layer);
            c.LeftFoot.Fill = 1f;
            c.RightFoot.Fill = 1f;

            // Knee pads, so a crouch reads from the front instead of being two boxes at an angle.
            c.LeftShin.Fitting("left knee pad", new Vector3(0f, 0.020f, 0.030f) * scale,
                new Vector3(0.150f, 0.160f, 0.090f) * scale, palette.Gun, layer);
            c.RightShin.Fitting("right knee pad", new Vector3(0f, 0.020f, 0.030f) * scale,
                new Vector3(0.150f, 0.160f, 0.090f) * scale, palette.Gun, layer);

            return c;
        }

        static Transform Hand(Transform parent, string name, Palette palette, int layer, float scale, float side)
        {
            Transform hand = Group(parent, name, Vector3.zero, layer).transform;
            Box(hand, "palm", new Vector3(0f, 0f, 0.020f) * scale, new Vector3(0.058f, 0.088f, 0.098f) * scale, palette.RemoteArms, false, layer);
            Box(hand, "thumb", new Vector3(-0.032f * side, 0.016f, 0.030f) * scale, new Vector3(0.028f, 0.030f, 0.055f) * scale, palette.RemoteArms, false, layer);
            return hand;
        }
    }
}
