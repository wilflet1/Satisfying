using System;

namespace Satisfying.Shared
{
    /// <summary>Server-authoritative weapon numbers. Shared so client prediction of spread/recoil matches.</summary>
    [Serializable]
    public class WeaponTuning
    {
        public string name = "AR";

        // The bottom of the range is 20, not 60: a bolt gun is a shot every second and a half and the
        // slider has to be able to say so.
        [Tune("Weapon", 20f, 1400f, Tip = "Rounds per minute. A bolt action lives at the bottom of this.")]
        public float rpm = 650f;

        [Tune("Weapon", 1f, 150f, Tip = "Chest damage at point blank. Every other zone is relative to this.")]
        public float damage = 24f;

        [Tune("Weapon", 1f, 5f, Tip = "Headshot damage multiplier.")]
        public float headMultiplier = 2.6f;

        [Tune("Weapon", 1f, 4f, Tip = "Neck damage multiplier - the sliver above the plate carrier.")]
        public float neckMultiplier = 1.9f;

        [Tune("Weapon", 0.5f, 3f, Tip = "Stomach damage multiplier - under the armour, over the belt.")]
        public float stomachMultiplier = 1.25f;

        [Tune("Weapon", 0.2f, 1.5f, Tip = "Leg and foot damage multiplier.")]
        public float limbMultiplier = 0.78f;

        [Tune("Weapon", 0.2f, 1.5f, Tip = "Arm damage multiplier. Separate from the legs so a rifle can be made to drop someone who takes it in the thigh but not someone who only loses an arm to it.")]
        public float armMultiplier = 0.78f;

        [Tune("Weapon", 1f, 200f, Tip = "Distance where damage starts dropping.")]
        public float falloffStart = 28f;

        [Tune("Weapon", 2f, 400f, Tip = "Distance where damage reaches its minimum.")]
        public float falloffEnd = 95f;

        [Tune("Weapon", 0.05f, 1f)]
        public float falloffMinMul = 0.55f;

        [Tune("Weapon", 10f, 500f)]
        public float range = 320f;

        [Tune("Weapon", 0f, 1f, Tip = "Penetration budget, in metres of plasterboard. 0 stops at the first thing it touches.")]
        public float penetration = 0.14f;

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

        [Tune("Weapon", 0f, 0.7f, Tip = "How far ahead of the firing hand the support hand sits. A rifle is a long way; a pistol is no distance at all.")]
        public float supportHandReach = 0.34f;

        [Tune("Weapon", -0.15f, 0.2f, Tip = "How far above the firing hand the support hand sits, measured along the weapon rather than the world.")]
        public float supportHandRise = 0.045f;

        [Tune("Weapon", 0.05f, 1.5f, Tip = "Seconds to fully aim this weapon.")]
        public float adsTime = 0.2f;

        [Tune("Weapon", 1f, 12f, Tip = "Pellets per shot (shotguns).")]
        public float pellets = 1f;

        [Tune("Weapon", 0f, 1f, Tip = "1 = full auto, 0 = semi automatic.")]
        public float automatic = 1f;

        [Tune("Weapon", 0f, 5f, Tip = "Seconds your vision is wrecked by a head hit you survive. A rifle round rings a helmet a great deal harder than a pistol round does.")]
        public float concussionTime = 1.9f;

        [Tune("Weapon", 0f, 1f, Tip = "How badly a survived head hit blurs your vision, at its worst.")]
        public float concussionStrength = 0.85f;

        [Tune("Weapon", 10f, 400f, Tip = "Metres this weapon's report carries. It decides how much wall a shot can be heard through, not just how loud it is up close.")]
        public float soundCarry = 170f;

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
            // Three familiar shapes with clearly different jobs: a rifle you can hold an angle with,
            // a submachine gun for the blockhouse, and a pistol that rewards a steady hand.
            WeaponTuning m4 = new WeaponTuning();
            m4.name = "M4A1";
            m4.rpm = 760f;
            m4.damage = 25f;
            m4.headMultiplier = 2.5f;
            m4.neckMultiplier = 1.9f;
            m4.stomachMultiplier = 1.25f;
            m4.limbMultiplier = 0.8f;
            m4.armMultiplier = 0.8f;
            m4.penetration = 0.28f;
            m4.falloffStart = 32f;
            m4.falloffEnd = 100f;
            m4.falloffMinMul = 0.62f;
            m4.range = 400f;
            m4.spreadBase = 1.05f;
            m4.spreadAdsMul = 0.07f;
            m4.spreadPerShot = 0.30f;
            m4.spreadRecovery = 4.6f;
            m4.recoilVertical = 1.25f;
            m4.recoilHorizontal = 0.4f;
            m4.recoilRecoverFraction = 0.74f;
            m4.magSize = 30f;
            m4.reloadTime = 2.15f;
            m4.concussionTime = 2.3f;
            m4.concussionStrength = 0.95f;
            m4.soundCarry = 210f;
            m4.supportHandReach = 0.415f;
            m4.supportHandRise = 0.055f;
            m4.adsTime = 0.21f;

            WeaponTuning mp5 = new WeaponTuning();
            mp5.name = "MP5";
            mp5.rpm = 800f;
            mp5.damage = 18f;
            mp5.headMultiplier = 2.2f;
            mp5.neckMultiplier = 1.7f;
            mp5.stomachMultiplier = 1.2f;
            mp5.limbMultiplier = 0.85f;
            mp5.armMultiplier = 0.85f;
            mp5.penetration = 0.17f;
            mp5.falloffStart = 16f;
            mp5.falloffEnd = 48f;
            mp5.falloffMinMul = 0.42f;
            mp5.range = 260f;
            mp5.spreadBase = 1.35f;
            mp5.spreadAdsMul = 0.1f;
            mp5.spreadPerShot = 0.24f;
            mp5.spreadRecovery = 5.6f;
            mp5.recoilVertical = 0.78f;
            mp5.recoilHorizontal = 0.46f;
            mp5.recoilRecoverFraction = 0.8f;
            mp5.magSize = 30f;
            mp5.reloadTime = 1.85f;
            mp5.concussionTime = 1.5f;
            mp5.concussionStrength = 0.7f;
            mp5.soundCarry = 150f;
            mp5.supportHandReach = 0.305f;
            mp5.supportHandRise = 0.037f;
            mp5.adsTime = 0.15f;

