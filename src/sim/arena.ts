import { ARENA, massToRadius, speedFor } from './config.ts';

export type Phase = 'lobby' | 'countdown' | 'live' | 'over';

export interface Input {
  /** Movement direction, magnitude 0..1. */
  moveX: number;
  moveY: number;
  /** Aim direction, unit length. */
  aimX: number;
  aimY: number;
  dash: boolean;
  pull: boolean;
  /** Client input sequence, echoed back so the client can reconcile. */
  seq: number;
}

export const emptyInput = (): Input => ({
  moveX: 0,
  moveY: 0,
  aimX: 1,
  aimY: 0,
  dash: false,
  pull: false,
  seq: 0,
});

export interface Player {
  id: string;
  name: string;
  bot: boolean;
  /** Palette index, so every blob is a different colour. */
  hue: number;
  alive: boolean;
  x: number;
  y: number;
  vx: number;
  vy: number;
  mass: number;
  r: number;
  aimX: number;
  aimY: number;
  cooldown: number;
  grace: number;
  pullTimer: number;
  pullCooldown: number;
  /** Seconds left where this blob cannot pick pellets up. */
  absorbLock: number;
  /** Running totals for the scoreboard. */
  kills: number;
  damage: number;
  /** Last input sequence applied, echoed in snapshots for reconciliation. */
  ack: number;
  input: Input;
  /** Bot-only reaction timer. */
  think: number;
}

export interface Chunk {
  id: number;
  owner: string;
  hue: number;
  x: number;
  y: number;
  vx: number;
  vy: number;
  mass: number;
  r: number;
  life: number;
  armed: number;
}

export interface Pellet {
  id: number;
  x: number;
  y: number;
  vx: number;
  vy: number;
  mass: number;
  r: number;
  hue: number;
}

/** Things that happened this tick, drained by the server to notify clients. */
export interface Events {
  hits: Array<{ x: number; y: number; hue: number; power: number }>;
  deaths: Array<{ x: number; y: number; hue: number; id: string; by: string }>;
  dashes: Array<{ x: number; y: number; hue: number }>;
  absorbs: Array<{ x: number; y: number; hue: number }>;
}

const emptyEvents = (): Events => ({ hits: [], deaths: [], dashes: [], absorbs: [] });

function mulberry32(seed: number) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/**
 * The authoritative arena simulation.
 *
 * Pure logic with no rendering and no I/O, so the server can run it headless and
 * the client can re-run it to predict its own blob. Everything in the game is
 * mass moving between three states — inside a player, in flight as a chunk, or
 * lying free as a pellet — and every interaction is a transfer between them.
 * That single rule is what makes the fight readable: you can always see where
 * the mass went.
 */
export class Arena {
  players = new Map<string, Player>();
  chunks: Chunk[] = [];
  pellets: Pellet[] = [];

  phase: Phase = 'lobby';
  /** Seconds remaining in the current phase. */
  timer: number = 0;
  /** Seconds elapsed in the live round. */
  elapsed = 0;
  ringRadius: number = ARENA.ring.startRadius;
  winner: string | null = null;

  events: Events = emptyEvents();

  private nextId = 1;
  private rand: () => number;

  /**
   * The simulation's seeded random source. Bots draw from this rather than
   * Math.random so a seed reproduces a round exactly — without it the balance
   * soak returns different numbers every run and can neither be trusted nor
   * bisected.
   */
  random(): number {
    return this.rand();
  }

  constructor(seed = 1) {
    this.rand = mulberry32(seed);
    this.seedAmbient();
  }

  // --- membership ---------------------------------------------------------

