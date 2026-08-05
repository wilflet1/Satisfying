import { ARENA, massToRadius, speedFor } from '../sim/config.ts';
import { PROTOCOL_VERSION, type Phase, type ServerMsg, type SnapshotMsg } from './protocol.ts';

export interface ViewBlob {
  id: string;
  x: number;
  y: number;
  r: number;
  hue: number;
}

export interface ViewEvent {
  x: number;
  y: number;
  hue: number;
  power: number;
}

export interface View {
  phase: Phase;
  timer: number;
  ring: number;
  winner: string | null;
  players: ViewBlob[];
  chunks: ViewBlob[];
  pellets: ViewBlob[];
  /** Your own blob, locally predicted. Null when dead or not yet spawned. */
  me: ViewBlob | null;
  alive: boolean;
  /** Size as shown to the player, not internal mass. */
  mass: number;
  kills: number;
  /** Seconds of spawn protection left, and seconds until respawn when dead. */
  protect: number;
  respawnIn: number;
  /** Drained once per frame. */
  hits: ViewEvent[];
  deaths: ViewEvent[];
  dashes: ViewEvent[];
}

export interface Roster {
  id: string;
  name: string;
  hue: number;
  bot: boolean;
}

type Status = 'connecting' | 'live' | 'closed' | 'error';

/** Snapshots older than this many milliseconds are discarded. */
const BUFFER_MS = 1500;

/**
 * Client half of the netcode.
 *
 * Two things happen here. Remote bodies are *interpolated*: they render one
 * interpolation window in the past, between the two snapshots that bracket that
 * moment, which turns 20 discrete updates a second into smooth motion. Your own
 * blob is *predicted*: it integrates your input immediately and is then eased
 * toward the server's version rather than snapped to it.
 *
 * That easing is the whole trick, and it works here because the blob already
 * lags your finger by design. A correction reads as the goo settling, which the
 * player has been trained to expect since the first second of play — so latency
 * hides inside the material rather than showing up as a rubber-band.
 */
export class NetClient {
  status: Status = 'connecting';
  id: string | null = null;
  roster: Roster[] = [];
  ping = 0;
  error = '';

  private ws: WebSocket | null = null;
  private snaps: Array<{ at: number; s: SnapshotMsg }> = [];
  private seq = 0;
  /** Offset from local clock to the arrival time of snapshots. */
  private interpDelay = 1000 / ARENA.tickRate + 60;

  // Locally predicted own-blob state.
  private px = 0;
  private py = 0;
  private pvx = 0;
  private pvy = 0;
  private pr = massToRadius(ARENA.blob.startMass);
  private predicting = false;

  private pendingDash = false;
  private pendingPull = false;
  private lastPingAt = 0;

  private drained = { hits: [] as ViewEvent[], deaths: [] as ViewEvent[], dashes: [] as ViewEvent[] };
  private lastAppliedTick = -1;

  connect(url: string, name: string) {
    try {
      this.ws = new WebSocket(url);
    } catch (e) {
      this.status = 'error';
      this.error = String(e);
      return;
    }
    this.ws.onopen = () => {
      this.send({ t: 'join', v: PROTOCOL_VERSION, name });
    };
    this.ws.onclose = () => {
      if (this.status !== 'error') this.status = 'closed';
    };
    this.ws.onerror = () => {
      this.status = 'error';
      this.error = 'connection failed';
    };
    this.ws.onmessage = (ev) => this.onMessage(String(ev.data));
  }

  disconnect() {
    this.ws?.close();
    this.ws = null;
  }

