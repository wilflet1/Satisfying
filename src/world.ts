import { CFG, slotOffset } from './config';

export type Obstacle = SlitGate | BlastDoor | Saw;

export interface SlitGate {
  kind: 'slit';
  y: number;
  /** World-x centre of each slit, left to right. */
  slits: number[];
  /** Blob count required to thread it. */
  need: number;
  state: 'pending' | 'passed' | 'hit';
  flash: number;
}

export interface BlastDoor {
  kind: 'door';
  y: number;
  /** Volume a single blob must carry to break through. */
  req: number;
  broken: boolean;
  state: 'pending' | 'passed' | 'hit';
  flash: number;
}

export interface Saw {
  kind: 'saw';
  y: number;
  x: number;
  vx: number;
  rot: number;
  flash: number;
  /** Blades are confined to a band so a clear side always exists. */
  minX: number;
  maxX: number;
}

export interface Droplet {
  x: number;
  y: number;
  taken: boolean;
  /** Absorption animation progress, 0..1. */
  fade: number;
}

/** Render primitive consumed by the shader. Saws pack radius/rotation into hw/hh. */
export interface Prim {
  cx: number;
  cy: number;
  hw: number;
  hh: number;
  /** 0 = gate wall, 1 = blast door, 2 = saw blade. */
  kind: number;
  flash: number;
}

/** Deterministic PRNG so a seed reproduces a run exactly during testing. */
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
 * The endless channel. Obstacles are generated ahead of the camera and pruned
 * behind it. The opening sequence is authored (teach → test → twist) and the
 * generator takes over once every mechanic has been introduced.
 */
export class World {
  obstacles: Obstacle[] = [];
  droplets: Droplet[] = [];

  private rand: () => number;
  private cursor = 0;
  private scripted = 0;
  /** Blob count the previous obstacle demanded, for reconfiguration spacing. */
  private lastNeed = 1;

  constructor(seed = Date.now()) {
    this.rand = mulberry32(seed);
    this.reset(seed);
  }

  reset(seed = Date.now()) {
    this.rand = mulberry32(seed);
    this.obstacles = [];
    this.droplets = [];
    this.cursor = CFG.spawn.runway;
    this.scripted = 0;
    this.lastNeed = 1;
  }

  /** Difficulty in 0..1, driven by how deep the run has gone. */
  private difficulty(depth: number) {
    return Math.min(1, depth / CFG.spawn.tightenOver);
  }

  update(dt: number, camY: number) {
    for (const o of this.obstacles) {
      o.flash = Math.max(0, o.flash - dt * 3.5);
      if (o.kind === 'saw') {
        o.rot += CFG.saw.spinRate * dt;
        o.x += o.vx * dt;
        if (o.x < o.minX || o.x > o.maxX) {
          o.x = Math.max(o.minX, Math.min(o.maxX, o.x));
          o.vx *= -1;
        }
      }
    }
    for (const d of this.droplets) {
      if (d.taken && d.fade < 1) d.fade = Math.min(1, d.fade + dt * 5);
    }
    this.prune(camY);
  }

  generateTo(y: number, depth: number, speed: number) {
    while (this.cursor < y) {
      const d = this.difficulty(depth);
      // Decide *what* comes next before deciding how far away it goes, so the
      // gap can pay for the reconfiguration that obstacle will demand.
      const plan = this.plan(d);
      const secs =
        CFG.spawn.gapSecondsStart + (CFG.spawn.gapSecondsEnd - CFG.spawn.gapSecondsStart) * d;
      const steps = Math.abs(plan.need - this.lastNeed);
      const gap = speed * (secs + steps * CFG.spawn.perStepSeconds);

      this.sprinkleDroplets(this.cursor + gap * 0.5, gap * 0.28);
      this.cursor += gap;
      this.cursor += this.emit(plan, this.cursor, d, speed);
      this.lastNeed = plan.need;
    }
  }

  /** Chooses the next obstacle. `need` is the blob count it demands. */
  private plan(d: number): { kind: 'slit' | 'door' | 'saws'; need: number } {
    // The opening is authored so each mechanic is introduced in a safe context
    // before it can kill you: split, then merge, then split wider, then weave.
    const script: Array<'slit2' | 'door' | 'slit3' | 'saws'> = ['slit2', 'door', 'slit3', 'saws'];
    if (this.scripted < script.length) {
      const step = script[this.scripted++];
      if (step === 'slit2') return { kind: 'slit', need: 2 };
      if (step === 'door') return { kind: 'door', need: 1 };
      if (step === 'slit3') return { kind: 'slit', need: 3 };
      return { kind: 'saws', need: this.lastNeed };
    }

    const roll = this.rand();
    if (roll < 0.45) {
      const maxNeed = Math.min(CFG.maxBlobs, 2 + Math.floor(d * 4 + this.rand() * 1.5));
      return { kind: 'slit', need: 2 + Math.floor(this.rand() * (maxNeed - 1)) };
    }
    if (roll < 0.72) return { kind: 'door', need: 1 };
    // Saws don't demand a count, so they cost no reconfiguration runway.
    return { kind: 'saws', need: this.lastNeed };
  }