  addPlayer(id: string, name: string, bot = false): Player {
    const hue = this.pickHue();
    const p: Player = {
      id,
      name,
      bot,
      hue,
      alive: false,
      x: 0,
      y: 0,
      vx: 0,
      vy: 0,
      mass: ARENA.blob.startMass,
      r: massToRadius(ARENA.blob.startMass),
      aimX: 1,
      aimY: 0,
      cooldown: 0,
      grace: 0,
      pullTimer: 0,
      pullCooldown: 0,
      absorbLock: 0,
      kills: 0,
      damage: 0,
      ack: 0,
      input: emptyInput(),
      think: 0,
    };
    this.players.set(id, p);
    // Joining mid-round drops you straight in rather than making you wait.
    if (this.phase === 'live') this.spawn(p);
    return p;
  }

  removePlayer(id: string) {
    const p = this.players.get(id);
    if (p?.alive) this.scatter(p.x, p.y, p.mass, p.hue);
    this.players.delete(id);
  }

  setInput(id: string, input: Input) {
    const p = this.players.get(id);
    if (!p) return;
    p.input = input;
    p.ack = input.seq;
  }

  get humanCount() {
    let n = 0;
    for (const p of this.players.values()) if (!p.bot) n++;
    return n;
  }

  get aliveCount() {
    let n = 0;
    for (const p of this.players.values()) if (p.alive) n++;
    return n;
  }

  private pickHue(): number {
    const used = new Set<number>();
    for (const p of this.players.values()) used.add(p.hue);
    for (let i = 0; i < ARENA.match.maxPlayers; i++) if (!used.has(i)) return i;
    return 0;
  }

  // --- round flow ---------------------------------------------------------

  private spawn(p: Player) {
    // Spawn on a ring inset from the wall, spread away from everyone else.
    let best = { x: 0, y: 0, d: -1 };
    for (let i = 0; i < 12; i++) {
      const a = this.rand() * Math.PI * 2;
      const rad = this.ringRadius * (0.35 + this.rand() * 0.5);
      const x = Math.cos(a) * rad;
      const y = Math.sin(a) * rad;
      let d = 1e9;
      for (const o of this.players.values()) {
        if (o === p || !o.alive) continue;
        d = Math.min(d, Math.hypot(o.x - x, o.y - y));
      }
      if (d > best.d) best = { x, y, d };
    }
    p.x = best.x;
    p.y = best.y;
    p.vx = 0;
    p.vy = 0;
    p.mass = ARENA.blob.startMass;
    p.r = massToRadius(p.mass);
    p.alive = true;
    p.grace = 1;
    p.cooldown = 0;
    p.pullTimer = 0;
    p.pullCooldown = 0;
    p.absorbLock = 0;
  }

  startRound() {
    this.chunks.length = 0;
    this.pellets.length = 0;
    this.seedAmbient();
    this.ringRadius = ARENA.ring.startRadius;
    this.elapsed = 0;
    this.winner = null;
    for (const p of this.players.values()) {
      p.kills = 0;
      p.damage = 0;
      this.spawn(p);
    }
    this.phase = 'countdown';
    this.timer = ARENA.match.countdown;
  }

  private endRound() {
    this.phase = 'over';
    this.timer = ARENA.match.intermission;
    let best: Player | null = null;
    for (const p of this.players.values()) {
      if (!p.alive) continue;
      if (!best || p.mass > best.mass) best = p;
    }
    this.winner = best?.id ?? null;
  }

  /** How many ambient pellets the arena should be holding right now. */
  private ambientTarget(): number {
    const t = Math.min(1, this.elapsed / ARENA.match.seconds);
    const { ambient, ambientEnd } = ARENA.goo;
    return Math.round(ambient + (ambientEnd - ambient) * t);
  }

  private seedAmbient() {
    while (this.pellets.length < ARENA.goo.ambient) this.spawnAmbient();
  }

  private spawnAmbient() {
    const a = this.rand() * Math.PI * 2;
    const rad = Math.sqrt(this.rand()) * this.ringRadius * 0.94;
    this.pellets.push({
      id: this.nextId++,
      x: Math.cos(a) * rad,
      y: Math.sin(a) * rad,
      vx: 0,
      vy: 0,
      mass: ARENA.goo.ambientMass,
      r: massToRadius(ARENA.goo.ambientMass),
      hue: -1,
    });
  }

