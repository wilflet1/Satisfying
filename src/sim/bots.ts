import type { Arena, Player } from './arena.ts';
import { ARENA } from './config.ts';

/**
 * Bot brain. Runs server-side and just writes into a player's input, so a bot
 * is indistinguishable from a human at the simulation layer — which means every
 * balance change is tested against bots automatically, and a disconnecting
 * player can be handed to a bot mid-round without any special casing.
 *
 * The priority order is deliberately shallow: survive the ring, run from
 * anything bigger, shoot anything smaller, otherwise eat. Deeper planning made
 * them feel robotic; this reads as a nervous opportunist, which is funnier and
 * much closer to how people actually play.
 */
export function driveBot(arena: Arena, p: Player, dt: number) {
  if (!p.alive) return;

  p.think -= dt;
  const reacting = p.think <= 0;
  if (reacting) p.think = ARENA.bot.reaction;

  const inp = p.input;
  inp.dash = false;
  inp.pull = false;

  const dist = Math.hypot(p.x, p.y);
  const edge = arena.ringRadius - p.r * 2 - 12;

  // 1. The ring beats everything — being outside it is a guaranteed loss.
  if (dist > edge) {
    const d = Math.max(dist, 1e-3);
    inp.moveX = -p.x / d;
    inp.moveY = -p.y / d;
    if (reacting) aimAtNearestTarget(arena, p);
    return;
  }

  let threat: Player | null = null;
  let prey: Player | null = null;
  let threatD = Infinity;
  let preyD = Infinity;

  for (const o of arena.players.values()) {
    if (o === p || !o.alive) continue;
    const d = Math.hypot(o.x - p.x, o.y - p.y);
    if (d > ARENA.bot.sight) continue;
    if (o.mass > p.mass * ARENA.bot.fleeRatio) {
      if (d < threatD) {
        threatD = d;
        threat = o;
      }
    } else if (d < preyD) {
      preyD = d;
      prey = o;
    }
  }

  // 2. Run from anything meaningfully bigger, but stay inside the ring while
  //    doing it — fleeing into the wall is how naive bots kill themselves.
  if (threat && threatD < ARENA.bot.sight * 0.55) {
    const dx = p.x - threat.x;
    const dy = p.y - threat.y;
    const d = Math.max(Math.hypot(dx, dy), 1e-3);
    let fx = dx / d;
    let fy = dy / d;
    if (dist > edge * 0.8) {
      const inward = Math.max(dist, 1e-3);
      fx = fx * 0.35 - (p.x / inward) * 0.8;
      fy = fy * 0.35 - (p.y / inward) * 0.8;
      const m = Math.max(Math.hypot(fx, fy), 1e-3);
      fx /= m;
      fy /= m;
    }
    inp.moveX = fx;
    inp.moveY = fy;
    // Fire backwards while retreating — the recoil helps you escape.
    if (reacting && preyOrThreatInFront(p, threat)) {
      aimAt(p, threat, arena);
      inp.dash = threatD < ARENA.bot.fireRange;
    }
    return;
  }

  // 3. Shoot something smaller.
  if (prey && preyD < ARENA.bot.fireRange) {
    if (reacting) aimAt(p, prey, arena);
    const facing = p.aimX * (prey.x - p.x) + p.aimY * (prey.y - p.y);
    const cos = facing / Math.max(preyD, 1e-3);
    inp.dash = cos > ARENA.bot.fireAimTolerance;
    // Close the distance, but not so far that you overshoot into a brawl.
    const k = preyD > ARENA.bot.fireRange * 0.6 ? 1 : 0.2;
    inp.moveX = ((prey.x - p.x) / Math.max(preyD, 1e-3)) * k;
    inp.moveY = ((prey.y - p.y) / Math.max(preyD, 1e-3)) * k;
    return;
  }

  // 4. Otherwise eat. Nearest pellet, weighted so bots don't cross the arena
  //    for a crumb when there's a cluster nearby.
  let best: { x: number; y: number; score: number } | null = null;
  let cluster = 0;
  for (const g of arena.pellets) {
    const d = Math.hypot(g.x - p.x, g.y - p.y);
    if (d > ARENA.bot.sight) continue;
    if (d < ARENA.pull.radius) cluster++;
    const score = g.mass / (d + 20);
    if (!best || score > best.score) best = { x: g.x, y: g.y, score };
  }

  if (best) {
    const dx = best.x - p.x;
    const dy = best.y - p.y;
    const d = Math.max(Math.hypot(dx, dy), 1e-3);
    inp.moveX = dx / d;
    inp.moveY = dy / d;
    if (reacting) {
      p.aimX = dx / d;
      p.aimY = dy / d;
    }
    // Worth pulling when standing in a pile.
    inp.pull = cluster >= 4 && p.pullCooldown <= 0;
  } else {
    // Nothing in sight: drift toward the middle where the action ends up.
    const d = Math.max(dist, 1e-3);
    inp.moveX = -(p.x / d) * 0.5;
    inp.moveY = -(p.y / d) * 0.5;
  }

  if (reacting) aimAtNearestTarget(arena, p);
}

function preyOrThreatInFront(p: Player, o: Player): boolean {
  const dx = o.x - p.x;
  const dy = o.y - p.y;
  const d = Math.max(Math.hypot(dx, dy), 1e-3);
  return (p.aimX * dx + p.aimY * dy) / d > 0.2;
}

/** Lead the target and add a little error, so bots miss like people do. */
function aimAt(p: Player, o: Player, arena: Arena) {
  const dx = o.x - p.x;
  const dy = o.y - p.y;
  const d = Math.max(Math.hypot(dx, dy), 1e-3);
  const travel = d / ARENA.dash.speed;
  let ax = dx + o.vx * travel;
  let ay = dy + o.vy * travel;
  const m = Math.max(Math.hypot(ax, ay), 1e-3);
  ax /= m;
  ay /= m;

  const jitter = (arena.random() * 2 - 1) * ARENA.bot.aimJitter;
  const c = Math.cos(jitter);
  const s = Math.sin(jitter);
  p.aimX = ax * c - ay * s;
  p.aimY = ax * s + ay * c;
  p.input.aimX = p.aimX;
  p.input.aimY = p.aimY;
}

function aimAtNearestTarget(arena: Arena, p: Player) {
  let best: Player | null = null;
  let bd = Infinity;
  for (const o of arena.players.values()) {
    if (o === p || !o.alive) continue;
    const d = Math.hypot(o.x - p.x, o.y - p.y);
    if (d < bd) {
      bd = d;
      best = o;
    }
  }
  if (best) aimAt(p, best, arena);
}
