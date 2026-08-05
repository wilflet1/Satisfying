import { CFG } from './config.ts';
import { attachInput } from './input.ts';
import { NetClient, type View, type ViewBlob } from './net/client.ts';
import { LocalGame } from './net/local.ts';
import { Renderer, type RenderFrame } from './renderer.ts';
import { MAX_BLOBS } from './shaders/index.ts';
import { ARENA } from './sim/config.ts';
import { Splash } from './splash.ts';

// When embedded, the host owns <head> and there may be no viewport meta — an
// unscaled phone would render this at desktop width.
if (!document.querySelector('meta[name="viewport"]')) {
  const m = document.createElement('meta');
  m.name = 'viewport';
  m.content = 'width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no, viewport-fit=cover';
  document.head.appendChild(m);
}

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const canvas = $<HTMLCanvasElement>('gl');

let renderer: Renderer;
try {
  renderer = new Renderer(canvas);
} catch (err) {
  $('fatal').classList.add('show');
  $('fatal-msg').textContent = String(err instanceof Error ? err.message : err);
  throw err;
}

// --- connection ------------------------------------------------------------

/**
 * Server URL. Defaults to the origin this page was served from, so a single
 * deploy needs no configuration; `?server=` overrides it for pointing a locally
 * built client at a remote arena.
 */
function serverUrl(): string {
  const q = new URLSearchParams(location.search);
  const override = q.get('server');
  if (override) return override;
  const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const room = q.get('room');
  return `${proto}//${location.host}/ws${room ? `?room=${encodeURIComponent(room)}` : ''}`;
}

/**
 * Offline when asked for explicitly, and also when the page has no server to
 * talk to — a sandboxed embed forbids WebSockets outright, and a file:// build
 * has no origin to connect back to. Falling back means the game always plays.
 */
function wantsOffline(): boolean {
  const q = new URLSearchParams(location.search);
  if (q.get('offline') === '1') return true;
  if (q.get('server')) return false;
  // No origin to dial back to (file://), or embedded in a frame whose content
  // policy forbids sockets outright. Detecting it up front is worth the small
  // false-positive risk: the alternative is every embedded player staring at
  // "connecting" until a timeout they have no way to understand.
  if (location.protocol !== 'http:' && location.protocol !== 'https:') return true;
  try {
    if (window.self !== window.top) return true;
  } catch {
    return true; // cross-origin frame — reading .top threw
  }
  return false;
}

let net: NetClient | LocalGame = wantsOffline() ? new LocalGame() : new NetClient();
const offline = net instanceof LocalGame;
const splash = new Splash();

let playing = false;
let name = localStorage.getItem('blob.name') ?? '';

const nameInput = $<HTMLInputElement>('name');
nameInput.value = name;

function startGame() {
  name = (nameInput.value || 'Blob').slice(0, 14);
  try {
    localStorage.setItem('blob.name', name);
  } catch {
    /* private browsing */
  }
  $('menu').classList.remove('show');
  playing = true;
  net.connect(serverUrl(), name);

  // A server that never answers should drop the player into a bot game rather
  // than leaving them staring at "connecting" forever.
  if (!offline) {
    const watch = setInterval(() => {
      if (!playing) return clearInterval(watch);
      // A refused or closed socket is decisive — don't sit on it.
      if (net.status === 'error' || net.status === 'closed') {
        clearInterval(watch);
        fallbackOffline();
      }
    }, 250);
    setTimeout(() => {
      clearInterval(watch);
      if (playing && net.status !== 'live') fallbackOffline();
    }, 6000);
  }
}

let fellBack = false;
function fallbackOffline() {
  if (fellBack) return;
  fellBack = true;
  net.disconnect();
  // Every call site reads the `net` binding at call time, so swapping the
  // implementation here is enough — no listeners need rebinding.
  const local = new LocalGame();
  local.connect('', name);
  net = local;
}

