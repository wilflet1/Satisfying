/**
 * Local/self-hosted server. Runs the identical Room logic that the Cloudflare
 * Durable Object runs in production, so the netcode can be developed and tested
 * end-to-end on one machine before any of it is deployed.
 *
 *   node --experimental-strip-types server/node.ts [--port 8787]
 *
 * Also serves ./dist when it exists, so the built client and its server share
 * an origin and there is no CORS or mixed-content setup to get wrong.
 */
import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize, resolve } from 'node:path';
import { WebSocketServer, type WebSocket } from 'ws';
import { Room, type Sender } from './room.ts';
import { ARENA } from '../src/sim/config.ts';

const argv = process.argv;
const argOf = (name: string, fallback: string) => {
  const i = argv.indexOf(`--${name}`);
  return i >= 0 && argv[i + 1] ? argv[i + 1] : fallback;
};

const PORT = Number(argOf('port', process.env.PORT ?? '8787'));
const DIST = resolve(process.cwd(), 'dist');

// --- rooms ------------------------------------------------------------------

const rooms = new Map<string, Room>();

/** Simple matchmaking: fill the fullest room that still has space. */
function pickRoom(requested?: string): { id: string; room: Room } {
  if (requested) {
    let room = rooms.get(requested);
    if (!room) {
      room = new Room();
      rooms.set(requested, room);
    }
    return { id: requested, room };
  }
  let best: { id: string; room: Room } | null = null;
  for (const [id, room] of rooms) {
    if (room.full) continue;
    if (!best || room.playerCount > best.room.playerCount) best = { id, room };
  }
  if (best) return best;
  const id = `r${Math.random().toString(36).slice(2, 8)}`;
  const room = new Room();
  rooms.set(id, room);
  return { id, room };
}

// --- static files -----------------------------------------------------------

const MIME: Record<string, string> = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
};

async function serveStatic(req: IncomingMessage, res: ServerResponse) {
  const url = new URL(req.url ?? '/', 'http://localhost');
  if (url.pathname === '/healthz') {
    res.writeHead(200, { 'content-type': 'application/json' });
    res.end(JSON.stringify({ ok: true, rooms: rooms.size }));
    return;
  }

  // Resolve inside dist only — a request for ../../etc/passwd must not escape.
  const rel = normalize(decodeURIComponent(url.pathname)).replace(/^(\.\.[/\\])+/, '');
  let file = join(DIST, rel === '/' ? 'index.html' : rel);
  if (!resolve(file).startsWith(DIST)) {
    res.writeHead(403).end('forbidden');
    return;
  }
  try {
    const s = await stat(file);
    if (s.isDirectory()) file = join(file, 'index.html');
    const body = await readFile(file);
    res.writeHead(200, { 'content-type': MIME[extname(file)] ?? 'application/octet-stream' });
    res.end(body);
  } catch {
    // SPA fallback so deep links still boot the client.
    try {
      const body = await readFile(join(DIST, 'index.html'));
      res.writeHead(200, { 'content-type': MIME['.html'] });
      res.end(body);
    } catch {
      res.writeHead(404).end('not found');
    }
  }
}

// --- wiring -----------------------------------------------------------------

const http = createServer((req, res) => {
  serveStatic(req, res).catch(() => res.writeHead(500).end('error'));
});

const wss = new WebSocketServer({ server: http, path: '/ws' });

wss.on('connection', (ws: WebSocket, req: IncomingMessage) => {
  const url = new URL(req.url ?? '/ws', 'http://localhost');
  const { id: roomId, room } = pickRoom(url.searchParams.get('room') ?? undefined);

  const sender: Sender = {
    send: (data) => {
      if (ws.readyState === ws.OPEN) ws.send(data);
    },
    close: () => ws.close(),
  };

  const playerId = room.join(sender, Date.now());
  ws.on('message', (data) => room.message(playerId, String(data), Date.now()));
  ws.on('close', () => {
    room.leave(playerId);
    // Reclaim empty rooms so a long-lived server doesn't accumulate them.
    if (room.playerCount === 0) rooms.delete(roomId);
  });
  ws.on('error', () => ws.close());
});

const dt = 1 / ARENA.tickRate;
setInterval(() => {
  const now = Date.now();
  for (const room of rooms.values()) room.step(dt, now);
}, 1000 / ARENA.tickRate);

http.listen(PORT, () => {
  console.log(`blob-brawl server on :${PORT}  (ws://localhost:${PORT}/ws)`);
});
