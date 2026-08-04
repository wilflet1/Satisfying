import { BlobSystem } from './blobs';
import { CFG } from './config';
import { Renderer, type RenderFrame } from './renderer';
import { MAX_BLOBS, MAX_PRIMS } from './shaders';
import { World, type Prim } from './world';

export type GameState = 'title' | 'playing' | 'dead';

/**
 * Per-run instrumentation. The funnel is the only honest answer to "is this
 * balanced" — the playtest harness reads these to tell a hard game apart from
 * a broken one.
 */
export interface Stats {
  gatePassed: number;
  gateHit: number;
  sawHit: number;
  doorBreak: number;
  doorFail: number;
  dropletsTaken: number;
}

export interface Hud {
  state: GameState;
  score: number;
  best: number;
  combo: number;
  multiplier: number;
  /** Mass as a fraction of the cap, for the bar. */
  massFrac: number;
  count: number;
  speedN: number;
}

const BEST_KEY = 'quicksilver.best';

export class Game {
  readonly blobs = new BlobSystem();
  readonly world = new World();

  state: GameState = 'title';
  camY = 0;
  private startY = 0;
  private elapsed = 0;
  private time = 0;
  score = 0;
  best = 0;
  combo = 0;
  stats: Stats = emptyStats();

  // --- juice ---
  private trauma = 0;
  private hitstop = 0;
  private slowmo = 0;
  private flash = 0;
  private flashCol: [number, number, number] = [1, 1, 1];
  private chroma = 0;
  private pulse = 0;
  private pulseX = 0;
  private pulseY = 0;

  // --- preallocated GPU upload buffers ---
  private blobBuf = new Float32Array(MAX_BLOBS * 4);
  private primBuf = new Float32Array(MAX_PRIMS * 4);
  private primMetaBuf = new Float32Array(MAX_PRIMS * 2);
  private prims: Prim[] = [];

  /** `?seed=N` pins world generation so a run can be reproduced exactly. */
  private readonly fixedSeed: number | null;

  constructor(readonly renderer: Renderer) {
    const stored = Number(localStorage.getItem(BEST_KEY) ?? 0);
    this.best = Number.isFinite(stored) ? stored : 0;
    const s = Number(new URLSearchParams(location.search).get('seed'));
    this.fixedSeed = Number.isFinite(s) && s !== 0 ? s : null;
  }

  get viewH() {
    return this.renderer.viewH;
  }

  get lineY() {
    return this.camY + this.viewH * CFG.playerScreenY;
  }

  get speed() {
    const t = Math.min(1, this.elapsed / CFG.speed.rampSeconds);
    // Ease-out so the early ramp is felt and the late ramp doesn't run away.
    const e = 1 - (1 - t) * (1 - t);
    return CFG.speed.start + (CFG.speed.max - CFG.speed.start) * e;
  }

  get speedN() {
    return (this.speed - CFG.speed.start) / (CFG.speed.max - CFG.speed.start);
  }

  get multiplier() {
    return Math.min(CFG.combo.max, 1 + this.combo * CFG.combo.step);
  }

  start() {
    this.blobs.reset();
    this.world.reset(this.fixedSeed ?? Date.now());
    this.camY = 0;
    this.startY = 0;
    this.elapsed = 0;
    this.score = 0;
    this.combo = 0;
    this.trauma = 0;
    this.hitstop = 0;
    this.slowmo = 0;
    this.flash = 0;
    this.chroma = 0;
    this.pulse = 0;
    this.stats = emptyStats();
    this.blobs.lineY = this.lineY;
    for (const b of this.blobs.blobs) b.y = this.lineY;
    this.state = 'playing';
  }

  // --- input surface -----------------------------------------------------

  steer(worldX: number) {
    const lim = CFG.channelHalfW - 3;
    this.blobs.targetX = Math.max(-lim, Math.min(lim, worldX));
  }

  doSplit() {
    if (this.state !== 'playing') return;
    if (this.blobs.split()) {
      this.chroma = Math.max(this.chroma, 0.7);
      this.trauma = Math.min(1, this.trauma + 0.18);
      haptic(8);
    }
  }

  doMerge() {
    if (this.state !== 'playing') return;
    if (this.blobs.mergeAll()) haptic(14);
  }

  // --- simulation --------------------------------------------------------

