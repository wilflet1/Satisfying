using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// A built weapon plus the handles the animation and IK need: the part that reciprocates when it
    /// fires, the magazine that drops on a reload, where each hand goes, and the point that has to line
    /// up with the middle of the screen when you aim.
    /// </summary>
    public sealed class WeaponModel
    {
        public GameObject Root;
        public Transform Bolt;              // charging handle / cocking lever / slide
        public Transform Magazine;
        public Transform Muzzle;
        public Transform GripAnchor;        // firing hand
        public Transform ForegripAnchor;    // support hand
        public Transform MagAnchor;         // support hand during a reload
        public Transform StockAnchor;       // the end that hits things when you melee

        /// <summary>One per SightKind. The active one is what gets lined up with the screen centre.</summary>
        public Transform[] SightAnchors = new Transform[4];
        public GameObject[] SightModels = new GameObject[4];
        public int ActiveSight;

        public Transform SightAnchor
        {
            get
            {
                Transform anchor = SightAnchors[Mathf.Clamp(ActiveSight, 0, SightAnchors.Length - 1)];
                return anchor != null ? anchor : SightAnchors[0];
            }
        }

        /// <summary>Fits an optic: only one is ever on the gun at a time.</summary>
        public void SetSight(int index)
        {
            ActiveSight = Mathf.Clamp(index, 0, SightModels.Length - 1);
            for (int i = 0; i < SightModels.Length; i++)
            {
                if (SightModels[i] == null) continue;
                bool on = i == ActiveSight;
                if (SightModels[i].activeSelf != on) SightModels[i].SetActive(on);
            }
        }

        /// <summary>
        /// A rifle's bolt and a pistol's slide lock back on the last round; an HK roller lock does not.
        /// It is one bool and it is the difference between "I am out" reading at a glance and not.
        /// </summary>
        public bool HoldsOpenWhenEmpty = true;

        /// <summary>
        /// Puts the support hand exactly where BodyPose expects it for this weapon. Those two numbers
        /// are the same numbers - the arm hitbox is built from the tuning and the drawn hand is placed
        /// from the anchor, so the anchor has to be derived from the tuning rather than typed in beside
        /// it and left to drift.
        /// </summary>
        public void SetSupportGrip(WeaponTuning tuning)
        {
            if (tuning == null || ForegripAnchor == null || GripAnchor == null) return;
            ForegripAnchor.localPosition = GripAnchor.localPosition
                + new Vector3(0f, tuning.supportHandRise, tuning.supportHandReach);
        }

        public Vector3 BoltTravel = new Vector3(0f, 0f, -0.06f);
        public Vector3 MagazineEject = new Vector3(0f, -0.22f, 0f);
        public Vector3 MagazineEjectTilt = new Vector3(12f, 0f, 8f);
        public Vector3 HipOffset = new Vector3(0.14f, -0.13f, 0.28f);
        public float SwayScale = 1f;
    }

    /// <summary>
    /// The three starting weapons, built from the same box vocabulary and the same three materials so
    /// they read as one set. Swapping in real models later means filling the same anchors.
    /// </summary>
    public static class WeaponModels
    {
        public static WeaponModel Build(int index, Transform parent, Palette palette, int layer)
        {
            switch (index)
            {
                case 1: return BuildMp5(parent, palette, layer);
                case 2: return BuildUsp45(parent, palette, layer);
                case 3: return BuildSniper(parent, palette, layer);
                default: return BuildM4(parent, palette, layer);
            }
        }

        /// <summary>
        /// A grenade: a body, a fly-off lever and a ring. Small - 60 mm across - because a grenade
        /// drawn at the size people imagine one is a beach ball rolling across the floor.
        /// </summary>
        public static GameObject BuildGrenade(Transform parent, Palette palette, int layer)
        {
            GameObject go = Blockout.Group(parent, "grenade", Vector3.zero, layer);
            Transform t = go.transform;

            Blockout.Shape(t, "body", Vector3.zero, new Vector3(0.058f, 0.058f, 0.082f),
                MeshShapes.Head(), palette.Gun, layer);
            Piece(t, "fuse", new Vector3(0f, 0f, 0.048f), new Vector3(0.022f, 0.022f, 0.020f), palette.Metal, layer);
            Piece(t, "lever", new Vector3(0.026f, 0f, 0.020f), new Vector3(0.008f, 0.020f, 0.062f), palette.Metal, layer);
            Piece(t, "ring", new Vector3(-0.026f, 0f, 0.046f), new Vector3(0.020f, 0.005f, 0.020f), palette.Metal, layer);
            return go;
        }

        public static string Name(int index)
        {
            switch (index)
            {
                case 1: return "MP5";
                case 2: return "USP45";
                case 3: return "M700";
                default: return "M4A1";
            }
        }

        // ------------------------------------------------------------------ helpers
        static GameObject Piece(Transform parent, string name, Vector3 position, Vector3 size, Material material, int layer, Vector3 euler = default(Vector3))
        {
            GameObject go = Blockout.Box(parent, name, position, size, material, false, layer);
            if (euler != Vector3.zero) go.transform.localRotation = Quaternion.Euler(euler);
            return go;
        }

        /// <summary>
        /// A round part - a barrel, a buffer tube, an optic body. A gun is mostly flats and a square
        /// barrel was fine next to a character made of boxes; next to one made of lofted limbs it is
        /// the thing your eye goes to. Size reads (width, height, length) like every other piece.
        /// </summary>
        static GameObject Tube(Transform parent, string name, Vector3 position, Vector3 size, Material material, int layer)
        {
            return Blockout.Shape(parent, name, position, size, MeshShapes.Limb(1f), material, layer);
        }

        /// <summary>
        /// A ring of segments round an empty centre - an optic housing. Kept as separate pieces rather
        /// than a modelled tube precisely so that the middle of it stays a hole.
        /// </summary>
        static void Aperture(Transform parent, string name, Vector3 centre, float radius, float length,
                             float thickness, int segments, Material material, int layer)
        {
            GameObject ring = Blockout.Group(parent, name, centre, layer);
            float step = Mathf.PI * 2f / segments;
            float width = radius * step * 1.25f;
            for (int i = 0; i < segments; i++)
            {
                float a = i * step;
                // Turned so its width runs round the ring and its thickness points at the centre.
                Piece(ring.transform, "segment",
                    new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f),
                    new Vector3(width, thickness, length), material, layer,
                    new Vector3(0f, 0f, a * Mathf.Rad2Deg + 90f));
            }
        }

        static Transform Anchor(Transform parent, string name, Vector3 position, int layer)
        {
            GameObject go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        /// <summary>
        /// A notch and post you can actually aim with: two uprights with a gap between them, and a thin
        /// front post that shows through the gap. A solid block of a rear sight reads as a wall.
        /// </summary>
        static void BuildIronSights(WeaponModel m, Transform t, Palette palette, int layer,
                                    float sightTopY, float rearZ, float frontZ, float frontBaseY,
                                    float postWidth = 0.005f)
        {
            GameObject group = Blockout.Group(t, "iron sights", Vector3.zero, layer);
            const float notchGap = 0.014f;
            const float uprightHeight = 0.020f;
            float uprightY = sightTopY - uprightHeight * 0.5f;

            Piece(group.transform, "rear upright left", new Vector3(-(notchGap * 0.5f + 0.0035f), uprightY, rearZ),
                new Vector3(0.007f, uprightHeight, 0.010f), palette.Metal, layer);
            Piece(group.transform, "rear upright right", new Vector3(notchGap * 0.5f + 0.0035f, uprightY, rearZ),
                new Vector3(0.007f, uprightHeight, 0.010f), palette.Metal, layer);
            Piece(group.transform, "rear base", new Vector3(0f, sightTopY - uprightHeight - 0.004f, rearZ),
                new Vector3(0.032f, 0.010f, 0.014f), palette.GunDark, layer);

            float postHeight = 0.026f;

            // The post has to stand on something. Left to itself it is a sight blade hanging in mid
            // air above the barrel - on the M4 the gap was 36 mm, which is plainly visible on the
            // model and is the first thing you see down the sights.
            float postBottom = sightTopY - postHeight;
            if (postBottom - frontBaseY > 0.002f)
            {
                float baseHeight = postBottom - frontBaseY;
                Piece(group.transform, "front sight base", new Vector3(0f, frontBaseY + baseHeight * 0.5f, frontZ),
                    new Vector3(0.026f, baseHeight, 0.026f), palette.GunDark, layer);
            }

            Piece(group.transform, "front post", new Vector3(0f, sightTopY - postHeight * 0.5f, frontZ),
                new Vector3(postWidth, postHeight, 0.006f), palette.Metal, layer);
            // Protective ears, open at the top so the post stays readable.
            Piece(group.transform, "front ear left", new Vector3(-0.014f, sightTopY - postHeight * 0.5f, frontZ),
                new Vector3(0.005f, postHeight, 0.008f), palette.GunDark, layer);
            Piece(group.transform, "front ear right", new Vector3(0.014f, sightTopY - postHeight * 0.5f, frontZ),
                new Vector3(0.005f, postHeight, 0.008f), palette.GunDark, layer);

            m.SightModels[(int)SightKind.Iron] = group;
            m.SightAnchors[(int)SightKind.Iron] = Anchor(t, "iron sight line", new Vector3(0f, sightTopY - 0.002f, rearZ), layer);
        }

        /// <summary>A tube red dot. The reticle itself is drawn on the HUD when you are behind it.</summary>
        static void BuildRedDot(WeaponModel m, Transform t, Palette palette, int layer, float railY, float z)
        {
            GameObject group = Blockout.Group(t, "red dot", Vector3.zero, layer);
            float centreY = railY + 0.033f;

            Piece(group.transform, "mount", new Vector3(0f, railY + 0.010f, z), new Vector3(0.030f, 0.020f, 0.040f), palette.GunDark, layer);
            // Ten segments round an empty middle. It has to stay empty: this is a sight you look
            // THROUGH, and the obvious tidy-up - one round tube instead of a ring of plates - fills the
            // aperture with a solid plug and blinds you the moment you aim.
            Aperture(group.transform, "tube", new Vector3(0f, centreY, z), 0.021f, 0.062f, 0.007f, 10, palette.Gun, layer);

            m.SightModels[(int)SightKind.RedDot] = group;
            m.SightAnchors[(int)SightKind.RedDot] = Anchor(t, "red dot line", new Vector3(0f, centreY, z - 0.03f), layer);
        }

        /// <summary>A wide holographic window - a bigger, squarer sight picture than the tube.</summary>
        static void BuildHolo(WeaponModel m, Transform t, Palette palette, int layer, float railY, float z)
        {
            GameObject group = Blockout.Group(t, "holo", Vector3.zero, layer);
            float centreY = railY + 0.036f;

            Piece(group.transform, "body", new Vector3(0f, railY + 0.018f, z - 0.045f), new Vector3(0.046f, 0.036f, 0.070f), palette.Gun, layer);
            Piece(group.transform, "hood top", new Vector3(0f, centreY + 0.026f, z), new Vector3(0.058f, 0.008f, 0.050f), palette.Gun, layer);
            Piece(group.transform, "hood left", new Vector3(-0.026f, centreY, z), new Vector3(0.007f, 0.052f, 0.050f), palette.Gun, layer);
            Piece(group.transform, "hood right", new Vector3(0.026f, centreY, z), new Vector3(0.007f, 0.052f, 0.050f), palette.Gun, layer);
            Piece(group.transform, "hood base", new Vector3(0f, centreY - 0.026f, z), new Vector3(0.058f, 0.007f, 0.050f), palette.GunDark, layer);

            m.SightModels[(int)SightKind.Holo] = group;
            m.SightAnchors[(int)SightKind.Holo] = Anchor(t, "holo line", new Vector3(0f, centreY, z - 0.03f), layer);
        }

        /// <summary>
        /// A magnified optic. Unlike the other three this one is not really aimed THROUGH - the scope
        /// picture is rendered by a camera of its own and drawn over the middle of the screen - so what
        /// is modelled here is the outside of it: a tube, two bells, turrets and a set of rings. What
        /// matters mechanically is that the anchor is on the optical axis, because that axis is what
        /// the scope camera is pointed down.
        /// </summary>
        static void BuildScope(WeaponModel m, Transform t, Palette palette, int layer, float railY, float z,
                               float length = 0.30f)
        {
            GameObject group = Blockout.Group(t, "scope", Vector3.zero, layer);
            float centreY = railY + 0.052f;

            Tube(group.transform, "tube", new Vector3(0f, centreY, z), new Vector3(0.040f, 0.040f, length), palette.Gun, layer);
            Tube(group.transform, "ocular bell", new Vector3(0f, centreY, z - length * 0.5f - 0.030f),
                new Vector3(0.052f, 0.052f, 0.062f), palette.Gun, layer);
            Tube(group.transform, "objective bell", new Vector3(0f, centreY, z + length * 0.5f + 0.038f),
                new Vector3(0.062f, 0.062f, 0.080f), palette.Gun, layer);
            // The glass. Dark, so it reads as glass rather than as an open pipe you can see the sky through.
            Tube(group.transform, "objective glass", new Vector3(0f, centreY, z + length * 0.5f + 0.076f),
                new Vector3(0.054f, 0.054f, 0.006f), palette.GunDark, layer);

            Piece(group.transform, "elevation turret", new Vector3(0f, centreY + 0.032f, z - 0.01f),
                new Vector3(0.030f, 0.028f, 0.034f), palette.GunDark, layer);
            Piece(group.transform, "windage turret", new Vector3(0.030f, centreY, z - 0.01f),
                new Vector3(0.028f, 0.030f, 0.034f), palette.GunDark, layer);
            Piece(group.transform, "parallax turret", new Vector3(-0.030f, centreY, z - 0.01f),
                new Vector3(0.028f, 0.030f, 0.030f), palette.GunDark, layer);
            // The magnification ring, which is the thing the wheel is turning.
            Tube(group.transform, "power ring", new Vector3(0f, centreY, z - length * 0.5f + 0.012f),
                new Vector3(0.050f, 0.050f, 0.022f), palette.GunDark, layer);

            Piece(group.transform, "ring front", new Vector3(0f, centreY - 0.026f, z + 0.070f),
                new Vector3(0.026f, 0.052f, 0.020f), palette.GunDark, layer);
            Piece(group.transform, "ring rear", new Vector3(0f, centreY - 0.026f, z - 0.070f),
                new Vector3(0.026f, 0.052f, 0.020f), palette.GunDark, layer);

            m.SightModels[(int)SightKind.Scope] = group;
            m.SightAnchors[(int)SightKind.Scope] =
                Anchor(t, "scope line", new Vector3(0f, centreY, z - length * 0.5f - 0.055f), layer);
        }

        /// <summary>
        /// Picatinny slots down a rail. Five thin ribs read as a rail from a metre away and cost five
        /// boxes; a smooth bar reads as a handle.
        /// </summary>
        static void RailSlots(Transform parent, Palette palette, int layer, float y, float startZ, float endZ,
                              float width, int count)
        {
            GameObject group = Blockout.Group(parent, "rail slots", Vector3.zero, layer);
            for (int i = 0; i < count; i++)
            {
                float k = count <= 1 ? 0f : i / (float)(count - 1);
                Piece(group.transform, "slot", new Vector3(0f, y, Mathf.Lerp(startZ, endZ, k)),
                    new Vector3(width, 0.006f, 0.008f), palette.Gun, layer);
            }
        }

        /// <summary>Grip serrations, cut across whichever axis the caller points them down.</summary>
        static void Serrations(Transform parent, Palette palette, int layer, Vector3 centre, Vector3 size,
                               float spacing, int count, bool alongZ)
        {
            GameObject group = Blockout.Group(parent, "serrations", centre, layer);
            for (int i = 0; i < count; i++)
            {
                float offset = (i - (count - 1) * 0.5f) * spacing;
                Vector3 at = alongZ ? new Vector3(0f, 0f, offset) : new Vector3(offset, 0f, 0f);
                Piece(group.transform, "cut", at, size, palette.GunDark, layer);
            }
        }

        /// <summary>Trigger, guard and the finger's way in. Every gun in the set gets the same three.</summary>
        static void TriggerGroup(Transform parent, Palette palette, int layer, float y, float z, float scale = 1f)
        {
            GameObject group = Blockout.Group(parent, "trigger group", new Vector3(0f, y, z), layer);
            Piece(group.transform, "guard front", new Vector3(0f, -0.006f * scale, 0.042f * scale),
                new Vector3(0.020f, 0.048f, 0.012f) * scale, palette.GunDark, layer);
            Piece(group.transform, "guard bottom", new Vector3(0f, -0.028f * scale, 0.012f * scale),
                new Vector3(0.020f, 0.011f, 0.070f) * scale, palette.GunDark, layer);
            Piece(group.transform, "trigger", new Vector3(0f, -0.012f * scale, 0.016f * scale),
                new Vector3(0.009f, 0.030f, 0.010f) * scale, palette.Metal, layer, new Vector3(-8f, 0f, 0f));
        }

        // ------------------------------------------------------------------ M4A1
        static WeaponModel BuildM4(Transform parent, Palette palette, int layer)
        {
            WeaponModel m = new WeaponModel();
            m.Root = Blockout.Group(parent, "M4A1", Vector3.zero, layer);
            Transform t = m.Root.transform;

            Piece(t, "upper receiver", new Vector3(0f, 0.022f, 0.05f), new Vector3(0.052f, 0.072f, 0.30f), palette.Gun, layer);
            Piece(t, "lower receiver", new Vector3(0f, -0.032f, 0.02f), new Vector3(0.046f, 0.055f, 0.22f), palette.GunDark, layer);
            Piece(t, "top rail", new Vector3(0f, 0.064f, 0.10f), new Vector3(0.040f, 0.016f, 0.40f), palette.GunDark, layer);
            Piece(t, "handguard", new Vector3(0f, 0.006f, 0.33f), new Vector3(0.056f, 0.060f, 0.28f), palette.GunDark, layer);
            Tube(t, "barrel", new Vector3(0f, 0.012f, 0.55f), new Vector3(0.024f, 0.024f, 0.30f), palette.Metal, layer);
            Piece(t, "gas block", new Vector3(0f, 0.030f, 0.50f), new Vector3(0.034f, 0.040f, 0.05f), palette.Metal, layer);
            Tube(t, "muzzle device", new Vector3(0f, 0.012f, 0.715f), new Vector3(0.036f, 0.036f, 0.07f), palette.Metal, layer);
            Piece(t, "grip", new Vector3(0f, -0.115f, -0.085f), new Vector3(0.040f, 0.145f, 0.058f), palette.GunDark, layer, new Vector3(-17f, 0f, 0f));
            Tube(t, "buffer tube", new Vector3(0f, 0.010f, -0.215f), new Vector3(0.042f, 0.042f, 0.20f), palette.Gun, layer);
            Piece(t, "stock", new Vector3(0f, -0.005f, -0.245f), new Vector3(0.056f, 0.085f, 0.13f), palette.GunDark, layer);
            Piece(t, "butt plate", new Vector3(0f, -0.010f, -0.325f), new Vector3(0.058f, 0.105f, 0.035f), palette.Gun, layer);
            Piece(t, "cheek riser", new Vector3(0f, 0.042f, -0.250f), new Vector3(0.038f, 0.024f, 0.115f), palette.Gun, layer);
            Piece(t, "castle nut", new Vector3(0f, 0.010f, -0.128f), new Vector3(0.048f, 0.056f, 0.022f), palette.Metal, layer);

            // The right side of an AR is where all the character is: port, deflector, forward assist.
            Piece(t, "ejection port", new Vector3(0.028f, 0.030f, 0.030f), new Vector3(0.008f, 0.034f, 0.075f), palette.GunDark, layer);
            Piece(t, "brass deflector", new Vector3(0.030f, 0.008f, -0.010f), new Vector3(0.014f, 0.030f, 0.030f), palette.Gun, layer);
            Piece(t, "forward assist", new Vector3(0.028f, 0.006f, -0.030f), new Vector3(0.016f, 0.022f, 0.026f), palette.Gun, layer);
            Piece(t, "magazine release", new Vector3(0.026f, -0.032f, 0.045f), new Vector3(0.010f, 0.020f, 0.020f), palette.Metal, layer);
            Piece(t, "bolt catch", new Vector3(-0.026f, -0.026f, 0.030f), new Vector3(0.008f, 0.024f, 0.038f), palette.Metal, layer);
            Piece(t, "selector", new Vector3(-0.026f, -0.030f, -0.060f), new Vector3(0.014f, 0.016f, 0.030f), palette.Metal, layer);
            Piece(t, "sling loop", new Vector3(0f, -0.020f, 0.455f), new Vector3(0.026f, 0.028f, 0.010f), palette.Metal, layer);

            TriggerGroup(t, palette, layer, -0.048f, -0.030f);
            RailSlots(t, palette, layer, 0.074f, -0.06f, 0.28f, 0.044f, 7);
            // Vents down the handguard, which is what stops it reading as a length of pipe.
            Serrations(t, palette, layer, new Vector3(0.030f, 0.006f, 0.33f), new Vector3(0.006f, 0.030f, 0.016f), 0.046f, 5, true);
            Serrations(t, palette, layer, new Vector3(-0.030f, 0.006f, 0.33f), new Vector3(0.006f, 0.030f, 0.016f), 0.046f, 5, true);

            GameObject bolt = Blockout.Group(t, "charging handle", new Vector3(0f, 0.058f, -0.085f), layer);
            Piece(bolt.transform, "handle", Vector3.zero, new Vector3(0.048f, 0.016f, 0.055f), palette.Metal, layer);
            m.Bolt = bolt.transform;
            m.BoltTravel = new Vector3(0f, 0f, -0.075f);

            GameObject magazine = Blockout.Group(t, "magazine", new Vector3(0f, -0.135f, 0.045f), layer);
            magazine.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
            Piece(magazine.transform, "body", Vector3.zero, new Vector3(0.036f, 0.165f, 0.072f), palette.GunDark, layer);
            Piece(magazine.transform, "floor plate", new Vector3(0f, -0.090f, 0f), new Vector3(0.040f, 0.018f, 0.078f), palette.Gun, layer);
            m.Magazine = magazine.transform;
            m.MagazineEject = new Vector3(0f, -0.26f, 0.02f);

            BuildIronSights(m, t, palette, layer, 0.086f, -0.055f, 0.615f, 0.020f);
            BuildRedDot(m, t, palette, layer, 0.072f, 0.02f);
            BuildHolo(m, t, palette, layer, 0.072f, 0.02f);
            BuildScope(m, t, palette, layer, 0.072f, 0.06f, 0.26f);
            m.SetSight((int)SightKind.Iron);

            m.Muzzle = Anchor(t, "muzzle", new Vector3(0f, 0.012f, 0.76f), layer);
            m.GripAnchor = Anchor(t, "grip anchor", new Vector3(0f, -0.095f, -0.075f), layer);
            m.ForegripAnchor = Anchor(t, "foregrip anchor", new Vector3(0f, -0.040f, 0.34f), layer);
            m.MagAnchor = Anchor(t, "mag anchor", new Vector3(0f, -0.20f, 0.05f), layer);
            m.StockAnchor = Anchor(t, "stock", new Vector3(0f, -0.010f, -0.325f), layer);
            m.HipOffset = new Vector3(0.155f, -0.150f, 0.30f);
            m.HoldsOpenWhenEmpty = true;
            return m;
        }

        // ------------------------------------------------------------------ MP5
        static WeaponModel BuildMp5(Transform parent, Palette palette, int layer)
        {
            WeaponModel m = new WeaponModel();
            m.Root = Blockout.Group(parent, "MP5", Vector3.zero, layer);
            Transform t = m.Root.transform;

            Piece(t, "receiver", new Vector3(0f, 0.018f, 0.03f), new Vector3(0.050f, 0.070f, 0.28f), palette.Gun, layer);
            Piece(t, "handguard", new Vector3(0f, -0.004f, 0.245f), new Vector3(0.060f, 0.068f, 0.20f), palette.GunDark, layer);
            Tube(t, "barrel", new Vector3(0f, 0.012f, 0.375f), new Vector3(0.024f, 0.024f, 0.10f), palette.Metal, layer);
            Piece(t, "trigger group", new Vector3(0f, -0.035f, -0.030f), new Vector3(0.046f, 0.050f, 0.16f), palette.GunDark, layer);
            Piece(t, "grip", new Vector3(0f, -0.105f, -0.065f), new Vector3(0.038f, 0.135f, 0.055f), palette.GunDark, layer, new Vector3(-15f, 0f, 0f));
            Piece(t, "stock rail", new Vector3(0f, 0.010f, -0.195f), new Vector3(0.046f, 0.038f, 0.19f), palette.Gun, layer);
            Piece(t, "butt", new Vector3(0f, -0.005f, -0.290f), new Vector3(0.052f, 0.090f, 0.035f), palette.GunDark, layer);

            // The bits that make an MP5 an MP5: the claw mount, the tri-lug, the paddle behind the well.
            Piece(t, "claw mount", new Vector3(0f, 0.056f, 0.02f), new Vector3(0.042f, 0.024f, 0.19f), palette.Gun, layer);
            Tube(t, "tri lug", new Vector3(0f, 0.012f, 0.415f), new Vector3(0.034f, 0.034f, 0.026f), palette.Metal, layer);
            Piece(t, "paddle release", new Vector3(0f, -0.062f, 0.045f), new Vector3(0.030f, 0.034f, 0.014f), palette.GunDark, layer, new Vector3(24f, 0f, 0f));
            Piece(t, "ejection port", new Vector3(0.026f, 0.030f, 0.075f), new Vector3(0.008f, 0.030f, 0.062f), palette.GunDark, layer);
            Piece(t, "selector", new Vector3(-0.024f, -0.028f, -0.048f), new Vector3(0.014f, 0.018f, 0.032f), palette.Metal, layer);
            Tube(t, "cocking tube", new Vector3(-0.038f, 0.046f, 0.28f), new Vector3(0.024f, 0.024f, 0.16f), palette.Gun, layer);
            Piece(t, "sling loop", new Vector3(0f, -0.026f, 0.325f), new Vector3(0.024f, 0.026f, 0.010f), palette.Metal, layer);
            Tube(t, "stock rod left", new Vector3(-0.020f, 0.010f, -0.230f), new Vector3(0.015f, 0.015f, 0.130f), palette.Metal, layer);
            Tube(t, "stock rod right", new Vector3(0.020f, 0.010f, -0.230f), new Vector3(0.015f, 0.015f, 0.130f), palette.Metal, layer);

            TriggerGroup(t, palette, layer, -0.052f, -0.020f, 0.95f);
            RailSlots(t, palette, layer, 0.070f, -0.05f, 0.09f, 0.046f, 4);
            Serrations(t, palette, layer, new Vector3(0.032f, -0.004f, 0.245f), new Vector3(0.006f, 0.034f, 0.014f), 0.038f, 4, true);
            Serrations(t, palette, layer, new Vector3(-0.032f, -0.004f, 0.245f), new Vector3(0.006f, 0.034f, 0.014f), 0.038f, 4, true);

            GameObject bolt = Blockout.Group(t, "cocking handle", new Vector3(-0.046f, 0.050f, 0.215f), layer);
            Piece(bolt.transform, "lever", Vector3.zero, new Vector3(0.030f, 0.022f, 0.048f), palette.Metal, layer);
            m.Bolt = bolt.transform;
            m.BoltTravel = new Vector3(0f, 0f, -0.055f);

            GameObject magazine = Blockout.Group(t, "magazine", new Vector3(0f, -0.140f, 0.115f), layer);
            magazine.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
            Piece(magazine.transform, "body", Vector3.zero, new Vector3(0.032f, 0.195f, 0.056f), palette.GunDark, layer);
            Piece(magazine.transform, "floor plate", new Vector3(0f, -0.105f, 0f), new Vector3(0.036f, 0.016f, 0.062f), palette.Gun, layer);
            m.Magazine = magazine.transform;
            m.MagazineEject = new Vector3(0f, -0.27f, 0.01f);

            BuildIronSights(m, t, palette, layer, 0.079f, -0.030f, 0.365f, 0.020f);
            BuildRedDot(m, t, palette, layer, 0.053f, 0.03f);
            BuildHolo(m, t, palette, layer, 0.053f, 0.03f);
            BuildScope(m, t, palette, layer, 0.053f, 0.05f, 0.22f);
            m.SetSight((int)SightKind.Iron);

            m.Muzzle = Anchor(t, "muzzle", new Vector3(0f, 0.012f, 0.43f), layer);
            m.GripAnchor = Anchor(t, "grip anchor", new Vector3(0f, -0.085f, -0.055f), layer);
            m.ForegripAnchor = Anchor(t, "foregrip anchor", new Vector3(0f, -0.048f, 0.25f), layer);
            m.MagAnchor = Anchor(t, "mag anchor", new Vector3(0f, -0.215f, 0.12f), layer);
            m.StockAnchor = Anchor(t, "stock", new Vector3(0f, -0.005f, -0.290f), layer);
            m.HipOffset = new Vector3(0.145f, -0.140f, 0.28f);
            m.SwayScale = 1.1f;
            // A roller-delayed bolt does not lock back on the last round. You find out by pulling the
            // trigger, which is the whole personality of the gun.
            m.HoldsOpenWhenEmpty = false;
            return m;
        }

        // ------------------------------------------------------------------ USP45
        static WeaponModel BuildUsp45(Transform parent, Palette palette, int layer)
        {
            WeaponModel m = new WeaponModel();
            m.Root = Blockout.Group(parent, "USP45", Vector3.zero, layer);
            Transform t = m.Root.transform;

            Piece(t, "frame", new Vector3(0f, -0.028f, 0.020f), new Vector3(0.032f, 0.046f, 0.145f), palette.GunDark, layer);
            Piece(t, "trigger guard", new Vector3(0f, -0.052f, 0.008f), new Vector3(0.026f, 0.040f, 0.018f), palette.GunDark, layer);
            Piece(t, "grip", new Vector3(0f, -0.105f, -0.030f), new Vector3(0.036f, 0.130f, 0.050f), palette.GunDark, layer, new Vector3(-13f, 0f, 0f));
            Tube(t, "barrel", new Vector3(0f, 0.018f, 0.150f), new Vector3(0.019f, 0.019f, 0.030f), palette.Metal, layer);
            Piece(t, "accessory rail", new Vector3(0f, -0.008f, 0.105f), new Vector3(0.022f, 0.010f, 0.070f), palette.GunDark, layer);
            Piece(t, "beavertail", new Vector3(0f, -0.004f, -0.050f), new Vector3(0.028f, 0.016f, 0.034f), palette.GunDark, layer);
            Piece(t, "hammer", new Vector3(0f, 0.004f, -0.058f), new Vector3(0.012f, 0.030f, 0.014f), palette.Metal, layer, new Vector3(22f, 0f, 0f));
            Piece(t, "slide stop", new Vector3(-0.019f, -0.008f, 0.010f), new Vector3(0.008f, 0.014f, 0.040f), palette.Metal, layer);
            Piece(t, "mag release", new Vector3(0f, -0.048f, -0.048f), new Vector3(0.024f, 0.016f, 0.012f), palette.Metal, layer);
            TriggerGroup(t, palette, layer, -0.040f, -0.014f, 0.85f);
            Serrations(t, palette, layer, new Vector3(0f, -0.105f, -0.030f), new Vector3(0.038f, 0.008f, 0.010f), 0.020f, 5, true);

            GameObject slide = Blockout.Group(t, "slide", new Vector3(0f, 0.020f, 0.055f), layer);
            Piece(slide.transform, "body", Vector3.zero, new Vector3(0.034f, 0.046f, 0.195f), palette.Gun, layer);
            Piece(slide.transform, "ejection port", new Vector3(0.018f, 0.010f, 0.030f), new Vector3(0.006f, 0.024f, 0.055f), palette.GunDark, layer);
            Serrations(slide.transform, palette, layer, new Vector3(0.018f, 0f, -0.062f), new Vector3(0.006f, 0.040f, 0.007f), 0.013f, 5, true);
            Serrations(slide.transform, palette, layer, new Vector3(-0.018f, 0f, -0.062f), new Vector3(0.006f, 0.040f, 0.007f), 0.013f, 5, true);
            m.Bolt = slide.transform;
            m.BoltTravel = new Vector3(0f, 0f, -0.048f);

            GameObject magazine = Blockout.Group(t, "magazine", new Vector3(0f, -0.108f, -0.030f), layer);
            magazine.transform.localRotation = Quaternion.Euler(-13f, 0f, 0f);
            Piece(magazine.transform, "body", Vector3.zero, new Vector3(0.026f, 0.125f, 0.038f), palette.Metal, layer);
            Piece(magazine.transform, "floor plate", new Vector3(0f, -0.072f, 0f), new Vector3(0.032f, 0.014f, 0.046f), palette.Gun, layer);
            m.Magazine = magazine.transform;
            m.MagazineEject = new Vector3(0f, -0.20f, 0f);
            m.MagazineEjectTilt = new Vector3(-18f, 0f, 6f);

            // The pistol's sights live on the frame rather than the slide: a sight picture that
            // reciprocates with every shot is not one you can aim with.
            BuildIronSights(m, t, palette, layer, 0.056f, -0.020f, 0.138f, 0.040f, 0.004f);
            BuildRedDot(m, t, palette, layer, 0.043f, 0.02f);
            BuildHolo(m, t, palette, layer, 0.043f, 0.02f);
            BuildScope(m, t, palette, layer, 0.043f, 0.03f, 0.14f);
            m.SetSight((int)SightKind.Iron);

            m.Muzzle = Anchor(t, "muzzle", new Vector3(0f, 0.020f, 0.175f), layer);
            m.GripAnchor = Anchor(t, "grip anchor", new Vector3(0f, -0.075f, -0.026f), layer);
            m.ForegripAnchor = Anchor(t, "support hand", new Vector3(-0.030f, -0.100f, -0.010f), layer);
            m.MagAnchor = Anchor(t, "mag anchor", new Vector3(0f, -0.195f, -0.045f), layer);
            m.StockAnchor = Anchor(t, "butt", new Vector3(0f, -0.150f, -0.045f), layer);
            m.HipOffset = new Vector3(0.135f, -0.130f, 0.32f);
            m.SwayScale = 1.25f;
            m.HoldsOpenWhenEmpty = true;
            return m;
        }
    
        // ------------------------------------------------------------------ M700
        /// <summary>
        /// A bolt gun: long barrel, heavy stock, five rounds, and a scope that is most of its length.
        /// The bolt handle sticks out to the right and cycles on every shot, which is the read that
        /// tells you at a glance this is not something you can hold a trigger down on.
        /// </summary>
        static WeaponModel BuildSniper(Transform parent, Palette palette, int layer)
        {
            WeaponModel m = new WeaponModel();
            m.Root = Blockout.Group(parent, "M700", Vector3.zero, layer);
            Transform t = m.Root.transform;

            Piece(t, "receiver", new Vector3(0f, 0.014f, 0.03f), new Vector3(0.048f, 0.060f, 0.30f), palette.Gun, layer);
            Piece(t, "top rail", new Vector3(0f, 0.050f, 0.03f), new Vector3(0.038f, 0.014f, 0.28f), palette.GunDark, layer);
            Tube(t, "barrel", new Vector3(0f, 0.010f, 0.44f), new Vector3(0.030f, 0.030f, 0.52f), palette.Metal, layer);
            Tube(t, "muzzle brake", new Vector3(0f, 0.010f, 0.725f), new Vector3(0.042f, 0.042f, 0.075f), palette.GunDark, layer);

            // A laminated stock: a wide fore-end you can rest on anything, and a cheek piece.
            Piece(t, "fore end", new Vector3(0f, -0.030f, 0.26f), new Vector3(0.062f, 0.058f, 0.34f), palette.WallDark, layer);
            Piece(t, "belly", new Vector3(0f, -0.052f, 0.05f), new Vector3(0.056f, 0.048f, 0.20f), palette.WallDark, layer);
            Piece(t, "grip", new Vector3(0f, -0.108f, -0.075f), new Vector3(0.042f, 0.130f, 0.062f), palette.WallDark, layer, new Vector3(-14f, 0f, 0f));
            Piece(t, "comb", new Vector3(0f, 0.012f, -0.215f), new Vector3(0.050f, 0.062f, 0.20f), palette.WallDark, layer);
            Piece(t, "cheek piece", new Vector3(0f, 0.050f, -0.225f), new Vector3(0.044f, 0.026f, 0.150f), palette.WallDark, layer);
            Piece(t, "butt pad", new Vector3(0f, -0.012f, -0.322f), new Vector3(0.054f, 0.115f, 0.030f), palette.GunDark, layer);
            Piece(t, "bipod stud", new Vector3(0f, -0.058f, 0.40f), new Vector3(0.018f, 0.020f, 0.014f), palette.Metal, layer);
            Piece(t, "sling loop", new Vector3(0f, -0.058f, 0.14f), new Vector3(0.024f, 0.024f, 0.010f), palette.Metal, layer);

            Serrations(t, palette, layer, new Vector3(0f, -0.060f, 0.26f), new Vector3(0.058f, 0.006f, 0.014f), 0.052f, 5, true);
            TriggerGroup(t, palette, layer, -0.044f, -0.020f);

            // The bolt. Body along the receiver, handle out to the right and turned down.
            GameObject bolt = Blockout.Group(t, "bolt", new Vector3(0f, 0.030f, -0.055f), layer);
            Piece(bolt.transform, "body", Vector3.zero, new Vector3(0.024f, 0.024f, 0.090f), palette.Metal, layer);
            Piece(bolt.transform, "handle", new Vector3(0.034f, -0.012f, -0.030f), new Vector3(0.052f, 0.016f, 0.016f), palette.Metal, layer);
            Piece(bolt.transform, "knob", new Vector3(0.060f, -0.020f, -0.030f), new Vector3(0.022f, 0.022f, 0.022f), palette.GunDark, layer);
            m.Bolt = bolt.transform;
            m.BoltTravel = new Vector3(0f, 0f, -0.095f);

            // Internal box magazine: a floor plate rather than a stick.
            GameObject magazine = Blockout.Group(t, "magazine", new Vector3(0f, -0.072f, 0.06f), layer);
            Piece(magazine.transform, "floor plate", Vector3.zero, new Vector3(0.044f, 0.016f, 0.110f), palette.Metal, layer);
            Piece(magazine.transform, "box", new Vector3(0f, -0.030f, 0f), new Vector3(0.038f, 0.048f, 0.096f), palette.GunDark, layer);
            m.Magazine = magazine.transform;
            m.MagazineEject = new Vector3(0f, -0.24f, 0.02f);

            // Irons exist as a backup and because a gun with no sight at all looks unfinished, but the
            // scope is what it is for and what it starts on.
            BuildIronSights(m, t, palette, layer, 0.070f, -0.070f, 0.560f, 0.026f);
            BuildRedDot(m, t, palette, layer, 0.058f, 0.02f);
            BuildHolo(m, t, palette, layer, 0.058f, 0.02f);
            BuildScope(m, t, palette, layer, 0.058f, 0.045f, 0.34f);
            m.SetSight((int)SightKind.Scope);

            m.Muzzle = Anchor(t, "muzzle", new Vector3(0f, 0.010f, 0.775f), layer);
            m.GripAnchor = Anchor(t, "grip anchor", new Vector3(0f, -0.088f, -0.066f), layer);
            m.ForegripAnchor = Anchor(t, "foregrip anchor", new Vector3(0f, -0.038f, 0.37f), layer);
            m.MagAnchor = Anchor(t, "mag anchor", new Vector3(0f, -0.17f, 0.06f), layer);
            m.StockAnchor = Anchor(t, "stock", new Vector3(0f, -0.012f, -0.322f), layer);
            m.HipOffset = new Vector3(0.175f, -0.165f, 0.24f);
            m.SwayScale = 1.35f;
            m.HoldsOpenWhenEmpty = true;
            return m;
        }
    }
}
