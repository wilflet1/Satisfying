/**
 * Browser end-to-end test. Boots the real server, opens two real browser
 * clients into the same arena, plays them, and checks that each renders and can
 * see the other. This is the only test that covers the whole stack at once —
 * shader compile, netcode, interpolation, input and HUD — so it is the one that
 * proves the thing actually works rather than merely type-checks.
 *
 * Run: node --experimental-strip-types scripts/browser-test.mjs
 */
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync } from 'node:fs';

const PORT = 8898;
const SHOTS = process.argv.includes('--shots')
  ? process.argv[process.argv.indexOf('--shots') + 1]
  : 'playtest';
mkdirSync(SHOTS, { recursive: true });

function findChromium() {
  const root = process.env.PLAYWRIGHT_BROWSERS_PATH || '/opt/pw-browsers';
  if (!existsSync(root)) return undefined;
  const dir = readdirSync(root).find((d) => /^chromium-\d+$/.test(d));
  const bin = dir && `${root}/${dir}/chrome-linux/chrome`;
  return bin && existsSync(bin) ? bin : undefined;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = [];

const server = spawn(
  process.execPath,
  ['--experimental-strip-types', 'server/node.ts', '--port', String(PORT)],
  { stdio: ['ignore', 'pipe', 'pipe'] },
);
const serverErr = [];
server.stderr.on('data', (d) => serverErr.push(String(d)));

for (let i = 0; i < 60; i++) {
  try {
    if ((await fetch(`http://127.0.0.1:${PORT}/healthz`)).ok) break;
  } catch { /* not up */ }
  await sleep(150);
}

const browser = await chromium.launch({
  executablePath: findChromium(),
  args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'],
});

async function openClient(name) {
  const page = await browser.newPage({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1 });
  const errors = [];
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`${name}: ${m.text()}`); });
  page.on('pageerror', (e) => errors.push(`${name}: ${e}`));
  await page.goto(`http://127.0.0.1:${PORT}/?room=test`, { waitUntil: 'load' });
  await page.waitForFunction(() => !!window.__bb, null, { timeout: 20000 });
  await page.fill('#name', name);
  await page.click('#play');
  await page.waitForFunction(() => window.__bb.net.status === 'live', null, { timeout: 15000 });
  return { page, errors, name };
}

try {
  const a = await openClient('Alpha');
  const b = await openClient('Beta');
  await sleep(1500);

  // Drive both with real pointer gestures so the stick code is exercised too.
  for (let i = 0; i < 24; i++) {
    for (const c of [a, b]) {
      const ang = (i / 24) * Math.PI * 2 + (c.name === 'Beta' ? Math.PI : 0);
      await c.page.mouse.move(100 + Math.cos(ang) * 50, 600 + Math.sin(ang) * 50);
      await c.page.evaluate((an) => {
        const { net } = window.__bb;
        net.sendInput(Math.cos(an), Math.sin(an), Math.cos(an), Math.sin(an));
        if (Math.random() < 0.12) net.dash();
      }, ang);
    }
    await sleep(120);
  }
  await sleep(600);

  await a.page.screenshot({ path: `${SHOTS}/arena-a.png` });
  await b.page.screenshot({ path: `${SHOTS}/arena-b.png` });

  for (const c of [a, b]) {
    const s = await c.page.evaluate(() => {
      const { net, renderer } = window.__bb;
      const gl = renderer.gl;
      // The scene target rather than the default framebuffer: the latter reads
      // back blank after compositing unless preserveDrawingBuffer is on.
      const { lit, peak, samples } = renderer.debugSampleScene();
      const v = net.view();
      return {
        status: net.status,
        ping: net.ping,
        humans: net.roster.filter((r) => !r.bot).length,
        roster: net.roster.length,
        phase: v?.phase ?? null,
        alive: v?.alive ?? false,
        others: v?.players.length ?? 0,
        pellets: v?.pellets.length ?? 0,
        hasMe: !!v?.me,
        glError: gl.getError(),
        litFraction: +(lit / samples).toFixed(3),
        peak,
      };
    });
    console.log(`${c.name}: ${JSON.stringify(s)}`);

    if (c.errors.length) fail.push(c.errors[0]);
    if (s.glError !== 0) fail.push(`${c.name}: GL error ${s.glError}`);
    if (s.humans < 2) fail.push(`${c.name} only sees ${s.humans} humans — clients aren't sharing an arena`);
    if (s.roster < 3) fail.push(`${c.name}: roster ${s.roster}, bots did not fill the arena`);
    if (s.pellets === 0) fail.push(`${c.name} sees no pellets`);
    if (s.litFraction < 0.05) fail.push(`${c.name}: only ${(s.litFraction * 100).toFixed(1)}% of the frame is lit — nothing drew`);
    if (s.peak < 0.35) fail.push(`${c.name}: frame peak brightness ${s.peak} — no blobs or ring visible`);
    if (!s.hasMe && s.alive) fail.push(`${c.name}: alive but has no predicted blob to steer`);
    if (!s.phase) fail.push(`${c.name}: no snapshot phase`);
  }
} catch (e) {
  fail.push(String(e));
}

await browser.close();
server.kill();

if (fail.length) {
  console.error(`\nFAIL\n- ${fail.join('\n- ')}`);
  if (serverErr.length) console.error(`\nserver stderr:\n${serverErr.slice(-4).join('')}`);
  process.exit(1);
}
console.log('\nPASS');
