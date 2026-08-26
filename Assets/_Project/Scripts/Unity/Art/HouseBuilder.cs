using System.Collections.Generic;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// A house on a plot, built to a plan rather than by eye.
    ///
    /// The first version of this was assembled out of percentages of the building's own width, which
    /// is how it ended up with a gap at every junction and a staircase buried under the floor above
    /// it: nothing lined up with anything because no two pieces were measured from the same thing.
    /// This one works the way a drawing does - a grid of named lines in metres, every wall placed
    /// BETWEEN two of them, every opening cut out of the wall that owns it. If two things touch, it
    /// is because they share a line.
    ///
    /// PLOT: 46 x 38. The building sits on the south half; the rest is drive, side paths and garden,
    /// which is where the long sightlines are.
    ///
    ///        W0        W1        W2        W3        W4              (x lines, west to east)
    ///         |         |         |         |         |
    ///   N3 ---+-------------------+---------+---------+---  back wall
    ///         |     KITCHEN (tile)          | DINING  |
    ///   N2 ---+---------+---------+---------+---------+---  SPINE (plasterboard)
    ///         | UTILITY |  HALL   |     LIVING (wood) |
    ///   N1 ---+---------+ stairs  +-------------------+
    ///         | GARAGE  |         |                   |
    ///   N0 ---+---------+---------+-------------------+---  front wall
    ///              door     porch        window
    ///
    /// Upstairs sits on the same lines: BATHROOM over the utility, LANDING over the hall, MASTER
    /// BEDROOM over the living room and dining room. The stairwell is a hole cut in the first floor
    /// from the same four numbers the staircase is built from, so the stairs have somewhere to
    /// arrive - which is the specific thing that was broken.
    ///
    /// THREE WAYS BETWEEN THE FLOORS, on purpose: the staircase, the landing window onto the porch
    /// roof, and the garage roof - which you reach off the crates inside the garage. Nobody owns the
    /// building by holding one of them.
    ///
    /// FLOORS ARE MATERIALS AND THAT IS AUDIBLE. Boards in the living room, dining room, hall and
    /// bedrooms; tile in the kitchen, utility and bathroom; bare concrete in the garage, on the drive
    /// and on the paths; sheet steel on the garage roof. The spine wall between the kitchen and the
    /// living room is plasterboard with tile on one side and boards on the other, and that pairing is
    /// what the whole plan is built around: you hear which room he is in, then you shoot through the
    /// wall at him.
    /// </summary>
    public static class HouseBuilder
    {
        // ---------------------------------------------------------------- the plan, in metres
        const float W0 = -11f;      // west outside wall
        const float W1 = -5f;       // garage / utility east wall
        const float W2 = 0f;        // hall east wall
        const float W3 = 6.5f;      // kitchen east wall / dining west wall
        const float W4 = 11f;       // east outside wall

        const float N0 = -8f;       // front wall
        const float N1 = -2.5f;     // garage back wall, stair foot
        const float N2 = 2.5f;      // the spine
        const float N3 = 8f;        // back wall

        const float Wall = 0.30f;       // outside and structural walls
        const float Soft = 0.14f;       // internal stud walls - the shootable ones
        const float Storey = 3.1f;      // floor to ceiling
        const float Slab = 0.30f;       // thickness of the first floor
        const float Upper = Storey + Slab;      // floor level upstairs
        const float Ridge = Upper + Storey;     // top of the walls

        const float PlotX = 46f;
        const float PlotZ = 38f;

        const float DoorW = 1.3f;
        const float DoorH = 2.2f;

        // The stairwell. The staircase and the hole in the floor above it are both built from these,
        // which is the entire reason they meet.
        const float StairX0 = W1 + 0.5f;
        const float StairX1 = W2 - 0.5f;
        const float StairZ0 = N1;
        const float StairZ1 = N2 - 0.3f;

        public static ArenaBuilder.Result Build(SpawnSet spawns, Palette palette, int layer, WorldModel model,
                                                List<ZoneDef> zones)
        {
            GameObject root = new GameObject("House");
            Transform t = root.transform;

            Plot(t, palette, layer, model);
            GroundFloor(t, palette, layer, model);
            FirstFloor(t, palette, layer, model);
            Outside(t, palette, layer, model);
            Yard(t, palette, layer, model);

            // Four rooms worth holding: two down, one up, one outside. The hill has to make you change
            // floor and it has to make you leave the building.
            zones.Clear();
            zones.Add(Zone("the living room", (W2 + W4) * 0.5f, 0f, (N0 + N2) * 0.5f,
                           W4 - W2 - 0.8f, Storey - 0.4f, N2 - N0 - 0.8f));
            zones.Add(Zone("the kitchen", (W0 + W3) * 0.5f, 0f, (N2 + N3) * 0.5f,
                           W3 - W0 - 0.8f, Storey - 0.4f, N3 - N2 - 0.8f));
            zones.Add(Zone("the master bedroom", (W2 + W4) * 0.5f, Upper, (N1 + N3) * 0.5f,
                           W4 - W2 - 0.8f, Storey - 0.4f, N3 - N1 - 0.8f));
            zones.Add(Zone("the drive", -17f, 0f, -5f, 9f, 3.4f, 11f));

            Spawns(spawns);

            ArenaBuilder.Result result = new ArenaBuilder.Result();
            result.Root = root;
            result.Spawns = spawns;
            result.Bounds = new Bounds(Vector3.zero, new Vector3(PlotX, 16f, PlotZ));
            result.Stations = new List<ArenaBuilder.Station>();
            return result;
        }

        static ZoneDef Zone(string name, float x, float floorY, float z, float sizeX, float height, float sizeZ)
        {
            ZoneDef zone;
            zone.Name = name;
            zone.Bounds = new Box(new Vec3(x, floorY + height * 0.5f, z), new Vec3(sizeX, height, sizeZ));
            return zone;
        }

        /// <summary>
        /// Eight spawns round the edge of the plot. SpawnSet already picks the one furthest from
        /// everybody alive, so what matters here is that there are enough of them, that none is inside
        /// a room that can become the hill, and that none of them is looking down another one's throat.
        /// </summary>
        static void Spawns(SpawnSet spawns)
        {
            spawns.Points.Clear();
            spawns.Add(new Vec3(-20f, 0f, -16f), 35f);      // south west corner, behind the car
            spawns.Add(new Vec3(19f, 0f, -16f), 325f);      // south east, off the porch
            spawns.Add(new Vec3(20f, 0f, 14f), 220f);       // north east garden
            spawns.Add(new Vec3(-20f, 0f, 15f), 140f);      // north west, behind the shed
            spawns.Add(new Vec3(2f, 0f, 16f), 180f);        // back fence
            spawns.Add(new Vec3(-2f, 0f, -16.5f), 0f);      // front kerb
            spawns.Add(new Vec3(-20f, 0f, 3f), 90f);        // west path
            spawns.Add(new Vec3(20f, 0f, -2f), 270f);       // east path
        }

        // ================================================================== ground and boundary

        static void Plot(Transform t, Palette palette, int layer, WorldModel model)
        {
            Blockout.Box(t, "ground", new Vector3(0f, -0.5f, 0f), new Vector3(PlotX, 1f, PlotZ),
                palette.Ground, true, layer);

            const float h = 2.4f;
            Solid(t, palette, layer, "boundary north", new Vector3(0f, h * 0.5f, PlotZ * 0.5f), new Vector3(PlotX, h, 0.4f));
            Solid(t, palette, layer, "boundary south", new Vector3(0f, h * 0.5f, -PlotZ * 0.5f), new Vector3(PlotX, h, 0.4f));
            Solid(t, palette, layer, "boundary east", new Vector3(PlotX * 0.5f, h * 0.5f, 0f), new Vector3(0.4f, h, PlotZ));
            Solid(t, palette, layer, "boundary west", new Vector3(-PlotX * 0.5f, h * 0.5f, 0f), new Vector3(0.4f, h, PlotZ));

            Panel(t, palette, layer, model, "drive", new Vector3(-17f, 0.03f, -5f), new Vector3(9f, 0.06f, 12f),
                SurfaceKind.Concrete, palette.Tile);
            Panel(t, palette, layer, model, "path east", new Vector3(14f, 0.03f, 0f), new Vector3(5f, 0.06f, 30f),
                SurfaceKind.Concrete, palette.WallDark);
            Panel(t, palette, layer, model, "path west", new Vector3(-14f, 0.03f, 6f), new Vector3(5f, 0.06f, 18f),
                SurfaceKind.Concrete, palette.WallDark);
        }

        // ================================================================== ground floor

        static void GroundFloor(Transform t, Palette palette, int layer, WorldModel model)
        {
            float y = Storey * 0.5f;

            // ---- floors, one per room, each between its own plan lines.
            Floor(t, palette, layer, model, "garage floor", W0, W1, N0, N1, SurfaceKind.Concrete, palette.Tile);
            Floor(t, palette, layer, model, "utility floor", W0, W1, N1, N2, SurfaceKind.Concrete, palette.Tile);
            Floor(t, palette, layer, model, "hall floor", W1, W2, N0, N2, SurfaceKind.Wood, palette.Timber);
            Floor(t, palette, layer, model, "living floor", W2, W4, N0, N2, SurfaceKind.Wood, palette.Timber);
            Floor(t, palette, layer, model, "kitchen floor", W0, W3, N2, N3, SurfaceKind.Concrete, palette.Tile);
            Floor(t, palette, layer, model, "dining floor", W3, W4, N2, N3, SurfaceKind.Wood, palette.Timber);

            // ---- the shell. Each run spans two corner lines, so the corners meet.
            AlongX(t, palette, layer, model, "front", W0, W4, N0, y, Storey, Wall,
                   Hole.Door(-8f, 3.6f, 2.6f),                       // garage opening
                   Hole.Door(-2.5f, DoorW, DoorH),                   // front door
                   Hole.Window(7f, 3.2f, 1.6f, 1.35f));              // living room window

            AlongX(t, palette, layer, model, "back", W0, W4, N3, y, Storey, Wall,
                   Hole.Door(-9f, DoorW, DoorH),                     // kitchen door to the garden
                   Hole.Window(-2f, 3.6f, 1.4f, 1.55f),              // over the sink
                   Hole.Window(8.75f, 2.6f, 1.6f, 1.4f));            // dining

            AlongZ(t, palette, layer, model, "west", N0, N3, W0, y, Storey, Wall,
                   Hole.Window(5.5f, 2.4f, 1.4f, 1.5f));

            AlongZ(t, palette, layer, model, "east", N0, N3, W4, y, Storey, Wall,
                   Hole.Window(-5f, 2.6f, 1.6f, 1.35f),
                   Hole.Door(5f, DoorW, DoorH));                     // side door into the dining room

            // ---- internal structure.
            // Garage and utility are separated from the rest by a block wall with two doors in it.
            AlongZ(t, palette, layer, model, "garage east", N0, N2, W1, y, Storey, Wall,
                   Hole.Door(-5.5f, DoorW, DoorH),                   // garage to hall
                   Hole.Door(0f, DoorW, DoorH));                     // utility to hall
            AlongX(t, palette, layer, model, "garage back", W0, W1, N1, y, Storey, Wall,
                   Hole.Door(-8f, DoorW, DoorH));

            // THE SPINE. Plasterboard, tile on the north side and boards on the south. This is the
            // wall the map exists for, and the doorway is offset so going round is a real decision.
            AlongX(t, palette, layer, model, "spine", W0, W4, N2, y, Storey, Soft,
                   Hole.Door(-9f, DoorW, DoorH),                     // utility into the kitchen
                   Hole.Door(3f, 1.8f, 2.4f));                       // kitchen into the living room

            // Hall from the living room: another partition, so the ground floor is rooms.
            AlongZ(t, palette, layer, model, "hall east", N0, N2, W2, y, Storey, Soft,
                   Hole.Door(-5.5f, DoorW, DoorH),
                   Hole.Door(1f, 1.8f, 2.4f));

            Stairs(t, palette, layer);
            GroundFurniture(t, palette, layer);
        }

        /// <summary>
        /// The staircase: sixteen treads running north up the hall, arriving through the hole in the
        /// first floor. Each tread is a solid block from the ground up to its own height, so there is
        /// nothing to fall through and nothing to catch a foot on.
        /// </summary>
        static void Stairs(Transform t, Palette palette, int layer)
        {
            const int steps = 16;
            float run = (StairZ1 - StairZ0) / steps;
            float rise = Upper / steps;
            float x = (StairX0 + StairX1) * 0.5f;
            float width = StairX1 - StairX0;

            for (int i = 0; i < steps; i++)
            {
                float top = rise * (i + 1);
                float z = StairZ0 + run * (i + 0.5f);
                Blockout.Box(t, "step " + i, new Vector3(x, top * 0.5f, z), new Vector3(width, top, run),
                    palette.Timber, true, layer);
            }

            // Balustrade down the open side. Chest high: cover from the hall, none from the landing.
            Solid(t, palette, layer, "banister", new Vector3(StairX1 + 0.07f, Upper * 0.5f + 0.55f,
                (StairZ0 + StairZ1) * 0.5f), new Vector3(0.14f, 1.1f, StairZ1 - StairZ0));
        }

        static void GroundFurniture(Transform t, Palette palette, int layer)
        {
            // Kitchen: a run of units along the back and an island to fight round.
            Solid(t, palette, layer, "counter", new Vector3(-3f, 0.47f, N3 - 0.65f), new Vector3(14f, 0.94f, 0.7f));
            Solid(t, palette, layer, "island", new Vector3(-2.5f, 0.46f, 4.6f), new Vector3(3.4f, 0.92f, 1.2f));
            Solid(t, palette, layer, "fridge", new Vector3(-10f, 0.95f, 6.6f), new Vector3(0.85f, 1.9f, 0.85f));

            Solid(t, palette, layer, "dining table", new Vector3(8.7f, 0.38f, 5.2f), new Vector3(2.4f, 0.76f, 1.4f));

            Solid(t, palette, layer, "sofa", new Vector3(8.4f, 0.42f, -6f), new Vector3(3.2f, 0.84f, 1.0f));
            Solid(t, palette, layer, "coffee table", new Vector3(8.4f, 0.22f, -3.8f), new Vector3(1.5f, 0.44f, 0.9f));
            Solid(t, palette, layer, "sideboard", new Vector3(0.9f, 0.45f, -1.5f), new Vector3(0.6f, 0.9f, 2.6f));

            Solid(t, palette, layer, "washer", new Vector3(-10f, 0.45f, 1.6f), new Vector3(0.8f, 0.9f, 0.8f));

            // Garage: a bench, and crates you climb to the roof hatch off.
            Solid(t, palette, layer, "bench", new Vector3(-10.2f, 0.5f, -4.5f), new Vector3(1.0f, 1.0f, 4.5f));
            Solid(t, palette, layer, "crate a", new Vector3(-6.2f, 0.6f, -6.8f), new Vector3(1.2f, 1.2f, 1.2f));
            Solid(t, palette, layer, "crate b", new Vector3(-6.2f, 1.65f, -5.6f), new Vector3(1.2f, 0.9f, 1.2f));
        }

        // ================================================================== first floor

        static void FirstFloor(Transform t, Palette palette, int layer, WorldModel model)
        {
            float y = Upper + Storey * 0.5f;

            // ---- the slab, in four strips round the stairwell, all cut from the stair lines.
            Deck(t, palette, layer, model, "deck west", W0, StairX0, N0, N3);
            Deck(t, palette, layer, model, "deck east", StairX1, W4, N0, N3);
            Deck(t, palette, layer, model, "deck south", StairX0, StairX1, N0, StairZ0);
            Deck(t, palette, layer, model, "deck north", StairX0, StairX1, StairZ1, N3);

            // ---- upper shell, on exactly the same lines as the lower one.
            AlongX(t, palette, layer, model, "upper front", W0, W4, N0, y, Storey, Wall,
                   Hole.Window(-8f, 2.4f, 1.4f, Upper + 1.4f),
                   Hole.Window(7f, 2.8f, 1.5f, Upper + 1.4f));
            AlongX(t, palette, layer, model, "upper back", W0, W4, N3, y, Storey, Wall,
                   Hole.Window(-6f, 2.6f, 1.4f, Upper + 1.4f),
                   Hole.Window(8.5f, 2.6f, 1.5f, Upper + 1.4f));

            // The landing window is the outside way in: off the garage roof, along to the porch roof,
            // and through here. It is tall and low so it can be vaulted rather than mantled.
            AlongZ(t, palette, layer, model, "upper west", N0, N3, W0, y, Storey, Wall,
                   Hole.Window(-4.5f, 2.0f, 2.0f, Upper + 1.15f));
            AlongZ(t, palette, layer, model, "upper east", N0, N3, W4, y, Storey, Wall,
                   Hole.Window(1.5f, 2.4f, 1.5f, Upper + 1.4f));

            // ---- upstairs partitions. Plasterboard, so the bedroom can be fought through as well as
            // walked into - it is the upstairs hill.
            AlongZ(t, palette, layer, model, "bedroom wall", N0, N3, W2, y, Storey, Soft,
                   Hole.Door(0.5f, DoorW, DoorH + Upper - DoorH));
            AlongX(t, palette, layer, model, "bathroom wall", W0, W2, N1, y, Storey, Soft,
                   Hole.Door(-8f, DoorW, DoorH + Upper - DoorH));

            UpperFurniture(t, palette, layer);
        }

        static void UpperFurniture(Transform t, Palette palette, int layer)
        {
            Solid(t, palette, layer, "bed", new Vector3(8.4f, Upper + 0.32f, 3f), new Vector3(2.2f, 0.64f, 2.0f));
            Solid(t, palette, layer, "wardrobe", new Vector3(10.1f, Upper + 1.15f, -5.5f), new Vector3(1.3f, 2.3f, 0.75f));
            Solid(t, palette, layer, "chest", new Vector3(1.0f, Upper + 0.45f, 5.5f), new Vector3(0.6f, 0.9f, 1.8f));
            Solid(t, palette, layer, "bath", new Vector3(-9f, Upper + 0.32f, -5.5f), new Vector3(1.9f, 0.64f, 0.85f));

            // A rail round the open side of the stairwell so you do not simply walk into it.
            Solid(t, palette, layer, "landing rail", new Vector3(StairX1 + 0.07f, Upper + 0.55f,
                (StairZ0 + StairZ1) * 0.5f), new Vector3(0.14f, 1.1f, StairZ1 - StairZ0));
            Solid(t, palette, layer, "landing rail end", new Vector3((StairX0 + StairX1) * 0.5f, Upper + 0.55f,
                StairZ0 + 0.07f), new Vector3(StairX1 - StairX0, 1.1f, 0.14f));
        }

        // ================================================================== roofs and the way up

        static void Outside(Transform t, Palette palette, int layer, WorldModel model)
        {
            Solid(t, palette, layer, "roof", new Vector3((W0 + W4) * 0.5f, Ridge + 0.2f, (N0 + N3) * 0.5f),
                new Vector3(W4 - W0 + 0.8f, 0.4f, N3 - N0 + 0.8f));

            // Garage roof: sheet steel, and loud. You get onto it off the crates through the hatch.
            Panel(t, palette, layer, model, "garage roof", new Vector3((W0 + W1) * 0.5f, Storey + 0.15f, (N0 + N1) * 0.5f - 1.1f),
                new Vector3(W1 - W0, 0.3f, N1 - N0 - 2.2f), SurfaceKind.Metal, palette.Metal);
            Panel(t, palette, layer, model, "garage roof north", new Vector3((W0 + W1) * 0.5f + 1.6f, Storey + 0.15f, N1 - 1.1f),
                new Vector3(W1 - W0 - 3.2f, 0.3f, 2.2f), SurfaceKind.Metal, palette.Metal);

            // The porch: a roof outside the front door, level with the garage roof, under the landing
            // window. This is the middle of the outside route.
            Solid(t, palette, layer, "porch post west", new Vector3(W1 + 0.3f, Storey * 0.5f, N0 - 2.4f),
                new Vector3(0.28f, Storey, 0.28f));
            Solid(t, palette, layer, "porch post east", new Vector3(W2 - 0.3f, Storey * 0.5f, N0 - 2.4f),
                new Vector3(0.28f, Storey, 0.28f));
            Panel(t, palette, layer, model, "porch roof", new Vector3((W1 + W2) * 0.5f, Storey + 0.15f, N0 - 1.3f),
                new Vector3(W2 - W1 + 0.8f, 0.3f, 2.8f), SurfaceKind.Wood, palette.Timber);
            Panel(t, palette, layer, model, "porch deck", new Vector3((W1 + W2) * 0.5f, 0.09f, N0 - 1.3f),
                new Vector3(W2 - W1 + 0.8f, 0.18f, 2.8f), SurfaceKind.Wood, palette.Timber);

            // And the bit that joins the two roofs, so the route is continuous rather than a jump.
            Panel(t, palette, layer, model, "roof link", new Vector3(W1 - 0.4f, Storey + 0.15f, N0 - 0.9f),
                new Vector3(2.2f, 0.3f, 2.0f), SurfaceKind.Metal, palette.Metal);
        }

        static void Yard(Transform t, Palette palette, int layer, WorldModel model)
        {
            Solid(t, palette, layer, "shed", new Vector3(-15f, 1.25f, 13f), new Vector3(4.2f, 2.5f, 3.2f));
            Panel(t, palette, layer, model, "shed door", new Vector3(-15f, 1.1f, 11.35f),
                new Vector3(1.9f, 2.2f, 0.12f), SurfaceKind.Metal, palette.Metal);
            Solid(t, palette, layer, "shed step", new Vector3(-12.2f, 0.55f, 13f), new Vector3(1.4f, 1.1f, 2.2f));

            Panel(t, palette, layer, model, "garden fence", new Vector3(5f, 0.95f, 12f),
                new Vector3(16f, 1.9f, 0.12f), SurfaceKind.Wood, palette.Timber);

            Solid(t, palette, layer, "car body", new Vector3(-19f, 0.7f, -7f), new Vector3(2.1f, 1.0f, 4.8f));
            Solid(t, palette, layer, "car roof", new Vector3(-19f, 1.45f, -6.6f), new Vector3(1.9f, 0.6f, 2.4f));
            Solid(t, palette, layer, "skip", new Vector3(-15.5f, 0.85f, 2.5f), new Vector3(2.4f, 1.7f, 4.2f));

            Solid(t, palette, layer, "planter front", new Vector3(4f, 0.5f, -12.5f), new Vector3(6.0f, 1.0f, 1.3f));
            Solid(t, palette, layer, "planter east", new Vector3(17f, 0.5f, -8f), new Vector3(1.3f, 1.0f, 7.0f));
            Solid(t, palette, layer, "hedge", new Vector3(2f, 0.95f, 17f), new Vector3(26f, 1.9f, 1.1f));
            Solid(t, palette, layer, "bins", new Vector3(13f, 0.6f, -10f), new Vector3(2.2f, 1.2f, 1.0f));
        }

        // ================================================================== primitives

        static void Solid(Transform t, Palette palette, int layer, string name, Vector3 centre, Vector3 size)
        {
            Blockout.Box(t, name, centre, size, palette.Wall, true, layer);
        }

        static void Panel(Transform t, Palette palette, int layer, WorldModel model, string name,
                          Vector3 centre, Vector3 size, SurfaceKind kind, Material material)
        {
            Blockout.Box(t, name, centre, size, material, true, layer);
            model.AddPanel(centre.ToSim(), size.ToSim(), kind);
        }

        static void Floor(Transform t, Palette palette, int layer, WorldModel model, string name,
                          float x0, float x1, float z0, float z1, SurfaceKind kind, Material material)
        {
            Panel(t, palette, layer, model, name,
                new Vector3((x0 + x1) * 0.5f, 0.06f, (z0 + z1) * 0.5f),
                new Vector3(x1 - x0, 0.12f, z1 - z0), kind, material);
        }

        /// <summary>A piece of the first floor. Boards, and thin enough to shoot up through.</summary>
        static void Deck(Transform t, Palette palette, int layer, WorldModel model, string name,
                         float x0, float x1, float z0, float z1)
        {
            if (x1 - x0 < 0.05f || z1 - z0 < 0.05f) return;
            Panel(t, palette, layer, model, name,
                new Vector3((x0 + x1) * 0.5f, Upper - Slab * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(x1 - x0, Slab, z1 - z0), SurfaceKind.Wood, palette.Timber);
        }

        /// <summary>An opening: where along the wall, how wide, how tall, and how high off the ground.</summary>
        struct Hole
        {
            public float At;
            public float Width;
            public float Height;
            public float CentreY;
            public bool Glazed;

            public static Hole Door(float at, float width, float height)
            {
                Hole h;
                h.At = at; h.Width = width; h.Height = height;
                h.CentreY = height * 0.5f + 0.06f; h.Glazed = false;
                return h;
            }

            public static Hole Window(float at, float width, float height, float centreY)
            {
                Hole h;
                h.At = at; h.Width = width; h.Height = height; h.CentreY = centreY; h.Glazed = true;
                return h;
            }
        }

        static void AlongX(Transform t, Palette palette, int layer, WorldModel model, string name,
                           float x0, float x1, float z, float centreY, float height, float thickness,
                           params Hole[] holes)
        {
            Run(t, palette, layer, model, name, x0, x1, z, centreY, height, thickness, true, holes);
        }

        static void AlongZ(Transform t, Palette palette, int layer, WorldModel model, string name,
                           float z0, float z1, float x, float centreY, float height, float thickness,
                           params Hole[] holes)
        {
            Run(t, palette, layer, model, name, z0, z1, x, centreY, height, thickness, false, holes);
        }

        /// <summary>
        /// The one wall builder in the file.
        ///
        /// Openings are sorted along the run and the wall is emitted as the solid spans BETWEEN them,
        /// plus a sill and a header for each. That is why there are no gaps this time: every piece is
        /// bounded by two numbers out of the same list, so the pieces tile the run exactly by
        /// construction instead of by three separate calls happening to agree.
        /// </summary>
        static void Run(Transform t, Palette palette, int layer, WorldModel model, string name,
                        float a0, float a1, float fixedAxis, float centreY, float height, float thickness,
                        bool alongX, Hole[] holes)
        {
            float bottom = centreY - height * 0.5f;
            float top = centreY + height * 0.5f;

            if (holes != null && holes.Length > 1)
            {
                for (int i = 1; i < holes.Length; i++)
                {
                    Hole key = holes[i];
                    int j = i - 1;
                    while (j >= 0 && holes[j].At > key.At) { holes[j + 1] = holes[j]; j--; }
                    holes[j + 1] = key;
                }
            }

            float cursor = a0;
            int index = 0;

            if (holes != null)
            {
                for (int i = 0; i < holes.Length; i++)
                {
                    float half = holes[i].Width * 0.5f;
                    float start = Mathf.Clamp(holes[i].At - half, a0, a1);
                    float end = Mathf.Clamp(holes[i].At + half, a0, a1);
                    if (end - start < 0.02f) continue;

                    Block(t, palette, layer, name + " " + index++, cursor, start, fixedAxis,
                          centreY, height, thickness, alongX);
                    cursor = end;

                    float holeBottom = Mathf.Max(bottom, holes[i].CentreY - holes[i].Height * 0.5f);
                    float holeTop = Mathf.Min(top, holes[i].CentreY + holes[i].Height * 0.5f);

                    if (holeBottom - bottom > 0.02f)
                        Block(t, palette, layer, name + " sill " + i, start, end, fixedAxis,
                              (bottom + holeBottom) * 0.5f, holeBottom - bottom, thickness, alongX);
                    if (top - holeTop > 0.02f)
                        Block(t, palette, layer, name + " head " + i, start, end, fixedAxis,
                              (holeTop + top) * 0.5f, top - holeTop, thickness, alongX);

                    if (!holes[i].Glazed) continue;

                    // The pane fills the opening exactly, so a round that breaks it opens the way.
                    Vector3 glassCentre = alongX
                        ? new Vector3((start + end) * 0.5f, (holeBottom + holeTop) * 0.5f, fixedAxis)
                        : new Vector3(fixedAxis, (holeBottom + holeTop) * 0.5f, (start + end) * 0.5f);
                    Vector3 glassSize = alongX
                        ? new Vector3(end - start, holeTop - holeBottom, thickness * 0.4f)
                        : new Vector3(thickness * 0.4f, holeTop - holeBottom, end - start);
                    model.AddWindow(glassCentre.ToSim(), glassSize.ToSim());
                }
            }

            Block(t, palette, layer, name + " " + index, cursor, a1, fixedAxis, centreY, height, thickness, alongX);
        }

        static void Block(Transform t, Palette palette, int layer, string name, float from, float to,
                          float fixedAxis, float centreY, float height, float thickness, bool alongX)
        {
            if (to - from < 0.02f) return;

            Vector3 centre = alongX
                ? new Vector3((from + to) * 0.5f, centreY, fixedAxis)
                : new Vector3(fixedAxis, centreY, (from + to) * 0.5f);
            Vector3 size = alongX
                ? new Vector3(to - from, height, thickness)
                : new Vector3(thickness, height, to - from);
            Blockout.Box(t, name, centre, size, palette.Wall, true, layer);
        }
    }
}
