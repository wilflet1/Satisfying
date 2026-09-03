/**
 * Every gameplay tuning knob for the arena. The simulation is shared verbatim
 * between server and client (the client re-runs it to predict its own blob), so
 * these values must be identical on both sides — which is why they live in one
 * module with no imports of their own.
 */

const area = (r: number) => Math.PI * r * r;

const START_MASS = area(11);

/**
 * Mass per displayed point of size, chosen so a fresh blob reads as 40.
 *
 * The internal quantity is an area, which is what the physics and the radius
 * need, but showing it raw meant the HUD said "380" and a shot took 42 off it.
 * That looks ruinous even when it isn't. Size is the number the player actually
 * reasons about, so it is the number the costs are denominated in: a shot costs
 * exactly one point, and two once you are big.
 */
export const MASS_UNIT = START_MASS / 40;

/** Internal mass to the number shown on screen. */
export const massToSize = (m: number) => Math.max(0, Math.round(m / MASS_UNIT));

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
    /**
     * Loose goo outside the boundary dissolves. Without this the arena has an
     * exploit shaped like a strategy: step out, hoover up everything the
     * shrinking ring left behind where nobody dares follow, and walk back in
     * enormous. The ring has to delete what it passes over, not just hurt
     * whoever stands there.
     */
    dissolveMargin: 2,
  },

  blob: {
    startMass: START_MASS,
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
     * Seconds of total invulnerability after spawning. One second was nowhere
     * near enough: a new player needs time to find their thumbs before anything
     * is allowed to shoot them, and five bots converging on a stationary target
     * ends a round before they have worked out which stick moves.
     */
    /**
     * Protection on respawning mid-round.
     */
    spawnProtect: 3,
    /**
     * Protection at the start of a round. Deliberately outlasts the bots'
     * opening truce: if the shield expires the moment they switch on, the
     * player is exposed at exactly the worst instant and dies to the first
     * volley — which is what a "4.5 second" shield was actually delivering.
     * Overlapping them buys a couple of seconds of watching the fight start
     * from safety, which is when it finally becomes obvious what to do.
     */
    openingProtect: 8,
    /** Seconds dead before respawning, and the share of a full blob you return with. */
    respawnDelay: 2.5,
    respawnMass: 0.75,
  },

  /** The attack: fire a chunk of yourself. */
  dash: {
    /**
     * A shot costs a flat one point of size, or two once you are big — not a
     * share of your mass. A percentage punishes exactly the players who are
     * already losing: the smaller you got, the less each shot did, so falling
     * behind was self-reinforcing and firing felt like bleeding out. A flat
     * cost means a small blob and a large one both pay something they can
     * afford, and shooting stops being a resource you agonise over.
     */
    cost: MASS_UNIT,
    bigCost: MASS_UNIT * 2,
    /** Size at which shots become the bigger, harder-hitting kind. */
    bigAtSize: 60,
    /**
     * Floor on a chunk's radius. A one-point shot is only 1.7 units across by
     * area — a speck you cannot see leave your body, let alone track across the
     * arena. Sized up again after the field of view widened, since a wider view
     * shrinks everything on screen and quietly undid the first correction.
     * The floor applies to the real radius, not just the drawn one, so what you
     * see is exactly what collides.
     */
    minRadius: 5,
    speed: 300,
    /** Backward recoil applied to the shooter. */
    recoil: 62,
    cooldown: 0.32,
    /** Seconds before a chunk goes inert and turns into free goo. */
    life: 1.5,
    /** Chunks can't hit their owner for this long after firing. */
    armTime: 0.22,
    drag: 1.5,
    /**
     * Mass torn off the victim, as a multiple of the chunk's own mass.
     *
     * Damage had to be decoupled from cost for a flat price to work at all:
     * with cost and damage both proportional to the chunk, making shots cheap
     * made them harmless, and a kill needed twenty hits. At 4x, a normal shot
     * takes four points off someone and a big one takes eight — so roughly ten
     * clean hits finish a full-size blob, and being big genuinely hits harder.
     */
    steal: 4,
    /**
     * Share of stolen mass credited straight to the shooter; the rest scatters.
     * This is not a detail — with everything scattered, the victim is standing
     * at the impact point and simply re-eats what was torn off them, so a clean
     * hit nets zero and rounds stalemate with nobody able to finish anyone.
     */
    directShare: 0.5,
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
    /**
     * Seconds during which goo knocked out of a player cannot be re-absorbed by
     * that same player. Anyone else may take it immediately.
     *
     * This is what turns a hit into a contested moment: the mass is lying there
     * in the victim's own colour, they can see it, and they have to survive
     * three seconds — or drive the attacker off — before they can have it back.
     */
    ownerLock: 3,
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
    /**
     * Firing range. Was over half the arena radius, which meant a bot could
     * reach you from anywhere and there was no such thing as a safe distance.
     */
    fireRange: 78,
    fireAimTolerance: 0.8,
    /** Bots flee opponents at least this many times their own mass. */
    fleeRatio: 1.8,
    /**
     * Reaction delay in seconds at skill 0 and skill 1. Human visual reaction
     * is around 250ms before you add a thumb moving across glass, so a bot
     * reacting in 160ms was strictly superhuman.
     */
    reactionEasy: 0.62,
    reactionHard: 0.3,
    /** Aim error in radians, and error in how far they lead a moving target. */
    aimJitterEasy: 0.42,
    aimJitterHard: 0.16,
    leadErrorEasy: 0.7,
    leadErrorHard: 0.15,
    /**
     * Skill is spread across the lobby rather than uniform, so a beginner has
     * someone they can beat and a good player still has someone to fear. Index
     * i of n gets skill i/(n-1) scaled into this band.
     */
    skillMin: 0.0,
    skillMax: 0.85,
    /**
     * Targets already being attacked by this many other bots are ignored. The
     * single biggest reason a lone human got destroyed instantly: every bot
     * independently picked the nearest target, which was always the newcomer
     * who hadn't learned to run away yet.
     */
    maxAttackersPerTarget: 2,
    /** Bots ignore everyone for this long at the start of a round. */
    openingTruce: 5,
  },
} as const;

export const massToRadius = (m: number) => Math.sqrt(Math.max(m, 0) / Math.PI);

/** Max speed for a blob of this radius — bigger is slower, so size has a cost. */
export function speedFor(r: number): number {
  const { baseSpeed, referenceRadius, speedFalloff } = ARENA.blob;
  return baseSpeed * Math.pow(referenceRadius / Math.max(r, 0.001), speedFalloff);
}
