using System.Collections.Generic;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// A house, and the three rooms in it worth fighting over.
    ///
    /// The brief for this map was compact, sensible, and with more than one way into everywhere - so
    /// it is laid out like a house rather than like an arena. Twenty metres by fourteen of plot, a
    /// two storey building on it, and a yard round the back.
    ///
    ///                        N
    ///        +-------------------------------+
    ///        |  yard        [shed]           |
    ///        |    +----------------------+   |
    ///        |    | kitchen  |  living   |   |     upstairs, over the same footprint:
    ///        |    |  (tile)  |  (wood)   |   |       landing + bedroom (boards) over living
    ///        |    |----+ ----+           |   |       bathroom over kitchen
    ///        |    | hall    stairs       |   |
    ///        +----+---[porch]------------+---+
    ///                        S  (front)
    ///
    /// The three ways between the floors are the staircase, the landing window onto the porch roof
    /// (mantle up from the yard wall), and the hole in the bedroom floor over the living room - so
    /// nobody can hold the stairs and own the building.
    ///
    /// Floors are deliberately different materials: tiles in the kitchen, boards everywhere else,
    /// concrete outside. That is audible - Synth.Footstep makes boards ring and concrete slap - and
    /// it is the point of the wall between the kitchen and the living room being plasterboard: you
    /// hear which side of it he is on, and then you shoot through it.
    /// </summary>
    public static class HouseBuilder
    {
        // The building, in metres. Everything else is derived so the whole house can be resized.
        const float Width = 13f;         // x, side to side
        const float Depth = 10f;         // z, front to back
        const float StoreyHeight = 2.9f;
        const float WallThickness = 0.28f;
        const float SoftThickness = 0.12f;   // an internal stud wall

        const float PlotWidth = 21f;
        const float PlotDepth = 17f;

        public static ArenaBuilder.Result Build(SpawnSet spawns, Palette palette, int layer, WorldModel model,
                                                List<ZoneDef> zones)
        {
            GameObject root = new GameObject("House");
            Transform t = root.transform;

            float half = Width * 0.5f;
            float back = Depth * 0.5f;
            float upper = StoreyHeight + 0.25f;      // the floor level of the first floor

            Ground(t, palette, layer);
            Yard(t, palette, layer, model);

            GroundFloor(t, palette, layer, model, half, back, upper);
            FirstFloor(t, palette, layer, model, half, back, upper);
            Roof(t, palette, layer, half, back, upper);

            // ---- the three rooms worth holding. Two down, one up, so the hill makes you change floor.
            zones.Clear();
            zones.Add(Zone("the living room", new Vector3(half * 0.45f, 1.2f, 0f), new Vector3(6.0f, 2.6f, 8.6f)));
            zones.Add(Zone("the kitchen", new Vector3(-half * 0.55f, 1.2f, back * 0.42f), new Vector3(5.4f, 2.6f, 5.0f)));
            zones.Add(Zone("the bedroom", new Vector3(half * 0.45f, upper + 1.2f, -back * 0.25f), new Vector3(6.0f, 2.6f, 5.4f)));

            // ---- spawns, at opposite corners of the plot and outside the building. Nobody starts on
            // the hill, and nobody starts in a room with one door.
            spawns.Points.Clear();
            spawns.Add(new Vec3(-PlotWidth * 0.42f, 0f, -PlotDepth * 0.40f), 35f);
            spawns.Add(new Vec3(PlotWidth * 0.42f, 0f, PlotDepth * 0.40f), 215f);
            spawns.Add(new Vec3(PlotWidth * 0.42f, 0f, -PlotDepth * 0.40f), 320f);
            spawns.Add(new Vec3(-PlotWidth * 0.42f, 0f, PlotDepth * 0.40f), 130f);

            ArenaBuilder.Result result = new ArenaBuilder.Result();
            result.Root = root;
            result.Spawns = spawns;
            result.Bounds = new Bounds(Vector3.zero, new Vector3(PlotWidth, 12f, PlotDepth));
            result.Stations = new List<ArenaBuilder.Station>();
            return result;
        }

        static ZoneDef Zone(string name, Vector3 centre, Vector3 size)
        {
            ZoneDef zone;
            zone.Name = name;
            zone.Bounds = new Box(centre.ToSim(), size.ToSim());
            return zone;
        }

        // ================================================================== outside

        static void Ground(Transform t, Palette palette, int layer)
        {
            Blockout.Box(t, "plot", new Vector3(0f, -0.5f, 0f), new Vector3(PlotWidth, 1f, PlotDepth),
                palette.Ground, true, layer);

            // A low wall right round it, so the fight stays on the plot and there is something to
            // mantle from at the back.
            float h = 1.15f;
            Slab(t, palette, layer, "wall north", new Vector3(0f, h * 0.5f, PlotDepth * 0.5f), new Vector3(PlotWidth, h, 0.35f));
            Slab(t, palette, layer, "wall south", new Vector3(0f, h * 0.5f, -PlotDepth * 0.5f), new Vector3(PlotWidth, h, 0.35f));
            Slab(t, palette, layer, "wall east", new Vector3(PlotWidth * 0.5f, h * 0.5f, 0f), new Vector3(0.35f, h, PlotDepth));
            Slab(t, palette, layer, "wall west", new Vector3(-PlotWidth * 0.5f, h * 0.5f, 0f), new Vector3(0.35f, h, PlotDepth));
        }

        static void Yard(Transform t, Palette palette, int layer, WorldModel model)
        {
            float back = Depth * 0.5f;

            // A shed against the back wall: cover in the open, and a step up to the boundary wall,
            // which is how you get onto the porch roof without using the stairs.
            Slab(t, palette, layer, "shed", new Vector3(-6.2f, 1.05f, back + 3.4f), new Vector3(3.0f, 2.1f, 2.4f));
            Slab(t, palette, layer, "shed step", new Vector3(-4.4f, 0.45f, back + 3.4f), new Vector3(0.7f, 0.9f, 2.0f));

            // Sheet metal: you can hear it and you can just about shoot through it, which makes
            // standing behind it a decision rather than a free win.
            Panel(t, palette, layer, model, "shed door", new Vector3(-6.2f, 1.0f, back + 2.15f),
                new Vector3(1.6f, 2.0f, 0.10f), SurfaceKind.Metal, palette.Metal);

            // Something to break line of sight across the front, and a fence panel you can shoot.
            Slab(t, palette, layer, "planter", new Vector3(6.4f, 0.5f, -back - 3.2f), new Vector3(3.6f, 1.0f, 1.2f));
            Panel(t, palette, layer, model, "fence", new Vector3(-7.6f, 0.9f, -back - 3.0f),
                new Vector3(4.4f, 1.8f, 0.10f), SurfaceKind.Wood, palette.Timber);

            // Concrete path round the east side: the flanking route, and it sounds different underfoot.
            Blockout.Box(t, "path", new Vector3(Width * 0.5f + 1.6f, 0.02f, 0f), new Vector3(2.4f, 0.06f, PlotDepth - 2f),
                palette.WallDark, false, layer);
        }

        // ================================================================== ground floor

        static void GroundFloor(Transform t, Palette palette, int layer, WorldModel model,
                                float half, float back, float upper)
        {
            float h = StoreyHeight;

            // ---- floors. Boards through the living room and hall, tile in the kitchen. These are
            // thin slabs laid on the plot rather than the plot itself, so SurfaceAt can tell them
            // apart and your boots know which room you are in.
            Panel(t, palette, layer, model, "living floor", new Vector3(half * 0.45f, 0.03f, 0f),
                new Vector3(Width * 0.52f, 0.06f, Depth - 0.4f), SurfaceKind.Wood, palette.Timber);
            Panel(t, palette, layer, model, "kitchen floor", new Vector3(-half * 0.52f, 0.03f, back * 0.42f),
                new Vector3(Width * 0.46f, 0.06f, Depth * 0.56f), SurfaceKind.Concrete, palette.Tile);
            Panel(t, palette, layer, model, "hall floor", new Vector3(-half * 0.52f, 0.03f, -back * 0.55f),
                new Vector3(Width * 0.46f, 0.06f, Depth * 0.42f), SurfaceKind.Wood, palette.Timber);

            // ---- outside walls, with the openings cut in them.
            // Front: the door, and a window into the living room.
            Opening(t, palette, layer, "front west", new Vector3(-half * 0.62f, h * 0.5f, -back),
                new Vector3(Width * 0.42f, h, WallThickness), 1.3f, 2.2f, 1.1f);              // front door
            ArenaBuilder.PublicGlazedWall(t, palette, layer, model, new Vector3(half * 0.45f, h * 0.5f, -back),
                new Vector3(Width * 0.52f, h, WallThickness), 2.4f, 1.3f, 1.45f);             // living window

            // Back: kitchen door out to the yard, and a big window over the sink.
            Opening(t, palette, layer, "back west", new Vector3(-half * 0.52f, h * 0.5f, back),
                new Vector3(Width * 0.46f, h, WallThickness), 1.2f, 2.1f, 1.05f);
            ArenaBuilder.PublicGlazedWall(t, palette, layer, model, new Vector3(half * 0.45f, h * 0.5f, back),
                new Vector3(Width * 0.52f, h, WallThickness), 2.8f, 1.2f, 1.5f);

            // Sides: a window each, low enough to vault.
            ArenaBuilder.PublicGlazedWall(t, palette, layer, model, new Vector3(half, h * 0.5f, back * 0.3f),
                new Vector3(WallThickness, h, Depth * 0.5f), 2.2f, 1.4f, 1.2f);
            Slab(t, palette, layer, "west wall", new Vector3(-half, h * 0.5f, 0f), new Vector3(WallThickness, h, Depth));

            // ---- the internal wall between the kitchen and the living room. THIS is the one the map
            // is built around: plasterboard, with a tile floor on one side and boards on the other, so
            // you can hear which room he is in and then put a round through the wall at him.
            Panel(t, palette, layer, model, "partition", new Vector3(0f, h * 0.5f, back * 0.35f),
                new Vector3(SoftThickness, h, Depth * 0.62f), SurfaceKind.Drywall, palette.Plaster);

            // A doorway between them, offset so the partition is worth shooting rather than walking round.
            Opening(t, palette, layer, "partition front", new Vector3(0f, h * 0.5f, -back * 0.55f),
                new Vector3(SoftThickness, h, Depth * 0.42f), 1.2f, 2.1f, 1.05f);

            // ---- kitchen furniture: a counter to crouch behind and an island to fight round.
            Slab(t, palette, layer, "counter", new Vector3(-half * 0.52f, 0.48f, back * 0.78f), new Vector3(Width * 0.42f, 0.96f, 0.7f));
            Slab(t, palette, layer, "island", new Vector3(-half * 0.55f, 0.46f, back * 0.12f), new Vector3(2.4f, 0.92f, 1.1f));

            // ---- living room: a sofa and a low table, and the staircase.
            Slab(t, palette, layer, "sofa", new Vector3(half * 0.75f, 0.42f, -back * 0.45f), new Vector3(2.6f, 0.84f, 0.95f));
            Slab(t, palette, layer, "table", new Vector3(half * 0.42f, 0.24f, back * 0.15f), new Vector3(1.5f, 0.48f, 0.9f));

            Stairs(t, palette, layer, model, half, back, upper);
        }

        /// <summary>
        /// The staircase, against the hall's west wall. Built as steps you walk up rather than a ramp,
        /// because the movement code steps and a ramp would make it a slide.
        /// </summary>
        static void Stairs(Transform t, Palette palette, int layer, WorldModel model, float half, float back, float upper)
        {
            const int steps = 14;
            float run = 0.30f;
            float rise = upper / steps;
            float x = -half * 0.78f;
            float startZ = -back * 0.86f;

            for (int i = 0; i < steps; i++)
            {
                float z = startZ + run * i;
                Blockout.Box(t, "step " + i, new Vector3(x, rise * (i + 0.5f) * 0.5f + rise * i * 0.5f, z),
                    new Vector3(1.25f, rise * (i + 1f), run), palette.WallDark, true, layer);
            }

            // A half landing at the top so you arrive on something rather than on the last step.
            Slab(t, palette, layer, "stair landing", new Vector3(x, upper - 0.06f, startZ + run * steps + 0.6f),
                new Vector3(1.25f, 0.12f, 1.2f));

            // The banister. Chest high, so the stairs are cover from the hall but not from upstairs.
            Slab(t, palette, layer, "banister", new Vector3(x + 0.72f, upper * 0.55f, startZ + run * steps * 0.5f),
                new Vector3(0.12f, 1.0f, run * steps));
        }

        // ================================================================== first floor

        static void FirstFloor(Transform t, Palette palette, int layer, WorldModel model,
                               float half, float back, float upper)
        {
            float h = StoreyHeight;

            // ---- the floor, in two pieces with a gap between them. Boards: loud, and thin enough to
            // shoot up through from the living room if you know where he is standing.
            Panel(t, palette, layer, model, "bedroom floor", new Vector3(half * 0.45f, upper - 0.06f, -back * 0.25f),
                new Vector3(Width * 0.52f, 0.12f, Depth * 0.60f), SurfaceKind.Wood, palette.Timber);
            Panel(t, palette, layer, model, "bathroom floor", new Vector3(-half * 0.52f, upper - 0.06f, back * 0.42f),
                new Vector3(Width * 0.46f, 0.12f, Depth * 0.56f), SurfaceKind.Wood, palette.Timber);
            Panel(t, palette, layer, model, "landing floor", new Vector3(-half * 0.52f, upper - 0.06f, -back * 0.55f),
                new Vector3(Width * 0.46f, 0.12f, Depth * 0.42f), SurfaceKind.Wood, palette.Timber);

            // A gap in the floor over the living room - the third way between the storeys. You can
            // drop through it either way and mantle back up from the table underneath.
            Panel(t, palette, layer, model, "bedroom floor north", new Vector3(half * 0.45f, upper - 0.06f, back * 0.44f),
                new Vector3(Width * 0.52f, 0.12f, Depth * 0.10f), SurfaceKind.Wood, palette.Timber);

            // ---- outside walls of the upper storey.
            float y = upper + h * 0.5f;
            ArenaBuilder.PublicGlazedWall(t, palette, layer, model, new Vector3(half * 0.45f, y, -back),
                new Vector3(Width * 0.52f, h, WallThickness), 2.2f, 1.3f, upper + 1.35f);
            Slab(t, palette, layer, "upper front west", new Vector3(-half * 0.62f, y, -back), new Vector3(Width * 0.42f, h, WallThickness));
            Slab(t, palette, layer, "upper back", new Vector3(0f, y, back), new Vector3(Width, h, WallThickness));
            Slab(t, palette, layer, "upper east", new Vector3(half, y, 0f), new Vector3(WallThickness, h, Depth));

            // The landing window, over the porch roof. This is the outside route in: up the shed, along
            // the boundary wall, onto the porch, through here.
            ArenaBuilder.PublicGlazedWall(t, palette, layer, model, new Vector3(-half, y, -back * 0.55f),
                new Vector3(WallThickness, h, Depth * 0.42f), 1.6f, 1.6f, upper + 1.1f);
            Slab(t, palette, layer, "upper west", new Vector3(-half, y, back * 0.3f), new Vector3(WallThickness, h, Depth * 0.5f));

            // ---- the wall between the bedroom and the landing: plasterboard again, and the bedroom
            // is the upstairs hill, so it can be fought through as well as walked into.
            Panel(t, palette, layer, model, "upper partition", new Vector3(0f, y, -back * 0.25f),
                new Vector3(SoftThickness, h, Depth * 0.44f), SurfaceKind.Drywall, palette.Plaster);
            Opening(t, palette, layer, "bedroom door", new Vector3(0f, y, back * 0.36f),
                new Vector3(SoftThickness, h, Depth * 0.5f), 1.1f, 2.05f, upper + 1.03f);

            // ---- a wardrobe and a bed, for cover in the room people will be fighting over.
            Slab(t, palette, layer, "wardrobe", new Vector3(half * 0.82f, upper + 1.05f, -back * 0.62f), new Vector3(1.1f, 2.1f, 0.65f));
            Slab(t, palette, layer, "bed", new Vector3(half * 0.42f, upper + 0.28f, -back * 0.25f), new Vector3(2.0f, 0.56f, 1.5f));

            // The porch roof outside the landing window: the thing you mantle onto.
            Slab(t, palette, layer, "porch roof", new Vector3(-half - 1.3f, upper - 0.35f, -back * 0.55f),
                new Vector3(2.8f, 0.3f, 3.6f));
            Slab(t, palette, layer, "porch post", new Vector3(-half - 2.5f, upper * 0.5f, -back * 0.95f),
                new Vector3(0.25f, upper, 0.25f));
        }

        static void Roof(Transform t, Palette palette, int layer, float half, float back, float upper)
        {
            float top = upper + StoreyHeight;
            Slab(t, palette, layer, "roof", new Vector3(0f, top + 0.15f, 0f), new Vector3(Width + 0.5f, 0.3f, Depth + 0.5f));
        }

        // ================================================================== helpers

        static void Slab(Transform t, Palette palette, int layer, string name, Vector3 centre, Vector3 size)
        {
            Blockout.Box(t, name, centre, size, palette.Wall, true, layer);
        }

        /// <summary>
        /// A piece of the building that a round can go through, registered with the world model so the
        /// simulation knows what it is made of. The material also decides what your boots sound like
        /// on it, which is why the floors are panels too.
        /// </summary>
        static void Panel(Transform t, Palette palette, int layer, WorldModel model, string name,
                          Vector3 centre, Vector3 size, SurfaceKind kind, Material material)
        {
            Blockout.Box(t, name, centre, size, material, true, layer);
            model.AddPanel(centre.ToSim(), size.ToSim(), kind);
        }

        /// <summary>A wall with a doorway in it: two cheeks and a header, no glass.</summary>
        static void Opening(Transform t, Palette palette, int layer, string name, Vector3 centre, Vector3 size,
                            float holeWidth, float holeHeight, float holeCentreY)
        {
            bool thinX = size.x <= size.z;
            float thickness = thinX ? size.x : size.z;
            float span = thinX ? size.z : size.x;

            float halfHole = Mathf.Max(0.2f, Mathf.Min(holeWidth, span - 0.2f)) * 0.5f;
            float holeTop = holeCentreY + holeHeight * 0.5f;
            float wallTop = centre.y + size.y * 0.5f;
            float wallBottom = centre.y - size.y * 0.5f;
            float holeBottom = holeCentreY - holeHeight * 0.5f;

            float sill = holeBottom - wallBottom;
            if (sill > 0.01f)
                Slab(t, palette, layer, name + " sill", new Vector3(centre.x, wallBottom + sill * 0.5f, centre.z),
                    thinX ? new Vector3(thickness, sill, span) : new Vector3(span, sill, thickness));

            float header = wallTop - holeTop;
            if (header > 0.01f)
                Slab(t, palette, layer, name + " header", new Vector3(centre.x, holeTop + header * 0.5f, centre.z),
                    thinX ? new Vector3(thickness, header, span) : new Vector3(span, header, thickness));

            float cheek = span * 0.5f - halfHole;
            if (cheek <= 0.01f) return;

            float offset = halfHole + cheek * 0.5f;
            Vector3 cheekSize = thinX ? new Vector3(thickness, holeHeight, cheek) : new Vector3(cheek, holeHeight, thickness);
            Slab(t, palette, layer, name + " cheek a",
                thinX ? new Vector3(centre.x, holeCentreY, centre.z + offset) : new Vector3(centre.x + offset, holeCentreY, centre.z),
                cheekSize);
            Slab(t, palette, layer, name + " cheek b",
                thinX ? new Vector3(centre.x, holeCentreY, centre.z - offset) : new Vector3(centre.x - offset, holeCentreY, centre.z),
                cheekSize);
        }
    }
}
