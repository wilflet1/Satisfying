/**
 * One-thumb control mapping.
 *
 * Steering is *absolute*: where your thumb is across the screen is where the
 * formation goes. Relative dragging tested worse — it costs a frame of
 * re-orientation every time you lift, and gate alignment is unforgiving enough
 * without it. The formation sits high on screen so the thumb never covers it.
 *
 *   drag        → steer
 *   tap         → split (one more blob)
 *   swipe up    → merge everything back into one
 */

export interface InputHandlers {
  steer(clientX: number): void;
  split(): void;
  merge(): void;
  /** Any press while on the title or death screen. */
  confirm(): boolean;
}

const TAP_MS = 260;
const TAP_SLOP = 14;
const SWIPE_UP = 45;

export function attachInput(target: HTMLElement, h: InputHandlers) {
  let downX = 0;
  let downY = 0;
  let downT = 0;
  let active = false;
  let swiped = false;
  let consumedByConfirm = false;

  const onDown = (e: PointerEvent) => {
    target.setPointerCapture?.(e.pointerId);
    active = true;
    swiped = false;
    downX = e.clientX;
    downY = e.clientY;
    downT = performance.now();
    // A press that starts a run must not also register as a split.
    consumedByConfirm = h.confirm();
    if (!consumedByConfirm) h.steer(e.clientX);
  };

  const onMove = (e: PointerEvent) => {
    if (e.pointerType !== 'touch' || active) h.steer(e.clientX);
    if (!active || swiped || consumedByConfirm) return;
    // Fire the merge the instant the gesture is unambiguous rather than
    // waiting for pointerup — at 100 units/sec, that delay is a hit.
    if (downY - e.clientY > SWIPE_UP && Math.abs(e.clientX - downX) < SWIPE_UP * 1.6) {
      swiped = true;
      h.merge();
    }
  };

  const onUp = (e: PointerEvent) => {
    if (!active) return;
    active = false;
    if (consumedByConfirm || swiped) return;
    const dt = performance.now() - downT;
    const moved = Math.hypot(e.clientX - downX, e.clientY - downY);
    if (dt < TAP_MS && moved < TAP_SLOP) h.split();
  };

  target.addEventListener('pointerdown', onDown);
  target.addEventListener('pointermove', onMove);
  target.addEventListener('pointerup', onUp);
  target.addEventListener('pointercancel', () => {
    active = false;
  });
  target.addEventListener('contextmenu', (e) => e.preventDefault());

  // Keyboard fallback for desktop playtesting.
  const keys = new Set<string>();
  let keyX = 0;
  window.addEventListener('keydown', (e) => {
    const k = e.key.toLowerCase();
    if (['arrowleft', 'arrowright', 'a', 'd', ' ', 'arrowup', 'w', 's', 'arrowdown'].includes(k)) {
      e.preventDefault();
    }
    if (keys.has(k)) return;
    keys.add(k);
    if (k === ' ' || k === 'enter') {
      if (!h.confirm()) h.split();
    }
    if (k === 's' || k === 'arrowdown' || k === 'w' || k === 'arrowup') h.merge();
  });
  window.addEventListener('keyup', (e) => keys.delete(e.key.toLowerCase()));

  /** Called once per frame so held arrow keys glide instead of teleporting. */
  function tickKeyboard(dt: number, width: number) {
    const left = keys.has('arrowleft') || keys.has('a');
    const right = keys.has('arrowright') || keys.has('d');
    if (!left && !right) return;
    keyX += (Number(right) - Number(left)) * dt * width * 0.9;
    keyX = Math.max(0, Math.min(width, keyX));
    h.steer(keyX);
  }

  /** Keeps the keyboard cursor in sync when the pointer takes over. */
  function syncKeyboardTo(clientX: number) {
    keyX = clientX;
  }

  return { tickKeyboard, syncKeyboardTo };
}
