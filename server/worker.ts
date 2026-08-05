/// <reference types="@cloudflare/workers-types" />
import { Room, type Sender } from './room.ts';
import { ARENA } from '../src/sim/config.ts';

/**
 * Cloudflare deployment: one Durable Object per match.
 *
 * A Durable Object is a single-threaded actor with a stable identity, which is
 * exactly the shape of a game room — every player in a match talks to the same
 * instance, so the authoritative simulation needs no cross-node coordination at
 * all. The Room class here is the same one the local Node server runs, so the
 * netcode is developed and tested on a laptop and deployed unchanged.
 *
 * Note on cost: a live match ticks 20 times a second, so it never hibernates
 * while it is being played. Duration is billed for the length of a match, not
 * per connection.
 */
export interface Env {
  ARENA_ROOM: DurableObjectNamespace;
  ASSETS?: Fetcher;
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === '/ws') {
      // Everyone naming the same room lands on the same Durable Object.
      const roomName = url.searchParams.get('room') ?? 'public';
      const id = env.ARENA_ROOM.idFromName(roomName);
      return env.ARENA_ROOM.get(id).fetch(request);
    }

    if (url.pathname === '/healthz') {
      return Response.json({ ok: true });
    }

    // Everything else is the static client, served by Workers Assets.
    if (env.ASSETS) return env.ASSETS.fetch(request);
    return new Response('not found', { status: 404 });
  },
};

export class ArenaRoom implements DurableObject {
  private room = new Room();
  private sockets = new Map<WebSocket, string>();
  private ticking = false;

  constructor(
    private state: DurableObjectState,
    private env: Env,
  ) {
    void this.env;
  }

  async fetch(request: Request): Promise<Response> {
    if (request.headers.get('Upgrade') !== 'websocket') {
      return new Response('expected websocket', { status: 426 });
    }

    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];

    // Hibernation-aware accept: the runtime can evict this object between
    // messages without dropping the connection, which is what keeps an idle
    // lobby from billing duration.
    this.state.acceptWebSocket(server);

    const sender: Sender = {
      send: (data) => {
        try {
          server.send(data);
        } catch {
          /* socket closing */
        }
      },
      close: () => {
        try {
          server.close();
        } catch {
          /* already closed */
        }
      },
    };

    const playerId = this.room.join(sender, Date.now());
    this.sockets.set(server, playerId);
    this.ensureTicking();

    return new Response(null, { status: 101, webSocket: client });
  }

  webSocketMessage(ws: WebSocket, message: string | ArrayBuffer) {
    const id = this.sockets.get(ws);
    if (id) this.room.message(id, typeof message === 'string' ? message : '', Date.now());
  }

  webSocketClose(ws: WebSocket) {
    this.dropSocket(ws);
  }

  webSocketError(ws: WebSocket) {
    this.dropSocket(ws);
  }

  private dropSocket(ws: WebSocket) {
    const id = this.sockets.get(ws);
    if (id) this.room.leave(id);
    this.sockets.delete(ws);
  }

  /**
   * The tick loop. Uses a plain interval rather than alarms: alarms have a
   * one-second floor, and this needs 20 ticks a second.
   */
  private ensureTicking() {
    if (this.ticking) return;
    this.ticking = true;
    const dt = 1 / ARENA.tickRate;
    const timer = setInterval(() => {
      if (this.sockets.size === 0) {
        clearInterval(timer);
        this.ticking = false;
        return;
      }
      this.room.step(dt, Date.now());
    }, 1000 / ARENA.tickRate);
  }
}
