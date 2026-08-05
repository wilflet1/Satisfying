import { Arena } from '../src/sim/arena.ts';
import { assignSkills, driveBot } from '../src/sim/bots.ts';
import { ARENA, massToSize } from '../src/sim/config.ts';
import {
  PROTOCOL_VERSION,
  round1,
  sanitiseInput,
  type ChunkWire,
  type PelletWire,
  type PlayerWire,
  type ServerMsg,
} from '../src/net/protocol.ts';

/**
 * Anything that can deliver bytes to one client. Keeping the room ignorant of
 * the transport is what lets the identical room logic run under a plain Node
 * WebSocket server locally and inside a Cloudflare Durable Object in
 * production — the netcode is tested in one place and deployed to the other.
 */
export interface Sender {
  send(data: string): void;
  close(): void;
}

interface Conn {
  id: string;
  sender: Sender;
  name: string;
  joined: boolean;
  /** Last time we heard anything, for idle eviction. */
  lastSeen: number;
}

let seq = 0;
const newId = () => `p${(++seq).toString(36)}${Math.floor(Math.random() * 1296).toString(36)}`;

/**
 * One match. Owns an Arena, the connected players, and the bots that top it up.
 *
 * Bots are ordinary players driven by a local brain, so a room is never empty
 * and a solo player still gets a real game — which also means the netcode path
 * is exercised identically whether anyone else has joined or not.
 */
export class Room {
  readonly arena: Arena;
  private conns = new Map<string, Conn>();
  private botIds: string[] = [];
  private tick = 0;
  private rosterDirty = true;

  constructor(seed = Date.now()) {
    this.arena = new Arena(seed);
  }

  get playerCount() {
    return this.conns.size;
  }

  get full() {
    return this.conns.size >= ARENA.match.maxPlayers;
  }

  join(sender: Sender, now: number): string {
    const id = newId();
    this.conns.set(id, { id, sender, name: 'Blob', joined: false, lastSeen: now });
    this.send(id, { t: 'hello', v: PROTOCOL_VERSION, id, hz: ARENA.tickRate });
    return id;
  }

  leave(id: string) {
    this.conns.delete(id);
    this.arena.removePlayer(id);
    this.rosterDirty = true;
    this.rebalanceBots();
  }

  message(id: string, raw: string, now: number) {
    const conn = this.conns.get(id);
    if (!conn) return;
    conn.lastSeen = now;

    let msg: unknown;
    try {
      msg = JSON.parse(raw);
    } catch {
      return;
    }
    if (typeof msg !== 'object' || msg === null) return;
    const m = msg as Record<string, unknown>;

    if (m.t === 'ping') {
      this.send(id, { t: 'pong', c: Number(m.c) || 0 });
      return;
    }

    if (m.t === 'join' && !conn.joined) {
      // Names are shown to other players, so they are length-capped and
      // stripped of control characters before they go anywhere near a roster.
      const given = typeof m.name === 'string' ? m.name : '';
      conn.name =
        given.replace(/[\u0000-\u001f\u007f]/g, '').trim().slice(0, 14) || 'Blob';
      conn.joined = true;
      this.arena.addPlayer(id, conn.name, false);
      this.rosterDirty = true;
      this.rebalanceBots();
      return;
    }

    if (m.t === 'in' && conn.joined) {
      const inp = sanitiseInput(m);
      if (!inp) return;
      this.arena.setInput(id, {
        moveX: inp.m[0],
        moveY: inp.m[1],
        aimX: inp.a[0],
        aimY: inp.a[1],
        dash: inp.d === 1,
        pull: inp.p === 1,
        seq: inp.s,
      });
    }
  }

  /** Keeps the arena populated to `fillTo` with bots, without exceeding max. */
  private rebalanceBots() {
    const humans = this.arena.humanCount;
    const want = Math.max(0, Math.min(ARENA.match.fillTo, ARENA.match.maxPlayers) - humans);

    while (this.botIds.length > want) {
      const id = this.botIds.pop()!;
      this.arena.removePlayer(id);
      this.rosterDirty = true;
    }
    while (this.botIds.length < want) {
      const id = `bot${this.botIds.length}-${Math.floor(Math.random() * 1e6).toString(36)}`;
      this.arena.addPlayer(id, BOT_NAMES[this.botIds.length % BOT_NAMES.length], true);
      this.botIds.push(id);
      this.rosterDirty = true;
    }
    assignSkills(this.arena);
  }

