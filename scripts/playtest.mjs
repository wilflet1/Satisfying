/**
 * Headless playtest. Two passes, because they answer different questions and
 * conflating them makes both useless:
 *
 *   1. LOGIC SOAK — steps the simulation at a fixed dt with rendering out of the
 *      loop, so a software rasteriser's frame rate can't distort the result.
 *      A scripted "good player" bot must survive; a do-nothing bot must die.
 *      That pair is the difficulty curve having teeth in both directions.
 *   2. RENDER SMOKE — a handful of real GPU frames, to catch shader compile
 *      failures, GL errors, and a black screen, plus reference screenshots.
 *
 *   node scripts/playtest.mjs [--url http://...] [--shots dir] [--seed N]
 */
import { chromium } from 'playwright';
import { existsSync, mkdirSync, readdirSync } from 'node:fs';
import { argv } from 'node:process';

const arg = (name, fallback) => {
  const i = argv.indexOf(`--${name}`);
  return i >= 0 ? argv[i + 1] : fallback;
};

const URL_BASE = arg('url', 'http://localhost:4173');
const SHOTS = arg('shots', 'playtest');
const SEED = Number(arg('seed', '20260804'));

/**
 * Prefer the Chromium already installed in the environment over whatever build
 * this playwright version pins — the two drift apart and downloading a second
 * copy is a slow way to get the same browser.
 */
function findChromium() {
  const root = process.env.PLAYWRIGHT_BROWSERS_PATH || '/opt/pw-browsers';
  if (!existsSync(root)) return undefined;
  const dir = readdirSync(root).find((d) => /^chromium-\d+$/.test(d));
  const bin = dir && `${root}/${dir}/chrome-linux/chrome`;
  return bin && existsSync(bin) ? bin : undefined;
}

mkdirSync(SHOTS, { recursive: true });

const browser = await chromium.launch({
  executablePath: findChromium(),
  args: [
    '--use-gl=angle',
    '--use-angle=swiftshader',
    '--enable-unsafe-swiftshader',
    '--ignore-gpu-blocklist',
  ],
});

// A tall phone viewport — the shape the game is designed for. DPR 1 because
// SwiftShader is fill-rate bound and this pass isn't measuring image quality.
const page = await browser.newPage({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1 });

const errors = [];
page.on('console', (m) => {
  if (m.type() === 'error') errors.push(m.text());
});
page.on('pageerror', (e) => errors.push(String(e)));

await page.goto(`${URL_BASE}/?seed=${SEED}`, { waitUntil: 'load' });
await page.waitForFunction(() => !!window.__qs, null, { timeout: 20000 });
await page.waitForTimeout(400);

const gpu = await page.evaluate(() => {
  const gl = window.__qs.renderer.gl;
  const dbg = gl.getExtension('WEBGL_debug_renderer_info');
  return dbg ? gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
});
console.log(`renderer: ${gpu}\n`);

