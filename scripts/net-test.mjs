/**
 * End-to-end multiplayer test. Boots the real server, connects real WebSocket
 * clients, and plays them. No browser involved — the netcode is independent of
 * rendering, and testing it here makes it fast enough to run on every change.
 *
 * It answers the questions that only show up with several players connected:
 * does everyone see everyone, are inputs acknowledged, does the room fill with
 * bots when you're alone, and — the one that decides whether this is shippable
 * on mobile data — how many bytes per second does a client actually receive?
 *
 * Run: node --experimental-strip-types scripts/net-test.mjs
 */
import { spawn } from 'node:child_process';
import { WebSocket } from 'ws';

const PORT = 8899;
const URL = `ws://127.0.0.1:${PORT}/ws`;

const server = spawn(
  process.execPath,
  ['--experimental-strip-types', 'server/node.ts', '--port', String(PORT)],
  { stdio: ['ignore', 'pipe', 'pipe'] },
);
const serverErr = [];
server.stderr.on('data', (d) => serverErr.push(String(d)));

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = [];

async function waitForServer() {
  for (let i = 0; i < 60; i++) {
    try {
      const res = await fetch(`http://127.0.0.1:${PORT}/healthz`);
      if (res.ok) return true;
    } catch {
      /* not up yet */
    }
    await sleep(150);
  }
  return false;
}

/** A scripted player: joins, then drives inputs like a person mashing about. */
function makeClient(name, room) {
  const c = {
    name,
    ws: new WebSocket(`${URL}?room=${room}`),
    id: null,
    snaps: 0,
    bytes: 0,
    roster: [],
    lastAck: -1,
    ackOk: true,
    seq: 0,
    seenPlayers: new Set(),
    events: { hits: 0, deaths: 0, dashes: 0 },
    phases: new Set(),
    errors: [],
  };

  c.ws.on('message', (data) => {
    c.bytes += data.length ?? String(data).length;
    let m;
    try {
      m = JSON.parse(String(data));
    } catch {
      c.errors.push('unparseable frame');
      return;
    }
    if (m.t === 'hello') {
      c.id = m.id;
      c.ws.send(JSON.stringify({ t: 'join', v: m.v, name }));
    } else if (m.t === 'roster') {
      c.roster = m.r;
    } else if (m.t === 'snap') {
      c.snaps++;
      c.phases.add(m.ph);
      for (const p of m.P) c.seenPlayers.add(p[0]);
      c.events.hits += m.eh.length;
      c.events.deaths += m.ed.length;
      c.events.dashes += m.es.length;
      // The ack must never run backwards or exceed what we actually sent.
      if (m.ack > c.seq) c.ackOk = false;
      if (m.ack < c.lastAck) c.ackOk = false;
      c.lastAck = m.ack;
    }
  });
  c.ws.on('error', (e) => c.errors.push(String(e)));

  c.drive = () => {
    if (c.ws.readyState !== WebSocket.OPEN || !c.id) return;
    c.seq++;
    const a = Math.random() * Math.PI * 2;
    c.ws.send(
      JSON.stringify({
        t: 'in',
        s: c.seq,
        m: [Math.cos(a), Math.sin(a)],
        a: [Math.cos(a), Math.sin(a)],
        d: Math.random() < 0.12 ? 1 : 0,
        p: Math.random() < 0.03 ? 1 : 0,
      }),
    );
  };
  return c;
}

async function run() {
  if (!(await waitForServer())) {
    console.error('server never came up\n' + serverErr.join(''));
    process.exit(1);
  }

  // --- 1. Solo player should be given a room full of bots -----------------
  const solo = makeClient('Solo', 'solo');
  await sleep(800);
  for (let i = 0; i < 40; i++) {
    solo.drive();
    await sleep(50);
  }
  const soloBots = solo.roster.filter((r) => r[3] === 1).length;
  console.log(`solo: roster ${solo.roster.length} (${soloBots} bots), snapshots ${solo.snaps}`);
  if (soloBots < 3) fail.push(`solo player got only ${soloBots} bots — a lone player has no game`);
  solo.ws.close();

  // --- 2. Eight humans in one room ---------------------------------------
  const clients = [];
  for (let i = 0; i < 8; i++) clients.push(makeClient(`Player${i + 1}`, 'brawl'));
  await sleep(900);

  const t0 = Date.now();
  for (const c of clients) c.bytes = 0;
  const ticker = setInterval(() => {
    for (const c of clients) c.drive();
  }, 50);
  await sleep(9000);
  clearInterval(ticker);
  const secs = (Date.now() - t0) / 1000;

  const totals = clients.reduce(
    (a, c) => ({
      snaps: a.snaps + c.snaps,
      hits: a.hits + c.events.hits,
      deaths: a.deaths + c.events.deaths,
      dashes: a.dashes + c.events.dashes,
    }),
    { snaps: 0, hits: 0, deaths: 0, dashes: 0 },
  );

  const rates = clients.map((c) => c.snaps / secs);
  const kbps = clients.map((c) => c.bytes / secs / 1024);
  const avgKbps = kbps.reduce((a, b) => a + b, 0) / kbps.length;
  const humansSeen = Math.max(...clients.map((c) => c.roster.filter((r) => r[3] === 0).length));

  console.log(
    `8 clients: humans in roster ${humansSeen}, snapshot rate ` +
      `${(rates.reduce((a, b) => a + b, 0) / rates.length).toFixed(1)}/s, ` +
      `downstream ${avgKbps.toFixed(1)} KB/s/client`,
  );
  console.log(
    `  events seen — dashes ${totals.dashes}, hits ${totals.hits}, deaths ${totals.deaths}`,
  );
  console.log(`  phases observed: ${[...new Set(clients.flatMap((c) => [...c.phases]))].join(', ')}`);

  for (const c of clients) {
    if (c.errors.length) fail.push(`${c.name}: ${c.errors[0]}`);
    if (!c.ackOk) fail.push(`${c.name}: input acknowledgement went backwards or ran ahead`);
    if (c.snaps < 40) fail.push(`${c.name} only received ${c.snaps} snapshots`);
    // Everyone must see everyone, or players are invisible to each other.
    if (c.seenPlayers.size < 8) {
      fail.push(`${c.name} only ever saw ${c.seenPlayers.size} of 8 players`);
    }
  }
  if (humansSeen < 8) fail.push(`roster only ever showed ${humansSeen}/8 humans`);
  if (totals.dashes === 0) fail.push('no dash events reached any client');

  // Bandwidth is the make-or-break number for a mobile release.
  if (avgKbps > 60) fail.push(`downstream ${avgKbps.toFixed(1)} KB/s/client is too heavy for mobile`);

  for (const c of clients) c.ws.close();
  await sleep(200);
}

try {
  await run();
} catch (e) {
  fail.push(String(e));
}

server.kill();
if (fail.length) {
  console.error(`\nFAIL\n- ${fail.join('\n- ')}`);
  if (serverErr.length) console.error(`\nserver stderr:\n${serverErr.join('')}`);
  process.exit(1);
}
console.log('\nPASS');