$('play').addEventListener('click', startGame);
nameInput.addEventListener('keydown', (e) => {
  if (e.key === 'Enter') startGame();
});

const input = attachInput(canvas, {
  dash: () => net.dash(),
  pull: () => net.pull(),
  confirm: () => !playing,
});

$('pull-btn').addEventListener('pointerdown', (e) => {
  e.preventDefault();
  e.stopPropagation();
  net.pull();
});

// --- camera + presentation --------------------------------------------------

let camX = 0;
let camY = 0;
let viewW = 220;
let trauma = 0;
let chroma = 0;
let flash = 0;
let flashCol: [number, number, number] = [1, 1, 1];
let pulse = 0;
let pulseX = 0;
let pulseY = 0;
let time = 0;
let aimGuide: [number, number, number, number] = [0, 0, 0, 0];
let shield: [number, number, number, number] = [0, 0, 0, 0];

const blobBuf = new Float32Array(MAX_BLOBS * 4);
const defBuf = new Float32Array(MAX_BLOBS * 4);

/** Deformation state per rendered body, keyed by id — purely local garnish. */
const wobble = new Map<string, { px: number; py: number; dx: number; dy: number; vx: number; vy: number }>();

function deform(id: string, x: number, y: number, dt: number): [number, number] {
  let w = wobble.get(id);
  if (!w) {
    w = { px: x, py: y, dx: 0, dy: 0, vx: 0, vy: 0 };
    wobble.set(id, w);
  }
  const g = CFG.goo;
  // Velocity is derived from rendered motion, so interpolated remote bodies
  // squash and stretch exactly like the locally predicted one.
  const vx = dt > 0 ? (x - w.px) / dt : 0;
  const vy = dt > 0 ? (y - w.py) / dt : 0;
  w.px = x;
  w.py = y;

  const tx = vx * g.stretchPerSpeed;
  const ty = vy * g.stretchPerSpeed;
  w.vx += ((tx - w.dx) * g.spring - w.vx * g.damp) * dt;
  w.vy += ((ty - w.dy) * g.spring - w.vy * g.damp) * dt;
  w.dx += w.vx * dt;
  w.dy += w.vy * dt;
  const m = Math.hypot(w.dx, w.dy);
  if (m > g.maxStretch) {
    w.dx *= g.maxStretch / m;
    w.dy *= g.maxStretch / m;
  }
  return [w.dx, w.dy];
}

let n = 0;
let maxR = 0;
function put(b: ViewBlob, dt: number) {
  if (n >= MAX_BLOBS) return;
  const o = n * 4;
  blobBuf[o] = b.x;
  blobBuf[o + 1] = b.y;
  blobBuf[o + 2] = b.r;
  blobBuf[o + 3] = b.hue;
  const [dx, dy] = deform(b.id, b.x, b.y, dt);
  const mag = Math.hypot(dx, dy);
  defBuf[o] = mag;
  defBuf[o + 1] = mag > 1e-4 ? dx / mag : 1;
  defBuf[o + 2] = mag > 1e-4 ? dy / mag : 0;
  defBuf[o + 3] = 0;
  maxR = Math.max(maxR, b.r);
  n++;
}

