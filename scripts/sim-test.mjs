/**
 * Headless balance soak. No rendering, no network, fully deterministic — bots
 * draw from the arena's seeded RNG — so a seed reproduces a round exactly and
 * these numbers can be trusted and bisected.
 *
 * The headline test is the NOVICE run. Balancing expert bots against expert
 * bots proved nothing about a real person's first thirty seconds: the first
 * build shipped with five bots that all independently picked the nearest target
 * (reliably the newest player), reacted in 160ms, and could reach two thirds of
 * the arena. It read as an instant, inexplicable death. A model of a beginner —
 * slow to react, wandering, firing badly, never fleeing — is the only automated
 * check that would have caught it.
 *
 * Run: node --experimental-strip-types scripts/sim-test.mjs
 */
import { Arena } from '../src/sim/arena.ts';
import { assignSkills, driveBot } from '../src/sim/bots.ts';
import { ARENA } from '../src/sim/config.ts';

const dt = 1 / ARENA.tickRate;

/**
 * A person who has played for eight seconds: slow to react, wanders toward
 * whatever is shiny, fires roughly at whoever is nearest, and — critically —
 * never runs away, because they haven't learned that they should.
 */
function driveNovice(arena, p, t) {
  if (!p.alive) return;
  const inp = p.input;
  inp.dash = false;
  inp.pull = false;

  // Re-decides about twice a second; a good player does this every frame.
  if (Math.floor(t * 2) === p.__last) return;
  p.__last = Math.floor(t * 2);

  let goo = null;
  let gd = Infinity;
  for (const g of arena.pellets) {
    const d = Math.hypot(g.x - p.x, g.y - p.y);
    if (d < gd) {
      gd = d;
      goo = g;
    }
  }
  if (goo) {
    const d = Math.max(gd, 1e-3);
    inp.moveX = ((goo.x - p.x) / d) * 0.8;
    inp.moveY = ((goo.y - p.y) / d) * 0.8;
  }

  // Vaguely points at the nearest opponent and fires now and then.
  let near = null;
  let nd = Infinity;
  for (const o of arena.players.values()) {
    if (o === p || !o.alive) continue;
    const d = Math.hypot(o.x - p.x, o.y - p.y);
    if (d < nd) {
      nd = d;
      near = o;
    }
  }
  if (near) {
    const d = Math.max(nd, 1e-3);
    const err = (arena.random() * 2 - 1) * 0.5;
    const ax = (near.x - p.x) / d;
    const ay = (near.y - p.y) / d;
    inp.aimX = ax * Math.cos(err) - ay * Math.sin(err);
    inp.aimY = ax * Math.sin(err) + ay * Math.cos(err);
    inp.dash = arena.random() < 0.25 && nd < 90;
  }
}

function playRound(seed, botCount, { novice = false } = {}) {
  const arena = new Arena(seed);
  if (novice) arena.addPlayer('human', 'Novice', false);
  for (let i = 0; i < botCount; i++) arena.addPlayer(`bot${i}`, `Bot ${i + 1}`, true);
  assignSkills(arena);

  const s = {
    hits: 0,
    kills: 0,
    dashes: 0,
    nan: false,
    ticks: 0,
    noviceDeaths: 0,
    noviceFirstDeath: null,
    noviceAliveTicks: 0,
    noviceDamage: 0,
  };

  let guard = 0;
  while (arena.phase !== 'live' && guard++ < 500) {
    arena.beginTick();
    for (const p of arena.players.values()) if (p.bot) driveBot(arena, p, dt);
    arena.step(dt);
  }

  const me = arena.players.get('human');
  while (arena.phase === 'live' && s.ticks < ARENA.tickRate * 200) {
    const t = s.ticks * dt;
    arena.beginTick();
    for (const p of arena.players.values()) {
      if (p.bot) driveBot(arena, p, dt);
      else if (novice) driveNovice(arena, p, t);
    }
    arena.step(dt);
    s.ticks++;

    s.hits += arena.events.hits.length;
    s.kills += arena.events.deaths.length;
    s.dashes += arena.events.dashes.length;

    if (me) {
      if (me.alive) s.noviceAliveTicks++;
      for (const d of arena.events.deaths) {
        if (d.id !== 'human') continue;
        s.noviceDeaths++;
        if (s.noviceFirstDeath === null) s.noviceFirstDeath = +t.toFixed(1);
      }
      s.noviceDamage = Math.round(me.damage);
    }

    for (const p of arena.players.values()) {
      if (!Number.isFinite(p.x + p.y + p.mass + p.r)) s.nan = true;
    }
  }

  s.seconds = +(s.ticks * dt).toFixed(1);
  s.noviceAlive = +((s.noviceAliveTicks / Math.max(1, s.ticks)) * 100).toFixed(0);
  s.winner = arena.winner;
  return s;
}

// --- bots only: does the fight work at all? --------------------------------
const brawls = [1, 2, 3].map((seed) => playRound(seed, 6));
console.log('bots only:');
for (const r of brawls) {
  console.log(`  ${r.seconds}s  dashes:${r.dashes}  hits:${r.hits}  kills:${r.kills}`);
}

// --- with a beginner in the lobby ------------------------------------------
const novices = [11, 12, 13, 14].map((seed) => playRound(seed, 5, { novice: true }));
console.log('\nnovice player, 90s rounds:');
for (const r of novices) {
  console.log(
    `  alive ${r.noviceAlive}% of the round, died ${r.noviceDeaths}x` +
      `, first death at ${r.noviceFirstDeath ?? 'never'}s, dealt ${r.noviceDamage} damage`,
  );
}

const fails = [];
const totalDashes = brawls.reduce((a, r) => a + r.dashes, 0);
const totalHits = brawls.reduce((a, r) => a + r.hits, 0);
const totalKills = brawls.reduce((a, r) => a + r.kills, 0);

for (const r of [...brawls, ...novices]) if (r.nan) fails.push('NaN leaked into the simulation');
if (totalDashes < 60) fails.push(`bots barely attacked (${totalDashes} dashes)`);
if (totalHits < 25) fails.push(`almost nothing connected (${totalHits} hits)`);
if (totalKills < 6) fails.push(`only ${totalKills} kills — no stakes`);

// The gates that encode the actual complaint: a beginner must get to play.
const avgAlive = novices.reduce((a, r) => a + r.noviceAlive, 0) / novices.length;
const avgDeaths = novices.reduce((a, r) => a + r.noviceDeaths, 0) / novices.length;
const earliest = Math.min(...novices.map((r) => r.noviceFirstDeath ?? 999));

if (earliest < 10) {
  fails.push(`a novice died after only ${earliest}s — no time to learn the controls`);
}
if (avgAlive < 60) {
  fails.push(`a novice is alive only ${avgAlive.toFixed(0)}% of the round — mostly spectating`);
}
if (avgDeaths > 6) {
  fails.push(`a novice dies ${avgDeaths.toFixed(1)}x per round — relentless`);
}
if (novices.every((r) => r.noviceDamage === 0)) {
  fails.push('a novice never landed anything — no sense of progress');
}

console.log(
  `\nbots — dashes ${totalDashes}, hits ${totalHits}, kills ${totalKills}` +
    `, hit rate ${((totalHits / Math.max(1, totalDashes)) * 100).toFixed(0)}%` +
    `\nnovice — alive ${avgAlive.toFixed(0)}% of round, ${avgDeaths.toFixed(1)} deaths, ` +
    `earliest death ${earliest}s`,
);

if (fails.length) {
  console.error(`\nFAIL\n- ${fails.join('\n- ')}`);
  process.exit(1);
}
console.log('\nPASS');
