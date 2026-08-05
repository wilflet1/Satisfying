/**
 * Twin floating sticks.
 *
 * Left half of the screen moves, right half aims and fires. Both sticks appear
 * wherever the thumb lands rather than sitting in a fixed corner — fixed sticks
 * demand the player look down to find them, and in a game decided by half a
 * second of positioning that is the difference between playing and fumbling.
 *
 * Firing happens on *release* of the aim stick, so you can line a shot up and
 * hold it. A quick tap fires immediately along your existing aim.
 */

export interface Sticks {
  moveX: number;
  moveY: number;
  aimX: number;
  aimY: number;
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
const TAP_MS = 220;

export function attachInput(target: HTMLElement, h: InputHandlers) {
  const state: Sticks = {
    moveX: 0,
    moveY: 0,
    aimX: 1,
    aimY: 0,
    visual: {
      move: { active: false, ox: 0, oy: 0, x: 0, y: 0 },
      aim: { active: false, ox: 0, oy: 0, x: 0, y: 0 },
    },
  };

  let movePointer: number | null = null;
  let aimPointer: number | null = null;
  let aimDownAt = 0;
  let aimMoved = false;

  const half = () => target.clientWidth / 2;

  const onDown = (e: PointerEvent) => {
    if (h.confirm()) return;
    target.setPointerCapture?.(e.pointerId);
    const left = e.clientX < half();
    if (left && movePointer === null) {
      movePointer = e.pointerId;
      const v = state.visual.move;
      v.active = true;
      v.ox = v.x = e.clientX;
      v.oy = v.y = e.clientY;
    } else if (!left && aimPointer === null) {
      aimPointer = e.pointerId;
      aimDownAt = performance.now();
      aimMoved = false;
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
      if (len > DEAD_ZONE) {
        aimMoved = true;
        const m = Math.hypot(x, y) || 1;
        state.aimX = x / m;
        state.aimY = y / m;
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
      const quick = performance.now() - aimDownAt < TAP_MS;
      // Either a deliberate aimed shot or a snap tap along the current aim.
      if (aimMoved || quick) h.dash();
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
  function sample(): Sticks {
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
    }
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
