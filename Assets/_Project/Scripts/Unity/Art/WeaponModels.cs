using UnityEngine;

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
        public Transform SightAnchor;       // aligns with the screen centre when aiming

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
            Piece(t, "front sight", new Vector3(0f, 0.062f, 0.615f), new Vector3(0.012f, 0.036f, 0.014f), palette.Metal, layer);
            Piece(t, "rear sight", new Vector3(0f, 0.068f, -0.055f), new Vector3(0.030f, 0.030f, 0.030f), palette.Metal, layer);
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

            m.Muzzle = Anchor(t, "muzzle", new Vector3(0f, 0.012f, 0.76f), layer);
            m.GripAnchor = Anchor(t, "grip anchor", new Vector3(0f, -0.095f, -0.075f), layer);
            m.ForegripAnchor = Anchor(t, "foregrip anchor", new Vector3(0f, -0.040f, 0.34f), layer);
            m.MagAnchor = Anchor(t, "mag anchor", new Vector3(0f, -0.20f, 0.05f), layer);
            m.SightAnchor = Anchor(t, "sight", new Vector3(0f, 0.078f, -0.055f), layer);
            m.HipOffset = new Vector3(0.145f, -0.135f, 0.26f);
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
            Piece(t, "front sight hood", new Vector3(0f, 0.058f, 0.365f), new Vector3(0.038f, 0.042f, 0.032f), palette.Metal, layer);
            Piece(t, "rear drum", new Vector3(0f, 0.060f, -0.030f), new Vector3(0.036f, 0.038f, 0.036f), palette.Metal, layer);
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

            m.Muzzle = Anchor(t, "muzzle", new Vector3(0f, 0.012f, 0.43f), layer);
            m.GripAnchor = Anchor(t, "grip anchor", new Vector3(0f, -0.085f, -0.055f), layer);
            m.ForegripAnchor = Anchor(t, "foregrip anchor", new Vector3(0f, -0.048f, 0.25f), layer);
            m.MagAnchor = Anchor(t, "mag anchor", new Vector3(0f, -0.215f, 0.12f), layer);
            m.SightAnchor = Anchor(t, "sight", new Vector3(0f, 0.074f, -0.030f), layer);
            m.HipOffset = new Vector3(0.135f, -0.125f, 0.24f);
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
            Piece(slide.transform, "front sight", new Vector3(0f, 0.030f, 0.082f), new Vector3(0.010f, 0.014f, 0.010f), palette.Metal, layer);
            Piece(slide.transform, "rear sight", new Vector3(0f, 0.030f, -0.078f), new Vector3(0.028f, 0.014f, 0.012f), palette.Metal, layer);
            m.Bolt = slide.transform;
            m.BoltTravel = new Vector3(0f, 0f, -0.048f);

            GameObject magazine = Group(t, "magazine", new Vector3(0f, -0.108f, -0.030f), layer);
            magazine.transform.localRotation = Quaternion.Euler(-13f, 0f, 0f);
            Piece(magazine.transform, "body", Vector3.zero, new Vector3(0.026f, 0.125f, 0.038f), palette.Metal, layer);
            Piece(magazine.transform, "floor plate", new Vector3(0f, -0.072f, 0f), new Vector3(0.032f, 0.014f, 0.046f), palette.Gun, layer);
            m.Magazine = magazine.transform;
            m.MagazineEject = new Vector3(0f, -0.20f, 0f);
            m.MagazineEjectTilt = new Vector3(-18f, 0f, 6f);

            m.Muzzle = Anchor(t, "muzzle", new Vector3(0f, 0.020f, 0.175f), layer);
            m.GripAnchor = Anchor(t, "grip anchor", new Vector3(0f, -0.075f, -0.026f), layer);
            m.ForegripAnchor = Anchor(t, "support hand", new Vector3(-0.030f, -0.100f, -0.010f), layer);
            m.MagAnchor = Anchor(t, "mag anchor", new Vector3(0f, -0.195f, -0.045f), layer);
            m.SightAnchor = Anchor(t, "sight", new Vector3(0f, 0.054f, -0.020f), layer);
            m.HipOffset = new Vector3(0.115f, -0.115f, 0.30f);
            m.SwayScale = 1.25f;
            return m;
        }
    }
}
