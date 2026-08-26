namespace Satisfying.Shared
{
    /// <summary>
    /// What a piece of the map is made of. It decides three things that all want the same answer:
    /// whether a round goes through it, what your boots sound like on it, and what a grenade sounds
    /// like bouncing off it.
    /// </summary>
    public enum SurfaceKind : byte
    {
        Concrete = 0,
        Wood = 1,
        Drywall = 2,
        Metal = 3,
        Glass = 4
    }

    /// <summary>
    /// A piece of map that a round can go through, and what it costs to do it.
    ///
    /// Penetration is deliberately NOT a property of every wall. A game where everything is
    /// shootable is a game with no cover in it, and a game where nothing is is a game where the
    /// wall you are standing behind is as good as a bunker. So panels are placed by hand, by the
    /// map, and the map is built by the same code on both machines - which means the client can
    /// draw a tracer that goes through the same wall the server let it through, without any of it
    /// being replicated.
    /// </summary>
    public struct PanelDef
    {
        public Box Bounds;
        public SurfaceKind Kind;
    }

    /// <summary>
    /// How hard each material is to shoot through, in "penetration metres per metre of material".
    /// A weapon's penetration budget is spent against these, so the numbers are readable: drywall at
    /// 1 means a rifle with 0.16 of budget gets through 160 mm of it.
    /// </summary>
    [System.Serializable]
    public class PenetrationTuning
    {
        [Tune("Penetration", 0.1f, 30f, Tip = "Cost per metre of plasterboard. The soft stuff - internal partitions, hollow doors.")]
        public float drywall = 1f;

        [Tune("Penetration", 0.1f, 30f, Tip = "Cost per metre of timber - floorboards, joists, a garden fence.")]
        public float wood = 2.4f;

        [Tune("Penetration", 0.1f, 30f, Tip = "Cost per metre of glass. Almost free, and it breaks anyway.")]
        public float glass = 0.3f;

        [Tune("Penetration", 0.1f, 40f, Tip = "Cost per metre of sheet metal - a garage door, a locker.")]
        public float metal = 7f;

        [Tune("Penetration", 0.1f, 60f, Tip = "Cost per metre of concrete. Nothing in this game gets through much of it.")]
        public float concrete = 14f;

        [Tune("Penetration", 0f, 1f, Tip = "Damage left after a round has spent its whole budget getting through something.")]
        public float exitDamageFloor = 0.35f;

        [Tune("Penetration", 1f, 4f, Tip = "How many surfaces one round may cross before it stops.")]
        public float maxLayers = 2f;

        public float CostPerMetre(SurfaceKind kind)
        {
            switch (kind)
            {
                case SurfaceKind.Wood: return wood;
                case SurfaceKind.Drywall: return drywall;
                case SurfaceKind.Glass: return glass;
                case SurfaceKind.Metal: return metal;
                default: return concrete;
            }
        }

        public int MaxLayersInt { get { return MathK.Clamp(MathK.RoundToInt(maxLayers), 1, 4); } }

        public PenetrationTuning Clone() { return (PenetrationTuning)MemberwiseClone(); }
    }

    /// <summary>
    /// Tracing a round through things instead of stopping it at the first one.
    ///
    /// The rule is a budget: a weapon carries so many "penetration metres", each material costs so
    /// many per metre of itself, and a round comes out the far side with the damage it has left. A
    /// rifle round crosses a plasterboard partition and still hurts; the same round meets a concrete
    /// wall and does not come out at all. That is the entire model, and it is the whole reason a
    /// soft wall is worth building a room around.
    /// </summary>
    public static class Penetration
    {
        /// <summary>
        /// The slab a ray crosses: where it goes in, where it comes out, and what it was made of.
        /// Entry is the distance to the near face - which for a ray starting inside the panel is 0.
        /// </summary>
        public static bool Slab(in Box box, Vec3 origin, Vec3 direction, float maxDistance,
                                out float entry, out float exit)
        {
            entry = 0f;
            exit = 0f;

            Vec3 min = box.Min;
            Vec3 max = box.Max;
            float near = 0f;
            float far = maxDistance;

            for (int axis = 0; axis < 3; axis++)
            {
                float o = axis == 0 ? origin.x : axis == 1 ? origin.y : origin.z;
                float d = axis == 0 ? direction.x : axis == 1 ? direction.y : direction.z;
                float lo = axis == 0 ? min.x : axis == 1 ? min.y : min.z;
                float hi = axis == 0 ? max.x : axis == 1 ? max.y : max.z;

                if (MathK.Abs(d) < 1e-6f)
                {
                    if (o < lo || o > hi) return false;      // parallel and outside the slab
                    continue;
                }

                float inverse = 1f / d;
                float t0 = (lo - o) * inverse;
                float t1 = (hi - o) * inverse;
                if (t0 > t1) { float swap = t0; t0 = t1; t1 = swap; }

                if (t0 > near) near = t0;
                if (t1 < far) far = t1;
                if (near > far) return false;
            }

            if (far <= 0f) return false;
            entry = MathK.Max(0f, near);
            exit = far;
            return exit > entry;
        }

        /// <summary>
        /// The nearest penetrable panel a ray enters, at or just beyond `atDistance`.
        ///
        /// The caller has already found where the round stopped on the world; this answers "and was
        /// that thing something I can go through". Matching on the entry distance rather than just
        /// picking the nearest panel is what keeps a panel BEHIND a concrete wall from letting a
        /// round through the concrete.
        /// </summary>
        public static bool PanelAt(WorldModel model, Vec3 origin, Vec3 direction, float atDistance,
                                   float tolerance, out PanelDef panel, out float thickness, out float exit)
        {
            panel = new PanelDef();
            thickness = 0f;
            exit = 0f;
            if (model == null) return false;

            bool found = false;
            float bestEntry = float.MaxValue;

            for (int i = 0; i < model.Panels.Count; i++)
            {
                float entry, leave;
                if (!Slab(model.Panels[i].Bounds, origin, direction, atDistance + tolerance + 8f, out entry, out leave))
                    continue;
                if (entry > atDistance + tolerance) continue;
                if (leave <= atDistance - tolerance) continue;      // already behind us
                if (entry >= bestEntry) continue;

                bestEntry = entry;
                panel = model.Panels[i];
                thickness = leave - entry;
                exit = leave;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// What is left of a round after crossing `thickness` of `kind`, given a budget. Returns false
        /// when it does not come out at all.
        /// </summary>
        public static bool Through(PenetrationTuning tuning, float budget, SurfaceKind kind, float thickness,
                                   out float damageScale)
        {
            damageScale = 0f;
            if (tuning == null || budget <= 0f || thickness <= 0f) return false;

            float cost = thickness * tuning.CostPerMetre(kind);
            if (cost > budget) return false;

            // Spending nothing costs nothing; spending the lot leaves the floor.
            float spent = MathK.Clamp01(cost / MathK.Max(0.0001f, budget));
            damageScale = MathK.Lerp(1f, MathK.Clamp01(tuning.exitDamageFloor), spent);
            return true;
        }
    }
}
