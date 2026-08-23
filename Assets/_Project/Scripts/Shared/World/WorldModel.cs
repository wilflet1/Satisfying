using System.Collections.Generic;

namespace Satisfying.Shared
{
    /// <summary>A pane of glass. Solid until something goes through it, then an opening.</summary>
    public struct WindowDef
    {
        public Box Bounds;
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

        public void Clear()
        {
            Windows.Clear();
            Props.Clear();
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
