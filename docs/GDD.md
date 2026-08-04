# QUICKSILVER — GDD

**Hook (1 sentence):** You are a falling mass of liquid chrome — split apart to thread narrow gates, merge back into one heavy sphere to smash through blast doors.

**Genre / Platform / Audience:** Endless one-thumb arcade faller. Mobile web first (WebGL2), wrappable to native. Ages 10+, the hyper-casual / "satisfying visuals" audience that shares clips.

## Core loop (the 30-second cycle)

```
steer  →  read the gate ahead  →  choose SPLIT or MERGE  →  pass cleanly (combo++, +mass)
       →  channel speeds up  →  repeat
```

Every 1.5–3 seconds the channel presents an obstacle that demands a topology decision. The decision is always a live tradeoff:

| You are | Good at | Bad at |
|---|---|---|
| **Merged** (1 blob) | Smashing blast doors, narrow profile for weaving | Fitting through multi-slit gates; one saw hit costs a huge share of your mass |
| **Split** (2–6 blobs) | Threading slit gates; damage is isolated to one small blob | Wide formation is hard to weave through saws; can't break doors |

## Session shape

30–90 second runs, instant restart (no menu between attempts). Score is **depth in metres**; mass is your health bar. A session is 5–15 runs chasing a personal best.

## Win / lose / progression

- **Lose:** total mass drops below the minimum — the last droplet pops.
- **Score:** metres descended × combo multiplier. Clean gate passes build combo; any hit resets it.
- **Progression (v1):** personal best stored locally, plus the natural difficulty ramp within a run (speed rises, gates tighten, doors need more mass).

## Mechanics (5, hard cap for v1)

1. **Steer** — absolute thumb-x maps to the formation's centre; the mass springs toward it with liquid lag and overshoot.
2. **Split** — tap. The largest blob halves in two, kicking apart laterally. Total volume is conserved; radius per blob is `sqrt(V / n·π)`. Max 6.
3. **Merge** — swipe up. All blobs converge and fuse; each fusion fires a shockwave. Volume is conserved. The player's requested count is the authority, not the live blob count, so tapping split mid-collapse halts it at the new count instead of forcing a round trip through 1.
4. **Gates** — *Slit gate* (n gaps at the formation's slot pitch; must be split to n and aligned). *Blast door* (must be a single blob at or above a mass threshold; otherwise you splat). *Saw gauntlet* (free-moving blades; weave, and being split means a hit only costs one blob's share).
5. **Droplets** — free mass scattered off the fast line. The detour is the risk/reward.

**Damage model:** each impact costs a *share* of the hit blob's volume, which is what makes splitting act as insurance — a hit while split costs one small blob rather than the whole mass — plus a flat floor so damage can't asymptote toward zero and leave a run unkillable.

**Spacing rule:** obstacles are spaced in *seconds of channel*, not world units (the run triples in speed), with extra runway proportional to how much the next obstacle changes the required blob count. A door followed immediately by a 4-slit gate isn't hard, it's unplayable.

## The goo layer (what makes it *satisfying*, not just readable)

The reference is the "oddly satisfying" genre — slime, ferrofluid, kinetic sand —
where the pleasure comes from **surface tension**: things necking, stretching,
wobbling and resolving. A first pass got this wrong by rendering rigid circles
that snapped to formation slots; it was legible and completely inert. Four
things carry the liquid read, and all of them are visual only — collision stays
circular, so deformation can never make a gate unfair:

1. **Domain warp.** The sample point is displaced by a slow sine field before
   distance is measured, so silhouettes ripple and — because it is evaluated per
   sample — the shading normals crawl with them.
2. **Soft-body deformation.** Each blob carries a stretch vector driven by its
   motion through an *underdamped* spring (ζ ≈ 0.38). The centre stays
   responsive so the game is still playable; only the surface lags and
   overshoots. Responsive core plus laggy skin is the whole trick. Stretch is
   area-preserving, so squashing never appears to change your mass.
3. **Mass-scaled surface tension.** The smooth-union radius tracks the largest
   blob, so a heavy mass bridges and necks across a wide gap while six small
   ones stay crisply separate.
4. **Satellite spray.** Splits, fusions and impacts throw droplets that share
   the same metaball field, so they flow back into the mass rather than popping
   out. Real liquid never separates cleanly; without these a split reads as one
   shape becoming two shapes rather than something tearing.

Plus a beat of slow motion on every fusion, so the coalescence is watched rather
than glimpsed.

## Art direction

Liquid chrome and iridescent oil-slick against a near-black tunnel. Everything is one raymarched 2D SDF field: blobs union with a polynomial smooth-min so merging is literally the renderer's native operation, shaded with a procedural studio env-map, Fresnel iridescence, and screen-space refraction of the parallax background. Obstacles are matte obsidian with emissive danger edges (cyan = slit, amber = door, red = saw).

References: ferrofluid macro photography, *Gravity Rush*'s chrome, Houdini fluid demo reels.

**Audio direction (NOT YET BUILT — see LATER.md):** low sub-bass channel drone that rises with speed; wet, granular splits; a single deep impact hit on door smash; no music — the drone is the music.

## Monetization

None in v1. Ads and IAP only after the loop is proven fun (rewarded revive is the natural first placement).

## Scope — v1 SHIPS WITH

- The three obstacle types + droplets, procedurally sequenced with a difficulty ramp
- Full metaball renderer with bloom, chromatic pulse, screen shake, hitstop, slow-mo on door break
- HUD (depth, mass bar, combo), title screen, death screen with instant retry
- Local best score, touch + keyboard input, responsive to any aspect ratio

## Explicitly cut from v1

Audio, level themes, skins/cosmetics, power-ups, daily challenges, leaderboards, tutorial screens (the first gates teach by doing), native wrapper, ads/IAP. New ideas go to `docs/LATER.md`, not the codebase.

## Success metric

A stranger picks up the phone, plays 3 runs without being told the controls, and asks what the game is called.

## Tuning notes from the first automated playtest pass

Numbers a design pass should watch, and where they currently sit (scripted
skilled bot, three seeds, 90s runs):

| Signal | Now | Reading |
|---|---|---|
| Skilled survival | 3/3 seeds to 90s | Generous. Expect to tighten once humans play. |
| Gate clear rate | 87 / 81 / 91 % | Healthy. Below ~60% means gates are unfair, not hard. |
| Saw hits per run | 16–23 | The intended chip-damage channel; saws, not gates, are the mass sink. |
| Door break vs fail | ~8 : 1 | Doors read as a reward for merging, not a tax. |
| Idle bot death | 28s | Fail pressure exists — doing nothing is not viable. |

Three findings from that pass worth remembering, because each looked like a
balance problem and was actually a bug:

1. The formation trailed `damp · v / k` behind the line it was supposed to sit
   on — 16 world units at top speed — desyncing the player's real position from
   the one collision and targeting reasoned about. Damping relative to the line's
   velocity fixes it. Gate clear rate went 3% → 40% on that change alone.
2. Percentage-only damage is unkillable by construction: every hit takes 30% of
   an ever-smaller quantity. A flat floor is what closes runs out.
3. A gate is not behind you when its centre crosses the line — it is still
   intersecting the formation for another `thickness + radius`. Releasing the
   steering line early yanks blobs out of the slit on the final frame.
