/**
 * Headless simulation soak. Runs full rounds of bots-only arena at the real tick
 * rate with no rendering and no network, which makes it fast enough to run on
 * every change and precise enough to answer design questions:
 *
 *   - do rounds actually resolve, or stalemate?
 *   - does combat happen, or do bots just farm pellets and ignore each other?
 *   - is mass conserved, or is the economy leaking?
 *
 * Run: node --experimental-strip-types scripts/sim-test.mjs
 */
import { Arena } from '../src/sim/arena.ts';
import { driveBot } from '../src/sim/bots.ts';
import { ARENA } from '../src/sim/config.ts';

const dt = 1 / ARENA.tickRate;

function playRound(seed, botCount) {
  const arena = new Arena(seed);
  for (let i = 0; i < botCount; i++) arena.addPlayer(`bot${i}`, `Bot ${i + 1}`, true);

  const stats = {
    hits: 0,
    kills: 0,
    dashes: 0,
    absorbs: 0,
    nan: false,
    ticks: 0,
    maxMass: 0,
    massSamples: [],
  };

  // Get past lobby/countdown into the live round.
  let guard = 0;
  while (arena.phase !== 'live' && guard++ < 500) {
    for (const p of arena.players.values()) if (p.bot) driveBot(arena, p, dt);
    arena.step(dt);
  }

  while (arena.phase === 'live' && stats.ticks < ARENA.tickRate * 200) {
    for (const p of arena.players.values()) if (p.bot) driveBot(arena, p, dt);
    arena.step(dt);
    stats.ticks++;

    stats.hits += arena.events.hits.length;
    stats.kills += arena.events.deaths.length;
    stats.dashes += arena.events.dashes.length;
    stats.absorbs += arena.events.absorbs.length;

    let total = 0;
    for (const p of arena.players.values()) {
      if (!Number.isFinite(p.x + p.y + p.mass + p.r)) stats.nan = true;
      if (p.alive) {
        total += p.mass;
        stats.maxMass = Math.max(stats.maxMass, p.mass);
      }
    }
    for (const c of arena.chunks) total += c.mass;
    for (const g of arena.pellets) total += g.mass;
    if (stats.ticks % 20 === 0) stats.massSamples.push(Math.round(total));
  }

  stats.seconds = +(stats.ticks * dt).toFixed(1);
  stats.alive = arena.aliveCount;
  stats.winner = arena.winner;
  stats.phase = arena.phase;
  stats.ringEnd = Math.round(arena.ringRadius);
  return stats;
}

// Several seeds, because one lucky round tells you nothing about the spread —
// and a mode that is a 90-second stalemate on one seed and a 7-second bloodbath
// on the next is not balanced, however good its average looks.
const rounds = [1, 2, 3, 4, 5, 6].map((s) => playRound(s, s > 4 ? 8 : 6));

console.log('rounds:');
for (const r of rounds) {
  console.log(
    `  ${r.seconds}s  alive:${r.alive}  dashes:${r.dashes}  hits:${r.hits}` +
      `  kills:${r.kills}  absorbs:${r.absorbs}  winner:${r.winner ?? 'none'}`,
  );
}

const fails = [];
for (const r of rounds) {
  if (r.nan) fails.push('NaN leaked into the simulation');
  if (r.phase === 'live') fails.push(`round never resolved (ran ${r.seconds}s)`);
}
const totalHits = rounds.reduce((a, r) => a + r.hits, 0);
const totalKills = rounds.reduce((a, r) => a + r.kills, 0);
const totalDashes = rounds.reduce((a, r) => a + r.dashes, 0);

if (totalDashes < 60) fails.push(`bots barely attacked (${totalDashes} dashes)`);
if (totalHits < 25) fails.push(`almost nothing connected (${totalHits} hits) — combat isn't happening`);
if (totalKills < 6) fails.push(`only ${totalKills} kills across ${rounds.length} rounds — no stakes`);
if (rounds.every((r) => r.winner === null)) fails.push('no round produced a winner');

// Round length is the headline feel metric: a round that ends in seconds gave
// nobody a chance to play, and one that runs the full clock with everyone alive
// was a stalemate.
const short = rounds.filter((r) => r.seconds < 25);
if (short.length > rounds.length / 3) {
  fails.push(`${short.length}/${rounds.length} rounds ended under 25s — too swingy`);
}
const stale = rounds.filter((r) => r.alive >= 5);
if (stale.length > rounds.length / 3) {
  fails.push(`${stale.length}/${rounds.length} rounds ended with 5+ alive — stalemate`);
}

// The economy should be closed: mass moves between blobs, chunks and pellets,
// but the arena tops up ambient goo, so expect growth, never collapse.
for (const r of rounds) {
  const first = r.massSamples[0] ?? 0;
  const last = r.massSamples[r.massSamples.length - 1] ?? 0;
  if (last < first * 0.35) {
    fails.push(`mass economy collapsed: ${first} -> ${last}`);
  }
}

const lens = rounds.map((r) => r.seconds).sort((a, b) => a - b);
console.log(
  `\ntotals — dashes ${totalDashes}, hits ${totalHits}, kills ${totalKills}` +
    `, hit rate ${((totalHits / Math.max(1, totalDashes)) * 100).toFixed(0)}%` +
    `\nround length — min ${lens[0]}s, median ${lens[lens.length >> 1]}s, max ${lens[lens.length - 1]}s`,
);

if (fails.length) {
  console.error(`\nFAIL\n- ${fails.join('\n- ')}`);
  process.exit(1);
}
console.log('\nPASS');
