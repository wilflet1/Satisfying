namespace Satisfying.Shared
{
    /// <summary>Where a grenade is in its life. Only one is ever in your hand.</summary>
    public enum GrenadeCarry : byte
    {
        /// <summary>On your belt. The weapon is up and nothing is happening.</summary>
        Stowed = 0,

        /// <summary>
        /// Weapon going away, grenade coming out. Slow on purpose - this is the commitment, and it is
        /// the part you can still change your mind about.
        /// </summary>
        Drawing = 1,

        /// <summary>
        /// In your hand, pin still IN. You can put it away again from here. Pressing a mouse button
        /// pulls the pin; letting go of it throws.
        /// </summary>
        Held = 2,

        /// <summary>Pin out, button down, winding up. Letting go is what throws it.</summary>
        Primed = 3
    }

    [System.Serializable]
    public class GrenadeTuning
    {
        [Tune("Grenade", 0f, 8f, Tip = "Grenades you spawn with.")]
        public float count = 2f;

        [Tune("Grenade", 0.2f, 3f, Tip = "Seconds to put the weapon away and get a grenade into your hand.")]
        public float drawTime = 0.85f;

        [Tune("Grenade", 0.05f, 2f, Tip = "Seconds from pulling the pin to being able to throw. You cannot release before this.")]
        public float primeTime = 0.35f;

        [Tune("Grenade", 0.05f, 1.5f, Tip = "Seconds from letting go to it leaving your hand.")]
        public float throwTime = 0.28f;

        [Tune("Grenade", 0.5f, 8f, Tip = "Fuse, from the moment it leaves your hand. Not cookable: the clock does not start in your hand.")]
        public float fuse = 2.5f;

        [Tune("Grenade", 2f, 30f, Tip = "Throw speed for a hard throw.")]
        public float hardSpeed = 15.5f;

        [Tune("Grenade", 1f, 20f, Tip = "Throw speed for a soft one - underarm, round a corner, down a stairwell.")]
        public float softSpeed = 6.5f;

        [Tune("Grenade", -20f, 45f, Tip = "Degrees above your aim a hard throw goes.")]
        public float hardLoft = 4f;

        [Tune("Grenade", -20f, 60f, Tip = "Degrees above your aim a soft one goes. Higher, because it is a lob.")]
        public float softLoft = 22f;

        [Tune("Grenade", 0f, 1f, Tip = "How much speed it keeps off a bounce.")]
        public float bounce = 0.34f;

        [Tune("Grenade", 0f, 1f, Tip = "How much it keeps sliding along a surface it hits.")]
        public float friction = 0.72f;

        [Tune("Grenade", 1f, 20f, Tip = "Kill radius. Inside this it is lethal at full health.")]
        public float lethalRadius = 2.6f;

        [Tune("Grenade", 1f, 30f, Tip = "Outer radius. Damage falls to nothing by here.")]
        public float radius = 7.5f;

        [Tune("Grenade", 10f, 400f, Tip = "Damage at the centre.")]
        public float damage = 135f;

        [Tune("Grenade", 0f, 1f, Tip = "1 makes cover stop the blast; 0 lets it through walls.")]
        public float needsLineOfSight = 1f;

        public int CountInt { get { return MathK.Max(0, MathK.RoundToInt(count)); } }
        public bool NeedsLineOfSight { get { return needsLineOfSight >= 0.5f; } }

        /// <summary>
        /// What a blast does at a distance. Flat inside the lethal radius and then straight down to
        /// nothing - not an inverse square, because an inverse square is impossible to read and the
        /// whole point of a grenade is that you can tell whether you are in it.
        /// </summary>
        public float DamageAt(float distance)
        {
            if (distance <= lethalRadius) return damage;
            float outer = MathK.Max(lethalRadius + 0.01f, radius);
            float k = MathK.Clamp01((distance - lethalRadius) / (outer - lethalRadius));
            return damage * (1f - k) * (1f - k);
        }

        public GrenadeTuning Clone() { return (GrenadeTuning)MemberwiseClone(); }
    }

    /// <summary>
    /// One grenade in the air, on the server. Clients are told where they are and draw them; they do
    /// not simulate them, because a grenade that lands somewhere different at each end is worse than
    /// one that arrives a hundred milliseconds late.
    /// </summary>
    public struct GrenadeState
    {
        public bool Active;
        public byte Id;
        public byte Owner;
        public Vec3 Position;
        public Vec3 Velocity;
        public float Fuse;

        /// <summary>Counted up as it bounces, so the view can make a noise on each one without the
        /// server having to send an event per bounce.</summary>
        public byte Bounces;

        /// <summary>What it last hit, so the noise is the right noise for the floor it is on.</summary>
        public SurfaceKind LastSurface;
    }

    public static class GrenadeSim
    {
        public const int MaxLive = 8;
        public const float Radius = 0.055f;

        /// <summary>
        /// One tick of one grenade: gravity, then a swept move that bounces off whatever it meets.
        ///
        /// It is swept rather than stepped because a grenade thrown hard covers a quarter of a metre a
        /// tick, and a stepped one tunnels through the very floor it is meant to be rattling along.
        /// </summary>
        public static void Step(ref GrenadeState g, ICollisionWorld world, WorldModel model,
                                GrenadeTuning tuning, float gravity, float dt, out bool bounced)
        {
            bounced = false;
            if (!g.Active) return;

            g.Fuse -= dt;
            g.Velocity += new Vec3(0f, -gravity * dt, 0f);

            float remaining = dt;
            for (int step = 0; step < 4 && remaining > 0.0001f; step++)
            {
                Vec3 delta = g.Velocity * remaining;
                float distance = delta.Magnitude;
                if (distance < 0.0001f) break;

                Vec3 direction = delta / distance;
                float hit;
                Vec3 normal;
                if (!world.Raycast(g.Position, direction, distance + Radius, out hit, out normal))
                {
                    g.Position += delta;
                    break;
                }

                // Stop a radius short of the surface, turn, and spend what is left of the tick.
                float travel = MathK.Max(0f, hit - Radius);
                g.Position += direction * travel;

                float into = Vec3.Dot(g.Velocity, normal);
                if (into < 0f)
                {
                    Vec3 alongSurface = g.Velocity - normal * into;
                    Vec3 rebound = normal * (-into * MathK.Clamp01(tuning.bounce));
                    g.Velocity = alongSurface * MathK.Clamp01(tuning.friction) + rebound;

                    // Only a real knock counts as a bounce. Without this a grenade at rest on the
                    // floor rattles once a tick for the rest of the round.
                    if (-into > 1.1f)
                    {
                        bounced = true;
                        g.Bounces = (byte)((g.Bounces + 1) & 15);
                        if (model != null) g.LastSurface = model.SurfaceAt(g.Position + normal * -0.08f);
                    }
                }

                remaining -= remaining * (distance > 0.0001f ? travel / distance : 1f);
                if (travel <= 0.0001f) break;      // wedged; let the next tick try again
            }

            // Nudge a grenade that has come to rest out of the floor rather than letting it creep in.
            if (g.Velocity.SqrMagnitude < 0.02f) g.Velocity = Vec3.Zero;
        }

        /// <summary>
        /// Where a throw starts and how fast. The loft is added to the aim rather than replacing it,
        /// so where you are looking is still where it goes - a grenade that ignored your pitch would
        /// be unthrowable up a staircase.
        /// </summary>
        public static void Throw(in PlayerSimState thrower, MovementTuning move, GrenadeTuning tuning,
                                 bool hard, out Vec3 position, out Vec3 velocity)
        {
            float loft = hard ? tuning.hardLoft : tuning.softLoft;
            float speed = hard ? tuning.hardSpeed : tuning.softSpeed;

            Vec3 aim = ViewMath.Forward(thrower.Yaw, MathK.Clamp(thrower.Pitch - loft, -89f, 89f));
            Vec3 right = ViewMath.FlatRight(thrower.Yaw);

            // Out of the throwing hand, not out of the middle of your face.
            position = thrower.EyePosition(move) + aim * 0.35f + right * 0.12f - Vec3.Up * 0.05f;

            // A grenade thrown from a moving player carries what the player was doing with it.
            velocity = aim * speed + thrower.Velocity.Flat * 0.55f;
        }
    }
}
