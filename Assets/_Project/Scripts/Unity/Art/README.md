# Art

Everything you can see is built from `Blockout` primitives at runtime. There is not a single binary
asset in the repository: clone it, press Play, and the arena, the duellists and the guns are all
there. That is a deliberate constraint and most of what makes the project pleasant to work on - no
importer settings, no meta files, no "works on my machine because my Library folder is warm".

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

## Materials

`Palette` generates every material in code, so there are no shader variants to strip and nothing to
import. It is also the single place to change if you move to URP or HDRP: `Palette.FindShader` walks
a list of shader names and takes the first that resolves.