            WeaponTuning usp = new WeaponTuning();
            usp.name = "USP45";
            usp.rpm = 420f;
            usp.damage = 41f;
            usp.headMultiplier = 2.4f;
            usp.neckMultiplier = 1.85f;
            usp.stomachMultiplier = 1.3f;
            usp.limbMultiplier = 0.75f;
            usp.armMultiplier = 0.75f;
            usp.penetration = 0.11f;
            usp.falloffStart = 18f;
            usp.falloffEnd = 55f;
            usp.falloffMinMul = 0.55f;
            usp.range = 240f;
            usp.spreadBase = 0.85f;
            usp.spreadAdsMul = 0.06f;
            usp.spreadPerShot = 0.75f;
            usp.spreadRecovery = 6.5f;
            usp.recoilVertical = 1.9f;
            usp.recoilHorizontal = 0.35f;
            usp.recoilRecoverFraction = 0.85f;
            usp.magSize = 12f;
            usp.reloadTime = 1.7f;
            usp.concussionTime = 0.85f;
            usp.concussionStrength = 0.45f;
            usp.soundCarry = 120f;
            usp.supportHandReach = 0.020f;
            usp.supportHandRise = -0.025f;
            usp.adsTime = 0.13f;
            usp.automatic = 0f;

            // ---- a 20 mm anti-materiel rifle. Hopeless from the hip and exact through the glass,
            // which is the whole trade: you cannot take a snap shot with it and you do not need a
            // second one. Two rounds in the magazine, and getting on the glass takes half a second.
            WeaponTuning sniper = new WeaponTuning();
            sniper.name = "M20 AMR";
            sniper.rpm = 40f;
            sniper.automatic = 0f;

            // 105 to the chest, and every zone but an arm is at or above 1.0, so anything except an
            // arm is one round inside the falloff. An arm is 0.9 - about five health left, which is a
            // man who is alive, knows exactly where you are, and cannot take another one.
            sniper.damage = 105f;
            sniper.headMultiplier = 2.2f;
            sniper.neckMultiplier = 1.6f;
            sniper.stomachMultiplier = 1.15f;
            sniper.limbMultiplier = 1.0f;
            sniper.armMultiplier = 0.9f;
            // Through a stud wall and out the other side still lethal, which is what it is for.
            sniper.penetration = 0.95f;

            // Nothing drops off inside 55 m, so "one tap within 50" is a property of the numbers
            // rather than a hope. SniperTests asserts it.
            sniper.falloffStart = 55f;
            sniper.falloffEnd = 220f;
            sniper.falloffMinMul = 0.62f;
            sniper.range = 400f;

            // Six and a half degrees from the hip is roughly a barn door at 20 m. Aimed, it is zero:
            // not "small", zero, because a rifle you have set up on is not a dice roll.
            sniper.spreadBase = 6.5f;
            sniper.spreadAdsMul = 0f;
            sniper.spreadMovePerSpeed = 0.9f;
            sniper.spreadCrouchMul = 0.9f;
            sniper.spreadProneMul = 0.8f;
            sniper.spreadPerShot = 2.4f;
            sniper.spreadRecovery = 3f;

            sniper.recoilVertical = 7.5f;
            sniper.recoilHorizontal = 0.7f;
            sniper.recoilRecoverFraction = 0.55f;
            sniper.recoilRecoverSpeed = 4f;

            sniper.magSize = 3f;
            sniper.reloadTime = 4.2f;
            sniper.adsTime = 0.55f;
            sniper.concussionTime = 2.8f;
            sniper.concussionStrength = 1f;
            sniper.soundCarry = 300f;
            sniper.supportHandReach = 0.44f;
            sniper.supportHandRise = 0.05f;

            return new WeaponTuning[] { m4, mp5, usp, sniper };
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
        public PenetrationTuning penetration = new PenetrationTuning();
        public KothTuning koth = new KothTuning();
        public GrenadeTuning grenade = new GrenadeTuning();
        public WeaponTuning[] weapons = WeaponTuning.DefaultLoadout();
        public SightTuning[] sights = SightTuning.Defaults();

        public WeaponTuning Weapon(int index)
        {
            if (weapons == null || weapons.Length == 0) return new WeaponTuning();
            return weapons[MathK.Clamp(index, 0, weapons.Length - 1)];
        }

        public SightTuning Sight(int index)
        {
            if (sights == null || sights.Length == 0) return new SightTuning();
            return sights[MathK.Clamp(index, 0, sights.Length - 1)];
        }

        public GameTuning Clone()
        {
            GameTuning c = new GameTuning();
            c.move = move.Clone();
            c.match = match.Clone();
            c.penetration = penetration.Clone();
            c.koth = koth.Clone();
            c.grenade = grenade.Clone();
            c.weapons = new WeaponTuning[weapons.Length];
            for (int i = 0; i < weapons.Length; i++) c.weapons[i] = weapons[i].Clone();
            c.sights = new SightTuning[sights.Length];
            for (int i = 0; i < sights.Length; i++) c.sights[i] = sights[i].Clone();
            return c;
        }
    }
}