  private send(msg: unknown) {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) this.ws.send(JSON.stringify(msg));
  }

  private onMessage(raw: string) {
    let msg: ServerMsg;
    try {
      msg = JSON.parse(raw) as ServerMsg;
    } catch {
      return;
    }

    if (msg.t === 'hello') {
      this.id = msg.id;
      this.status = 'live';
      return;
    }
    if (msg.t === 'pong') {
      this.ping = Math.round(performance.now() - msg.c);
      return;
    }
    if (msg.t === 'roster') {
      this.roster = msg.r.map(([id, name, hue, bot]) => ({ id, name, hue, bot: bot === 1 }));
      return;
    }
    if (msg.t !== 'snap') return;

    const now = performance.now();
    this.snaps.push({ at: now, s: msg });
    while (this.snaps.length > 2 && now - this.snaps[0].at > BUFFER_MS) this.snaps.shift();

    // Events fire once, on arrival — interpolating them would smear a hit
    // across several frames and destroy its impact.
    if (msg.k > this.lastAppliedTick) {
      this.lastAppliedTick = msg.k;
      for (const [x, y, hue, pow] of msg.eh) {
        this.drained.hits.push({ x, y, hue, power: pow / 100 });
      }
      for (const [x, y, hue] of msg.ed) this.drained.deaths.push({ x, y, hue, power: 1 });
      for (const [x, y, hue] of msg.es) this.drained.dashes.push({ x, y, hue, power: 1 });
    }

    this.reconcile(msg);
  }

  /** Ease the predicted blob toward the authoritative one. */
  private reconcile(s: SnapshotMsg) {
    if (!this.id) return;
    const mine = s.P.find((p) => p[0] === this.id);
    if (!mine) return;
    const [, x, y, r, , , alive] = mine;
    this.pr = r;

    if (!alive) {
      this.predicting = false;
      return;
    }
    if (!this.predicting) {
      // First frame alive, or respawn: adopt the server's position outright.
      this.px = x;
      this.py = y;
      this.pvx = 0;
      this.pvy = 0;
      this.predicting = true;
      return;
    }

    const dx = x - this.px;
    const dy = y - this.py;
    const err = Math.hypot(dx, dy);
    // A large divergence means a collision or a hit the client didn't predict;
    // snapping there is correct and reads as being shoved.
    if (err > 26) {
      this.px = x;
      this.py = y;
    } else {
      this.px += dx * 0.18;
      this.py += dy * 0.18;
    }
  }

  /** Called every animation frame with the player's current intent. */
  sendInput(moveX: number, moveY: number, aimX: number, aimY: number) {
    this.seq++;
    this.send({
      t: 'in',
      s: this.seq,
      m: [round3(moveX), round3(moveY)],
      a: [round3(aimX), round3(aimY)],
      d: this.pendingDash ? 1 : 0,
      p: this.pendingPull ? 1 : 0,
    });
    this.pendingDash = false;
    this.pendingPull = false;

    const now = performance.now();
    if (now - this.lastPingAt > 2000) {
      this.lastPingAt = now;
      this.send({ t: 'ping', c: now });
    }
  }

  dash() {
    this.pendingDash = true;
  }
  pull() {
    this.pendingPull = true;
  }

  /** Advance local prediction for the player's own blob. */
  predict(dt: number, moveX: number, moveY: number) {
    if (!this.predicting) return;
    const top = speedFor(this.pr);
    const mv = Math.hypot(moveX, moveY);
    if (mv > 0.01) {
      const nx = moveX / Math.max(mv, 1);
      const ny = moveY / Math.max(mv, 1);
      this.pvx += (nx * top - this.pvx) * ARENA.blob.accel * dt;
      this.pvy += (ny * top - this.pvy) * ARENA.blob.accel * dt;
    } else {
      const k = Math.exp(-ARENA.blob.drag * dt);
      this.pvx *= k;
      this.pvy *= k;
    }
    this.px += this.pvx * dt;
    this.py += this.pvy * dt;
  }

  /** Build the interpolated view of the world for this frame. */
  view(): View | null {
    if (this.snaps.length === 0) return null;
    const target = performance.now() - this.interpDelay;

    let a = this.snaps[0];
    let b = this.snaps[this.snaps.length - 1];
    for (let i = 0; i < this.snaps.length - 1; i++) {
      if (this.snaps[i].at <= target && this.snaps[i + 1].at >= target) {
        a = this.snaps[i];
        b = this.snaps[i + 1];
        break;
      }
    }
    // Past the newest snapshot (a stalled connection): hold the last state
    // rather than extrapolating bodies into positions they never occupied.
    const span = b.at - a.at;
    const t = span > 1 ? Math.max(0, Math.min(1, (target - a.at) / span)) : 1;
    const latest = this.snaps[this.snaps.length - 1].s;

    // Player hue lives in the roster, not the snapshot, so it is resolved here
    // rather than being repeated in every frame on the wire.
    const players = lerpSet(a.s.P, b.s.P, t, true).map((p) => ({
      ...p,
      hue: hueOf(p.id, this.roster),
    }));
    const chunks = lerpSet(a.s.C, b.s.C, t, false);
    const pellets = lerpSet(a.s.G, b.s.G, t, false);

    const mineWire = latest.P.find((p) => p[0] === this.id);
    const alive = mineWire ? mineWire[6] === 1 : false;
    const me: ViewBlob | null =
      alive && this.predicting
        ? { id: this.id!, x: this.px, y: this.py, r: this.pr, hue: hueOf(this.id!, this.roster) }
        : null;

    // The predicted copy replaces the interpolated one, so your own blob never
    // renders a interpolation-window behind your thumb.
    const others = me ? players.filter((p) => p.id !== this.id) : players;

    const hits = this.drained.hits;
    const deaths = this.drained.deaths;
    const dashes = this.drained.dashes;
    this.drained = { hits: [], deaths: [], dashes: [] };

    return {
      phase: latest.ph,
      timer: latest.tm,
      ring: lerpNum(a.s.rr, b.s.rr, t),
      winner: latest.w,
      players: others,
      chunks,
      pellets,
      me,
      alive,
      mass: mineWire ? mineWire[8] : 0,
      kills: mineWire ? mineWire[7] : 0,
      protect: latest.pr ?? 0,
      respawnIn: latest.rs ?? 0,
      hits,
      deaths,
      dashes,
    };
  }
}

const round3 = (n: number) => Math.round(n * 1000) / 1000;
const lerpNum = (a: number, b: number, t: number) => a + (b - a) * t;

function hueOf(id: string, roster: Roster[]): number {
  return roster.find((r) => r.id === id)?.hue ?? 0;
}

/**
 * Interpolate two snapshots of the same entity kind, matching by id. Bodies
 * present in only one snapshot appear or disappear rather than sliding in from
 * a position they never had.
 */
function lerpSet(
  aList: Array<Array<string | number>>,
  bList: Array<Array<string | number>>,
  t: number,
  isPlayer: boolean,
): ViewBlob[] {
  const out: ViewBlob[] = [];
  const prev = new Map<string | number, Array<string | number>>();
  for (const e of aList) prev.set(e[0], e);

  for (const e of bList) {
    if (isPlayer && e[6] !== 1) continue;
    const id = String(e[0]);
    const bx = e[1] as number;
    const by = e[2] as number;
    const br = e[3] as number;
    const hue = isPlayer ? 0 : (e[4] as number);
    const p = prev.get(e[0]);
    if (p) {
      out.push({
        id,
        x: lerpNum(p[1] as number, bx, t),
        y: lerpNum(p[2] as number, by, t),
        r: lerpNum(p[3] as number, br, t),
        hue,
      });
    } else {
      out.push({ id, x: bx, y: by, r: br, hue });
    }
  }
  return out;
}
