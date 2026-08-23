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
        public Transform[] SightAnchors = new Transform[3];
        public GameObject[] SightModels = new GameObject[3];
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
                default: return BuildM4(parent, palette, layer);
            }
        }

        public static string Name(int index)
        {
            switch (index)
            {
                case 1: return "MP5";
                case 2: return "USP45";
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
                                    float sightTopY, float rearZ, float frontZ, float postWidth = 0.005f)
        {
            GameObject group = Group(t, "iron sights", Vector3.zero, layer);
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
            GameObject group = Group(t, "red dot", Vector3.zero, layer);
            float centreY = railY + 0.033f;

            Piece(group.transform, "mount", new Vector3(0f, railY + 0.010f, z), new Vector3(0.030f, 0.020f, 0.040f), palette.GunDark, layer);
            Piece(group.transform, "tube top", new Vector3(0f, centreY + 0.021f, z), new Vector3(0.044f, 0.006f, 0.062f), palette.Gun, layer);
            Piece(group.transform, "tube bottom", new Vector3(0f, centreY - 0.021f, z), new Vector3(0.044f, 0.006f, 0.062f), palette.Gun, layer);
            Piece(group.transform, "tube left", new Vector3(-0.019f, centreY, z), new Vector3(0.006f, 0.038f, 0.062f), palette.Gun, layer);
            Piece(group.transform, "tube right", new Vector3(0.019f, centreY, z), new Vector3(0.006f, 0.038f, 0.062f), palette.Gun, layer);

            m.SightModels[(int)SightKind.RedDot] = group;
            m.SightAnchors[(int)SightKind.RedDot] = Anchor(t, "red dot line", new Vector3(0f, centreY, z - 0.03f), layer);
        }

        /// <summary>A wide holographic window - a bigger, squarer sight picture than the tube.</summary>
        static void BuildHolo(WeaponModel m, Transform t, Palette palette, int layer, float railY, float z)
        {
            GameObject group = Group(t, "holo", Vector3.zero, layer);
            float centreY = railY + 0.036f;

            Piece(group.transform, "body", new Vector3(0f, railY + 0.018f, z - 0.045f), new Vector3(0.046f, 0.036f, 0.070f), palette.Gun, layer);
            Piece(group.transform, "hood top", new Vector3(0f, centreY + 0.026f, z), new Vector3(0.058f, 0.008f, 0.050f), palette.Gun, layer);
            Piece(group.transform, "hood left", new Vector3(-0.026f, centreY, z), new Vector3(0.007f, 0.052f, 0.050f), palette.Gun, layer);
            Piece(group.transform, "hood right", new Vector3(0.026f, centreY, z), new Vector3(0.007f, 0.052f, 0.050f), palette.Gun, layer);
            Piece(group.transform, "hood base", new Vector3(0f, centreY - 0.026f, z), new Vector3(0.058f, 0.007f, 0.050f), palette.GunDark, layer);

            m.SightModels[(int)SightKind.Holo] = group;
            m.SightAnchors[(int)SightKind.Holo] = Anchor(t, "holo line", new Vector3(0f, centreY, z - 0.03f), layer);
        }

        static GameObject Group(Transform parent, string name, Vector3 position, int layer)
        {
            GameObject go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go;
        }

        // ------------------------------------------------------------------ M4A1
        static WeaponModel BuildM4(Transform parent, Palette palette, int layer)
        {
            WeaponModel m = new WeaponModel();
            m.Root = Group(parent, "M4A1", Vector3.zero, layer);
            Transform t = m.Root.transform;

            Piece(t, "upper receiver", new Vector3(0f, 0.022f, 0.05f), new Vector3(0.052f, 0.072f, 0.30f), palette.Gun, layer);
            Piece(t, "lower receiver", new Vector3(0f, -0.032f, 0.02f), new Vector3(0.046f, 0.055f, 0.22f), palette.GunDark, layer);
            Piece(t, "top rail", new Vector3(0f, 0.064f, 0.10f), new Vector3(0.040f, 0.016f, 0.40f), palette.GunDark, layer);
            Piece(t, "handguard", new Vector3(0f, 0.006f, 0.33f), new Vector3(0.056f, 0.060f, 0.28f), palette.GunDark, layer);
            Piece(t, "barrel", new Vector3(0f, 0.012f, 0.55f), new Vector3(0.024f, 0.024f, 0.30f), palette.Metal, layer);
            Piece(t, "gas block", new Vector3(0f, 0.030f, 0.50f), new Vector3(0.034f, 0.040f, 0.05f), palette.Metal, layer);
            Piece(t, "muzzle device", new Vector3(0f, 0.012f, 0.715f), new Vector3(0.034f, 0.034f, 0.07f), palette.Metal, layer);
            Piece(t, "grip", new Vector3(0f, -0.115f, -0.085f), new Vector3(0.040f, 0.145f, 0.058f), palette.GunDark, layer, new Vector3(-17f, 0f, 0f));
            Piece(t, "buffer tube", new Vector3(0f, 0.010f, -0.215f), new Vector3(0.040f, 0.050f, 0.20f), palette.Gun, layer);
            Piece(t, "stock", new Vector3(0f, -0.005f, -0.245f), new Vector3(0.056f, 0.085f, 0.13f), palette.GunDark, layer);
            Piece(t, "butt plate", new Vector3(0f, -0.010f, -0.325f), new Vector3(0.058f, 0.105f, 0.035f), palette.Gun, layer);

            GameObject bolt = Group(t, "charging handle", new Vector3(0f, 0.058f, -0.085f), layer);
            Piece(bolt.transform, "handle", Vector3.zero, new Vector3(0.048f, 0.016f, 0.055f), palette.Metal, layer);
            m.Bolt = bolt.transform;
            m.BoltTravel = new Vector3(0f, 0f, -0.075f);

            GameObject magazine = Group(t, "magazine", new Vector3(0f, -0.135f, 0.045f), layer);
            magazine.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
            Piece(magazine.transform, "body", Vector3.zero, new Vector3(0.036f, 0.165f, 0.072f), palette.GunDark, layer);
            Piece(magazine.transform, "floor plate", new Vector3(0f, -0.090f, 0f), new Vector3(0.040f, 0.018f, 0.078f), palette.Gun, layer);
            m.Magazine = magazine.transform;
            m.MagazineEject = new Vector3(0f, -0.26f, 0.02f);

            BuildIronSights(m, t, palette, layer, 0.086f, -0.055f, 0.615f);
            BuildRedDot(m, t, palette, layer, 0.072f, 0.02f);
            BuildHolo(m, t, palette, layer, 0.072f, 0.02f);
            m.SetSight((int)SightKind.Iron);

            m.Muzzle = Anchor(t, "muzzle", new Vector3(0f, 0.012f, 0.76f), layer);
            m.GripAnchor = Anchor(t, "grip anchor", new Vector3(0f, -0.095f, -0.075f), layer);
            m.ForegripAnchor = Anchor(t, "foregrip anchor", new Vector3(0f, -0.040f, 0.34f), layer);
            m.MagAnchor = Anchor(t, "mag anchor", new Vector3(0f, -0.20f, 0.05f), layer);
            m.StockAnchor = Anchor(t, "stock", new Vector3(0f, -0.010f, -0.325f), layer);
            m.HipOffset = new Vector3(0.155f, -0.150f, 0.30f);
            return m;
        }

        // ------------------------------------------------------------------ MP5
        static WeaponModel BuildMp5(Transform parent, Palette palette, int layer)
        {
            WeaponModel m = new WeaponModel();
            m.Root = Group(parent, "MP5", Vector3.zero, layer);
            Transform t = m.Root.transform;

            Piece(t, "receiver", new Vector3(0f, 0.018f, 0.03f), new Vector3(0.050f, 0.070f, 0.28f), palette.Gun, layer);
            Piece(t, "handguard", new Vector3(0f, -0.004f, 0.245f), new Vector3(0.060f, 0.068f, 0.20f), palette.GunDark, layer);
            Piece(t, "barrel", new Vector3(0f, 0.012f, 0.375f), new Vector3(0.022f, 0.022f, 0.10f), palette.Metal, layer);
            Piece(t, "trigger group", new Vector3(0f, -0.035f, -0.030f), new Vector3(0.046f, 0.050f, 0.16f), palette.GunDark, layer);
            Piece(t, "grip", new Vector3(0f, -0.105f, -0.065f), new Vector3(0.038f, 0.135f, 0.055f), palette.GunDark, layer, new Vector3(-15f, 0f, 0f));
            Piece(t, "stock rail", new Vector3(0f, 0.010f, -0.195f), new Vector3(0.046f, 0.038f, 0.19f), palette.Gun, layer);
            Piece(t, "butt", new Vector3(0f, -0.005f, -0.290f), new Vector3(0.052f, 0.090f, 0.035f), palette.GunDark, layer);

            GameObject bolt = Group(t, "cocking handle", new Vector3(-0.046f, 0.050f, 0.215f), layer);
            Piece(bolt.transform, "lever", Vector3.zero, new Vector3(0.030f, 0.022f, 0.048f), palette.Metal, layer);
            m.Bolt = bolt.transform;
            m.BoltTravel = new Vector3(0f, 0f, -0.055f);

            GameObject magazine = Group(t, "magazine", new Vector3(0f, -0.140f, 0.115f), layer);
            magazine.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
            Piece(magazine.transform, "body", Vector3.zero, new Vector3(0.032f, 0.195f, 0.056f), palette.GunDark, layer);
            Piece(magazine.transform, "floor plate", new Vector3(0f, -0.105f, 0f), new Vector3(0.036f, 0.016f, 0.062f), palette.Gun, layer);
            m.Magazine = magazine.transform;
            m.MagazineEject = new Vector3(0f, -0.27f, 0.01f);

            BuildIronSights(m, t, palette, layer, 0.079f, -0.030f, 0.365f);
            BuildRedDot(m, t, palette, layer, 0.053f, 0.03f);
            BuildHolo(m, t, palette, layer, 0.053f, 0.03f);
            m.SetSight((int)SightKind.Iron);

            m.Muzzle = Anchor(t, "muzzle", new Vector3(0f, 0.012f, 0.43f), layer);
            m.GripAnchor = Anchor(t, "grip anchor", new Vector3(0f, -0.085f, -0.055f), layer);
            m.ForegripAnchor = Anchor(t, "foregrip anchor", new Vector3(0f, -0.048f, 0.25f), layer);
            m.MagAnchor = Anchor(t, "mag anchor", new Vector3(0f, -0.215f, 0.12f), layer);
            m.StockAnchor = Anchor(t, "stock", new Vector3(0f, -0.005f, -0.290f), layer);
            m.HipOffset = new Vector3(0.145f, -0.140f, 0.28f);
            m.SwayScale = 1.1f;
            return m;
        }

        // ------------------------------------------------------------------ USP45
        static WeaponModel BuildUsp45(Transform parent, Palette palette, int layer)
        {
            WeaponModel m = new WeaponModel();
            m.Root = Group(parent, "USP45", Vector3.zero, layer);
            Transform t = m.Root.transform;

            Piece(t, "frame", new Vector3(0f, -0.028f, 0.020f), new Vector3(0.032f, 0.046f, 0.145f), palette.GunDark, layer);
            Piece(t, "trigger guard", new Vector3(0f, -0.052f, 0.008f), new Vector3(0.026f, 0.040f, 0.018f), palette.GunDark, layer);
            Piece(t, "grip", new Vector3(0f, -0.105f, -0.030f), new Vector3(0.036f, 0.130f, 0.050f), palette.GunDark, layer, new Vector3(-13f, 0f, 0f));
            Piece(t, "barrel", new Vector3(0f, 0.018f, 0.150f), new Vector3(0.018f, 0.018f, 0.030f), palette.Metal, layer);

            GameObject slide = Group(t, "slide", new Vector3(0f, 0.020f, 0.055f), layer);
            Piece(slide.transform, "body", Vector3.zero, new Vector3(0.034f, 0.046f, 0.195f), palette.Gun, layer);
            m.Bolt = slide.transform;
            m.BoltTravel = new Vector3(0f, 0f, -0.048f);

            GameObject magazine = Group(t, "magazine", new Vector3(0f, -0.108f, -0.030f), layer);
            magazine.transform.localRotation = Quaternion.Euler(-13f, 0f, 0f);
            Piece(magazine.transform, "body", Vector3.zero, new Vector3(0.026f, 0.125f, 0.038f), palette.Metal, layer);
            Piece(magazine.transform, "floor plate", new Vector3(0f, -0.072f, 0f), new Vector3(0.032f, 0.014f, 0.046f), palette.Gun, layer);
            m.Magazine = magazine.transform;
            m.MagazineEject = new Vector3(0f, -0.20f, 0f);
            m.MagazineEjectTilt = new Vector3(-18f, 0f, 6f);

            // The pistol's sights live on the frame rather than the slide: a sight picture that
            // reciprocates with every shot is not one you can aim with.
            BuildIronSights(m, t, palette, layer, 0.056f, -0.020f, 0.138f, 0.004f);
            BuildRedDot(m, t, palette, layer, 0.043f, 0.02f);
            BuildHolo(m, t, palette, layer, 0.043f, 0.02f);
            m.SetSight((int)SightKind.Iron);

            m.Muzzle = Anchor(t, "muzzle", new Vector3(0f, 0.020f, 0.175f), layer);
            m.GripAnchor = Anchor(t, "grip anchor", new Vector3(0f, -0.075f, -0.026f), layer);
            m.ForegripAnchor = Anchor(t, "support hand", new Vector3(-0.030f, -0.100f, -0.010f), layer);
            m.MagAnchor = Anchor(t, "mag anchor", new Vector3(0f, -0.195f, -0.045f), layer);
            m.StockAnchor = Anchor(t, "butt", new Vector3(0f, -0.150f, -0.045f), layer);
            m.HipOffset = new Vector3(0.135f, -0.130f, 0.32f);
            m.SwayScale = 1.25f;
            return m;
        }
    }
}