  /** Advance one server tick and broadcast. Call at ARENA.tickRate. */
  step(dt: number, now: number) {
    this.arena.beginTick();
    for (const p of this.arena.players.values()) {
      if (p.bot) driveBot(this.arena, p, dt);
    }
    this.arena.step(dt);
    this.tick++;

    // Inputs are edge-triggered: consuming them here stops a single tap from
    // firing every tick until the next packet lands on a laggy connection.
    for (const p of this.arena.players.values()) {
      if (!p.bot) {
        p.input.dash = false;
        p.input.pull = false;
      }
    }

    if (this.rosterDirty) {
      this.broadcast({
        t: 'roster',
        r: [...this.arena.players.values()].map(
          (p) => [p.id, p.name, p.hue, p.bot ? 1 : 0] as [string, string, number, 0 | 1],
        ),
      });
      this.rosterDirty = false;
    }

    this.broadcastSnapshot();

    // Drop connections that have gone quiet — a half-open socket otherwise
    // holds a player slot open forever.
    for (const [id, c] of this.conns) {
      if (now - c.lastSeen > 20000) {
        c.sender.close();
        this.leave(id);
      }
    }
  }

  private broadcastSnapshot() {
    const a = this.arena;
    const P: PlayerWire[] = [];
    for (const p of a.players.values()) {
      P.push([
        p.id,
        round1(p.x),
        round1(p.y),
        round1(p.r * 10) / 10,
        Math.round(p.aimX * 100),
        Math.round(p.aimY * 100),
        p.alive ? 1 : 0,
        p.kills,
        // Sent as displayed size, not raw area: it is what the HUD shows and
        // it is a smaller number on the wire.
        massToSize(p.mass),
      ]);
    }
    const C: ChunkWire[] = a.chunks.map((c) => [
      c.id,
      round1(c.x),
      round1(c.y),
      round1(c.r * 10) / 10,
      c.hue,
    ]);
    const G: PelletWire[] = a.pellets.map((g) => [
      g.id,
      round1(g.x),
      round1(g.y),
      round1(g.r * 10) / 10,
      g.hue,
    ]);

    const base = {
      t: 'snap' as const,
      k: this.tick,
      ph: a.phase,
      tm: Math.round(a.timer * 10) / 10,
      rr: round1(a.ringRadius),
      w: a.winner,
      P,
      C,
      G,
      eh: a.events.hits.map(
        (h) => [round1(h.x), round1(h.y), h.hue, Math.round(h.power * 100)] as [number, number, number, number],
      ),
      ed: a.events.deaths.map((d) => [round1(d.x), round1(d.y), d.hue] as [number, number, number]),
      es: a.events.dashes.map((d) => [round1(d.x), round1(d.y), d.hue] as [number, number, number]),
    };

    // Most of the snapshot is identical for everyone, so the shared body is
    // built once and only the few genuinely per-player fields are added.
    for (const [id, c] of this.conns) {
      if (!c.joined) continue;
      const me = this.arena.players.get(id);
      c.sender.send(
        JSON.stringify({
          ...base,
          ack: me?.ack ?? 0,
          pr: Math.round((me?.protect ?? 0) * 10) / 10,
          rs: Math.round((me?.respawnIn ?? 0) * 10) / 10,
        }),
      );
    }
  }

  private send(id: string, msg: ServerMsg) {
    this.conns.get(id)?.sender.send(JSON.stringify(msg));
  }

  private broadcast(msg: ServerMsg) {
    const s = JSON.stringify(msg);
    for (const c of this.conns.values()) if (c.joined) c.sender.send(s);
  }
}

const BOT_NAMES = ['Blorp', 'Gloop', 'Squish', 'Wobble', 'Splat', 'Dollop', 'Gunk', 'Ooze'];