  update(dtRaw: number) {
    const dtReal = Math.min(dtRaw, 1 / 20);
    this.time += dtReal;

    // Hitstop freezes the world outright; slow-mo scales it. Both decay in
    // real time so they never depend on the timescale they're modifying.
    if (this.hitstop > 0) {
      this.hitstop -= dtReal;
      this.decayJuice(dtReal);
      return;
    }
    if (this.slowmo > 0) this.slowmo -= dtReal;
    const scale = this.slowmo > 0 ? CFG.juice.slowmoScale : 1;
    const dt = dtReal * scale;

    this.decayJuice(dtReal);

    if (this.state !== 'playing') {
      // Idle drift keeps the title and death screens alive.
      this.camY += 10 * dt;
      this.blobs.lineY = this.lineY;
      this.blobs.update(dt, 10);
      this.world.generateTo(this.camY + this.viewH + 300, 0, CFG.speed.start);
      this.world.update(dt, this.camY);
      return;
    }

    this.elapsed += dt;
    this.camY += this.speed * dt;
    this.score += this.speed * dt * 0.1 * this.multiplier;

    this.blobs.lineY = this.lineY;
    this.blobs.update(dt, this.speed);

    this.world.generateTo(this.camY + this.viewH + 300, this.camY - this.startY, this.speed);
    this.world.update(dt, this.camY);

    this.collide();
    this.drainBlobEvents();

    if (this.blobs.totalVolume < CFG.mass.min) this.die();
  }

  private decayJuice(dt: number) {
    this.trauma = Math.max(0, this.trauma - CFG.juice.shakeDecay * dt * 0.16);
    this.flash = Math.max(0, this.flash - dt * 5);
    this.chroma = Math.max(0, this.chroma - dt * 2.6);
    this.pulse = Math.max(0, this.pulse - dt * 2.2);
  }

  private drainBlobEvents() {
    if (this.blobs.fusedThisFrame > 0) {
      const c = this.blobs.blobs[0];
      this.pulse = 1;
      this.pulseX = c?.x ?? 0;
      this.pulseY = c?.y ?? this.lineY;
      this.chroma = Math.max(this.chroma, 1);
      this.trauma = Math.min(1, this.trauma + 0.12 * this.blobs.fusedThisFrame);
      this.blobs.fusedThisFrame = 0;
      haptic(10);
    }
    this.blobs.splitThisFrame = false;
  }

  private collide() {
    const hh = CFG.gate.wallThickness / 2;
    const line = this.lineY;

    for (const o of this.world.obstacles) {
      if (o.kind === 'saw') {
        for (const b of this.blobs.blobs) {
          const dx = b.x - o.x;
          const dy = b.y - o.y;
          const reach = CFG.saw.radius * 0.92 + b.r;
          if (dx * dx + dy * dy < reach * reach && b.grace <= 0) {
            if (this.blobs.damage(b, CFG.damage.saw) > 0) {
              this.stats.sawHit++;
              o.flash = 1;
              this.onHit(0.55, [1, 0.25, 0.3]);
            }
          }
        }
        continue;
      }

      if (o.kind === 'slit') {
        const reachY = hh + 0.5;
        if (o.state === 'pending') {
          for (const b of this.blobs.blobs) {
            if (Math.abs(b.y - o.y) > reachY + b.r) continue;
            // Fits only if the whole blob clears one slit.
            const fits = o.slits.some((s) => Math.abs(b.x - s) + b.r <= CFG.gate.slitHalfW);
            if (!fits && b.grace <= 0) {
              if (this.blobs.damage(b, CFG.damage.wall) > 0) {
                this.stats.gateHit++;
                o.state = 'hit';
                o.flash = 1;
                this.onHit(0.7, [0.4, 0.9, 1]);
              }
            }
          }
        }
      } else if (o.kind === 'door') {
        const reachY = CFG.gate.wallThickness * 0.8 + 0.5;
        if (!o.broken) {
          for (const b of this.blobs.blobs) {
            if (Math.abs(b.y - o.y) > reachY + b.r) continue;
            // One blob, heavy enough: a clean break. Otherwise you burst
            // through anyway but leave a lot of mass on the door.
            const solo = this.blobs.count === 1;
            if (solo && b.vol >= o.req) {
              o.broken = true;
              o.state = 'passed';
              o.flash = 1;
              this.onDoorBreak();
            } else if (b.grace <= 0) {
              this.blobs.damage(b, CFG.damage.doorFail);
              this.stats.doorFail++;
              o.broken = true;
              o.state = 'hit';
              o.flash = 1;
              this.onHit(0.85, [1, 0.6, 0.15]);
            }
          }
        }
      }

      // Resolve once the obstacle is fully behind the player line.
      if (o.state === 'pending' && line > o.y + band(o) + 8) {
        o.state = 'passed';
        this.stats.gatePassed++;
        this.combo++;
        this.blobs.addVolume(CFG.combo.reward);
      }
    }

    // Droplets.
    for (const d of this.world.droplets) {
      if (d.taken) continue;
      for (const b of this.blobs.blobs) {
        const dx = b.x - d.x;
        const dy = b.y - d.y;
        const reach = b.r + CFG.droplet.radius;
        if (dx * dx + dy * dy < reach * reach) {
          d.taken = true;
          this.stats.dropletsTaken++;
          this.blobs.addVolume(CFG.droplet.volume);
          this.chroma = Math.max(this.chroma, 0.35);
          break;
        }
      }
    }
  }

  private onHit(traumaAdd: number, col: [number, number, number]) {
    this.combo = 0;
    this.trauma = Math.min(1, this.trauma + traumaAdd);
    this.hitstop = CFG.juice.hitstopHit;
    this.flash = 0.55;
    this.flashCol = col;
    this.chroma = 1.4;
    haptic(35);
  }