function buildFrame(view: View | null, dt: number): RenderFrame {
  n = 0;
  maxR = 0;
  blobBuf.fill(0);
  defBuf.fill(0);

  if (view) {
    // Own blob first so it is never the one culled by the uniform budget.
    if (view.me) put(view.me, dt);
    for (const p of view.players) put(p, dt);
    for (const c of view.chunks) put(c, dt);

    // Pellets are the most numerous and least important: sort by distance to
    // the camera so the ones the player can actually see survive the cull.
    const pellets = view.pellets
      .map((g) => ({ g, d: (g.x - camX) ** 2 + (g.y - camY) ** 2 }))
      .sort((a, b) => a.d - b.d);
    for (const { g } of pellets) put(g, dt);
    for (const d of splash.drops) {
      if (d.r < 0.05) continue;
      put({ id: `s${d.r}${d.x | 0}`, x: d.x, y: d.y, r: d.r, hue: d.tint }, dt);
    }
  }

  const sh = trauma * trauma;
  const a = time * 47;
  const warn = view ? 1 - (view.ring - ARENA.ring.endRadius) / (ARENA.ring.startRadius - ARENA.ring.endRadius) : 0;

  return {
    time,
    camX,
    camY,
    viewW,
    ring: view?.ring ?? ARENA.ring.startRadius,
    ringWarn: Math.max(0, Math.min(1, warn)),
    blobCount: n,
    blobs: blobBuf,
    blobDef: defBuf,
    smoothK: Math.max(CFG.goo.smoothMin, Math.min(CFG.goo.smoothMax, maxR * CFG.goo.smoothScale)),
    aim: aimGuide,
    shield,
    pulse,
    pulseX,
    pulseY,
    chroma,
    flash,
    flashCol,
    shakeX: Math.sin(a * 1.7) * sh * 0.012,
    shakeY: Math.cos(a * 2.3) * sh * 0.012,
  };
}

// --- HUD --------------------------------------------------------------------

function updateHud(view: View | null) {
  $('status').textContent =
    offline || fellBack
      ? 'offline · vs bots'
      : net.status === 'live'
        ? `${net.ping} ms`
        : net.status === 'connecting'
          ? 'connecting…'
          : net.status;
  if (!view) return;

  $('timer').textContent = view.phase === 'live' ? `${Math.max(0, Math.ceil(view.timer))}` : '';

  // First-run coaching: shown while spawn-protected, which is exactly the
  // window where the player is safe enough to read it.
  const coach = $('coach');
  coach.classList.toggle('show', view.protect > 0.2 && view.alive);
  $('mass').textContent = view.alive ? `${Math.round(view.mass)}` : '—';
  $('kills').textContent = `${view.kills}`;

  const board = net.roster
    .slice()
    .sort((a, b) => a.hue - b.hue)
    .map((r) => {
      const alive = view.players.some((p) => p.id === r.id) || (view.me && r.id === net.id);
      return `<li class="${alive ? '' : 'out'}"><i style="--h:${r.hue}"></i>${escapeHtml(r.name)}${r.bot ? ' <small>bot</small>' : ''}</li>`;
    })
    .join('');
  $('board').innerHTML = board;

  const banner = $('banner');
  if (view.phase === 'countdown') {
    banner.textContent = `${Math.max(1, Math.ceil(view.timer))}`;
    banner.classList.add('show');
  } else if (view.phase === 'over') {
    const w = net.roster.find((r) => r.id === view.winner);
    banner.textContent = view.winner === net.id ? 'YOU WIN' : w ? `${w.name} wins` : 'round over';
    banner.classList.add('show');
  } else if (!view.alive && view.phase === 'live') {
    // Death is temporary now, so the banner counts you back in rather than
    // telling you it's over.
    banner.textContent = `back in ${Math.max(1, Math.ceil(view.respawnIn))}`;
    banner.classList.add('show');
  } else {
    banner.classList.remove('show');
  }
}

const escapeHtml = (s: string) =>
  s.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);

// --- loop -------------------------------------------------------------------

let last = performance.now();
let sendAccum = 0;

