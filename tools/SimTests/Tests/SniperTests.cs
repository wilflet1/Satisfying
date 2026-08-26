using Satisfying.Shared;

namespace Satisfying.Tests
{
    /// <summary>
    /// The bolt gun exists to do one specific thing, and "one round inside fifty metres" is a promise
    /// made of numbers that four separate knobs can quietly break. So it is asserted rather than
    /// eyeballed: change any of the multipliers, the damage or the falloff and this says so.
    /// </summary>
    public static class SniperTests
    {
        const int Sniper = 3;

        public static void Register()
        {
            TestRunner.Add("sniper/one round inside fifty metres, anywhere but an arm", () =>
            {
                GameTuning tuning = new GameTuning();
                WeaponTuning rifle = tuning.Weapon(Sniper);
                float health = tuning.match.maxHealth;

                HitZone[] lethal =
                {
                    HitZone.Head, HitZone.Neck, HitZone.Chest, HitZone.Stomach, HitZone.Leg, HitZone.Foot
                };

                float[] ranges = { 1f, 10f, 25f, 40f, 50f };
                for (int r = 0; r < ranges.Length; r++)
                {
                    for (int z = 0; z < lethal.Length; z++)
                    {
                        float damage = ShotSolver.Damage(rifle, lethal[z], ranges[r]);
                        Assert.True(damage >= health,
                            lethal[z] + " at " + ranges[r] + " m does " + damage.ToString("0.0") + ", needs " + health);
                    }
                }
            });

            TestRunner.Add("sniper/an arm leaves them alive and barely", () =>
            {
                GameTuning tuning = new GameTuning();
                WeaponTuning rifle = tuning.Weapon(Sniper);
                float health = tuning.match.maxHealth;

                // Survivable at every range inside the falloff, and never by much: a man who is still
                // up, knows exactly where you are, and cannot take a second one.
                float[] ranges = { 1f, 25f, 50f };
                for (int r = 0; r < ranges.Length; r++)
                {
                    float damage = ShotSolver.Damage(rifle, HitZone.Arm, ranges[r]);
                    Assert.True(damage < health, "an arm at " + ranges[r] + " m must not kill");
                    Assert.True(health - damage <= 12f,
                        "an arm at " + ranges[r] + " m should leave under 12, leaves " + (health - damage).ToString("0.0"));
                }
            });

            TestRunner.Add("sniper/hopeless from the hip, exact through the glass", () =>
            {
                GameTuning tuning = new GameTuning();
                WeaponTuning rifle = tuning.Weapon(Sniper);
                SightTuning scope = tuning.Sight((int)SightKind.Scope);

                PlayerSimState hip = new PlayerSimState();
                hip.Height = tuning.move.standHeight;
                hip.Stance = Stance.Stand;
                hip.Grounded = true;
                hip.Stamina = tuning.move.staminaMax;
                hip.ArmStamina = tuning.move.staminaMax;
                hip.Ads = 0f;

                PlayerSimState aimed = hip;
                aimed.Ads = 1f;

                float fromHip = MovementCore.CurrentSpread(in hip, tuning.move, rifle, scope);
                float throughGlass = MovementCore.CurrentSpread(in aimed, tuning.move, rifle, scope);

                Assert.True(fromHip > 5f, "from the hip it should be hopeless, is " + fromHip.ToString("0.00"));
                Assert.Near(throughGlass, 0f, 0.0001f, "aimed it should be exact, not nearly");

                // And worse than every other weapon from the hip, or it is not a trade.
                for (int w = 0; w < 3; w++)
                {
                    float other = MovementCore.CurrentSpread(in hip, tuning.move, tuning.Weapon(w),
                                                             tuning.Sight((int)SightKind.Iron));
                    Assert.True(fromHip > other, "the rifle should be the worst thing to hip fire");
                }
            });

            TestRunner.Add("sniper/the scope is a variable optic and clamps to its own range", () =>
            {
                GameTuning tuning = new GameTuning();
                SightTuning scope = tuning.Sight((int)SightKind.Scope);

                Assert.True(scope.IsScope, "the scope is a scope");
                Assert.True(scope.IsVariable, "and a variable one");

                // It goes down to life size. IsScope has to read the TOP of the range for that to be
                // possible - judging it on the bottom would stop a 1-18x being a scope at all, and the
                // picture would simply not be drawn.
                Assert.Near(scope.magnification, 1f, 0.001f, "the bottom of the range is 1x");
                Assert.True(scope.magnificationMax >= 10f, "and the top is worth having");
                Assert.Near(scope.ClampMagnification(0.2f), 1f, 0.001f, "never below life size");
                Assert.Near(scope.ClampMagnification(999f), scope.magnificationMax, 0.001f, "clamps down to the top");
                Assert.Near(scope.ClampMagnification(6f), 6f, 0.001f, "leaves what it can");

                // The other three are not scopes, or every gun in the game would sprout a tube.
                for (int i = 0; i < 3; i++)
                    Assert.False(tuning.Sight(i).IsScope, "sight " + i + " is not a scope");
            });

            TestRunner.Add("sniper/a sight index still fits the two bits it travels in", () =>
            {
                // SightIndex is written with WriteBits(2), so the scope at 3 is the last one that fits.
                GameTuning tuning = new GameTuning();
                Assert.Equal((int)SightKind.Scope, 3, "the scope is sight 3");
                Assert.True(tuning.sights.Length <= 4, "four sights is the wire limit");
                Assert.True(tuning.weapons.Length <= 8, "eight weapons is the wire limit");

                PlayerNetState state = new PlayerNetState();
                state.SightIndex = (byte)SightKind.Scope;
                state.WeaponIndex = Sniper;
                state.Health = 100;
                state.Ammo = 5;
                state.Height = 1.82f;

                NetBuffer b = new NetBuffer(256);
                b.ResetWrite();
                state.Write(b);
                byte[] packet = b.ToArray();

                NetBuffer r = new NetBuffer(256);
                r.ResetRead(packet, packet.Length);
                PlayerNetState back = PlayerNetState.Read(r);
                Assert.Equal(back.SightIndex, (int)SightKind.Scope, "sight survives the wire");
                Assert.Equal(back.WeaponIndex, Sniper, "weapon survives the wire");
            });
        }
    }
}
