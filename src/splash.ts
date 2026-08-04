import { CFG } from './config';

export interface Drop {
  x: number;
  y: number;
  vx: number;
  vy: number;
  r0: number;
  r: number;
  life: number;
  maxLife: number;
  tint: number;
}

/**
 * Decorative droplets thrown off when the metal splits, fuses or takes a hit.
 *
 * These carry no gameplay weight at all — no mass, no collision. They exist
 * because real liquid never separates cleanly: a pinch-off sprays satellites,
 * and an impact throws a crown. Without them a split reads as one shape
 * becoming two shapes; with them it reads as something tearing.
 *
 * They render inside the same metaball field as the player, so they smoothly
 * re-absorb into the mass when they fall back into it.
 */
export class Splash {
  drops: Drop[] = [];

  clear() {
    this.drops.length = 0;
  }

  /**
   * @param spread  Half-angle of the spray, radians. π/2 is a full fan.
   * @param dir     Centre direction of the spray, radians.
   */
  burst(x: number, y: number, count: number, tint = 0, dir = 0, spread = Math.PI) {
    const { speed, radius, life } = CFG.goo.splash;
    for (let i = 0; i < count; i++) {
      const a = dir + (Math.random() * 2 - 1) * spread;
      const v = speed * (0.45 + Math.random() * 0.85);
      const r = radius * (0.5 + Math.random() * 0.8);
      this.drops.push({
        x,
        y,
        vx: Math.cos(a) * v,
        vy: Math.sin(a) * v,
        r0: r,
        r,
        life: life * (0.6 + Math.random() * 0.7),
        maxLife: life,
        tint,
      });
    }
  }

  /** @param lineVel Speed of the channel, so droplets travel with the world. */
  update(dt: number, lineVel: number) {
    const drag = CFG.goo.splash.drag;
    for (let i = this.drops.length - 1; i >= 0; i--) {
      const d = this.drops[i];
      d.life -= dt;
      if (d.life <= 0) {
        this.drops.splice(i, 1);
        continue;
      }
      const k = Math.exp(-drag * dt);
      d.vx *= k;
      d.vy *= k;
      d.x += d.vx * dt;
      d.y += (d.vy + lineVel) * dt;
      // Shrink on a curve so droplets hold their size then vanish quickly,
      // rather than fading out linearly the whole way.
      const t = Math.max(0, d.life / d.maxLife);
      d.r = d.r0 * Math.sqrt(t);
    }
  }
}
