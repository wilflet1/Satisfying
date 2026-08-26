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
            if (!withCollider && collider != null) Discard(collider);

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
            if (collider != null) Discard(collider);
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// A generated mesh, sized the same way a Box is: localScale IS the size in metres, because
        /// every shape MeshShapes hands out is normalised into the unit cube. No collider - nothing
        /// built out of these is ever collided with, the simulation owns that.
        /// </summary>
        public static GameObject Shape(Transform parent, string name, Vector3 center, Vector3 size,
                                       Mesh mesh, Material material, int layer)
        {
            GameObject go = new GameObject(name);
            go.layer = layer;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        /// <summary>
        /// Throwing away a primitive's collider. Object.Destroy is deferred to the end of the frame,
        /// which never comes outside play mode - and the shot sheet builds these bodies from an editor
        /// menu item, where a deferred destroy is an error in the console and a collider that stays.
        /// </summary>
        static void Discard(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
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
            public float Fill = 1f;         // lofted shapes already draw their ends in, so they meet flush

            public static Bone Build(Transform parent, string name, float width, float depth,
                                     Mesh shape, Material material, int layer)
            {
                Bone b = new Bone();
                b.Width = width;
                b.Depth = depth;
                b.Joint = Group(parent, name, Vector3.zero, layer).transform;
                b.Mesh = Shape(b.Joint, name + " mesh", Vector3.zero, Vector3.one, shape, material, layer).transform;
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
                return Shape(Joint, name, new Vector3(at.x, -at.y, at.z), new Vector3(size.x, size.y, size.z),
                    MeshShapes.Kit(), material, layer);
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
            public Material Kit;

            /// <summary>The bits that live inside your own camera. Wearing your own skull is not a look.</summary>
            /// <summary>
            /// Strips the parts of the body you cannot be shown your own of.
            ///
            /// The head and neck are obvious - the camera is inside the skull. The CHEST is the one
            /// that took a photograph to notice: your eyes sit about 220 mm above your own shoulders
            /// and your shoulders are 400 mm across, so from up there they subtend most of the screen.
            /// Look down at anything past about thirty degrees and the entire view was a featureless
            /// slab of shoulder - which is exactly what "why do I still look like a block" looks like
            /// from the inside.
            ///
            /// What is left is what you actually want to see of yourself: belt, hips, legs and boots.
            /// The plate carrier goes with the chest because it hangs off that bone, and no shooter
            /// has ever shown you the top of your own body armour.
            /// </summary>
            public void SetFirstPerson()
            {
                Head.gameObject.SetActive(false);
                Neck.SetVisible(false);
                Chest.SetVisible(false);
            }

            /// <summary>
            /// Hides every drawn piece while leaving the joints where they are. An avatar replaces
            /// what you SEE; the skeleton underneath still has to be there, because the weapon hangs
            /// off the firing hand and the hitbox is built from the same joints.
            /// </summary>
            public void SetBodyVisible(bool visible)
            {
                ShowMesh(Head, visible);
                ShowBone(Neck, visible);
                ShowBone(Chest, visible);
                ShowBone(Stomach, visible);
                ShowBone(LeftUpperArm, visible);
                ShowBone(LeftForearm, visible);
                ShowBone(RightUpperArm, visible);
                ShowBone(RightForearm, visible);
                ShowMesh(LeftHand, visible);
                ShowMesh(RightHand, visible);
                ShowBone(LeftThigh, visible);
                ShowBone(LeftShin, visible);
                ShowBone(LeftFoot, visible);
                ShowBone(RightThigh, visible);
                ShowBone(RightShin, visible);
                ShowBone(RightFoot, visible);
            }

            static void ShowBone(Bone bone, bool visible)
            {
                if (bone != null) ShowMesh(bone.Joint, visible);
            }

            static void ShowMesh(Transform t, bool visible)
            {
                if (t == null) return;
                Renderer[] renderers = t.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = visible;
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

        /// <summary>
        /// `variant` picks the kit colour and skin tone from Palette's fixed sets, so two duellists
        /// without avatars are not the same mannequin twice. It changes NOTHING about the shape: the
        /// sizes below are the same for everybody, because they are the sizes the hitbox capsules are
        /// laid over.
        /// </summary>
        public static Character Duellist(Transform parent, string name, Palette palette, Material skin, int layer,
                                         float scale = 1f, int variant = -1)
        {
            Character c = new Character();
            if (variant >= 0)
            {
                skin = palette.SkinFor(variant);
                c.Kit = palette.KitFor(variant);
            }
            c.Skin = skin;
            c.Root = new GameObject(name);
            if (parent != null) c.Root.transform.SetParent(parent, false);
            c.Root.layer = layer;
            Transform t = c.Root.transform;

            Material kit = c.Kit != null ? c.Kit : palette.WallDark;

            // Nothing drawn on a duellist may stick out past the capsules PlayerHitbox tests, or there
            // is a sliver of him you can see and cannot shoot. The chest is the one place it is allowed
            // to look like it does: the chest capsule is 0.31 across and the drawn shoulders are 0.40,
            // but the arm capsules start at the shoulder joints and cover the difference - so a round
            // fired at someone's deltoid registers, as an arm, which is what it is.
            //
            // ---- torso. Wider than it is deep, which is most of what makes a person read as a person:
            // the chest lofts out from a narrower waist, so the shoulders are the widest thing on him.
            c.Chest = Bone.Build(t, "chest", 0.400f * scale, 0.262f * scale, MeshShapes.Torso(0.76f), skin, layer);
            c.Stomach = Bone.Build(t, "stomach", 0.300f * scale, 0.240f * scale, MeshShapes.Torso(0.92f), kit, layer);
            c.Neck = Bone.Build(t, "neck", 0.120f * scale, 0.125f * scale, MeshShapes.Limb(1.05f), skin, layer);

            // ---- KIT. Everything below is a CHOICE, keyed off the variant, so two duellists without
            // avatars are wearing different things rather than the same outfit in two colours.
            //
            // The one rule every piece obeys: nothing may stick out past the capsule underneath it.
            // Satisfying/Shots/Check the silhouette measures exactly that, over every variant, so a
            // new piece of kit that would be visible and unshootable is caught before it ships.
            int look = variant < 0 ? 0 : Mathf.Abs(variant);
            Material webbing = palette.Accent;

            // Plate carrier and belt ride on the joints, not on the stretched meshes, so they keep
            // their proportions whatever the bone underneath is doing. Across, out of the front, and
            // up the bone - see Bone.Fitting.
            int torso = look % 4;
            if (torso == 0)
            {
                // Plate carrier with a mag rack.
                c.Chest.Fitting("plate carrier", new Vector3(0f, 0.008f, 0.155f) * scale,
                    new Vector3(0.410f, 0.280f, 0.200f) * scale, kit, layer);
                c.Chest.Fitting("mag pouch", new Vector3(0f, 0.140f, 0.060f) * scale,
                    new Vector3(0.190f, 0.070f, 0.110f) * scale, webbing, layer);
            }
            else if (torso == 1)
            {
                // Chest rig: narrower, higher, with two pouches.
                c.Chest.Fitting("chest rig", new Vector3(0f, 0.030f, 0.185f) * scale,
                    new Vector3(0.330f, 0.230f, 0.150f) * scale, kit, layer);
                c.Chest.Fitting("pouch left", new Vector3(-0.085f, 0.145f, 0.130f) * scale,
                    new Vector3(0.110f, 0.060f, 0.100f) * scale, webbing, layer);
                c.Chest.Fitting("pouch right", new Vector3(0.085f, 0.145f, 0.130f) * scale,
                    new Vector3(0.110f, 0.060f, 0.100f) * scale, webbing, layer);
            }
            else if (torso == 2)
            {
                // Plates front and back, no rack - a lighter, flatter silhouette.
                c.Chest.Fitting("front plate", new Vector3(0f, 0.055f, 0.150f) * scale,
                    new Vector3(0.360f, 0.140f, 0.230f) * scale, kit, layer);
                c.Chest.Fitting("back plate", new Vector3(0f, -0.060f, 0.150f) * scale,
                    new Vector3(0.360f, 0.130f, 0.230f) * scale, kit, layer);
            }
            else
            {
                // A jacket: no armour at all, just a heavier torso and a collar.
                c.Chest.Fitting("jacket", new Vector3(0f, 0f, 0.150f) * scale,
                    new Vector3(0.395f, 0.255f, 0.250f) * scale, kit, layer);
                c.Chest.Fitting("collar", new Vector3(0f, 0f, 0.268f) * scale,
                    new Vector3(0.240f, 0.200f, 0.055f) * scale, kit, layer);
            }

            // Straps, on most of them.
            if ((look / 4) % 3 != 0)
            {
                c.Chest.Fitting("shoulder strap left", new Vector3(-0.130f, 0.100f, 0.190f) * scale,
                    new Vector3(0.080f, 0.090f, 0.200f) * scale, kit, layer);
                c.Chest.Fitting("shoulder strap right", new Vector3(0.130f, 0.100f, 0.190f) * scale,
                    new Vector3(0.080f, 0.090f, 0.200f) * scale, kit, layer);
            }

            // Belt, and sometimes a dump pouch on the hip.
            c.Stomach.Fitting("belt", new Vector3(0f, 0f, 0.038f) * scale,
                new Vector3(0.318f, 0.258f, 0.070f) * scale, palette.Gun, layer);
            if ((look / 12) % 2 == 0)
                c.Stomach.Fitting("dump pouch", new Vector3(0.105f, 0.060f, 0.055f) * scale,
                    new Vector3(0.100f, 0.110f, 0.100f) * scale, webbing, layer);

            // ---- head. Its own group because it pitches with the aim while the neck only leans.
            // The skull is an ovoid laid on its side: the shape's own +Z is front to back, which is the
            // longer axis of a head, so it goes in unrotated and the sizes read as (width, height, depth).
            c.Head = Group(t, "head", Vector3.zero, layer).transform;
            Shape(c.Head, "skull", new Vector3(0f, 0.004f, -0.006f) * scale,
                new Vector3(0.180f, 0.212f, 0.225f) * scale, MeshShapes.Head(), skin, layer);
            Shape(c.Head, "jaw", new Vector3(0f, -0.070f, 0.030f) * scale,
                new Vector3(0.145f, 0.080f, 0.160f) * scale, MeshShapes.Kit(), skin, layer);
            // Helmet and ear cups are kept inside the 0.105 m head sphere. A helmet that overhangs it
            // is a helmet you can put a round through for no damage, which feels exactly as bad as it
            // sounds and is impossible to diagnose from the shooting end.
            int headwear = (look / 24) % 4;
            if (headwear == 0 || headwear == 1)
            {
                // Helmet, with or without the brim.
                Shape(c.Head, "helmet", new Vector3(0f, 0.058f, -0.008f) * scale,
                    new Vector3(0.200f, 0.126f, 0.208f) * scale, MeshShapes.Head(), kit, layer);
                if (headwear == 0)
                    Shape(c.Head, "helmet brim", new Vector3(0f, 0.034f, 0.084f) * scale,
                        new Vector3(0.180f, 0.030f, 0.070f) * scale, MeshShapes.Kit(), kit, layer);
            }
            else if (headwear == 2)
            {
                // A cap: lower, softer, with a peak.
                Shape(c.Head, "cap", new Vector3(0f, 0.062f, -0.010f) * scale,
                    new Vector3(0.186f, 0.096f, 0.196f) * scale, MeshShapes.Head(), kit, layer);
                Shape(c.Head, "peak", new Vector3(0f, 0.044f, 0.080f) * scale,
                    new Vector3(0.164f, 0.022f, 0.078f) * scale, MeshShapes.Kit(), kit, layer);
            }
            else
            {
                // Bare headed, with a rolled beanie.
                Shape(c.Head, "beanie", new Vector3(0f, 0.066f, -0.006f) * scale,
                    new Vector3(0.184f, 0.088f, 0.192f) * scale, MeshShapes.Head(), kit, layer);
            }

            // Eyewear: goggles up, glasses on, or nothing.
            int eyes = (look / 96) % 3;
            if (eyes == 0)
                Shape(c.Head, "visor", new Vector3(0f, 0.010f, 0.094f) * scale,
                    new Vector3(0.150f, 0.058f, 0.034f) * scale, MeshShapes.Kit(), palette.GunDark, layer);
            else if (eyes == 1)
                Shape(c.Head, "goggles", new Vector3(0f, 0.052f, 0.078f) * scale,
                    new Vector3(0.170f, 0.036f, 0.048f) * scale, MeshShapes.Kit(), palette.GunDark, layer);

            // Ear protection, on about half of them.
            if ((look / 288) % 2 == 0)
            {
                Shape(c.Head, "ear left", new Vector3(-0.086f, 0.010f, -0.012f) * scale,
                    new Vector3(0.034f, 0.096f, 0.096f) * scale, MeshShapes.Kit(), palette.GunDark, layer);
                Shape(c.Head, "ear right", new Vector3(0.086f, 0.010f, -0.012f) * scale,
                    new Vector3(0.034f, 0.096f, 0.096f) * scale, MeshShapes.Kit(), palette.GunDark, layer);
            }

            // ---- arms. Every limb tapers towards the joint it ends at, which is what stops a person
            // built out of six tubes from looking like a person built out of six tubes.
            c.LeftUpperArm = Bone.Build(t, "left upper arm", 0.120f * scale, 0.120f * scale, MeshShapes.Limb(0.80f), skin, layer);
            c.LeftForearm = Bone.Build(t, "left forearm", 0.098f * scale, 0.098f * scale, MeshShapes.Limb(0.78f), skin, layer);
            c.RightUpperArm = Bone.Build(t, "right upper arm", 0.120f * scale, 0.120f * scale, MeshShapes.Limb(0.80f), skin, layer);
            c.RightForearm = Bone.Build(t, "right forearm", 0.098f * scale, 0.098f * scale, MeshShapes.Limb(0.78f), skin, layer);
            c.LeftHand = Hand(t, "left hand", palette, layer, scale, -1f);
            c.RightHand = Hand(t, "right hand", palette, layer, scale, 1f);

            // ---- legs
            c.LeftThigh = Bone.Build(t, "left thigh", 0.165f * scale, 0.175f * scale, MeshShapes.Limb(0.72f), kit, layer);
            c.RightThigh = Bone.Build(t, "right thigh", 0.165f * scale, 0.175f * scale, MeshShapes.Limb(0.72f), kit, layer);
            c.LeftShin = Bone.Build(t, "left shin", 0.128f * scale, 0.140f * scale, MeshShapes.Limb(0.74f), kit, layer);
            c.RightShin = Bone.Build(t, "right shin", 0.128f * scale, 0.140f * scale, MeshShapes.Limb(0.74f), kit, layer);
            c.LeftFoot = Bone.Build(t, "left boot", 0.112f * scale, 0.105f * scale, MeshShapes.Boot(), palette.Gun, layer);
            c.RightFoot = Bone.Build(t, "right boot", 0.112f * scale, 0.105f * scale, MeshShapes.Boot(), palette.Gun, layer);

            // Knee pads, so a crouch reads from the front instead of being two boxes at an angle.
            // Not everybody wears them.
            if ((look / 576) % 3 != 0)
            {
                c.LeftShin.Fitting("left knee pad", new Vector3(0f, 0.020f, 0.030f) * scale,
                    new Vector3(0.150f, 0.160f, 0.090f) * scale, palette.Gun, layer);
                c.RightShin.Fitting("right knee pad", new Vector3(0f, 0.020f, 0.030f) * scale,
                    new Vector3(0.150f, 0.160f, 0.090f) * scale, palette.Gun, layer);
            }

            // Thigh rig, on some.
            if ((look / 1728) % 2 == 0)
                // Tucked in against the thigh: at 0.070 out with a 0.040 half-depth it reached 110 mm
                // from the bone against a 95 mm capsule, which is a pouch you can see and cannot shoot.
                c.RightThigh.Fitting("thigh rig", new Vector3(0f, 0.042f, 0.180f) * scale,
                    new Vector3(0.110f, 0.055f, 0.150f) * scale, webbing, layer);

            return c;
        }

        /// <summary>A gloved hand: a palm, a thumb wrapped round the grip, and fingers closed over it.</summary>
        static Transform Hand(Transform parent, string name, Palette palette, int layer, float scale, float side)
        {
            Transform hand = Group(parent, name, Vector3.zero, layer).transform;
            Shape(hand, "palm", new Vector3(0f, 0f, 0.022f) * scale,
                new Vector3(0.056f, 0.090f, 0.100f) * scale, MeshShapes.Kit(), palette.RemoteArms, layer);
            Shape(hand, "fingers", new Vector3(0f, -0.030f, 0.055f) * scale,
                new Vector3(0.052f, 0.048f, 0.070f) * scale, MeshShapes.Kit(), palette.RemoteArms, layer);
            Rotated(Shape(hand, "thumb", new Vector3(-0.030f * side, 0.012f, 0.034f) * scale,
                new Vector3(0.026f, 0.026f, 0.062f) * scale, MeshShapes.Limb(0.9f), palette.RemoteArms, layer),
                new Vector3(0f, -22f * side, 0f));
            return hand;
        }
    }
}
