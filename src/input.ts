/**
 * Twin analog sticks. Left moves, right aims — and pushing the right stick past
 * the outer ring fires.
 *
 * The previous scheme had the right thumb hold-to-aim and release-to-fire,
 * which is self-contradictory: holding and tapping are the same gesture at
 * different speeds, so there was no way to line a shot up without taking it,
 * and no way to fire without aiming first.
 *
 * Distance solves it, because it is a second axis the thumb already controls:
 * nudge the stick and you are only turning, push it to the edge and you are
 * shooting. There is no mode to remember and nothing to reach for, and the
 * transition is continuous, so a player discovers firing by pushing harder —
 * which is what everyone tries first anyway.
 */

export interface Sticks {
  moveX: number;
  moveY: number;
  aimX: number;
  aimY: number;
  /** How far the aim stick is pushed, 0..1. */
  aimPower: number;
  /** True while the aim stick is past the fire threshold. */
  firing: boolean;
  /** Screen-space state for drawing the stick overlays. */
  visual: {
    move: { active: boolean; ox: number; oy: number; x: number; y: number };
    aim: { active: boolean; ox: number; oy: number; x: number; y: number };
  };
}

export interface InputHandlers {
  dash(): void;
  pull(): void;
  /** Any press while a menu is showing. Return true to swallow it. */
  confirm(): boolean;
}

const DEAD_ZONE = 6;
const MAX_RADIUS = 62;
/**
 * Fraction of the stick's travel at which it starts shooting. Set well short of
 * the rim: a thumb cannot reliably reach an exact edge, and a threshold you can
 * only hit by accident is worse than no threshold.
 */
const FIRE_AT = 0.7;

export const FIRE_THRESHOLD = FIRE_AT;

