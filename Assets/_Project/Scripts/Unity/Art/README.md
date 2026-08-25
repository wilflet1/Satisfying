# Art

Everything you can see is generated at runtime - primitives for the hard-edged things, lofted meshes
for the character. There is not a single binary asset in the repository: clone it, press Play, and
the arena, the duellists and the guns are all there. That is a deliberate constraint and most of what
makes the project pleasant to work on - no importer settings, no meta files, no "works on my machine
because my Library folder is warm".

## How the character is built

Not out of cubes any more. `Shared/Math/Loft.cs` sweeps an outline down an axis through a few rings,
and `ShapeCatalogue` is the five profiles it makes: a tapered limb, a lofted torso, an ovoid head,
a boot, and a chamfered box for kit. `MeshShapes` turns those into engine meshes and caches them, so
eleven meshes exist in a match however many duellists are in it.

The geometry lives in `Shared` rather than here on purpose. There is no Unity in the container this
was written in, so the only way to know a generated body is not inside out, not full of holes and
not the wrong size is to run the numbers - and `tools/SimTests` does, on every shape. The winding
test in particular earns its keep: a loft wound the other way is invisible from outside and solid
from inside, and looks completely reasonable in the source.

**Every shape is normalised into the unit cube**, centred on the origin, running -0.5 to +0.5 down
Z. That is what lets `Blockout.Bone` scale one by (width, depth, length) and get exactly a limb that
wide, that deep and that long. Break the normalisation and every proportion in the game moves at
once.

**Nothing drawn on a duellist may stick out past the capsules `PlayerHitbox` tests.** A helmet that
overhangs the head sphere is a helmet you can put a round through for no damage, and from the
shooting end that is indistinguishable from the netcode being broken. The one deliberate exception
is the shoulders: the chest capsule is 0.31 m across and the drawn shoulders are 0.40, because the
arm capsules start at the shoulder joints and cover the difference - a round in someone's deltoid
registers as an arm, which is what it is.

## The one thing that must not change

`Assets/_Project/Scripts/Shared/Sim/BodyPose.cs` decides where every bone of a player is, and
`PlayerHitbox` lays its capsules over exactly those joints. **The model is placed from the same
struct.** If you replace the character with a skinned mesh, bind its bones to `BodyPose`'s joints
rather than posing it any other way - the moment the drawn body and the shot body come from two
different pieces of code, you have a game where people are shot half a metre from where they are
standing, and you will not find out from a screenshot.

The same goes for weapons. A gun is a set of anchors:

| Anchor | What it must be |
|--------|-----------------|
| `GripAnchor` | Where the firing hand closes. `BodyPose.RightHand` is put here. |
| `ForegripAnchor` | The support hand. Derived from `supportHandReach` / `supportHandRise` by `SetSupportGrip` - do not type it in. |
| `SightAnchors[kind]` | The point that lands on the exact centre of the screen at full ADS. |
| `Muzzle` | Where a tracer starts. |
| `MagAnchor` | Where the support hand goes during a reload. |
| `StockAnchor` | The end that hits things when you melee. |
| `Bolt` | Reciprocates on a shot, holds back on empty if `HoldsOpenWhenEmpty`. |
| `Magazine` | Drops on a reload. |

Fill those in and everything else - the IK, the reload, the ADS alignment, the melee - keeps working
without knowing what the model is.

## If you want to bring in real models

Nothing stops you: swap `Blockout.Duellist` for a prefab and keep the joint bindings. Sources that
are genuinely free to ship, with the licence that makes them so:

| Source | Licence | Good for |
|--------|---------|----------|
| [Kenney](https://kenney.nl) | CC0 | Blockout characters and props, whole coherent sets |
| [Quaternius](https://quaternius.com) | CC0 | Low-poly characters and weapons, one consistent style |
| [Poly Pizza](https://poly.pizza) | CC0 / CC-BY | Huge library, filter by licence before you download |
| [Sketchfab](https://sketchfab.com) | mixed - filter to CC0 or CC-BY | One-offs; check the licence on every single file |
| [Mixamo](https://mixamo.com) | free with an Adobe account | Rigged humanoids and animation, which is the hard part |
| [ambientCG](https://ambientcg.com) | CC0 | Materials, if you ever want something other than flat colours |

CC-BY means you must credit the author somewhere the player can find it; CC0 means you owe nobody
anything. Keep a `CREDITS.md` from the first file you import rather than trying to reconstruct one
later.

Anything sold on a store is sold, and "modifying it to make it ours" does not change that - a
derivative of a work you have no licence to is still a work you have no licence to. If a paid asset
is the right one, buy it; if the budget is zero, the list above is genuinely enough to build a
shipping-looking game.

## Making the character better without importing anything

Most of what reads as "better models" is proportion and silhouette, and all of it is numbers in two
files:

- `ShapeCatalogue.Rings` - the profile of each shape. Where a limb tapers, how far the chest lofts
  out past the waist, how flat the toe of a boot is.
- `Blockout.Duellist` - the sizes those shapes are scaled to, and the kit hung off the joints.

Raising the ring count in a profile costs vertices and nothing else; the meshes are shared and a
duellist is a few thousand triangles either way.

## Materials

`Palette` generates every material in code, so there are no shader variants to strip and nothing to
import. It is also the single place to change if you move to URP or HDRP: `Palette.FindShader` walks
a list of shader names and takes the first that resolves.