// ---------------------------------------------------------------------------
// The bot. Injected once and reused by every soak run.
// ---------------------------------------------------------------------------
await page.addInitScript(() => {});
await page.evaluate(() => {
  /** Plays the channel the way the design intends. `skill` 0 = idle, 1 = good. */
  window.__bot = function bot(game, skill) {
    if (skill === 0) return;
    const line = game.lineY;
    // Look ahead by time, not distance — the channel triples in speed.
    const lookahead = game.speed * 2.4;

    // A gate is not "behind you" when its centre crosses the line — it is still
    // physically intersecting the formation for another (thickness + radius).
    // Releasing it early yanks the blobs out of the slit on the final frame.
    const r = game.blobs.blobs[0] ? game.blobs.blobs[0].r : 7;
    const clear = 4 + r + 3.6;

    let gate = null;
    for (const o of game.world.obstacles) {
      if (o.kind === 'saw' || o.y < line - clear) continue;
      if (!gate || o.y < gate.y) gate = o;
    }

    // Count is managed on the gate's schedule; steering is moment-to-moment.
    // Conflating the two makes the bot fly straight through blades it is
    // already inside, which no human would do.
    if (gate && gate.y - line < lookahead) {
      const need = gate.kind === 'door' ? 1 : gate.need;
      if (game.blobs.count > need) game.doMerge();
      else if (game.blobs.count < need) game.doSplit();
    }

    const blades = game.world.obstacles.filter(
      (o) => o.kind === 'saw' && o.y > line - 18 && o.y < line + game.speed * 1.1,
    );
    // Hold the gate line until it has fully cleared, blades or not.
    const gateClose = gate && gate.y - line < game.speed * 0.75;

    if (blades.length && !gateClose) {
      const half = ((game.blobs.count - 1) * 12) / 2;
      let bestX = 0;
      let bestScore = -1e9;
      const gateC =
        gate && gate.kind === 'slit'
          ? gate.slits.reduce((a, b) => a + b, 0) / gate.slits.length
          : 0;
      for (let x = -46 + half; x <= 46 - half; x += 2) {
        let clear = 1e9;
        for (const s of blades) {
          for (let i = 0; i < game.blobs.count; i++) {
            const bx = x + (i - (game.blobs.count - 1) / 2) * 12;
            clear = Math.min(clear, Math.abs(s.x - bx));
          }
        }
        // Prefer safety, but drift toward the gate when safety is equal.
        const score = Math.min(clear, 22) * 10 - Math.abs(x - gateC);
        if (score > bestScore) {
          bestScore = score;
          bestX = x;
        }
      }
      game.steer(bestX);
      return;
    }

    if (gate && gate.y - line < lookahead) {
      game.steer(
        gate.kind === 'door' ? 0 : gate.slits.reduce((a, b) => a + b, 0) / gate.slits.length,
      );
      return;
    }

    let bestDrop = null;
    for (const d of game.world.droplets) {
      if (d.taken || d.y < line) continue;
      if (!bestDrop || d.y < bestDrop.y) bestDrop = d;
    }
    game.steer(bestDrop ? bestDrop.x : 0);
  };

  /** Runs `seconds` of game time at a fixed dt with rendering disabled. */
  window.__soak = function soak(seconds, skill, seed) {
    const { game } = window.__qs;
    game.start();
    if (seed) game.world.reset(seed);
    const dt = 1 / 60;
    const steps = Math.round(seconds / dt);
    const r = {
      died: false,
      diedAt: null,
      maxCount: 1,
      minMass: 1,
      nan: false,
      maxCombo: 0,
    };
    for (let i = 0; i < steps; i++) {
      window.__bot(game, skill);
      game.update(dt);
      const h = game.hud();
      r.maxCount = Math.max(r.maxCount, game.blobs.count);
      r.maxCombo = Math.max(r.maxCombo, h.combo);
      r.minMass = Math.min(r.minMass, h.massFrac);
      if (!Number.isFinite(game.camY) || !Number.isFinite(game.score)) r.nan = true;
      for (const b of game.blobs.blobs) {
        if (!Number.isFinite(b.x + b.y + b.vol + b.r)) r.nan = true;
      }
      if (game.state === 'dead') {
        r.died = true;
        r.diedAt = +(i * dt).toFixed(1);
        break;
      }
    }
    Object.assign(r, game.stats);
    r.score = game.hud().score;
    r.seconds = r.diedAt ?? seconds;
    return r;
  };
});

// ---------------------------------------------------------------------------
// Pass 1 — logic soak
// ---------------------------------------------------------------------------
const skilled = [];
for (const seed of [SEED, SEED + 7, SEED + 91]) {
  skilled.push(await page.evaluate((s) => window.__soak(90, 1, s), seed));
}
const idle = await page.evaluate((s) => window.__soak(60, 0, s), SEED);

console.log('skilled bot, 90s runs:');
for (const r of skilled) console.log(`  ${JSON.stringify(r)}`);
console.log(`idle bot, 60s run:\n  ${JSON.stringify(idle)}\n`);

