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

// --- mechanics: spilled goo belongs to nobody except its former owner ------
// Directly asserted rather than inferred from aggregate balance, because the
// rule is invisible in the totals: mass still moves, hits still land, and the
// numbers look identical whether or not the lock is honoured.
function checkOwnerLock() {
  const a = new Arena(99);
  const victim = a.addPlayer('victim', 'V', false);
  const thief = a.addPlayer('thief', 'T', false);
  a.startRound();
  while (a.phase !== 'live') a.step(dt);
  victim.protect = 0;
  thief.protect = 0;

  // Park them apart, then land one chunk squarely on the victim.
  victim.x = 0; victim.y = 0; victim.vx = 0; victim.vy = 0;
  thief.x = -60; thief.y = 0;
  a.chunks.push({
    id: 9999, owner: 'thief', hue: thief.hue,
    x: 0, y: 0, vx: 0, vy: 0,
    mass: ARENA.blob.startMass * ARENA.dash.massFraction,
    r: 3, life: 1, armed: -1,
  });
  a.step(dt);

  // Read the loss off the shooter's damage tally rather than differencing the
  // victim's mass: the victim can absorb pellets in the very same tick, which
  // masks part of what was taken and makes the difference read low.
  const lost = thief.damage;
  const mine = a.pellets.filter((g) => g.from === 'victim');
  const spilled = mine.reduce((t, g) => t + g.mass, 0);
  const out = {
    lost: Math.round(lost),
    spilled: Math.round(spilled),
    halfOfLoss: Math.round(lost / 2),
    locked: mine.every((g) => g.lock > 0),
    lockSeconds: mine[0] ? +mine[0].lock.toFixed(1) : 0,
  };

  // The victim standing on their own spill must not be able to take it back...
  const target = mine[0];
  if (target) {
    victim.x = target.x; victim.y = target.y;
    const massAt = victim.mass;
    a.step(dt);
    out.victimTookItImmediately = victim.mass > massAt + 0.01;

    // ...but anyone else may, the instant it lands.
    const survivor = a.pellets.find((g) => g.from === 'victim');
    if (survivor) {
      thief.x = survivor.x; thief.y = survivor.y;
      const tMass = thief.mass;
      a.step(dt);
      out.thiefCouldTakeIt = thief.mass > tMass + 0.01;
    }
  }
  return out;
}

const lock = checkOwnerLock();
console.log(`owner lock: ${JSON.stringify(lock)}`);

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

if (!lock.locked) fails.push('spilled goo was not locked to the player it came from');
if (lock.victimTookItImmediately) fails.push('victim re-absorbed their own spill instantly');
if (lock.thiefCouldTakeIt === false) fails.push('nobody else could take the spill either — lock is too broad');
// Half of what the victim loses should be lying on the floor.
if (Math.abs(lock.spilled - lock.halfOfLoss) > Math.max(2, lock.halfOfLoss * 0.15)) {
  fails.push(`${lock.spilled} of ${lock.lost} lost mass hit the floor; expected about half (${lock.halfOfLoss})`);
}
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