  private onDoorBreak() {
    this.stats.doorBreak++;
    this.combo++;
    this.trauma = Math.min(1, this.trauma + 0.9);
    this.hitstop = CFG.juice.hitstopDoor;
    this.slowmo = CFG.juice.slowmoDoor;
    this.flash = 0.7;
    this.flashCol = [1, 0.85, 0.55];
    this.chroma = 1.8;
    this.pulse = 1;
    this.pulseX = this.blobs.blobs[0]?.x ?? 0;
    this.pulseY = this.lineY;
    this.blobs.addVolume(CFG.combo.reward * 1.5);
    haptic([18, 30, 24]);
  }

  private die() {
    this.state = 'dead';
    this.trauma = 1;
    this.flash = 0.8;
    this.flashCol = [1, 1, 1];
    this.chroma = 2;
    this.hitstop = 0.14;
    if (this.score > this.best) {
      this.best = this.score;
      try {
        localStorage.setItem(BEST_KEY, String(Math.floor(this.best)));
      } catch {
        /* private browsing — a lost best score isn't worth breaking the run */
      }
    }
    haptic([40, 60, 80]);
  }

  // --- presentation ------------------------------------------------------

  hud(): Hud {
    return {
      state: this.state,
      score: Math.floor(this.score),
      best: Math.floor(this.best),
      combo: this.combo,
      multiplier: this.multiplier,
      massFrac: Math.min(1, this.blobs.totalVolume / CFG.mass.max),
      count: this.blobs.count,
      speedN: this.state === 'playing' ? this.speedN : 0,
    };
  }

  buildFrame(): RenderFrame {
    const view = this.viewH;
    this.blobBuf.fill(0);

    let n = 0;
    for (const b of this.blobs.blobs) {
      if (n >= MAX_BLOBS) break;
      const o = n * 4;
      this.blobBuf[o] = b.x;
      this.blobBuf[o + 1] = b.y;
      // Blobs on grace flicker thinner, which reads as "hurt" without a HUD.
      this.blobBuf[o + 2] = b.r * (b.grace > 0 ? 0.9 + 0.1 * Math.sin(this.time * 45) : 1);
      this.blobBuf[o + 3] = 0;
      n++;
    }

    // Droplets ride in the same field so absorbing one is a real fluid merge.
    const top = this.camY - 10;
    const bot = this.camY + view + 10;
    for (const d of this.world.droplets) {
      if (n >= MAX_BLOBS) break;
      if (d.y < top || d.y > bot) continue;
      const o = n * 4;
      this.blobBuf[o] = d.x;
      this.blobBuf[o + 1] = d.y;
      this.blobBuf[o + 2] = CFG.droplet.radius * (1 - d.fade);
      this.blobBuf[o + 3] = 1;
      n++;
    }

    this.world.collectPrims(this.camY, view, this.prims, MAX_PRIMS);
    this.primBuf.fill(0);
    this.primMetaBuf.fill(0);
    for (let i = 0; i < this.prims.length; i++) {
      const p = this.prims[i];
      this.primBuf[i * 4] = p.cx;
      this.primBuf[i * 4 + 1] = p.cy;
      this.primBuf[i * 4 + 2] = p.hw;
      this.primBuf[i * 4 + 3] = p.hh;
      this.primMetaBuf[i * 2] = p.kind;
      this.primMetaBuf[i * 2 + 1] = p.flash;
    }

    // Trauma-squared shake, the standard curve: barely there at low trauma,
    // violent at high, and it never lingers.
    const sh = this.trauma * this.trauma;
    const a = this.time * 47;
    return {
      time: this.time,
      camY: this.camY,
      viewH: view,
      speedN: this.speedN,
      blobCount: n,
      blobs: this.blobBuf,
      primCount: this.prims.length,
      prims: this.primBuf,
      primMeta: this.primMetaBuf,
      pulse: this.pulse,
      pulseX: this.pulseX,
      pulseY: this.pulseY,
      chroma: this.chroma,
      flash: this.flash,
      flashCol: this.flashCol,
      shakeX: Math.sin(a * 1.7) * sh * 0.012,
      shakeY: Math.cos(a * 2.3) * sh * 0.012,
    };
  }
}

function emptyStats(): Stats {
  return { gatePassed: 0, gateHit: 0, sawHit: 0, doorBreak: 0, doorFail: 0, dropletsTaken: 0 };
}

/** Half the fun on a phone. Silently absent everywhere else. */
function haptic(pattern: number | number[]) {
  try {
    navigator.vibrate?.(pattern);
  } catch {
    /* unsupported */
  }
}

/** Half-height of an obstacle's collision band, used for pass resolution. */
function band(o: { kind: string }): number {
  return o.kind === 'door' ? CFG.gate.wallThickness * 0.8 : CFG.gate.wallThickness / 2;
}
