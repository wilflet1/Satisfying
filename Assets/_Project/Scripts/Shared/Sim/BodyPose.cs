namespace Satisfying.Shared
{
    /// <summary>
    /// Where every bone of a player is, worked out from nothing but replicated state.
    ///
    /// There is exactly one of these because there used to be two. The shootable shape was a head
    /// sphere and one fat capsule; the drawn body was six boxes posed by hand somewhere else. They
    /// agreed only roughly, which is the worst possible arrangement in a game about peeking - the
    /// silhouette you aim at is a promise about what the server will test. Everything that wants a
    /// limb now reads it from here: the hitbox, the model, the death pose.
    ///
    /// Positions are in CHARACTER space - origin at the feet, +Z the way they are facing, +Y up.
    /// That is the space the model is built in, so the view uses these numbers as they are; the
    /// hitbox turns them into world space with ToWorld.
    /// </summary>
    public struct BodyPose
    {
        public Vec3 Head;
        public Vec3 NeckBase;
        public Vec3 Shoulders;          // the shoulder line: where the arms hang from
        public Vec3 ChestTop;           // top of the chest CAPSULE, a radius below the shoulder line
        public Vec3 ChestBase;          // bottom of the ribcage
        public Vec3 Pelvis;

        public Vec3 LeftShoulder, LeftElbow, LeftHand;
        public Vec3 RightShoulder, RightElbow, RightHand;
        public Vec3 LeftHip, LeftKnee, LeftAnkle, LeftToe;
        public Vec3 RightHip, RightKnee, RightAnkle, RightToe;

        public float HeadRadius;
        public float NeckRadius;
        public float ChestRadius;
        public float StomachRadius;
        public float UpperArmRadius;
        public float ForearmRadius;
        public float ThighRadius;
        public float ShinRadius;
        public float FootRadius;

        /// <summary>Everything above is in metres for a 1.82 m duellist times this.</summary>
        public float Scale;

        // Proportions of a 1.82 m adult, in metres. Written out rather than derived so that changing
        // one of them changes one thing.
        const float ReferenceHeight = 1.82f;
        const float SkullDrop = 0.048f;      // eye line to the centre of the head
        const float HeadR = 0.105f;
        const float NeckLength = 0.105f;
        const float ShoulderDrop = 0.025f;   // base of the neck to the shoulder line
        const float ChestLength = 0.300f;
        const float StomachLength = 0.235f;
        // Shoulder to grip, which is longer than shoulder to wrist: 0.65 m is what it takes to hold a
        // rifle's handguard, and an arm shorter than that is an arm permanently stretched straight.
        const float UpperArmLength = 0.330f;
        const float ForearmLength = 0.320f;
        const float ThighLength = 0.462f;
        const float ShinLength = 0.442f;
        const float AnkleHeight = 0.075f;
        const float FootLength = 0.185f;
        const float ShoulderHalfWidth = 0.195f;
        const float HipHalfWidth = 0.105f;
        const float ThroatForward = 0.055f;  // the windpipe sits in front of the sternum, and has to be
                                             // in front of the chest capsule or the chest eats neck shots

        /// <summary>
        /// The weapon matters because the support hand is on it: a rifle puts the left hand forty
        /// centimetres down the handguard and a pistol puts it on top of the right one, and an arm
        /// hitbox that ignored that would be nowhere near the arm you can see.
        /// </summary>
        public static BodyPose Build(in PlayerSimState s, MovementTuning t, WeaponTuning weapon)
        {
            BodyPose p = new BodyPose();
            float k = t.standHeight > 0.2f ? t.standHeight / ReferenceHeight : 1f;
            p.Scale = k;

            p.HeadRadius = HeadR * k;
            p.NeckRadius = 0.055f * k;
            p.ChestRadius = 0.155f * k;
            p.StomachRadius = 0.152f * k;
            p.UpperArmRadius = 0.062f * k;
            p.ForearmRadius = 0.052f * k;
            p.ThighRadius = 0.095f * k;
            p.ShinRadius = 0.072f * k;
            p.FootRadius = 0.055f * k;

            float lean = s.EffectiveLean(t);
            float leanX = lean * t.leanOffset;
            float leanY = -MathK.Abs(lean) * t.leanDrop;
            float eyeY = s.EyeHeight(t) + leanY;

            if (s.Stance == Stance.Prone) BuildProne(ref p, in s, t, k, leanX, eyeY);
            else BuildUpright(ref p, in s, t, k, leanX, eyeY);

            BuildArms(ref p, in s, t, weapon, k, leanX, eyeY);

            // A capsule ends in a hemisphere, so a chest that reaches the shoulder line bulges a full
            // radius above it - out over the collarbones and in front of the windpipe, where it swallows
            // every neck shot. Pull the segment down by its own radius and the cap lands on the shoulder
            // line instead, which is where a chest actually stops.
            Vec3 down = (p.ChestBase - p.Shoulders).Normalized;
            p.ChestTop = p.Shoulders + down * p.ChestRadius;
            return p;
        }

        static void BuildUpright(ref BodyPose p, in PlayerSimState s, MovementTuning t, float k, float leanX, float eyeY)
        {
            // The torso is rigid and the legs fold. That is the whole difference between a crouch that
            // reads as a crouch and one that reads as a man who has been scaled down, which is what the
            // old code did - it multiplied the whole body by the height ratio.
            p.Head = new Vec3(leanX, eyeY + SkullDrop * k, 0f);
            Vec3 spineTop = p.Head - Vec3.Up * ((HeadR * 0.95f + NeckLength) * k);
            p.Shoulders = spineTop - Vec3.Up * (ShoulderDrop * k);
            p.ChestBase = p.Shoulders - Vec3.Up * (ChestLength * k);
            p.NeckBase = spineTop + Vec3.Forward * (ThroatForward * k);

            // Crouching sits the hips back behind the heels and pitches the chest over them, so the
            // knees do not have to swing quite so far forward to make up the height.
            float fold = Folded(in s, t);
            float hipBack = -0.085f * k * fold;

            // A slide throws the legs out in front and sits the hips back over them; a traversal tucks
            // them up underneath. Both live here rather than in the view, because both change the shape
            // people are shooting at - a slide that only looked like a slide would be a lie about where
            // the legs are.
            float ankleY = AnkleHeight * k;
            float ankleZ = 0f;
            float footDrop = ankleY * 0.45f;

            if (s.Sliding)
            {
                hipBack = -0.20f * k;
                ankleZ = 0.52f * k;
            }
            else if (s.Vaulting || s.Mantling)
            {
                hipBack = -0.06f * k;
                ankleZ = 0.26f * k;
            }

            p.Pelvis = p.ChestBase - Vec3.Up * (StomachLength * k) + Vec3.Forward * hipBack;

            if (s.Vaulting || s.Mantling)
            {
                // Knees to the chest: the feet come off the ground with the rest of you.
                ankleY = MathK.Max(AnkleHeight * k, p.Pelvis.y - 0.30f * k);
                footDrop = 0.02f * k;
            }

            p.LeftHip = p.Pelvis + Vec3.Right * (-HipHalfWidth * k);
            p.RightHip = p.Pelvis + Vec3.Right * (HipHalfWidth * k);
            p.LeftAnkle = new Vec3(-HipHalfWidth * k, ankleY, ankleZ);
            p.RightAnkle = new Vec3(HipHalfWidth * k, ankleY, ankleZ);
            p.LeftKnee = Knee(p.LeftHip, p.LeftAnkle, -1f, k, t.radius);
            p.RightKnee = Knee(p.RightHip, p.RightAnkle, 1f, k, t.radius);
            p.LeftToe = p.LeftAnkle + new Vec3(0f, -footDrop, FootLength * k);
            p.RightToe = p.RightAnkle + new Vec3(0f, -footDrop, FootLength * k);
        }

        static void BuildProne(ref BodyPose p, in PlayerSimState s, MovementTuning t, float k, float leanX, float eyeY)
        {
            // Flat out, propped on the elbows, and hung off the eye rather than the other way round:
            // the simulation decides where the eye is, so the head goes exactly there and the body
            // trails away behind it. The old code put the prone head half a metre in FRONT of the
            // camera, which meant a prone opponent could see round a corner their head was not at.
            //
            // A real man lying down is about 1.7 m long. This one is 1.4 m, because the capsule he
            // collides with is a 0.62 m blob and a shootable body that overhangs it by half a metre
            // spends its life inside the wall behind him.
            float ground = 0.155f * k;
            p.Head = new Vec3(leanX, eyeY, 0f);
            p.NeckBase = new Vec3(leanX * 0.9f, MathK.Max(ground, eyeY - 0.130f * k), -0.09f * k);
            p.Shoulders = new Vec3(leanX * 0.8f, MathK.Max(ground, eyeY - 0.190f * k), -0.17f * k);
            p.ChestBase = new Vec3(leanX * 0.5f, ground + 0.02f * k, -0.42f * k);
            p.Pelvis = new Vec3(0f, ground, -0.62f * k);

            // Legs splayed, which is both how people lie down and how the length gets foreshortened.
            p.LeftHip = p.Pelvis + Vec3.Right * (-HipHalfWidth * k);
            p.RightHip = p.Pelvis + Vec3.Right * (HipHalfWidth * k);
            p.LeftKnee = new Vec3(-0.16f * k, ground * 0.85f, -0.94f * k);
            p.RightKnee = new Vec3(0.16f * k, ground * 0.85f, -0.94f * k);
            p.LeftAnkle = new Vec3(-0.17f * k, ground * 0.60f, -1.26f * k);
            p.RightAnkle = new Vec3(0.17f * k, ground * 0.60f, -1.26f * k);
            p.LeftToe = p.LeftAnkle + new Vec3(0f, -0.02f * k, -FootLength * 0.55f * k);
            p.RightToe = p.RightAnkle + new Vec3(0f, -0.02f * k, -FootLength * 0.55f * k);
        }

        static void BuildArms(ref BodyPose p, in PlayerSimState s, MovementTuning t, WeaponTuning weapon,
                              float k, float leanX, float eyeY)
        {
            // Yaw is baked out in character space, so the only aim that matters here is the pitch.
            Vec3 look = ViewMath.Forward(0f, s.Pitch);
            float ads = MathK.Clamp01(s.Ads);

            // Shouldering a rifle blades you: the support shoulder comes forward and the firing one goes
            // back. Without it the support arm has to reach diagonally across the chest and ends up
            // stretched straight, which is the giveaway that a character is holding a prop.
            float blade = 0.135f * k * ads;
            p.LeftShoulder = p.Shoulders + Vec3.Right * (-ShoulderHalfWidth * k) + Vec3.Forward * blade;
            p.RightShoulder = p.Shoulders + Vec3.Right * (ShoulderHalfWidth * k) - Vec3.Forward * blade;

            Vec3 eye = new Vec3(leanX, eyeY, 0f);

            // Hip fire keeps the gun low and out to the strong side; aiming brings it onto the eye line.
            // Not too far out to the strong side: it is still a two-handed carry, and a gun at arm's
            // length off your hip is one the support hand cannot reach.
            Vec3 loose = p.Shoulders + look * (0.220f * k) + new Vec3(0.105f * k, -0.085f * k, 0f);
            Vec3 aimed = eye + look * (0.235f * k) + new Vec3(0.022f * k, -0.088f * k, 0f);
            Vec3 firing = Vec3.Lerp(loose, aimed, ads);

            // The gun goes up over the cover on a blind fire; the head does not, which is the point.
            if (s.BlindFire > 0.001f)
                firing += Vec3.Up * (t.blindFireRaise * s.BlindFire);

            p.RightHand = firing;

            // The support hand sits on the weapon, so it is measured in the weapon's frame: forward
            // along the bore and up off it, never in world axes. Point the gun at the sky and the
            // support hand goes with it. The weapon models put their foregrip anchor at exactly this
            // offset from the grip, which is what makes the drawn hand and the hitbox the same hand.
            Vec3 gunUp = ViewMath.Forward(0f, s.Pitch - 90f);
            float reach = weapon != null ? weapon.supportHandReach : 0.34f;
            float rise = weapon != null ? weapon.supportHandRise : 0.045f;
            p.LeftHand = firing + look * (reach * k) + gunUp * (rise * k);

            p.RightElbow = Elbow(p.RightShoulder, p.RightHand, 1f, k);
            p.LeftElbow = Elbow(p.LeftShoulder, p.LeftHand, -1f, k);

            // Prone puts the elbows on the deck, and "on" is as far as they go.
            float floor = 0.095f * k;
            if (p.RightElbow.y < floor) p.RightElbow.y = floor;
            if (p.LeftElbow.y < floor) p.LeftElbow.y = floor;
        }

        /// <summary>0 standing, 1 at the tuned crouch height. Above the stand height (nothing does this
        /// today, but tuning is live) it stays at 0 rather than folding backwards.</summary>
        static float Folded(in PlayerSimState s, MovementTuning t)
        {
            float span = t.standHeight - t.crouchHeight;
            if (span < 0.01f) return 0f;
            return MathK.Clamp01((t.standHeight - s.Height) / span);
        }

        /// <summary>
        /// The knee, placed rather than solved, breaking forward and a little outboard the way a squat
        /// actually goes.
        ///
        /// A crouch deep enough to put your eyes at 1.09 m is a full squat, and a full squat with
        /// ninety-centimetre legs throws the knee about forty centimetres past the toes - well outside
        /// the thirty-three-centimetre cylinder you collide with. So the knee is pulled back inside the
        /// cylinder and the thigh takes up the slack by being short. Nobody has ever complained that a
        /// crouching man's shins looked stubby; everybody complains about being shot round a corner.
        /// </summary>
        static Vec3 Knee(Vec3 hip, Vec3 ankle, float side, float k, float collisionRadius)
        {
            Vec3 knee = Joint(hip, ankle, ThighLength * k, ShinLength * k,
                              new Vec3(side * 0.28f, 0f, 0.96f));

            // The cap is on the cylinder, not on the bend, because the cylinder is the thing being
            // promised. Past this point the thigh is simply short.
            float limit = MathK.Max(0.05f, collisionRadius * 0.92f);
            float radius = MathK.Sqrt(knee.x * knee.x + knee.z * knee.z);
            if (radius > limit)
            {
                float shrink = limit / radius;
                knee.x *= shrink;
                knee.z *= shrink;
            }
            return knee;
        }

        /// <summary>
        /// The elbow, by the same rule as the knee: far enough off the shoulder-to-hand line that the
        /// two bones come out near their real lengths, breaking down and outboard - the direction that
        /// keeps an arm from folding backwards through the gun.
        /// </summary>
        static Vec3 Elbow(Vec3 shoulder, Vec3 hand, float side, float k)
        {
            return Joint(shoulder, hand, UpperArmLength * k, ForearmLength * k,
                         new Vec3(side * 0.38f, -0.92f, 0f));
        }

        /// <summary>
        /// The middle joint of a two-bone limb: law of cosines, with the bend taken perpendicular to the
        /// line between the ends so both bones come out at the length they are supposed to be. Which
        /// side of that line it bends towards is the caller's business - knees go forward, elbows go
        /// down and out - and a limb reaching further than it can just ends up straight.
        /// </summary>
        static Vec3 Joint(Vec3 root, Vec3 end, float upper, float lower, Vec3 bendHint)
        {
            Vec3 span = end - root;
            float d = span.Magnitude;
            if (d < 0.0001f) return root + bendHint.Normalized * upper;

            Vec3 dir = span / d;
            float along = MathK.Clamp((d * d + upper * upper - lower * lower) / (2f * d), 0f, upper);
            float bend = MathK.Sqrt(MathK.Max(0f, upper * upper - along * along));

            Vec3 perp = bendHint - dir * Vec3.Dot(bendHint, dir);
            if (perp.SqrMagnitude < 1e-6f) perp = Vec3.Up - dir * Vec3.Dot(Vec3.Up, dir);
            if (perp.SqrMagnitude < 1e-6f) perp = Vec3.Right - dir * Vec3.Dot(Vec3.Right, dir);

            return root + dir * along + perp.Normalized * bend;
        }

        /// <summary>Shifts every joint. Used by the death animation, which folds the body and then has
        /// to put the whole thing on the floor.</summary>
        public void Translate(Vec3 delta)
        {
            Head += delta;
            NeckBase += delta;
            Shoulders += delta;
            ChestTop += delta;
            ChestBase += delta;
            Pelvis += delta;
            LeftShoulder += delta; LeftElbow += delta; LeftHand += delta;
            RightShoulder += delta; RightElbow += delta; RightHand += delta;
            LeftHip += delta; LeftKnee += delta; LeftAnkle += delta; LeftToe += delta;
            RightHip += delta; RightKnee += delta; RightAnkle += delta; RightToe += delta;
        }

        /// <summary>Character space to world: yaw the whole skeleton and drop it at the foot position.</summary>
        public BodyPose ToWorld(Vec3 position, float yaw)
        {
            Vec3 right = ViewMath.FlatRight(yaw);
            Vec3 forward = ViewMath.FlatForward(yaw);

            BodyPose w = this;
            w.Head = Place(Head, position, right, forward);
            w.NeckBase = Place(NeckBase, position, right, forward);
            w.Shoulders = Place(Shoulders, position, right, forward);
            w.ChestTop = Place(ChestTop, position, right, forward);
            w.ChestBase = Place(ChestBase, position, right, forward);
            w.Pelvis = Place(Pelvis, position, right, forward);

            w.LeftShoulder = Place(LeftShoulder, position, right, forward);
            w.LeftElbow = Place(LeftElbow, position, right, forward);
            w.LeftHand = Place(LeftHand, position, right, forward);
            w.RightShoulder = Place(RightShoulder, position, right, forward);
            w.RightElbow = Place(RightElbow, position, right, forward);
            w.RightHand = Place(RightHand, position, right, forward);

            w.LeftHip = Place(LeftHip, position, right, forward);
            w.LeftKnee = Place(LeftKnee, position, right, forward);
            w.LeftAnkle = Place(LeftAnkle, position, right, forward);
            w.LeftToe = Place(LeftToe, position, right, forward);
            w.RightHip = Place(RightHip, position, right, forward);
            w.RightKnee = Place(RightKnee, position, right, forward);
            w.RightAnkle = Place(RightAnkle, position, right, forward);
            w.RightToe = Place(RightToe, position, right, forward);
            return w;
        }

        static Vec3 Place(Vec3 local, Vec3 origin, Vec3 right, Vec3 forward)
        {
            return origin + right * local.x + Vec3.Up * local.y + forward * local.z;
        }
    }
}
