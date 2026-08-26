using System.Collections.Generic;

namespace Satisfying.Shared
{
    /// <summary>A pane of glass. Solid until something goes through it, then an opening.</summary>
    public struct WindowDef
    {
        public Box Bounds;
    }

    /// <summary>
    /// A practice target. It has no state - it does not break or move - it exists so the server can
    /// tell you that you hit it, which is the whole point of a range.
    /// </summary>
    public struct TargetDef
    {
        public Box Bounds;
        public bool Head;
    }

    /// <summary>A movable object. Heavier means slower to drag, for you and for it.</summary>
    public struct PropDef
    {
        public Vec3 SpawnPosition;
        public Vec3 Size;
        public float Mass;
    }

    /// <summary>
    /// The parts of the map that can change, described identically on both machines because both
    /// build the same arena from the same code. Only the state has to be replicated, never the shapes.
    /// </summary>
    public sealed class WorldModel
    {
        public readonly List<WindowDef> Windows = new List<WindowDef>();
        public readonly List<PropDef> Props = new List<PropDef>();
        public readonly List<TargetDef> Targets = new List<TargetDef>();

        /// <summary>
        /// The parts of the map a round can go through. Placed by hand by the map, because a game
        /// where every wall is penetrable has no cover in it. Not replicated - both machines build
        /// the same arena from the same code, so both already know where the soft walls are.
        /// </summary>
        public readonly List<PanelDef> Panels = new List<PanelDef>();

        public void Clear()
        {
            Windows.Clear();
            Props.Clear();
            Targets.Clear();
            Panels.Clear();
        }

        public int AddTarget(Vec3 center, Vec3 size, bool head)
        {
            TargetDef def;
            def.Bounds = new Box(center, size);
            def.Head = head;
            Targets.Add(def);
            return Targets.Count - 1;
        }

        /// <summary>
        /// Nearest target along a ray. Targets are ordinary geometry as far as collision goes, so the
        /// caller passes the distance to the nearest wall as the limit: come back inside that and the
        /// target was what the round actually hit.
        /// </summary>
        public bool RaycastTargets(Vec3 origin, Vec3 direction, float maxDistance, out int index, out bool head, out float distance)
        {
            index = -1;
            head = false;
            distance = maxDistance;
            for (int i = 0; i < Targets.Count; i++)
            {
                float hit;
                if (!BoxMath.Raycast(Targets[i].Bounds, origin, direction, distance, out hit)) continue;
                distance = hit;
                index = i;
                head = Targets[i].Head;
            }
            return index >= 0;
        }

        public int AddPanel(Vec3 center, Vec3 size, SurfaceKind kind)
        {
            PanelDef def;
            def.Bounds = new Box(center, size);
            def.Kind = kind;
            Panels.Add(def);
            return Panels.Count - 1;
        }

        /// <summary>What is underfoot at a point, for footsteps and for anything bouncing on it.
        /// Panels are the only thing in the map that knows what it is made of, so a point that is not
        /// on one is concrete - which is what the ground is.</summary>
        public SurfaceKind SurfaceAt(Vec3 point)
        {
            for (int i = 0; i < Panels.Count; i++)
                if (Panels[i].Bounds.Contains(point)) return Panels[i].Kind;
            return SurfaceKind.Concrete;
        }

        public int AddWindow(Vec3 center, Vec3 size)
        {
            WindowDef def;
            def.Bounds = new Box(center, size);
            Windows.Add(def);
            return Windows.Count - 1;
        }

        public int AddProp(Vec3 position, Vec3 size, float mass)
        {
            PropDef def;
            def.SpawnPosition = position;
            def.Size = size;
            def.Mass = mass;
            Props.Add(def);
            return Props.Count - 1;
        }
    }

    public struct PropState
    {
        public Vec3 Position;
        public float Yaw;
        public byte Grabber;        // 255 = nobody has hold of it

        public bool IsHeld { get { return Grabber != 255; } }
    }

    /// <summary>What the server owns about the changeable world and sends to everyone.</summary>
    public sealed class WorldState
    {
        public bool[] WindowBroken = new bool[0];
        public PropState[] Props = new PropState[0];

        public void Reset(WorldModel model)
        {
            WindowBroken = new bool[model.Windows.Count];
            Props = new PropState[model.Props.Count];
            for (int i = 0; i < Props.Length; i++)
            {
                Props[i].Position = model.Props[i].SpawnPosition;
                Props[i].Yaw = 0f;
                Props[i].Grabber = 255;
            }
        }

        public bool IsBroken(int windowIndex)
        {
            return windowIndex >= 0 && windowIndex < WindowBroken.Length && WindowBroken[windowIndex];
        }

        /// <summary>
        /// Nearest intact pane along a ray. Broken ones are simply not there any more, which is what
        /// makes shooting a window open both a firing line and a sound path through it.
        /// </summary>
        public bool RaycastWindows(WorldModel model, Vec3 origin, Vec3 direction, float maxDistance,
                                   out int windowIndex, out float distance)
        {
            windowIndex = -1;
            distance = maxDistance;

            for (int i = 0; i < model.Windows.Count; i++)
            {
                if (IsBroken(i)) continue;
                float hit;
                if (!BoxMath.Raycast(model.Windows[i].Bounds, origin, direction, distance, out hit)) continue;
                distance = hit;
                windowIndex = i;
            }
            return windowIndex >= 0;
        }

        public int FindPropHeldBy(int peerId)
        {
            for (int i = 0; i < Props.Length; i++)
                if (Props[i].Grabber == (byte)peerId) return i;
            return -1;
        }
    }
}