  /** Break a quantity of mass into pellets — the universal "mass leaves a body". */
  private scatter(x: number, y: number, mass: number, hue: number, shards?: number) {
    const n = Math.max(1, shards ?? ARENA.goo.shardsPerHit);
    const each = mass / n;
    if (each <= 0) return;
    for (let i = 0; i < n; i++) {
      const a = this.rand() * Math.PI * 2;
      const sp = ARENA.goo.shardSpeed * (0.3 + this.rand() * 0.9);
      const d = this.rand() * ARENA.goo.shardSpread;
      this.pellets.push({
        id: this.nextId++,
        x: x + Math.cos(a) * d * 0.4,
        y: y + Math.sin(a) * d * 0.4,
        vx: Math.cos(a) * sp,
        vy: Math.sin(a) * sp,
        mass: each,
        r: massToRadius(each),
        hue,
      });
    }
  }

  // --- tick ---------------------------------------------------------------

  step(dt: number) {
    this.events = emptyEvents();

    if (this.phase === 'lobby') {
      if (this.players.size >= 2) this.startRound();
      return;
    }
    if (this.phase === 'countdown') {
      this.timer -= dt;
      // Blobs can drift during the countdown but nothing can hurt them.
      this.movePlayers(dt, false);
      this.movePellets(dt);
      if (this.timer <= 0) {
        this.phase = 'live';
        this.timer = ARENA.match.seconds;
      }
      return;
    }
    if (this.phase === 'over') {
      this.timer -= dt;
      this.movePellets(dt);
      if (this.timer <= 0) {
        if (this.players.size >= 2) this.startRound();
        else this.phase = 'lobby';
      }
      return;
    }

    // --- live ---
    this.timer -= dt;
    this.elapsed += dt;
    this.shrinkRing();
    this.movePlayers(dt, true);
    this.moveChunks(dt);
    this.movePellets(dt);
    this.resolveChunkHits();
    this.resolveAbsorb();
    this.resolveRing(dt);

    while (this.pellets.length < this.ambientTarget()) this.spawnAmbient();

    if (this.timer <= 0 || this.aliveCount <= (this.players.size > 1 ? 1 : 0)) this.endRound();
  }

  private shrinkRing() {
    const { startRadius, endRadius, holdFraction } = ARENA.ring;
    const t = this.elapsed / ARENA.match.seconds;
    const k = Math.max(0, Math.min(1, (t - holdFraction) / (1 - holdFraction)));
    this.ringRadius = startRadius + (endRadius - startRadius) * k;
  }