export function attachInput(target: HTMLElement, h: InputHandlers) {
  const state: Sticks = {
    moveX: 0,
    moveY: 0,
    aimX: 1,
    aimY: 0,
    aimPower: 0,
    firing: false,
    visual: {
      move: { active: false, ox: 0, oy: 0, x: 0, y: 0 },
      aim: { active: false, ox: 0, oy: 0, x: 0, y: 0 },
    },
  };

  let movePointer: number | null = null;
  let aimPointer: number | null = null;
  /** True once the player has used the aim stick at all. */
  let everAimed = false;

  const half = () => target.clientWidth / 2;

  const onDown = (e: PointerEvent) => {
    if (h.confirm()) return;
    try {
      // Throws for synthetic or already-released pointers; capture is a nicety
      // that keeps a drag alive off the edge of the element, never a
      // requirement, so failing to get it must not break input entirely.
      target.setPointerCapture?.(e.pointerId);
    } catch {
      /* not capturable */
    }
    const left = e.clientX < half();
    if (left && movePointer === null) {
      movePointer = e.pointerId;
      const v = state.visual.move;
      v.active = true;
      v.ox = v.x = e.clientX;
      v.oy = v.y = e.clientY;
    } else if (!left && aimPointer === null) {
      aimPointer = e.pointerId;
      const v = state.visual.aim;
      v.active = true;
      v.ox = v.x = e.clientX;
      v.oy = v.y = e.clientY;
    }
  };

  const onMove = (e: PointerEvent) => {
    if (e.pointerId === movePointer) {
      const v = state.visual.move;
      v.x = e.clientX;
      v.y = e.clientY;
      const [x, y] = vector(v.ox, v.oy, e.clientX, e.clientY);
      state.moveX = x;
      state.moveY = y;
    } else if (e.pointerId === aimPointer) {
      const v = state.visual.aim;
      v.x = e.clientX;
      v.y = e.clientY;
      const [x, y, len] = vector(v.ox, v.oy, e.clientX, e.clientY);
      state.aimPower = Math.min(1, len / MAX_RADIUS);
      if (len > DEAD_ZONE) {
        const m = Math.hypot(x, y) || 1;
        state.aimX = x / m;
        state.aimY = y / m;
        everAimed = true;
      }
    }
  };

  const release = (e: PointerEvent) => {
    if (e.pointerId === movePointer) {
      movePointer = null;
      state.moveX = 0;
      state.moveY = 0;
      state.visual.move.active = false;
    } else if (e.pointerId === aimPointer) {
      aimPointer = null;
      state.visual.aim.active = false;
      // Aim direction persists on release — you keep pointing where you left
      // off, so letting go stops the shooting without losing the aim.
      state.aimPower = 0;
    }
  };

  target.addEventListener('pointerdown', onDown);
  target.addEventListener('pointermove', onMove);
  target.addEventListener('pointerup', release);
  target.addEventListener('pointercancel', release);
  target.addEventListener('contextmenu', (e) => e.preventDefault());

  // --- desktop ------------------------------------------------------------
  const keys = new Set<string>();
  let mouseX = 0;
  let mouseY = 0;
  let usingMouse = false;

  window.addEventListener('keydown', (e) => {
    const k = e.key.toLowerCase();
    if (['w', 'a', 's', 'd', ' ', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright', 'e'].includes(k)) {
      e.preventDefault();
    }
    if (keys.has(k)) return;
    keys.add(k);
    if (k === ' ') {
      if (!h.confirm()) h.dash();
    }
    if (k === 'e' || k === 'shift') h.pull();
    if (k === 'enter') h.confirm();
  });
  window.addEventListener('keyup', (e) => keys.delete(e.key.toLowerCase()));
  window.addEventListener('mousemove', (e) => {
    usingMouse = true;
    mouseX = e.clientX;
    mouseY = e.clientY;
  });
  window.addEventListener('mousedown', (e) => {
    if (e.button !== 0) return;
    if (!h.confirm()) h.dash();
  });

  /** Folds keyboard/mouse into the same stick state, once per frame. */
  function sample(dt = 0): Sticks {
    const kx = (keys.has('d') || keys.has('arrowright') ? 1 : 0) - (keys.has('a') || keys.has('arrowleft') ? 1 : 0);
    const ky = (keys.has('s') || keys.has('arrowdown') ? 1 : 0) - (keys.has('w') || keys.has('arrowup') ? 1 : 0);
    if (kx || ky) {
      const m = Math.hypot(kx, ky) || 1;
      state.moveX = kx / m;
      // Screen y grows downward, world y grows upward.
      state.moveY = -ky / m;
    } else if (movePointer === null) {
      state.moveX = 0;
      state.moveY = 0;
    }

    if (usingMouse && aimPointer === null) {
      const dx = mouseX - target.clientWidth / 2;
      const dy = mouseY - target.clientHeight / 2;
      const m = Math.hypot(dx, dy);
      if (m > 1) {
        state.aimX = dx / m;
        state.aimY = -dy / m;
      }
    } else if (!everAimed && aimPointer === null) {
      // Until the aim stick is touched for the first time, point where you are
      // going. A brand new player is never left aiming at nothing.
      const mv = Math.hypot(state.moveX, state.moveY);
      if (mv > 0.15) {
        const k = Math.min(1, dt * 9);
        state.aimX += (state.moveX / mv - state.aimX) * k;
        state.aimY += (state.moveY / mv - state.aimY) * k;
        const am = Math.hypot(state.aimX, state.aimY) || 1;
        state.aimX /= am;
        state.aimY /= am;
      }
    }

    state.firing = state.aimPower >= FIRE_AT;
    return state;
  }

  return { sample, state };
}

/**
 * Screen delta to a world-space stick vector, clamped to the unit disc.
 * Screen y grows downward and world y grows upward, so y is negated here once
 * and never thought about again.
 */
function vector(ox: number, oy: number, x: number, y: number): [number, number, number] {
  const dx = x - ox;
  const dy = y - oy;
  const len = Math.hypot(dx, dy);
  if (len < DEAD_ZONE) return [0, 0, len];
  const k = Math.min(1, len / MAX_RADIUS) / len;
  return [dx * k, -dy * k, len];
}
