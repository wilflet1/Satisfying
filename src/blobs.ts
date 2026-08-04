import { CFG, slotOffset, volumeToRadius } from './config';

export interface Blob {
  x: number;
  y: number;
  vx: number;
  vy: number;
  /** Volume is the conserved quantity; radius is derived from it. */
  vol: number;
  r: number;
  /** Seconds of damage immunity remaining. */
  grace: number;

  // --- soft-body state, visual only; collision stays circular ---
  /** Deformation vector: direction and magnitude of the current stretch. */
  defX: number;
  defY: number;
  /** Velocity of the deformation spring — this is what makes it wobble. */
  dvX: number;
  dvY: number;
  /** Per-blob phase so surfaces don't all ripple in lockstep. */
  phase: number;
}

const newBlob = (x: number, y: number, vol: number, r: number): Blob => ({
  x,
  y,
  vx: 0,
  vy: 0,
  vol,
  r,
  grace: 0,
  defX: 0,
  defY: 0,
  dvX: 0,
  dvY: 0,
  phase: Math.random() * 6.283,
});

/**
 * The player: a cluster of liquid-metal blobs that conserve total volume across
 * splits and merges. Blobs spring toward numbered slots in a horizontal
 * formation centred on the steering target, which is what makes "split to 3 and
 * line up with 3 slits" a single readable action instead of three.
 */
export class BlobSystem {
  blobs: Blob[] = [];
  /** Steering target, world x. */
  targetX = 0;
  /** World y the formation is held at (set by the camera each frame). */
  lineY = 0;
  /**
   * How many blobs the player has asked for. This is the authority, not the
   * live blob count: a merge is "collapse until we reach targetCount", so the
   * player can halt a collapse mid-flight by tapping split, and a merge can
   * never overshoot the count they were aiming for.
   */
  targetCount = 1;

  /** Events drained by the game each frame, so juice stays out of physics. */
  fusedThisFrame = 0;
  splitThisFrame = false;
  /** Where the last split tore, for spraying satellite droplets. */
  splitPos: { x: number; y: number } | null = null;
  /** Where fusions happened this frame. */
  fusePos: Array<{ x: number; y: number }> = [];

  constructor() {
    this.reset();
  }

  reset() {
    this.blobs = [newBlob(0, 0, CFG.mass.start, volumeToRadius(CFG.mass.start))];
    this.targetX = 0;
    this.targetCount = 1;
  }

  get count() {
    return this.blobs.length;
  }

  /** True while extra blobs are still converging onto their targets. */
  get merging() {
    return this.blobs.length > this.targetCount;
  }

  get totalVolume() {
    let t = 0;
    for (const b of this.blobs) t += b.vol;
    return t;
  }

  centroidX() {
    let sx = 0;
    let sw = 0;
    for (const b of this.blobs) {
      sx += b.x * b.vol;
      sw += b.vol;
    }
    return sw > 0 ? sx / sw : 0;
  }

  /** Ask for one more blob. Also halts an in-flight merge at the new count. */
  split(): boolean {
    if (this.targetCount >= CFG.maxBlobs) return false;
    this.targetCount++;
    // Mid-collapse, raising the target is enough — the extra blobs that were
    // about to fuse simply stop converging. Only spawn when we're actually short.
    if (this.blobs.length >= this.targetCount) {
      this.splitThisFrame = true;
      return true;
    }

    let big = this.blobs[0];
    for (const b of this.blobs) if (b.vol > big.vol) big = b;

    const half = big.vol / 2;
    big.vol = half;
    big.r = volumeToRadius(half);
    big.vx -= CFG.split.kick;

    const twin = newBlob(big.x + big.r * 0.5, big.y, half, big.r);
    twin.vx = big.vx + CFG.split.kick * 2;
    twin.vy = big.vy;
    twin.grace = big.grace;
    this.blobs.push(twin);

    // Both halves are stretched along the tear as they pull apart, so the
    // separation reads as something pinching off rather than one circle
    // becoming two. The smooth-union does the necking; this does the strain.
    const k = CFG.goo.splitKick;
    big.defX -= k;
    twin.defX += k;
    this.splitPos = { x: big.x, y: big.y };
    this.splitThisFrame = true;
    this.sortBySlot();
    return true;
  }

  /** Collapse everything back into a single mass. */
  mergeAll(): boolean {
    if (this.blobs.length <= 1 && this.targetCount <= 1) return false;
    this.targetCount = 1;
    return true;
  }

  /** Keep blobs ordered left-to-right so slot assignment never crosses over. */
  private sortBySlot() {
    this.blobs.sort((a, b) => a.x - b.x);
  }

  /**
   * Apply damage to one blob; returns the volume actually lost.
   * @param kx,ky Impact direction, used to splat the blob against the hit.
   */
  damage(b: Blob, fraction: number, kx = 0, ky = 1): number {
    if (b.grace > 0) return 0;
    const lost = Math.min(b.vol, b.vol * fraction + CFG.mass.start * CFG.damage.flat);
    b.vol -= lost;
    b.r = volumeToRadius(b.vol);
    b.grace = CFG.saw.invuln;

    const len = Math.hypot(kx, ky) || 1;
    b.defX += (kx / len) * CFG.goo.hitKick;
    b.defY += (ky / len) * CFG.goo.hitKick;
    return lost;
  }

