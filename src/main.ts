import { Game } from './game';
import { attachInput } from './input';
import { Renderer } from './renderer';
import { Ui } from './ui';

// When embedded, the host owns <head> and there may be no viewport meta — an
// unscaled phone would render this at desktop width. Ensure one exists.
if (!document.querySelector('meta[name="viewport"]')) {
  const m = document.createElement('meta');
  m.name = 'viewport';
  m.content = 'width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no, viewport-fit=cover';
  document.head.appendChild(m);
}

const canvas = document.getElementById('gl') as HTMLCanvasElement;

let renderer: Renderer;
try {
  renderer = new Renderer(canvas);
} catch (err) {
  document.getElementById('fatal')!.classList.add('show');
  document.getElementById('fatal-msg')!.textContent = String(
    err instanceof Error ? err.message : err,
  );
  throw err;
}

const game = new Game(renderer);
const ui = new Ui();

const keyboard = attachInput(canvas, {
  steer: (clientX) => {
    keyboard.syncKeyboardTo(clientX);
    game.steer(renderer.clientToWorldX(clientX));
  },
  split: () => game.doSplit(),
  merge: () => game.doMerge(),
  confirm: () => {
    if (game.state === 'title' || game.state === 'dead') {
      game.start();
      return true;
    }
    return false;
  },
});

let resizePending = false;
const onResize = () => {
  if (resizePending) return;
  resizePending = true;
  requestAnimationFrame(() => {
    resizePending = false;
    renderer.resize();
  });
};
window.addEventListener('resize', onResize);
window.addEventListener('orientationchange', onResize);

let last = performance.now();
function frame(now: number) {
  const dt = Math.min((now - last) / 1000, 0.05);
  const frameMs = now - last;
  last = now;

  keyboard.tickKeyboard(dt, window.innerWidth);
  game.update(dt);
  renderer.render(game.buildFrame(), frameMs);
  ui.update(game.hud());

  requestAnimationFrame(frame);
}

// Returning from a background tab must not teleport the world forward.
document.addEventListener('visibilitychange', () => {
  if (!document.hidden) last = performance.now();
});

// Test hook: the playtest harness drives and inspects the game through this.
(window as unknown as { __qs: unknown }).__qs = { game, renderer };

document.getElementById('boot')?.classList.add('gone');
requestAnimationFrame((t) => {
  last = t;
  frame(t);
});
