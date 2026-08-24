using Satisfying.Shared;

namespace Satisfying.Tests
{
    public static class CombatTests
    {
        public static void Register()
        {
            TestRunner.Add("net/a round on a range target rings back with the distance", () =>
            {
                // Targets are not players: no damage, no score, just the confirmation. And it has to
                // work with nobody else on the server, which is when a range is actually used.
                WorldModel model = new WorldModel();
                model.AddTarget(new Vec3(0f, 1.15f, 40f), new Vec3(0.5f, 0.7f, 0.28f), false);
                model.AddTarget(new Vec3(0f, 1.68f, 40f), new Vec3(0.24f, 0.26f, 0.24f), true);

                SpawnSet spawns = new SpawnSet();
                spawns.Add(new Vec3(0f, 0f, 0f), 0f);

                NetHarness h = new NetHarness(BoxWorld.FlatGround(140f), spawns, model);
                h.Server.Tuning.match.warmupTime = 0f;
                for (int i = 0; i < h.Server.Tuning.weapons.Length; i++)
                {
                    h.Server.Tuning.weapons[i].spreadBase = 0f;
                    h.Server.Tuning.weapons[i].spreadPerShot = 0f;
                    h.Server.Tuning.weapons[i].spreadMovePerSpeed = 0f;
                }
                h.Server.PushTuning();

                NetClient shooter = h.AddClient("shooter");
                Assert.True(h.WaitForConnect(), "connected");
                h.Advance(1f);
                Assert.True(h.Server.Phase != MatchPhase.Live, "and is alone, so the match is not live");

                h.Bots[0].Behaviour = tick =>
                {
                    float eye = h.ServerPlayerOf(shooter) != null
                        ? h.ServerPlayerOf(shooter).Sim.EyeHeight(h.Server.Tuning.move) : 1.6f;
                    InputCommand c = InputCommand.Default(tick);
                    c.Yaw = 0f;
                    c.Pitch = ViewMath.PitchOf(new Vec3(0f, 1.15f - eye, 40f).Normalized);
                    if (tick % 30 == 0) c.Buttons |= Buttons.Fire;
                    return c;
                };
                h.Advance(2.5f);

                Assert.Greater(h.Sinks[0].TargetHits, 0.5f, "the shooter was told the target was hit");
                Assert.Near(h.Sinks[0].LastTargetDistance, 40f, 2f, "and how far out it was");
            });

            TestRunner.Add("combat/spread is identical on both machines", () =>
            {
                Vec3 aim = new Vec3(0.3f, -0.1f, 1f).Normalized;
                for (uint shot = 0; shot < 20; shot++)
                {
                    Vec3 onClient = ShotSolver.PelletDirection(aim, 2.5f, 3, shot, 0);
                    Vec3 onServer = ShotSolver.PelletDirection(aim, 2.5f, 3, shot, 0);
                    Assert.True(onClient == onServer, "same seed produces the same ray");
                }

                Vec3 other = ShotSolver.PelletDirection(aim, 2.5f, 4, 0, 0);
                Vec3 mine = ShotSolver.PelletDirection(aim, 2.5f, 3, 0, 0);
                Assert.True(!(other == mine), "different shooters roll differently");
            });

            TestRunner.Add("combat/spread stays inside the advertised cone", () =>
            {
                Vec3 aim = Vec3.Forward;
                float cone = 3f;
                float worst = 0f;
                for (uint shot = 0; shot < 500; shot++)
                {
                    Vec3 dir = ShotSolver.PelletDirection(aim, cone, 1, shot, 0);
                    float angle = MathK.Acos(MathK.Clamp(Vec3.Dot(dir, aim), -1f, 1f)) * MathK.Rad2Deg;
                    if (angle > worst) worst = angle;
                }
                Assert.Less(worst, cone + 0.01f, "no pellet leaves the cone");
                Assert.Greater(worst, cone * 0.8f, "the cone is actually used");
            });

            TestRunner.Add("combat/zero spread fires exactly down the sights", () =>
            {
                Vec3 aim = new Vec3(-0.4f, 0.2f, 1f).Normalized;
                Vec3 dir = ShotSolver.PelletDirection(aim, 0f, 2, 7, 0);
                Assert.Near(Vec3.Distance(dir, aim), 0f, 0.0001f, "pinpoint");
            });

            TestRunner.Add("combat/hitbox separates head, body and legs", () =>
            {
                MovementTuning t = new MovementTuning();
                PlayerSimState s = PlayerSimState.Spawn(Vec3.Zero, 0f, t, WeaponTuning.DefaultLoadout()[0]);
                PlayerHitbox box = PlayerHitbox.FromState(in s, t);

                HitTestResult r;
                Vec3 from = new Vec3(0f, 1.72f, -6f);
                Assert.True(RayGeometry.TestPlayer(from, Vec3.Forward, in box, 20f, out r), "ray reaches the player");
                Assert.Equal((int)r.Zone, (int)HitZone.Head, "eye level is a headshot");

                from = new Vec3(0f, 1.1f, -6f);
                Assert.True(RayGeometry.TestPlayer(from, Vec3.Forward, in box, 20f, out r), "chest ray hits");
                Assert.Equal((int)r.Zone, (int)HitZone.Body, "chest is a body shot");

                from = new Vec3(0f, 0.4f, -6f);
                Assert.True(RayGeometry.TestPlayer(from, Vec3.Forward, in box, 20f, out r), "leg ray hits");
                Assert.Equal((int)r.Zone, (int)HitZone.Limb, "legs are limbs");

                from = new Vec3(0f, 2.4f, -6f);
                Assert.False(RayGeometry.TestPlayer(from, Vec3.Forward, in box, 20f, out r), "over the head is a miss");
            });

            TestRunner.Add("combat/leaning exposes your head past cover", () =>
            {
                MovementTuning t = new MovementTuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = PlayerSimState.Spawn(Vec3.Zero, 0f, t, WeaponTuning.DefaultLoadout()[0]);

                // A ray down the corner line: misses the centred player, catches the leaning one.
                Vec3 from = new Vec3(t.leanOffset * 0.9f, 1.72f, -6f);
                HitTestResult r;

                PlayerHitbox centred = PlayerHitbox.FromState(in s, t);
                bool hitCentred = RayGeometry.TestPlayer(from, Vec3.Forward, in centred, 20f, out r) && r.Zone == HitZone.Head;

                InputCommand c = InputCommand.Default(0);
                c.LeanAxis = 1f;
                Sim.Run(ref s, c, t, w, 1f);
                PlayerHitbox leaning = PlayerHitbox.FromState(in s, t);
                bool hitLeaning = RayGeometry.TestPlayer(from, Vec3.Forward, in leaning, 20f, out r) && r.Zone == HitZone.Head;

                Assert.False(hitCentred, "hidden while centred");
                Assert.True(hitLeaning, "peeking gets you shot in the head");
            });

            TestRunner.Add("combat/damage falls off with range", () =>
            {
                WeaponTuning w = WeaponTuning.DefaultLoadout()[0];
                float close = ShotSolver.Damage(w, HitZone.Body, 5f);
                float mid = ShotSolver.Damage(w, HitZone.Body, (w.falloffStart + w.falloffEnd) * 0.5f);
                float far = ShotSolver.Damage(w, HitZone.Body, w.falloffEnd + 50f);

                Assert.Near(close, w.damage, 0.001f, "full damage inside the falloff start");
                Assert.Less(mid, close, "damage drops with distance");
                Assert.Near(far, w.damage * w.falloffMinMul, 0.001f, "clamped at the minimum");
                Assert.Near(ShotSolver.Damage(w, HitZone.Head, 5f), w.damage * w.headMultiplier, 0.001f, "headshot multiplier");
            });

            TestRunner.Add("combat/fire rate is respected", () =>
            {
                MovementTuning t = new MovementTuning();
                BoxWorld w = BoxWorld.FlatGround();
                WeaponTuning gun = WeaponTuning.DefaultLoadout()[0];
                PlayerSimState s = PlayerSimState.Spawn(Vec3.Zero, 0f, t, gun);

                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.Fire;
                int shots = 0;
                for (int i = 0; i < 64; i++)   // one second
                {
                    SimEvents ev = new SimEvents();
                    c.Tick = (uint)i;
                    MovementCore.Step(ref s, c, t, gun, Sim.Dt, w, ref ev);
                    shots += ev.ShotsFired;
                }
                float expected = gun.rpm / 60f;
                Assert.Near(shots, expected, expected * 0.15f, "roughly rpm/60 rounds in a second");
            });

            TestRunner.Add("combat/semi automatic needs a new trigger pull", () =>
            {
                MovementTuning t = new MovementTuning();
                BoxWorld w = BoxWorld.FlatGround();
                WeaponTuning dmr = WeaponTuning.DefaultLoadout()[2];
                Assert.False(dmr.IsAutomatic, "the DMR is semi auto");

                PlayerSimState s = PlayerSimState.Spawn(Vec3.Zero, 0f, t, dmr);
                InputCommand c = InputCommand.Default(0);
                c.WeaponIndex = 2;
                int shots = 0;
                for (int i = 0; i < 64; i++)
                {
                    if (i == 30) c.Buttons |= Buttons.Fire;   // let the weapon swap finish, then squeeze and hold
                    SimEvents ev = new SimEvents();
                    c.Tick = (uint)i;
                    MovementCore.Step(ref s, c, t, dmr, Sim.Dt, w, ref ev);
                    shots += ev.ShotsFired;
                }
                Assert.Equal(shots, 1, "held trigger fires exactly once");
            });

            TestRunner.Add("combat/empty magazine triggers a reload", () =>
            {
                MovementTuning t = new MovementTuning();
                BoxWorld w = BoxWorld.FlatGround();
                WeaponTuning gun = WeaponTuning.DefaultLoadout()[0];
                PlayerSimState s = PlayerSimState.Spawn(Vec3.Zero, 0f, t, gun);

                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.Fire;
                for (int i = 0; i < 64 * 5; i++)
                {
                    SimEvents ev = new SimEvents();
                    c.Tick = (uint)i;
                    MovementCore.Step(ref s, c, t, gun, Sim.Dt, w, ref ev);
                }
                Assert.True(s.Weapon.Ammo > 0, "reloaded after running dry, ammo=" + s.Weapon.Ammo);
                Assert.True(s.Weapon.Ammo <= gun.MagSizeInt, "never over a full magazine");
            });

            TestRunner.Add("combat/aiming and stance tighten the group", () =>
            {
                MovementTuning t = new MovementTuning();
                BoxWorld w = BoxWorld.FlatGround();
                WeaponTuning gun = WeaponTuning.DefaultLoadout()[0];

                float SpreadFor(bool ads, Stance stance, float moveY)
                {
                    PlayerSimState s = PlayerSimState.Spawn(Vec3.Zero, 0f, t, gun);
                    InputCommand c = InputCommand.Default(0);
                    c.StanceRequest = stance;
                    c.MoveY = moveY;
                    if (ads) c.Buttons |= Buttons.Ads;
                    Sim.Run(ref s, c, t, w, 1.5f);
                    return MovementCore.CurrentSpread(in s, t, gun);
                }

                float hipStanding = SpreadFor(false, Stance.Stand, 0f);
                float adsStanding = SpreadFor(true, Stance.Stand, 0f);
                float adsProne = SpreadFor(true, Stance.Prone, 0f);
                float hipMoving = SpreadFor(false, Stance.Stand, 1f);

                Assert.Less(adsStanding, hipStanding, "aiming tightens the cone");
                Assert.Less(adsProne, adsStanding, "prone tightens it further");
                Assert.Greater(hipMoving, hipStanding, "running makes you spray");
            });

            TestRunner.Add("blindfire/lifts the muzzle without exposing the head", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                Vec3 headBefore = s.EyePosition(t);
                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.BlindFire;
                Sim.Run(ref s, c, t, w, 0.6f);

                Assert.Near(s.BlindFire, 1f, 0.01f, "weapon fully raised");
                Assert.Near(Vec3.Distance(s.EyePosition(t), headBefore), 0f, 0.001f, "head did not move");
                Assert.Near(s.WeaponOrigin(t).y - s.EyePosition(t).y, t.blindFireRaise, 0.01f, "muzzle lifted over the cover");

                PlayerHitbox box = PlayerHitbox.FromState(in s, t);
                PlayerSimState idle = Sim.Fresh(t, Vec3.Zero);
                PlayerHitbox idleBox = PlayerHitbox.FromState(in idle, t);
                Assert.Near(Vec3.Distance(box.HeadCenter, idleBox.HeadCenter), 0f, 0.001f, "head hitbox unchanged - nothing new to shoot at");
            });