  addVolume(v: number) {
    const total = Math.min(this.totalVolume + v, CFG.mass.max);
    this.redistribute(total);
  }

  private redistribute(total: number) {
    const each = total / this.blobs.length;
    for (const b of this.blobs) {
      b.vol = each;
      b.r = volumeToRadius(each);
    }
  }

  /**
   * @param lineVel  Speed the formation line is travelling at. Damping is taken
   *   relative to it, so the cluster settles *on* the line instead of trailing
   *   it by `damp * v / k` — a lag that grew to 16 world units by top speed and
   *   silently desynced the player's real position from the one the collision
   *   and targeting code reasons about.
   */
  update(dt: number, lineVel = 0) {
    const n = this.blobs.length;
    if (n > 1) this.sortBySlot();
    const total = this.totalVolume;
    const targetEach = total / n;

    // Blobs are laid out across `slots` positions. When there are more blobs
    // than slots the mapping doubles them up evenly, so a 6 -> 3 merge collapses
    // as three simultaneous pairs rather than everything piling onto the centre.
    const slots = Math.max(1, Math.min(this.targetCount, n));
    const merging = n > this.targetCount;

    for (let i = 0; i < n; i++) {
      const b = this.blobs[i];

      // Volumes ease toward equal shares so a split reads as a clean halving
      // even though impacts leave the cluster lopsided.
      const k = Math.min(1, dt * CFG.split.equaliseRate);
      b.vol += (targetEach - b.vol) * k;
      b.r = volumeToRadius(b.vol);
      if (b.grace > 0) b.grace -= dt;

      const slotIdx = slots <= 1 || n <= 1 ? 0 : Math.round((i * (slots - 1)) / (n - 1));
      const slotX = this.targetX + slotOffset(slotIdx, slots);
      // Staggering the spring per slot makes the formation ripple into place.
      const lag = 1 - i * CFG.steer.slotLag;
      const ax = (slotX - b.x) * CFG.steer.springX * lag - b.vx * CFG.steer.dampX;
      const ay = (this.lineY - b.y) * CFG.steer.springY - (b.vy - lineVel) * CFG.steer.dampY;

      b.vx += ax * dt;
      b.vy += ay * dt;

      if (merging) {
        // Extra inward pull so convergence feels magnetic rather than springy.
        b.vx += Math.sign(slotX - b.x) * CFG.merge.pull * dt;
      }

      // --- soft body ---------------------------------------------------
      // Target stretch follows motion relative to the channel. The spring is
      // underdamped, so the surface overshoots and settles with a wobble
      // instead of tracking the centre rigidly.
      const g = CFG.goo;
      const tx = b.vx * g.stretchPerSpeed;
      const ty = (b.vy - lineVel) * g.stretchPerSpeed;
      b.dvX += ((tx - b.defX) * g.spring - b.dvX * g.damp) * dt;
      b.dvY += ((ty - b.defY) * g.spring - b.dvY * g.damp) * dt;
      b.defX += b.dvX * dt;
      b.defY += b.dvY * dt;

      const dmag = Math.hypot(b.defX, b.defY);
      if (dmag > g.maxStretch) {
        const sc = g.maxStretch / dmag;
        b.defX *= sc;
        b.defY *= sc;
      }

      b.x += b.vx * dt;
      b.y += b.vy * dt;

      // Keep the cluster inside the channel walls.
      const lim = CFG.channelHalfW - b.r;
      if (b.x < -lim) {
        b.x = -lim;
        b.vx = Math.abs(b.vx) * 0.35;
      } else if (b.x > lim) {
        b.x = lim;
        b.vx = -Math.abs(b.vx) * 0.35;
      }
    }

    if (merging) this.resolveFusion();
  }

  private resolveFusion() {
    for (let i = 0; i < this.blobs.length; i++) {
      for (let j = i + 1; j < this.blobs.length; j++) {
        const a = this.blobs[i];
        const b = this.blobs[j];
        const dx = a.x - b.x;
        const dy = a.y - b.y;
        const reach = (a.r + b.r) * CFG.merge.fuseFactor;
        if (dx * dx + dy * dy <= reach * reach) {
          // Momentum-weighted fuse, then volume is conserved exactly.
          const wa = a.vol;
          const wb = b.vol;
          const w = wa + wb;
          a.x = (a.x * wa + b.x * wb) / w;
          a.y = (a.y * wa + b.y * wb) / w;
          a.vx = (a.vx * wa + b.vx * wb) / w;
          a.vy = (a.vy * wa + b.vy * wb) / w;
          a.vol = w;
          a.r = volumeToRadius(w);
          a.grace = Math.max(a.grace, b.grace);
          // The merged mass bulges along the axis the two came together on.
          const sep = Math.hypot(dx, dy) || 1;
          a.defX += (dx / sep) * CFG.goo.fuseKick;
          a.defY += (dy / sep) * CFG.goo.fuseKick;
          this.fusePos.push({ x: a.x, y: a.y });
          this.blobs.splice(j, 1);
          this.fusedThisFrame++;
          j--;
          // One fusion per pass keeps a 6 -> 3 collapse reading as three
          // distinct pops instead of a single indistinct blur.
          if (this.blobs.length <= this.targetCount) return;
        }
      }
    }
  }
}