  private movePlayers(dt: number, live: boolean) {
    for (const p of this.players.values()) {
      if (!p.alive) continue;
      if (p.grace > 0) p.grace -= dt;
      if (p.cooldown > 0) p.cooldown -= dt;
      if (p.pullCooldown > 0) p.pullCooldown -= dt;
      if (p.pullTimer > 0) p.pullTimer -= dt;
      if (p.absorbLock > 0) p.absorbLock -= dt;

      const inp = p.input;
      const aim = Math.hypot(inp.aimX, inp.aimY);
      if (aim > 0.01) {
        p.aimX = inp.aimX / aim;
        p.aimY = inp.aimY / aim;
      }

      const top = speedFor(p.r);
      const mv = Math.hypot(inp.moveX, inp.moveY);
      if (mv > 0.01) {
        const nx = inp.moveX / Math.max(mv, 1);
        const ny = inp.moveY / Math.max(mv, 1);
        p.vx += (nx * top - p.vx) * ARENA.blob.accel * dt;
        p.vy += (ny * top - p.vy) * ARENA.blob.accel * dt;
      } else {
        const k = Math.exp(-ARENA.blob.drag * dt);
        p.vx *= k;
        p.vy *= k;
      }

      p.x += p.vx * dt;
      p.y += p.vy * dt;

      if (live && inp.dash) this.tryDash(p);
      if (live && inp.pull && p.pullCooldown <= 0) {
        p.pullTimer = ARENA.pull.duration;
        p.pullCooldown = ARENA.pull.cooldown;
      }
    }

    // Blobs push each other apart rather than overlapping — contact should feel
    // like two bodies of liquid meeting, not two ghosts.
    const list = [...this.players.values()].filter((p) => p.alive);
    for (let i = 0; i < list.length; i++) {
      for (let j = i + 1; j < list.length; j++) {
        const a = list[i];
        const b = list[j];
        const dx = b.x - a.x;
        const dy = b.y - a.y;
        const d = Math.hypot(dx, dy);
        const want = a.r + b.r;
        if (d > want || d < 1e-4) continue;
        const push = (want - d) * 0.5;
        const nx = dx / d;
        const ny = dy / d;
        // Heavier blobs give way less.
        const wa = b.mass / (a.mass + b.mass);
        const wb = a.mass / (a.mass + b.mass);
        a.x -= nx * push * 2 * wa;
        a.y -= ny * push * 2 * wa;
        b.x += nx * push * 2 * wb;
        b.y += ny * push * 2 * wb;
      }
    }
  }

  private tryDash(p: Player) {
    if (p.cooldown > 0) return;
    const spend = Math.min(p.mass * ARENA.dash.massFraction, ARENA.dash.maxMass);
    if (spend < ARENA.dash.minMass || p.mass - spend < ARENA.blob.minMass) return;

    p.mass -= spend;
    p.r = massToRadius(p.mass);
    p.cooldown = ARENA.dash.cooldown;

    const r = massToRadius(spend);
    this.chunks.push({
      id: this.nextId++,
      owner: p.id,
      hue: p.hue,
      x: p.x + p.aimX * (p.r + r * 0.6),
      y: p.y + p.aimY * (p.r + r * 0.6),
      vx: p.vx + p.aimX * ARENA.dash.speed,
      vy: p.vy + p.aimY * ARENA.dash.speed,
      mass: spend,
      r,
      life: ARENA.dash.life,
      armed: ARENA.dash.armTime,
    });

    // Recoil: firing shoves you backwards, so attacking is also repositioning.
    p.vx -= p.aimX * ARENA.dash.recoil;
    p.vy -= p.aimY * ARENA.dash.recoil;
    this.events.dashes.push({ x: p.x, y: p.y, hue: p.hue });
  }

  private moveChunks(dt: number) {
    const k = Math.exp(-ARENA.dash.drag * dt);
    for (let i = this.chunks.length - 1; i >= 0; i--) {
      const c = this.chunks[i];
      c.life -= dt;
      c.armed -= dt;
      c.vx *= k;
      c.vy *= k;
      c.x += c.vx * dt;
      c.y += c.vy * dt;
      if (c.life <= 0) {
        // A miss is not wasted — spent mass drops as goo anyone can claim.
        this.scatter(c.x, c.y, c.mass, c.hue, 2);
        this.chunks.splice(i, 1);
      }
    }
  }

  private movePellets(dt: number) {
    const k = Math.exp(-ARENA.goo.drag * dt);
    const pullers = [...this.players.values()].filter((p) => p.alive && p.pullTimer > 0);
    for (const g of this.pellets) {
      for (const p of pullers) {
        const dx = p.x - g.x;
        const dy = p.y - g.y;
        const d = Math.hypot(dx, dy);
        if (d > ARENA.pull.radius || d < 1e-3) continue;
        // Falls off with distance so pull is a scramble tool, not a vacuum.
        const f = ARENA.pull.force * (1 - d / ARENA.pull.radius);
        g.vx += (dx / d) * f * dt;
        g.vy += (dy / d) * f * dt;
      }
      g.vx *= k;
      g.vy *= k;
      g.x += g.vx * dt;
      g.y += g.vy * dt;
    }
  }

