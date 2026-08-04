/**
 * Every tuning knob in the game lives here. Nothing else hard-codes a balance
 * number — if a value is worth arguing about in a playtest, it belongs in CFG.
 *
 * World units: the channel is 100 wide (x from -50..50) and y increases
 * *downward* (the direction of travel). One "metre" of score is 10 world units.
 */

const area = (r: number) => Math.PI * r * r;

export const CFG = {
  /**
   * Channel half-width. Narrower than it first looks for a reason: the channel
   * is mapped to the full screen width, so it sets how big the metal renders.
   * At 50 a six-way split drew 18px beads on a phone and the whole material
   * pass was wasted. 40 keeps room for a 6-slit gate plus steering slack.
   */
  channelHalfW: 40,
  /** Where the formation sits vertically, as a fraction of the view height. */
  playerScreenY: 0.3,

  mass: {
    start: area(7),
    /** Below this the run ends. */
    min: area(2.3),
    /** Cap so late runs can't trivialise blast doors. */
    max: area(11),
  },

  /** Formation geometry: blob i sits at (i - (n-1)/2) * slotPitch from centre. */
  maxBlobs: 6,
  slotPitch: 10,

  speed: {
    start: 42,
    max: 118,
    /** Seconds to reach max speed. */
    rampSeconds: 100,
  },

  /** Spring constants for the liquid-lag steering. Higher spring = tighter. */
  steer: {
    springX: 210,
    dampX: 25,
    springY: 150,
    dampY: 22,
    /** Extra lag applied per blob so the formation ripples rather than snaps. */
    slotLag: 0.06,
  },

  split: {
    /** Lateral velocity kick given to each half when a blob divides. */
    kick: 46,
    /** Rate at which blob volumes equalise toward total/n. */
    equaliseRate: 9,
  },

  merge: {
    /** Blobs fuse when centre distance < (rA + rB) * this. */
    fuseFactor: 0.72,
    /** Inward pull applied while converging, on top of the slot spring. */
    pull: 130,
  },

  gate: {
    slitHalfW: 7,
    wallThickness: 3.6,
    /** Blast door mass requirement, as a fraction of starting mass. Ramps. */
    doorReqStart: 0.45,
    doorReqEnd: 0.72,
  },

  saw: {
    radius: 4.6,
    spinRate: 5.5,
    /** Lateral drift speed of a blade, world units/sec. */
    drift: 16,
    /** Per-blob grace period after being cut, seconds. */
    invuln: 0.45,
    /** Blades per gauntlet, at zero and full difficulty. */
    countStart: 2,
    countEnd: 4,
    /** Seconds of channel between consecutive blades. */
    spacingSeconds: 0.62,
    /**
     * Blades alternate between the left and right halves of the channel, so a
     * clear side always exists. This is the margin between a blade's band and
     * the centreline: positive keeps the halves apart, and it shrinks with
     * difficulty until late-run bands overlap and the weave gets genuinely mean.
     */
    laneMarginStart: 5,
    laneMarginEnd: -3,
  },

  /**
   * Fraction of a blob's own volume lost per impact. Charging a share of the
   * blob rather than a flat amount is what makes splitting act as insurance —
   * a hit while split costs one small blob, not the whole mass.
   */
  damage: {
    wall: 0.3,
    saw: 0.3,
    doorFail: 0.32,
    /**
     * Plus a flat cost, as a fraction of starting mass. Without it damage is
     * purely proportional, so every hit takes 30% of an ever-smaller quantity
     * and the player asymptotes toward death without ever arriving — an idle
     * run survived indefinitely at 4% mass. The floor is what closes runs out.
     */
    flat: 0.055,
  },

  droplet: {
    radius: 2.6,
    get volume() {
      return area(2.6);
    },
  },

  /**
   * Obstacle sequencing. Spacing is measured in *seconds of channel*, not world
   * units: the run triples in speed, and a fixed distance would silently shrink
   * the reaction window to below human latency by the end. Difficulty should
   * come from tighter gates, not from the channel outrunning the player.
   */
  spawn: {
    gapSecondsStart: 2.4,
    gapSecondsEnd: 1.45,
    /**
     * Extra seconds of runway per unit of blob-count change the next obstacle
     * demands. A door (1 blob) followed immediately by a 4-slit gate is not
     * "hard", it is unplayable: the merge is still collapsing when the gate
     * lands. Spacing has to pay for reconfiguration, not just for reaction.
     */
    perStepSeconds: 0.3,
    /** Depth over which spacing tightens and obstacles get meaner. */
    tightenOver: 900,
    /** Depth (world units) before the first obstacle — the runway. */
    runway: 220,
  },

  combo: {
    /** Score multiplier = 1 + combo * step, capped. */
    step: 0.12,
    max: 4,
    /** Mass rewarded for a clean gate pass. */
    reward: area(1.9),
  },

  juice: {
    hitstopHit: 0.07,
    hitstopDoor: 0.11,
    slowmoDoor: 0.28,
    slowmoScale: 0.32,
    shakeHit: 7,
    shakeDoor: 16,
    shakeDecay: 5.5,
  },

  render: {
    /** Scene buffer scale. Dropped automatically if the frame budget is missed. */
    sceneScale: 1,
    minSceneScale: 0.6,
    bloomScale: 0.25,
    bloomStrength: 0.7,
    /** Rounded-edge depth of the metaball dome, in world units. */
    bevel: 3.2,
    /** Smooth-min radius — how eagerly blobs flow into each other. */
    smoothK: 4.4,
  },
} as const;

export const volumeToRadius = (v: number) => Math.sqrt(Math.max(v, 0) / Math.PI);
export const radiusToVolume = (r: number) => area(r);

/** Horizontal offset of slot `i` in a formation of `n`. */
export function slotOffset(i: number, n: number): number {
  if (n <= 1) return 0;
  return (i - (n - 1) / 2) * CFG.slotPitch;
}