function frame(now: number) {
  const frameMs = now - last;
  const dt = Math.min(frameMs / 1000, 0.05);
  last = now;
  time += dt;

  const sticks = input.sample();
  const view = playing ? net.view() : null;

  if (playing && net.status === 'live') {
    net.predict(dt, sticks.moveX, sticks.moveY);
    // Input goes out at the server tick rate, not the frame rate: sending at
    // 120 Hz would triple upstream traffic for no extra authority.
    sendAccum += dt;
    if (sendAccum >= 1 / ARENA.tickRate) {
      sendAccum = 0;
      net.sendInput(sticks.moveX, sticks.moveY, sticks.aimX, sticks.aimY);
    }
  }

  if (view) {
    // Aim guide, so the player can see where a shot will actually go. Hidden
    // while dead or during the results screen.
    if (view.me && view.phase !== 'over') {
      const m = Math.hypot(sticks.aimX, sticks.aimY) || 1;
      aimGuide = [view.me.x, view.me.y, sticks.aimX / m, sticks.aimY / m];
      shield = [view.me.x, view.me.y, view.me.r + 5, view.protect > 0 ? 1 : 0];
    } else {
      aimGuide = [0, 0, 0, 0];
      shield = [0, 0, 0, 0];
    }

    // Camera follows the player, zooming out as they grow so a big blob can
    // still see what is about to hit it.
    const target = view.me ?? { x: camX, y: camY, r: 10 };
    const k = 1 - Math.exp(-6 * dt);
    camX += (target.x - camX) * k;
    camY += (target.y - camY) * k;
    const want = 190 + (view.me ? view.me.r * 6 : 40);
    viewW += (want - viewW) * (1 - Math.exp(-1.5 * dt));

    for (const e of view.dashes) splash.burst(e.x, e.y, 3, e.hue);
    for (const e of view.hits) {
      splash.burst(e.x, e.y, CFG.goo.splash.onHit, e.hue);
      trauma = Math.min(1, trauma + 0.3 + e.power * 0.5);
      chroma = Math.max(chroma, 1);
      pulse = 1;
      pulseX = e.x;
      pulseY = e.y;
    }
    for (const e of view.deaths) {
      splash.burst(e.x, e.y, 14, e.hue);
      trauma = Math.min(1, trauma + 0.7);
      flash = 0.5;
      flashCol = [1, 0.85, 0.6];
      pulse = 1;
      pulseX = e.x;
      pulseY = e.y;
    }
  }

  splash.update(dt, 0);
  trauma = Math.max(0, trauma - dt * 1.1);
  chroma = Math.max(0, chroma - dt * 2.6);
  flash = Math.max(0, flash - dt * 4);
  pulse = Math.max(0, pulse - dt * 2.2);

  renderer.render(buildFrame(view, dt), frameMs);
  updateHud(view);
  drawSticks(sticks);

  requestAnimationFrame(frame);
}

// --- stick overlay ----------------------------------------------------------

const stickEls = {
  moveBase: $('s-move-base'),
  moveNub: $('s-move-nub'),
  aimBase: $('s-aim-base'),
  aimNub: $('s-aim-nub'),
};

function drawSticks(s: ReturnType<typeof input.sample>) {
  place(stickEls.moveBase, stickEls.moveNub, s.visual.move);
  place(stickEls.aimBase, stickEls.aimNub, s.visual.aim);
}

function place(
  base: HTMLElement,
  nub: HTMLElement,
  v: { active: boolean; ox: number; oy: number; x: number; y: number },
) {
  base.style.opacity = v.active ? '1' : '0';
  nub.style.opacity = v.active ? '1' : '0';
  if (!v.active) return;
  base.style.transform = `translate(${v.ox}px, ${v.oy}px)`;
  const dx = v.x - v.ox;
  const dy = v.y - v.oy;
  const len = Math.hypot(dx, dy);
  const k = len > 62 ? 62 / len : 1;
  nub.style.transform = `translate(${v.ox + dx * k}px, ${v.oy + dy * k}px)`;
}

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
document.addEventListener('visibilitychange', () => {
  if (!document.hidden) last = performance.now();
});

(window as unknown as { __bb: unknown }).__bb = { net, renderer, get playing() { return playing; }, startGame };

$('boot')?.classList.add('gone');
$('menu').classList.add('show');
requestAnimationFrame((t) => {
  last = t;
  frame(t);
});