  private resolveChunkHits() {
    for (let i = this.chunks.length - 1; i >= 0; i--) {
      const c = this.chunks[i];
      for (const p of this.players.values()) {
        if (!p.alive) continue;
        if (p.id === c.owner && c.armed > 0) continue;
        const dx = p.x - c.x;
        const dy = p.y - c.y;
        if (dx * dx + dy * dy > (p.r + c.r) * (p.r + c.r)) continue;

        if (p.id === c.owner) {
          // Your own chunk coming home is simply reabsorbed.
          p.mass = Math.min(ARENA.blob.maxMass, p.mass + c.mass);
          p.r = massToRadius(p.mass);
        } else if (p.grace > 0) {
          continue;
        } else {
          const stolen = Math.min(
            Math.max(0, p.mass - ARENA.blob.minMass),
            c.mass * ARENA.dash.steal,
          );
          p.mass -= stolen;
          p.r = massToRadius(p.mass);
          p.grace = ARENA.blob.grace;

          p.absorbLock = ARENA.blob.absorbLock;

          const shooter = this.players.get(c.owner);
          const direct = stolen * ARENA.dash.directShare;
          if (shooter && shooter.alive) {
            shooter.damage += stolen;
            shooter.mass = Math.min(ARENA.blob.maxMass, shooter.mass + direct);
            shooter.r = massToRadius(shooter.mass);
          }

          // The rest sprays free, so a hit still starts a scramble — but the
          // shooter is paid immediately, which is what makes landing one feel
          // like an accomplishment rather than a coin flip.
          this.scatter(c.x, c.y, c.mass + stolen - (shooter ? direct : 0), c.hue);
          this.events.hits.push({
            x: c.x,
            y: c.y,
            hue: c.hue,
            power: Math.min(1, stolen / ARENA.blob.startMass),
          });

          if (p.mass <= ARENA.blob.minMass) this.kill(p, c.owner);
        }

        this.chunks.splice(i, 1);
        break;
      }
    }
  }

  private resolveAbsorb() {
    for (let i = this.pellets.length - 1; i >= 0; i--) {
      const g = this.pellets[i];
      for (const p of this.players.values()) {
        if (!p.alive) continue;
        if (p.absorbLock > 0) continue;
        if (p.r < g.r * ARENA.goo.absorbRatio) continue;
        const dx = p.x - g.x;
        const dy = p.y - g.y;
        if (dx * dx + dy * dy > (p.r + g.r) * (p.r + g.r)) continue;
        p.mass = Math.min(ARENA.blob.maxMass, p.mass + g.mass);
        p.r = massToRadius(p.mass);
        this.events.absorbs.push({ x: g.x, y: g.y, hue: p.hue });
        this.pellets.splice(i, 1);
        break;
      }
    }
  }

  private resolveRing(dt: number) {
    for (const p of this.players.values()) {
      if (!p.alive) continue;
      const d = Math.hypot(p.x, p.y);
      if (d <= this.ringRadius) continue;
      // Outside the ring you bleed mass into the void — no instant death, so
      // being caught out is a cost you can still play your way out of.
      const loss = Math.min(p.mass, ARENA.ring.drainPerSecond * dt);
      p.mass -= loss;
      p.r = massToRadius(p.mass);
      if (p.mass <= ARENA.blob.minMass) this.kill(p, '');
    }
  }

  private kill(p: Player, by: string) {
    p.alive = false;
    // Everything they were carrying bursts into the arena.
    this.scatter(p.x, p.y, Math.max(p.mass, ARENA.blob.minMass), p.hue, 7);
    this.events.deaths.push({ x: p.x, y: p.y, hue: p.hue, id: p.id, by });
    const killer = this.players.get(by);
    if (killer && killer !== p) killer.kills++;
    p.mass = 0;
    p.r = 0;
  }
}
