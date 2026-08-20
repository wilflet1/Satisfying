# Swapping in real art

Everything visible is generated at runtime from primitives, which is why the repository has no
binary assets. Replacing any of it is deliberately shallow.

## Weapons

`WeaponModels.Build` returns a `WeaponModel` — a root object plus the handles the rest of the game
needs. To use a real model, build it (or instantiate a prefab) and fill in the same fields:

| Field | What it must point at |
|-------|-----------------------|
| `Root` | The weapon object itself |
| `Muzzle` | Where tracers and the flash start |
| `Bolt` | The part that reciprocates when firing (bolt, cocking lever, slide) |
| `Magazine` | The magazine, so it can drop and return during a reload |
| `GripAnchor` | Where the firing hand goes |
| `ForegripAnchor` | Where the support hand rests |
| `MagAnchor` | Where the support hand goes during a reload |
| `SightAnchor` | The point that lines up with the centre of the screen when aiming |
| `BoltTravel`, `MagazineEject` | Local offsets for the animation |
| `HipOffset` | Resting position in front of the camera |

Nothing else needs to change: the IK arms reach for the anchors, and `WeaponAnimator` drives the
bolt and magazine from simulation state, so a new model is animated the moment it is wired up.

## Characters

`Blockout.Duellist` builds the body from boxes and exposes `Body`, `Chest`, `Head`, `LeftLeg` and
`RightLeg`. `RemotePlayerView` poses those transforms directly from replicated state. Point them at
the bones of a real rig (Mixamo works) and the lean, stance and aim posing carries over.

If you bring in a skinned character with its own animator, keep driving `Chest` for aim pitch and
the body roll for lean — the hitboxes in `PlayerHitbox.FromState` are derived from the simulation,
not from the visual, so the two must stay in agreement or you will be shooting at the wrong shape.

## Sound

`AudioBank.Build` synthesises every clip in `Synth`. Replace any field with an imported `AudioClip`
and the rest of the game is unchanged.

## Free sources that fit this style

- **Kenney** (kenney.nl) — CC0 blockout kits, weapons, UI. No attribution required.
- **Quaternius** (quaternius.com) — CC0 low-poly characters and props.
- **Poly Pizza** (poly.pizza) — CC0/CC-BY low-poly models, filterable by licence.
- **Mixamo** (mixamo.com) — free rigged characters and animations with an Adobe account.
- **Freesound** (freesound.org) — check each clip's licence; many are CC0.
- **Kenney Game Assets / OpenGameArt** — larger packs, mixed licences.

Check the licence yourself before shipping anything; CC0 is the only one that never needs
attribution.
