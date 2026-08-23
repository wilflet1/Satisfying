# Satisfying — one page design

## The pitch

A 1v1 duel built entirely around **how you move around a corner**. There is no loot, no
progression and no map rotation: two players, one arena, and a movement set deep enough that
winning a peek is a skill you can practise.

## The core loop

Approach a corner → choose how to expose yourself (lean, slow lean, side step, crouch, prone,
blind fire) → win or lose the trade → respawn a few seconds later with the same choice in front
of you. First to ten kills.

If that loop is not fun on its own, nothing else in the project matters, so everything else is
deliberately thin.

## What makes a peek interesting

Every way of showing yourself trades exposure for information or accuracy:

| Move | You gain | You pay |
|------|----------|---------|
| Lean (Q/E) | See past cover with only your head out | Head is exposed and offset from where they expect it; slower movement |
| Slow lean (Alt+Q/E) | Creep the angle open, often unseen | Takes far longer, drains stamina |
| Free lean (Alt + lean + mouse) | Hold any partial angle you like | Your view is locked while you set it |
| Side step (Alt+A/D) | Shift your whole body a metre without turning | Costs stamina, has a cooldown |
| Crouch / prone | Smaller, steadier, tighter groups | Slow, and standing back up takes time |
| Prone lean | A low roll almost nobody checks for | Barely any angle, and turning is glacial |
| Blind fire (V) | Suppress with the gun over cover, head hidden | Seven times the spread, aimed only by the mouse wheel |
| Sprint slide | Cross open ground low and fast, under things a crouch cannot pass | Spends the speed you had; you cannot steer much or start one from a walk |
| Slide jump | Keep the momentum instead of stopping | Puts you in the air, which is where you are easiest to hit |
| Vault | Cross a railing without going round it | A committed animation while you are silhouetted on top of it |
| Mantle | New angles: rooftops, window sills | Committed animation, big stamina cost |
| Stock bash (F) | A kill at arm's length with no reload, and a way through glass | You are committed for a third of a second and cannot shoot |

The arena exists to serve that table: hard corners, window sills at crouch and prone height, a
barricade with a gap you can only use lying down, a roof you can only reach by mantling, and one
long sightline that punishes standing still.

## The map answers back

Two things in the arena are not scenery.

**Glass.** Panes fill several of the openings. They stop nothing - a bullet passes through and
breaks them on its way - but while a pane is whole it is a wall as far as sound is concerned. Break
one, with a shot or with the stock, and you have opened a firing lane and a listening post in the
same motion. The trade is that everyone heard you do it, and the hole stays open for the rest of
the round.

**Movable objects.** Crates, barrels and pallets, from a few kilos up to a couple of hundred. Hold
**E** near one and your hands attach to it; you drag it at a speed set by its mass, and you carry
that slowness with you until you let go. A light pallet is a portable half-metre of cover; a heavy
crate is a barricade you can commit thirty seconds to building. Both are replicated and predicted,
so the object you are pushing is where you think it is even at 250 ms.

Together they turn a static arena into one that a player can edit mid-match: a sightline that
existed at the start of a round may not exist at the end of it.

## Weapons

Three, chosen so the choice is about range and how much movement you can afford:

- **M4A1** — the default; holds an angle at any range.
- **MP5** — fast and forgiving up close, falls apart past the blockhouse.
- **USP45** — semi automatic, two body shots or one head; rewards a still, patient peek.

Each one takes an optic from the gear menu (**G**): iron sights, a red dot or a holo. This is a
simulation choice rather than a cosmetic one - the optic scales aim-in time, aimed spread and zoom,
and the choice travels in the input stream so the server applies exactly what the client predicted.
Irons are the fastest onto target and the hardest to see through; the holo is the clearest picture
and the slowest to settle.

## Sound as information

Footsteps are the second source of truth in a duel, so they are treated like a weapon. Steps fall
on the walk cycle of the body you can see, play from that body's position with 3D attenuation, and
scale with stance and speed - a sprint is loud, a crouch walk is half of that, and prone is a
quarter you have to be close to hear.

What makes it worth listening to is that the geometry is in the mix. Each remote sound is tested
against the world with three lines from your ear to its source; the more of them a wall blocks, the
quieter and duller the sound gets, down to a fifth of its volume behind a low-pass. So a player
walking on the other side of a wall is a rumour, and the same player through an open doorway is a
position.

Glass ties the two systems together. An unbroken pane is a collider, so it muffles like any other
wall; a broken one is not, so the same footsteps come through sharp. Breaking a window to see
through it also means hearing through it, and it means the man on the other side can hear you.

## Match rules

First to ten eliminations. Respawn 2.2s at the spawn furthest from the opponent, with a short
spawn protection so nobody gets shot mid-materialise. Everything in that paragraph is a slider.

## The test range

A second map that is a drill course rather than an arena: one lane per ability, sized against the
tuning defaults. Change `slideHeight` in the tuning panel and the slide tunnel tells you about it
immediately. It exists because a movement game is only as good as the loop between changing a
number and feeling the result.

## What is deliberately missing

No loot, inventory, healing, classes, unlocks or matchmaking. This is a movement test bench with
a scoreboard; those systems would only slow down the loop it exists to prove.

## Success test

Two players who have never read the controls should, within one match, discover at least three
distinct ways to take the same corner - and argue about which one was right.
