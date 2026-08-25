namespace Satisfying.Shared
{
    /// <summary>
    /// Where the gun sits in front of your face, worked out from simulation state alone.
    ///
    /// This lives down here, away from the engine, for one reason: aiming makes a promise that can be
    /// checked. At full ADS the weapon's sight has to land on the exact centre of the screen, and every
    /// flourish that moves the gun - the crouch settle, the prone settle, the sprint carry, the slide
    /// tip, the drag - has to be gone by then. It was not. Crouching pushed the gun up twelve
    /// millimetres and prone pushed it up thirty and back sixty, applied AFTER the sights were lined up,
    /// which at a sight distance of 0.4 m is four degrees of error: the front post sat outside the rear
    /// notch, and lying down you were looking at the side of your own receiver.
    ///
    /// Now every flourish goes into one accumulator that is multiplied by (1 - Ads), and a test asserts
    /// the sight is centred in every stance.
    /// </summary>
    public struct ViewmodelPose
    {
        /// <summary>Position of the weapon root in the weapon camera's space.</summary>
        public Vec3 Position;

        /// <summary>Rotation of the weapon root, in degrees. Zero when fully aimed, always.</summary>
        public Vec3 Euler;

        /// <summary>
        /// hipOffset is the weapon's own carry position; sightLocal is where the active sight sits on
        /// the weapon. Sway, recoil kick and bob are frame-rate things and are added by the view on top.
        /// </summary>
        public static ViewmodelPose Build(in PlayerSimState s, MovementTuning move, FeelTuning feel,
                                          Vec3 hipOffset, Vec3 sightLocal, bool sprinting)
        {
            ViewmodelPose p = new ViewmodelPose();
            float ads = MathK.Clamp01(s.Ads);

            Vec3 nudge = new Vec3(feel.viewmodelX, feel.viewmodelY, feel.viewmodelZ);

            // Hip carry takes the player's viewmodel nudge; the aimed pose cannot, because the whole
            // point of the aimed pose is that the sight is on the centreline.
            Vec3 hip = hipOffset + nudge;
            Vec3 aimed = new Vec3(-sightLocal.x, -sightLocal.y, feel.adsSightDistance - sightLocal.z);

            p.Position = Vec3.Lerp(hip, aimed, ads);
            p.Euler = Vec3.Zero;

            Vec3 flourish = Vec3.Zero;
            Vec3 flourishEuler = Vec3.Zero;

            if (s.Sliding)
            {
                // Gun comes in tight and tips with the slide.
                flourish += new Vec3(-0.03f, 0.02f, -0.08f);
                flourishEuler += new Vec3(-6f, 10f, 16f);
            }
            else if (s.IsSwinging)
            {
                // Wind the stock back over the shoulder, then drive it forward.
                float windup = MathK.Clamp01(s.MeleeTimer / MathK.Max(0.02f, move.meleeWindup));
                float follow = MathK.Clamp01((s.MeleeTimer - move.meleeWindup) / MathK.Max(0.02f, move.meleeRecover));
                float swing = s.MeleeTimer < move.meleeWindup ? -windup : MathK.Lerp(1f, 0f, follow);

                flourish += new Vec3(-0.16f * MathK.Abs(swing), 0.10f * swing, 0.14f * swing);
                flourishEuler += new Vec3(-38f * swing, 30f * swing, 55f * swing);
            }
            else if (s.Stance == Stance.Prone)
            {
                // Elbows on the deck: the gun comes back and settles.
                flourish += new Vec3(0f, 0.03f, -0.06f);
                flourishEuler += new Vec3(0f, 0f, 4f);
            }
            else if (s.Stance == Stance.Crouch)
            {
                flourish += new Vec3(0f, 0.012f, 0f);
            }

            if (sprinting)
            {
                flourish += new Vec3(0.05f, -0.05f, -0.06f);
                flourishEuler += new Vec3(6f, -18f, feel.sprintTilt);
            }

            // Carrying something drops the muzzle: one hand is busy.
            if (s.CarryMass > 0f)
            {
                flourish += new Vec3(0.02f, -0.06f, -0.05f);
                flourishEuler += new Vec3(14f, -6f, -8f);
            }

            // The one rule: nothing above survives to full ADS.
            float settle = 1f - ads;
            p.Position += flourish * settle;
            p.Euler += flourishEuler * settle;

            // Blind fire is not a flourish, it is a different way of holding the gun - and the
            // simulation has already forced Ads to zero by the time it is above nothing.
            if (s.BlindFire > 0.001f)
            {
                float dial = MathK.Clamp(s.BlindAngle, -1f, 1f);
                float elevation = dial >= 0f ? dial * move.blindFirePitchMax : -dial * move.blindFirePitchMin;
                Vec3 blind = new Vec3(hip.x * 0.35f, 0.30f, hip.z * 0.75f);
                Vec3 blindEuler = new Vec3(-elevation, s.EffectiveLean(move) * move.blindFireYaw,
                                           -22f * MathK.Sign(hip.x));
                p.Position = Vec3.Lerp(p.Position, blind, s.BlindFire);
                p.Euler = Vec3.Lerp(p.Euler, blindEuler, s.BlindFire);
            }

            return p;
        }

        /// <summary>
        /// Where the sight ends up in the weapon camera's space. Only meaningful when Euler is zero,
        /// which is exactly the case the aiming promise is about.
        /// </summary>
        public Vec3 SightPoint(Vec3 sightLocal) { return Position + sightLocal; }
    }
}