  /** Places a planned obstacle at `y`; returns the depth it consumed. */
  private emit(
    plan: { kind: 'slit' | 'door' | 'saws'; need: number },
    y: number,
    d: number,
    speed: number,
  ): number {
    if (plan.kind === 'slit') return this.addSlit(y, plan.need);
    if (plan.kind === 'door') return this.addDoor(y, d);
    return this.addSaws(y, d, speed);
  }

  private addSlit(y: number, need: number): number {
    const n = Math.max(2, Math.min(CFG.maxBlobs, need));
    const span = Math.abs(slotOffset(0, n));
    // Shift the whole gate sideways by as much as the channel allows, so the
    // player has to both pick the right count *and* steer to it.
    const room = Math.max(0, CFG.channelHalfW - span - CFG.gate.slitHalfW - 1);
    const shift = (this.rand() * 2 - 1) * room;
    const slits: number[] = [];
    for (let i = 0; i < n; i++) slits.push(slotOffset(i, n) + shift);
    this.obstacles.push({ kind: 'slit', y, slits, need: n, state: 'pending', flash: 0 });
    return CFG.gate.wallThickness;
  }

  private addDoor(y: number, d: number): number {
    const frac = CFG.gate.doorReqStart + (CFG.gate.doorReqEnd - CFG.gate.doorReqStart) * d;
    this.obstacles.push({
      kind: 'door',
      y,
      req: CFG.mass.start * frac,
      broken: false,
      state: 'pending',
      flash: 0,
    });
    return CFG.gate.wallThickness * 1.6;
  }

  private addSaws(y: number, d: number, speed: number): number {
    const { countStart, countEnd, radius, drift, spacingSeconds } = CFG.saw;
    const n = Math.round(countStart + (countEnd - countStart) * d);
    const pitch = speed * spacingSeconds;
    const margin =
      CFG.saw.laneMarginStart + (CFG.saw.laneMarginEnd - CFG.saw.laneMarginStart) * d;
    const outer = CFG.channelHalfW - radius;

    for (let i = 0; i < n; i++) {
      // Alternate halves. The player always has somewhere to be, and weaving
      // between blades is a rhythm rather than a coin flip.
      const side = i % 2 === 0 ? -1 : 1;
      const lo = side < 0 ? -outer : margin;
      const hi = side < 0 ? -margin : outer;
      this.obstacles.push({
        kind: 'saw',
        y: y + i * pitch,
        x: lo + this.rand() * (hi - lo),
        vx: (this.rand() < 0.5 ? -1 : 1) * drift * (0.6 + this.rand() * 0.5 + d * 0.6),
        rot: this.rand() * 6.28,
        flash: 0,
        minX: Math.min(lo, hi),
        maxX: Math.max(lo, hi),
      });
    }
    return (n - 1) * pitch;
  }

  private sprinkleDroplets(y: number, spread: number) {
    const n = 2 + Math.floor(this.rand() * 3);
    for (let i = 0; i < n; i++) {
      // Biased toward the channel edges — the mass is off the fast line.
      const edge = this.rand() < 0.65 ? 1 : 0;
      const mag = edge ? 0.55 + this.rand() * 0.4 : this.rand() * 0.5;
      const x = (this.rand() < 0.5 ? -1 : 1) * mag * (CFG.channelHalfW - CFG.droplet.radius - 2);
      this.droplets.push({ x, y: y + (this.rand() * 2 - 1) * spread, taken: false, fade: 0 });
    }
  }

  private prune(camY: number) {
    const cut = camY - 60;
    this.obstacles = this.obstacles.filter((o) => o.y > cut);
    this.droplets = this.droplets.filter((d) => d.y > cut && d.fade < 1);
  }

  /** Wall segments of a slit gate, as [centre, halfWidth] pairs. */
  static wallSegments(g: SlitGate): Array<[number, number]> {
    const segs: Array<[number, number]> = [];
    const edges = g.slits.map((s) => [s - CFG.gate.slitHalfW, s + CFG.gate.slitHalfW]);
    let x = -CFG.channelHalfW;
    for (const [l, r] of edges) {
      if (l > x) segs.push([(x + l) / 2, (l - x) / 2]);
      x = Math.max(x, r);
    }
    if (x < CFG.channelHalfW) segs.push([(x + CFG.channelHalfW) / 2, (CFG.channelHalfW - x) / 2]);
    return segs;
  }

  /** Flatten everything visible into shader primitives, nearest first. */
  collectPrims(camY: number, viewH: number, out: Prim[], max: number) {
    out.length = 0;
    const top = camY - 20;
    const bot = camY + viewH + 20;
    for (const o of this.obstacles) {
      if (out.length >= max) break;
      if (o.y < top || o.y > bot) continue;
      if (o.kind === 'slit') {
        const hh = CFG.gate.wallThickness / 2;
        for (const [c, hw] of World.wallSegments(o)) {
          if (out.length >= max) break;
          out.push({ cx: c, cy: o.y, hw, hh, kind: 0, flash: o.flash });
        }
      } else if (o.kind === 'door') {
        if (o.broken) continue;
        out.push({
          cx: 0,
          cy: o.y,
          hw: CFG.channelHalfW,
          hh: CFG.gate.wallThickness * 0.8,
          kind: 1,
          flash: o.flash,
        });
      } else {
        out.push({ cx: o.x, cy: o.y, hw: CFG.saw.radius, hh: o.rot, kind: 2, flash: o.flash });
      }
    }
  }
}
