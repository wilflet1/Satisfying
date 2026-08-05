/**
 * Wire protocol. Compact positional arrays rather than objects, and every
 * coordinate rounded to a whole world unit — at 20 snapshots a second with 8
 * players the difference between readable JSON and this is roughly 4x the
 * bandwidth, which matters on mobile data.
 *
 * The server is authoritative for everything. Clients send intent only, and
 * re-simulate their own blob locally to hide the round trip.
 */

export const PROTOCOL_VERSION = 1;

export type Phase = 'lobby' | 'countdown' | 'live' | 'over';

// --- client -> server -------------------------------------------------------

export interface JoinMsg {
  t: 'join';
  v: number;
  name: string;
}

export interface InputMsg {
  t: 'in';
  /** Input sequence number, echoed back for reconciliation. */
  s: number;
  /** Move direction. */
  m: [number, number];
  /** Aim direction. */
  a: [number, number];
  /** Dash pressed this frame. */
  d: 0 | 1;
  /** Pull pressed this frame. */
  p: 0 | 1;
}

export interface PingMsg {
  t: 'ping';
  /** Client clock, echoed verbatim. */
  c: number;
}

export type ClientMsg = JoinMsg | InputMsg | PingMsg;

// --- server -> client -------------------------------------------------------

export interface HelloMsg {
  t: 'hello';
  v: number;
  /** Your player id. */
  id: string;
  /** Server tick rate, so the client can size its interpolation buffer. */
  hz: number;
}

/** Sent only when membership changes — names never belong in a 20 Hz snapshot. */
export interface RosterMsg {
  t: 'roster';
  /** [id, name, hue, isBot] */
  r: Array<[string, string, number, 0 | 1]>;
}

/** [id, x, y, r, aimX*100, aimY*100, alive, kills, mass] */
export type PlayerWire = [string, number, number, number, number, number, 0 | 1, number, number];
/** [id, x, y, r, hue] */
export type ChunkWire = [number, number, number, number, number];
/** [id, x, y, r, hue] */
export type PelletWire = [number, number, number, number, number];

export interface SnapshotMsg {
  t: 'snap';
  /** Server tick. */
  k: number;
  /** Last input sequence the server applied from you. */
  ack: number;
  ph: Phase;
  /** Seconds left in the current phase. */
  tm: number;
  /** Ring radius. */
  rr: number;
  /** Your spawn protection remaining, and your respawn countdown. */
  pr: number;
  rs: number;
  /** Winner id, when the round is over. */
  w: string | null;
  P: PlayerWire[];
  C: ChunkWire[];
  G: PelletWire[];
  /** Events: hits [x,y,hue,power*100], deaths [x,y,hue], dashes [x,y,hue]. */
  eh: Array<[number, number, number, number]>;
  ed: Array<[number, number, number]>;
  es: Array<[number, number, number]>;
}

export interface PongMsg {
  t: 'pong';
  c: number;
}

export type ServerMsg = HelloMsg | RosterMsg | SnapshotMsg | PongMsg;

export const round1 = (n: number) => Math.round(n);

/** Guards against malformed or hostile input without throwing. */
export function sanitiseInput(raw: unknown): InputMsg | null {
  if (typeof raw !== 'object' || raw === null) return null;
  const m = raw as Record<string, unknown>;
  if (m.t !== 'in') return null;
  const pair = (v: unknown): [number, number] => {
    if (!Array.isArray(v) || v.length < 2) return [0, 0];
    const x = Number(v[0]);
    const y = Number(v[1]);
    if (!Number.isFinite(x) || !Number.isFinite(y)) return [0, 0];
    // Clamp to the unit disc so a crafted packet can't outrun everyone.
    const len = Math.hypot(x, y);
    return len > 1 ? [x / len, y / len] : [x, y];
  };
  const seq = Number(m.s);
  return {
    t: 'in',
    s: Number.isFinite(seq) ? seq : 0,
    m: pair(m.m),
    a: pair(m.a),
    d: m.d ? 1 : 0,
    p: m.p ? 1 : 0,
  };
}