// ---------------------------------------------------------------------------
// Pass 2 — render smoke + reference screenshots
// ---------------------------------------------------------------------------
await page.evaluate(() => window.__qs.game.start());
await page.evaluate(() => {
  const { game } = window.__qs;
  for (let i = 0; i < 240; i++) {
    window.__bot(game, 1);
    game.update(1 / 60);
  }
});
await page.waitForTimeout(500);
await page.screenshot({ path: `${SHOTS}/01-run.png` });

await page.evaluate(() => {
  const { game } = window.__qs;
  game.blobs.targetX = 0;
  while (game.blobs.count < 5) game.doSplit();
});
await page.waitForTimeout(350);
await page.screenshot({ path: `${SHOTS}/02-split.png` });

await page.evaluate(() => window.__qs.game.doMerge());
await page.waitForTimeout(160);
await page.screenshot({ path: `${SHOTS}/03-merging.png` });

await page.waitForTimeout(500);
await page.screenshot({ path: `${SHOTS}/04-merged.png` });

const render = await page.evaluate(async () => {
  const { renderer } = window.__qs;
  const gl = renderer.gl;
  const t0 = performance.now();
  let frames = 0;
  await new Promise((done) => {
    const step = () => {
      frames++;
      if (performance.now() - t0 > 2000) return done();
      requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  });
  // Sample the centre of the framebuffer to prove something was actually drawn.
  const px = new Uint8Array(4);
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  gl.readPixels(
    (gl.drawingBufferWidth / 2) | 0,
    (gl.drawingBufferHeight / 2) | 0,
    1,
    1,
    gl.RGBA,
    gl.UNSIGNED_BYTE,
    px,
  );
  return {
    fps: +(frames / ((performance.now() - t0) / 1000)).toFixed(1),
    glError: gl.getError(),
    centrePixel: [px[0], px[1], px[2]],
  };
});
console.log(`render smoke: ${JSON.stringify(render)}\n`);

await browser.close();

// ---------------------------------------------------------------------------
// Verdict
// ---------------------------------------------------------------------------
const fails = [];
const note = [];

if (errors.length) fails.push(`console errors:\n    ${errors.join('\n    ')}`);
if (render.glError !== 0) fails.push(`GL error ${render.glError}`);
if (render.centrePixel.every((c) => c === 0)) fails.push('centre pixel is pure black — nothing drew');
if (skilled.some((r) => r.nan) || idle.nan) fails.push('NaN leaked into the simulation');

const survived = skilled.filter((r) => !r.died).length;
if (survived === 0) fails.push('skilled bot died on every seed — the channel is unsurvivable');
if (!skilled.some((r) => r.maxCount >= 4)) {
  fails.push('no run ever needed 4+ blobs — slit gates are not exercising the split mechanic');
}
const passRate = (r) => r.gatePassed / Math.max(1, r.gatePassed + r.gateHit + r.doorFail);
if (!skilled.some((r) => passRate(r) > 0.6)) {
  fails.push(
    `skilled bot clears only ${skilled.map((r) => (passRate(r) * 100).toFixed(0) + '%').join('/')} of gates — either the gates are unfair or the count-change window is too short`,
  );
}
if (!idle.died) fails.push('idle bot survived 60s — the game has no fail pressure');

// Not failures, but the numbers a design pass should look at.
note.push(`skilled survival: ${survived}/${skilled.length} seeds`);
note.push(`skilled scores: ${skilled.map((r) => r.score).join(', ')} m`);
note.push(`gate pass rate: ${skilled.map((r) => (passRate(r) * 100).toFixed(0) + '%').join(', ')}`);
note.push(
  `hits per run — gate ${skilled.map((r) => r.gateHit).join('/')}, saw ${skilled.map((r) => r.sawHit).join('/')}, door-fail ${skilled.map((r) => r.doorFail).join('/')}`,
);
note.push(`idle bot died at ${idle.diedAt}s`);
note.push(`SwiftShader fps: ${render.fps} (software raster — not a device estimate)`);

console.log(note.map((n) => `· ${n}`).join('\n'));

if (fails.length) {
  console.error(`\nFAIL\n- ${fails.join('\n- ')}`);
  process.exit(1);
}
console.log('\nPASS');
