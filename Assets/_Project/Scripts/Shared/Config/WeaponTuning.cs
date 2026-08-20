using System;

namespace Satisfying.Shared
{
    /// <summary>Server-authoritative weapon numbers. Shared so client prediction of spread/recoil matches.</summary>
    [Serializable]
    public class WeaponTuning
    {
        public string name = "AR";

        [Tune("Weapon", 60f, 1400f, Tip = "Rounds per minute.")]
        public float rpm = 650f;

        [Tune("Weapon", 1f, 150f, Tip = "Body damage at point blank.")]
        public float damage = 24f;

        [Tune("Weapon", 1f, 5f, Tip = "Headshot damage multiplier.")]
        public float headMultiplier = 2.6f;

        [Tune("Weapon", 0.2f, 1.5f, Tip = "Leg/arm damage multiplier.")]
        public float limbMultiplier = 0.78f;

        [Tune("Weapon", 1f, 200f, Tip = "Distance where damage starts dropping.")]
        public float falloffStart = 28f;

        [Tune("Weapon", 2f, 400f, Tip = "Distance where damage reaches its minimum.")]
        public float falloffEnd = 95f;

        [Tune("Weapon", 0.05f, 1f)]
        public float falloffMinMul = 0.55f;

        [Tune("Weapon", 10f, 500f)]
        public float range = 220f;

        [Tune("Weapon", 0f, 8f, Tip = "Base cone of fire in degrees while standing still, hip fired.")]
        public float spreadBase = 1.15f;

        [Tune("Weapon", 0f, 1f, Tip = "Spread multiplier while aiming.")]
        public float spreadAdsMul = 0.08f;

        [Tune("Weapon", 0f, 6f, Tip = "Extra spread per metre/second of movement.")]
        public float spreadMovePerSpeed = 0.22f;

        [Tune("Weapon", 0f, 1f, Tip = "Spread multiplier while crouched.")]
        public float spreadCrouchMul = 0.72f;

        [Tune("Weapon", 0f, 1f, Tip = "Spread multiplier while prone.")]
        public float spreadProneMul = 0.45f;

        [Tune("Weapon", 0f, 8f, Tip = "Spread added per shot, decays back down.")]
        public float spreadPerShot = 0.32f;

        [Tune("Weapon", 0.5f, 30f, Tip = "Spread recovery per second.")]
        public float spreadRecovery = 4.5f;

        [Tune("Weapon", 0f, 12f, Tip = "Upward recoil kick in degrees.")]
        public float recoilVertical = 1.35f;

        [Tune("Weapon", 0f, 8f, Tip = "Horizontal recoil magnitude in degrees.")]
        public float recoilHorizontal = 0.42f;

        [Tune("Weapon", 0f, 1f, Tip = "How much of the recoil the game pulls back down for you.")]
        public float recoilRecoverFraction = 0.72f;

        [Tune("Weapon", 0.5f, 20f, Tip = "Recoil recovery speed.")]
        public float recoilRecoverSpeed = 6.5f;

        [Tune("Weapon", 1f, 200f)]
        public float magSize = 30f;

        [Tune("Weapon", 0.2f, 6f)]
        public float reloadTime = 2.1f;

        [Tune("Weapon", 0.05f, 1.5f, Tip = "Seconds to fully aim this weapon.")]
        public float adsTime = 0.2f;

        [Tune("Weapon", 1f, 12f, Tip = "Pellets per shot (shotguns).")]
        public float pellets = 1f;

        [Tune("Weapon", 0f, 1f, Tip = "1 = full auto, 0 = semi automatic.")]
        public float automatic = 1f;

        public bool IsAutomatic { get { return automatic >= 0.5f; } }
        public float ShotInterval { get { return 60f / MathK.Max(1f, rpm); } }
        public int MagSizeInt { get { return MathK.Max(1, MathK.RoundToInt(magSize)); } }
        public int PelletsInt { get { return MathK.Max(1, MathK.RoundToInt(pellets)); } }

        public float DamageAtRange(float distance)
        {
            float t = MathK.InverseLerp(falloffStart, MathK.Max(falloffStart + 0.01f, falloffEnd), distance);
            return damage * MathK.Lerp(1f, falloffMinMul, t);
        }

        public WeaponTuning Clone() { return (WeaponTuning)MemberwiseClone(); }

        public static WeaponTuning[] DefaultLoadout()
        {
            WeaponTuning ar = new WeaponTuning();
            ar.name = "AR-15";

            WeaponTuning smg = new WeaponTuning();
            smg.name = "SMG-9";
            smg.rpm = 900f;
            smg.damage = 17f;
            smg.headMultiplier = 2.2f;
            smg.falloffStart = 14f;
            smg.falloffEnd = 45f;
            smg.falloffMinMul = 0.4f;
            smg.spreadBase = 1.5f;
            smg.spreadPerShot = 0.26f;
            smg.recoilVertical = 0.85f;
            smg.recoilHorizontal = 0.5f;
            smg.magSize = 35f;
            smg.reloadTime = 1.8f;
            smg.adsTime = 0.15f;
            smg.range = 120f;

            WeaponTuning dmr = new WeaponTuning();
            dmr.name = "DMR-7";
            dmr.rpm = 260f;
            dmr.damage = 55f;
            dmr.headMultiplier = 2.2f;
            dmr.falloffStart = 65f;
            dmr.falloffEnd = 180f;
            dmr.falloffMinMul = 0.8f;
            dmr.spreadBase = 0.6f;
            dmr.spreadAdsMul = 0.02f;
            dmr.spreadPerShot = 0.8f;
            dmr.recoilVertical = 2.6f;
            dmr.recoilHorizontal = 0.6f;
            dmr.magSize = 10f;
            dmr.reloadTime = 2.4f;
            dmr.adsTime = 0.28f;
            dmr.automatic = 0f;
            dmr.range = 320f;

            return new WeaponTuning[] { ar, smg, dmr };
        }
    }

    /// <summary>1v1 match rules.</summary>
    [Serializable]
    public class MatchTuning
    {
        [Tune("Match", 1f, 50f, Tip = "Eliminations needed to win the duel.")]
        public float killsToWin = 10f;

        [Tune("Match", 0f, 15f)]
        public float respawnDelay = 2.2f;

        [Tune("Match", 10f, 200f)]
        public float maxHealth = 100f;

        [Tune("Match", 0f, 60f, Tip = "Seconds of spawn protection.")]
        public float spawnProtection = 1.2f;

        [Tune("Match", 0f, 30f, Tip = "Seconds of countdown once both duellists are in.")]
        public float warmupTime = 3f;

        public MatchTuning Clone() { return (MatchTuning)MemberwiseClone(); }
    }

    /// <summary>Everything the host owns and replicates. One object = one sync message.</summary>
    [Serializable]
    public class GameTuning
    {
        public MovementTuning move = new MovementTuning();
        public MatchTuning match = new MatchTuning();
        public WeaponTuning[] weapons = WeaponTuning.DefaultLoadout();

        public WeaponTuning Weapon(int index)
        {
            if (weapons == null || weapons.Length == 0) return new WeaponTuning();
            return weapons[MathK.Clamp(index, 0, weapons.Length - 1)];
        }

        public GameTuning Clone()
        {
            GameTuning c = new GameTuning();
            c.move = move.Clone();
            c.match = match.Clone();
            c.weapons = new WeaponTuning[weapons.Length];
            for (int i = 0; i < weapons.Length; i++) c.weapons[i] = weapons[i].Clone();
            return c;
        }
    }
}
