namespace Satisfying.Shared
{
    /// <summary>
    /// Grabbing and dragging. Kept out of MovementCore because it needs the world, but run at the same
    /// fixed tick by both the server and the predicting client - a drag is driven entirely by the
    /// grabber's own input, so both ends compute the same path and prediction stays quiet.
    /// </summary>
    public static class PropSim
    {
        public const byte Nobody = 255;

        public static void Step(int playerId, ref PlayerSimState s, InputCommand cmd, MovementTuning t,
                                WorldModel model, WorldState world, ICollisionWorld collision, float dt,
                                ref SimEvents ev)
        {
            if (model == null || world == null || world.Props.Length == 0)
            {
                s.CarryMass = 0f;
                s.GrabSeqSeen = cmd.GrabSeq;
                return;
            }

            bool fresh = InputCommand.Advanced(cmd.GrabSeq, s.GrabSeqSeen);
            if (fresh) s.GrabSeqSeen = cmd.GrabSeq;

            int current = world.FindPropHeldBy(playerId);
            bool busy = s.Mantling || s.Sliding || s.IsSwinging || !s.Grounded;

            if (fresh)
            {
                if (current >= 0)
                {
                    world.Props[current].Grabber = Nobody;
                    current = -1;
                    ev.ReleasedProp = true;
                }
                else if (!busy)
                {
                    int found = FindGrabbable(playerId, in s, t, model, world);
                    if (found >= 0)
                    {
                        world.Props[found].Grabber = (byte)playerId;
                        current = found;
                        ev.GrabbedProp = true;
                    }
                }
            }

            if (current < 0)
            {
                s.CarryMass = 0f;
                return;
            }

            // Let go rather than dragging something through a wall you walked round.
            float reach = Vec3.Distance(world.Props[current].Position.Flat, s.Position.Flat);
            if (busy || reach > t.grabBreakDistance)
            {
                world.Props[current].Grabber = Nobody;
                s.CarryMass = 0f;
                ev.ReleasedProp = true;
                return;
            }

            PropDef def = model.Props[current];
            Vec3 forward = ViewMath.FlatForward(s.Yaw);
            Vec3 hold = s.Position + forward * (t.grabHoldDistance + def.Size.z * 0.5f);

            Vec3 position = world.Props[current].Position;
            Vec3 target = new Vec3(hold.x, position.y, hold.z);

            Vec3 next = Vec3.MoveTowards(position, target, DragSpeed(def.Mass, t) * dt);

            // Push it into geometry and it simply stops - the player keeps walking, the grip stretches.
            // The probe has to sit just AHEAD of the leading edge, never over the object's own footprint:
            // props live on the same collision layer this query uses, so a sphere centred on the object
            // would always hit the object itself and it would never move - only turn to face you.
            Vec3 step = next - position;
            float stepLen = new Vec3(step.x, 0f, step.z).Magnitude;
            bool blocked = false;
            if (collision != null && stepLen > 0.0005f)
            {
                float invLen = 1f / stepLen;
                Vec3 dir = new Vec3(step.x * invLen, 0f, step.z * invLen);
                float radius = MathK.Max(0.12f, MathK.Min(def.Size.x, def.Size.z) * 0.3f);
                // The probe must clear the prop's OWN body (it sits on the world layer this query uses, so a
                // sphere that touches it reads as blocked and the object would never move - only turn to face
                // you) and it must clear the floor. Push it a full radius past the leading face, and hold it a
                // radius above the base.
                float ahead = MathK.Max(def.Size.x, def.Size.z) * 0.5f + radius + 0.08f;
                float probeY = next.y + MathK.Max(def.Size.y * 0.5f, radius + 0.06f);
                Vec3 probe = new Vec3(next.x + dir.x * ahead, probeY, next.z + dir.z * ahead);
                blocked = collision.CheckSphere(probe, radius);
            }
            if (!blocked) world.Props[current].Position = next;

            world.Props[current].Yaw = s.Yaw;
            s.CarryMass = def.Mass;
        }

        /// <summary>
        /// How fast an object of this mass follows your hands. The movement core reads the same
        /// number to cap your own speed, so walking in a straight line can never outrun your grip.
        /// </summary>
        public static float DragSpeed(float mass, MovementTuning t)
        {
            return t.dragSpeedBase / (1f + MathK.Max(0f, mass) * MathK.Max(0f, t.dragMassFactor));
        }

        /// <summary>Nearest thing in front of you, within reach of where you are looking.</summary>
        public static int FindGrabbable(int playerId, in PlayerSimState s, MovementTuning t, WorldModel model, WorldState world)
        {
            Vec3 eye = s.EyePosition(t);
            Vec3 look = s.LookDirection();
            int best = -1;
            float bestScore = float.MaxValue;

            for (int i = 0; i < world.Props.Length && i < model.Props.Count; i++)
            {
                if (world.Props[i].IsHeld) continue;

                PropDef def = model.Props[i];
                Vec3 centre = world.Props[i].Position + Vec3.Up * (def.Size.y * 0.5f);
                Vec3 delta = centre - eye;
                float distance = delta.Magnitude;
                if (distance > t.grabRange + MathK.Max(def.Size.x, def.Size.z) * 0.5f) continue;
                if (distance > 0.05f && Vec3.Dot(delta.Normalized, look) < 0.55f) continue;

                if (distance >= bestScore) continue;
                bestScore = distance;
                best = i;
            }
            return best;
        }

        /// <summary>Drops whatever a player was holding - on death, or on leaving.</summary>
        public static void ReleaseAll(int playerId, WorldState world)
        {
            if (world == null) return;
            for (int i = 0; i < world.Props.Length; i++)
                if (world.Props[i].Grabber == (byte)playerId) world.Props[i].Grabber = Nobody;
        }
    }
}
