# Later

Ideas that came up during the v1 build and were deliberately **not** built. Scope
creep is the main thing that kills a finished game; things land here instead of
in the codebase until the core loop has been playtested by actual humans.

## Confirmed gaps in v1

- **Audio.** Nothing is wired up. The design calls for a sub-bass channel drone
  that rises with speed, a wet granular split, a deep impact on door break, and
  no music. This is the single largest remaining gap between "works" and "feels
  finished" — juice is disproportionately audio.
- **Death cause feedback.** The run ends with a number; it never says whether
  saws or gates killed you. The `Stats` struct already tracks it.

## Design ideas parked

- **Partial merge.** `targetCount` already supports collapsing to any count, and
  the slot mapping distributes extras evenly, so "swipe up = merge one step"
  (symmetric with tap = split) is a small change. Cut from v1 because getting to
  1 blob for a blast door would then take five swipes; it needs a second gesture
  (flick = merge all) that has to be tuned against accidental triggers.
- **Mass-gated cosmetic tiers.** Skins that unlock at depth thresholds — gold,
  obsidian, mercury. Pure retention, no loop change.
- **Rewarded revive.** The obvious first monetisation placement, once the loop is
  proven. Deliberately not before.
- **Daily seed.** `?seed=N` already pins world generation, so a shared daily
  challenge with a leaderboard is mostly UI work.
- **Wider obstacle vocabulary.** A crusher that narrows the channel; a viscous
  zone that slows the steering spring; a splitter that forces a split on contact.

## Technical follow-ups

- **Real-device performance.** The playtest harness runs under SwiftShader, which
  is software rasterisation — its frame rate says nothing about a phone. The
  renderer already backs off `sceneScale` on slow frames, but nobody has profiled
  this on actual mobile silicon yet. That's the next real measurement.
- **Native wrapper.** Capacitor round the existing build if it ever justifies a
  store listing.
- **Bloom quality.** Single blur pass at quarter res. A two-level downsample
  would look better on large screens for very little cost.