            TestRunner.Add("blindfire/costs you all your accuracy", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                WeaponTuning gun = WeaponTuning.DefaultLoadout()[0];

                PlayerSimState aimed = Sim.Fresh(t, Vec3.Zero);
                Sim.Run(ref aimed, InputCommand.Default(0), t, w, 0.6f);
                float normal = MovementCore.CurrentSpread(in aimed, t, gun);

                PlayerSimState blind = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.BlindFire;
                Sim.Run(ref blind, c, t, w, 0.6f);
                float blindSpread = MovementCore.CurrentSpread(in blind, t, gun);

                Assert.Near(blindSpread, normal * t.blindFireSpreadMul, 0.05f, "spread multiplied by the blind fire penalty");
            });

            TestRunner.Add("blindfire/you can keep walking", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = Sim.Forward();
                c.Buttons |= Buttons.BlindFire | Buttons.Fire;
                SimEvents ev = Sim.Run(ref s, c, t, w, 1.5f);

                Assert.Greater(s.Velocity.Flat.Magnitude, t.walkSpeed * t.blindFireSpeedMul * 0.9f, "still moving at a walk");
                Assert.Greater(ev.ShotsFired, 0f, "and still shooting");
            });

            TestRunner.Add("blindfire/the wheel dial changes where the rounds go", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();

                Vec3 DirectionFor(float dial)
                {
                    PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                    InputCommand c = InputCommand.Default(0);
                    c.Buttons |= Buttons.BlindFire;
                    c.BlindAngle = dial;
                    Sim.Run(ref s, c, t, w, 0.8f);
                    return s.WeaponDirection(t);
                }

                Vec3 flat = DirectionFor(0f);
                Vec3 up = DirectionFor(1f);
                Vec3 down = DirectionFor(-1f);

                // A neutral dial is not level - the muzzle is held above the eye line, so firing level would
                // send the round parallel to the crosshair and permanently high. It aims slightly down, to
                // converge on where you are actually looking.
                float flatPitch = ViewMath.PitchOf(flat);
                Assert.Greater(flatPitch, 0.2f, "dial at zero aims down off the raised muzzle");
                Assert.Less(flatPitch, 5f, "but only just - it is a convergence, not a dive");

                // The dial is an offset from that converged line, not from the horizon.
                Assert.Near(ViewMath.PitchOf(up), flatPitch - t.blindFirePitchMax, 1.5f, "dial up elevates the muzzle");
                Assert.Near(ViewMath.PitchOf(down), flatPitch - t.blindFirePitchMin, 1.5f, "dial down drops the muzzle");
            });

            TestRunner.Add("blindfire/a neutral dial lands where you are looking", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.BlindFire;
                c.BlindAngle = 0f;
                Sim.Run(ref s, c, t, w, 0.8f);

                Assert.Near(s.BlindFire, 1f, 0.01f, "fully blind firing");

                // Walk the shot out to the convergence distance and see how far it is off the crosshair line.
                Vec3 eye = s.EyePosition(t);
                Vec3 muzzle = eye + Vec3.Up * (t.blindFireRaise * s.BlindFire);
                Vec3 dir = s.WeaponDirection(t);
                Vec3 aimPoint = eye + s.LookDirection() * t.blindFireConvergeDist;

                float travel = (aimPoint - muzzle).Magnitude;
                Vec3 hit = muzzle + dir * travel;
                Assert.Less((hit - aimPoint).Magnitude, 0.05f, "the round converges on the crosshair");

                // And without the correction it would have been high by the whole muzzle raise.
                Vec3 uncorrected = muzzle + s.LookDirection() * travel;
                Assert.Greater((uncorrected - aimPoint).Magnitude, t.blindFireRaise * 0.9f, "which is the bug this fixes");
            });

            TestRunner.Add("blindfire/leaning swings the shot around the corner", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.BlindFire;
                c.LeanAxis = 1f;
                Sim.Run(ref s, c, t, w, 1f);

                float yaw = ViewMath.YawOf(s.WeaponDirection(t));
                Assert.Near(yaw, t.blindFireYaw, 2f, "muzzle swings toward the lean");
                Assert.Near(s.Yaw, 0f, 0.001f, "the view itself never turned");
            });

            TestRunner.Add("blindfire/blocks aiming down sights", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.Ads;
                Sim.Run(ref s, c, t, w, 0.6f);
                Assert.Near(s.Ads, 1f, 0.01f, "aimed in");

                c.Buttons |= Buttons.BlindFire;
                Sim.Run(ref s, c, t, w, 0.8f);
                Assert.Near(s.Ads, 0f, 0.01f, "raising the gun over cover drops you out of the sights");
            });

        }
    }
}