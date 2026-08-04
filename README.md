# QUICKSILVER

> Split to fit, merge to smash.

A one-thumb endless faller. You are a mass of liquid chrome dropping down a
channel: **tap** to split into more, smaller blobs to thread a multi-slit gate,
**swipe up** to merge back into one heavy sphere to break through a blast door,
and weave the formation through spinning blades in between. Score is depth in
metres; your mass is your health bar.

The whole game — tunnel, obstacles, and player — is a single screen-space signed
distance field. Blobs union with a polynomial smooth-min, so merging is the
renderer's native operation rather than an effect layered on top.

## Run it

```bash
npm install
npm run dev        # http://localhost:5173
```

```bash
npm run build      # → dist/, ~35 kB JS (13 kB gzipped), no runtime dependencies
npm run preview    # serve the build on :4173
```

Controls: **drag** to steer, **tap** to split, **swipe up** to merge. On desktop,
the mouse steers, <kbd>Space</kbd> splits and <kbd>S</kbd> merges.

`?seed=N` pins world generation so a run reproduces exactly.

## Test it

```bash
npm run build && npm run preview &
npm run playtest
```

The harness runs two passes, because they answer different questions:

1. **Logic soak** — steps the simulation at a fixed `dt` with rendering out of
   the loop, so frame rate can't distort the result. A scripted "good player" bot
   must survive; a do-nothing bot must die. That pair is the difficulty curve
   having teeth in both directions. It reports per-run funnel stats (gates
   cleared vs hit, saw hits, door breaks vs fails).
2. **Render smoke** — real GPU frames to catch shader compile failures, GL
   errors and a black screen, plus reference screenshots to `playtest/`.

Frame rate reported by the harness comes from SwiftShader (software
rasterisation) and is **not** a device estimate.

## Layout

| Path | What's in it |
|---|---|
| `src/config.ts` | Every tuning knob. Nothing else hard-codes a balance number. |
| `src/blobs.ts` | Player physics: split, merge, volume conservation, formation slots. |
| `src/world.ts` | Endless channel: obstacle planning, spacing, generation. |
| `src/game.ts` | State machine, collision, scoring, juice, instrumentation. |
| `src/shaders/` | The SDF scene pass, bloom, and final grade. |
| `src/renderer.ts` | WebGL2 setup, render targets, adaptive resolution. |
| `docs/GDD.md` | One-page design doc. |
| `docs/LATER.md` | Deliberately unbuilt ideas. |

## Status

Vertical slice. The core loop, all three obstacle types, the full material pass
and the difficulty ramp are in and verified. **There is no audio yet** — see
`docs/LATER.md`, which also lists what was consciously cut.
