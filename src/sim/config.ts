/**
 * Every gameplay tuning knob for the arena. The simulation is shared verbatim
 * between server and client (the client re-runs it to predict its own blob), so
 * these values must be identical on both sides — which is why they live in one
 * module with no imports of their own.
 */

const area = (r: number) => Math.PI * r * r;

export const ARENA = {
  /** Server ticks per second. Clients render at their own rate and interpolate. */
  tickRate: 20,

  match: {
    /** Round length in seconds, once at least two players are in. */
    seconds: 90,
    maxPlayers: 8,
    /** Bots are topped up to keep an arena this full. */
    fillTo: 6,
    /** Seconds of countdown before a round starts. */
    countdown: 3,
    /** Seconds the results screen holds before the next round. */
    intermission: 6,
  },

  /** The playfield is a circle that closes in over the round. */
  ring: {
    startRadius: 190,
    endRadius: 62,
    /** Fraction of the round spent at full size before it starts closing. */
    holdFraction: 0.25,
    /** Mass drained per second while outside the ring. */
    drainPerSecond: 26,
  },

  blob: {
    startMass: area(11),
    /** Eliminated below this. */
    minMass: area(3.4),
    maxMass: area(30),
    /** Speed at the reference radius; larger blobs are proportionally slower. */
    baseSpeed: 118,
    referenceRadius: 9,
    /** Speed falls off as (ref / r) ^ this. 0.5 keeps big blobs playable. */
    speedFalloff: 0.5,
    accel: 11,
    drag: 3.4,
    /** Seconds of immunity after being hit. */
    grace: 0.35,
    /**
     * Seconds after being hit during which you cannot absorb pellets. Stops the
     * victim from instantly vacuuming the goo that was just knocked out of them.
     */
    absorbLock: 0.75,
  },

  /** The attack: fire a chunk of yourself. */
  dash: {
    /** Share of your mass spent per shot, and its hard ceiling. */
    massFraction: 0.2,
    maxMass: area(7),
    /** A shot needs at least this much mass available to fire. */
    minMass: area(2.6),
    speed: 300,
    /** Backward recoil applied to the shooter. */
    recoil: 62,
    cooldown: 0.32,
    /** Seconds before a chunk goes inert and turns into free goo. */
    life: 1.5,
    /** Chunks can't hit their owner for this long after firing. */
    armTime: 0.22,
    drag: 1.5,
    /** Mass torn off the victim, as a multiple of the chunk's own mass. */
    steal: 1.5,
    /**
     * Share of stolen mass credited straight to the shooter; the rest scatters.
     * This is not a detail — with everything scattered, the victim is standing
     * at the impact point and simply re-eats what was torn off them, so a clean
     * hit nets zero and rounds stalemate with nobody able to finish anyone.
     */
    directShare: 0.6,
  },

  /** Free goo lying in the arena — the currency everything converts into. */
  goo: {
    /** Pellets scattered per impact, and how far they spread. */
    shardsPerHit: 4,
    shardSpread: 26,
    shardSpeed: 55,
    drag: 2.6,
    /**
     * Ambient pellets kept in the arena. This decays to `ambientEnd` over the
     * round: early on there is food for everyone, late on the only mass left is
     * inside other players. Scarcity is what forces the fight — with a constant
     * supply, farming is strictly safer than fighting and nobody ever engages.
     */
    ambient: 28,
    ambientEnd: 6,
    ambientMass: area(2.4),
    /** A blob must be this much bigger than a pellet to absorb it. */
    absorbRatio: 1.05,
  },

  /** Swipe up: briefly drag nearby free goo toward you. */
  pull: {
    radius: 62,
    force: 320,
    duration: 0.55,
    cooldown: 2.2,
  },

  bot: {
    /** How far a bot looks for goo and for targets. */
    sight: 150,
    /** Fires when a target is inside this range and roughly in front. */
    fireRange: 112,
    fireAimTolerance: 0.8,
    /** Bots flee opponents at least this many times their own mass. */
    fleeRatio: 1.8,
    /** Reaction delay in seconds, so bots feel human rather than frame-perfect. */
    reaction: 0.16,
    /** Aim error in radians at the moment of firing. */
    aimJitter: 0.22,
  },
} as const;

export const massToRadius = (m: number) => Math.sqrt(Math.max(m, 0) / Math.PI);

/** Max speed for a blob of this radius — bigger is slower, so size has a cost. */
export function speedFor(r: number): number {
  const { baseSpeed, referenceRadius, speedFalloff } = ARENA.blob;
  return baseSpeed * Math.pow(referenceRadius / Math.max(r, 0.001), speedFalloff);
}
