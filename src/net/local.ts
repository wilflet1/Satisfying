import { Arena } from '../sim/arena.ts';
import { driveBot } from '../sim/bots.ts';
import { ARENA } from '../sim/config.ts';
import type { Roster, View, ViewBlob, ViewEvent } from './client.ts';

/**
 * Offline mode: the same Arena the server runs, ticking inside the browser
 * against bots.
 *
 * It exists for two reasons. It is the only way to play where a WebSocket can't
 * be opened — a sandboxed embed, a flaky train connection, a dead server. And
 * because it is the identical simulation rather than a re-implementation, it
 * cannot drift from the online rules: a balance change lands in both at once.
 */
export class LocalGame {
  status: 'live' = 'live';
  id = 'me';
  roster: Roster[] = [];
  ping = 0;
  error = '';

  private arena = new Arena(Date.now());
  private accum = 0;
  private moveX = 0;
  private moveY = 0;
  private aimX = 1;
  private aimY = 0;
  private pendingDash = false;
  private pendingPull = false;
  private drained: { hits: ViewEvent[]; deaths: ViewEvent[]; dashes: ViewEvent[] } = {
    hits: [],
    deaths: [],
    dashes: [],
  };

  connect(_url: string, name: string) {
    this.arena.addPlayer(this.id, name || 'You', false);
    for (let i = 0; i < ARENA.match.fillTo - 1; i++) {
      this.arena.addPlayer(`bot${i}`, BOT_NAMES[i % BOT_NAMES.length], true);
    }
    this.syncRoster();
  }

  disconnect() {}

  private syncRoster() {
    this.roster = [...this.arena.players.values()].map((p) => ({
      id: p.id,
      name: p.name,
      hue: p.hue,
      bot: p.bot,
    }));
  }

  dash() {
    this.pendingDash = true;
  }
  pull() {
    this.pendingPull = true;
  }

  sendInput(moveX: number, moveY: number, aimX: number, aimY: number) {
    this.moveX = moveX;
    this.moveY = moveY;
    if (Math.hypot(aimX, aimY) > 0.01) {
      this.aimX = aimX;
      this.aimY = aimY;
    }
  }

  /** Drives the simulation on a fixed step, however fast the display runs. */
  predict(dt: number, moveX: number, moveY: number) {
    this.moveX = moveX;
    this.moveY = moveY;
    const step = 1 / ARENA.tickRate;
    this.accum = Math.min(this.accum + dt, step * 5);

    while (this.accum >= step) {
      this.accum -= step;
      this.arena.setInput(this.id, {
        moveX: this.moveX,
        moveY: this.moveY,
        aimX: this.aimX,
        aimY: this.aimY,
        dash: this.pendingDash,
        pull: this.pendingPull,
        seq: 0,
      });
      this.pendingDash = false;
      this.pendingPull = false;

      for (const p of this.arena.players.values()) if (p.bot) driveBot(this.arena, p, step);
      this.arena.step(step);

      for (const h of this.arena.events.hits) {
        this.drained.hits.push({ x: h.x, y: h.y, hue: h.hue, power: h.power });
      }
      for (const d of this.arena.events.deaths) {
        this.drained.deaths.push({ x: d.x, y: d.y, hue: d.hue, power: 1 });
      }
      for (const d of this.arena.events.dashes) {
        this.drained.dashes.push({ x: d.x, y: d.y, hue: d.hue, power: 1 });
      }
    }
  }

  view(): View {
    const me = this.arena.players.get(this.id);
    const blob = (
      id: string,
      x: number,
      y: number,
      r: number,
      hue: number,
    ): ViewBlob => ({ id, x, y, r, hue });

    const players: ViewBlob[] = [];
    for (const p of this.arena.players.values()) {
      if (!p.alive || p.id === this.id) continue;
      players.push(blob(p.id, p.x, p.y, p.r, p.hue));
    }

    const events = this.drained;
    this.drained = { hits: [], deaths: [], dashes: [] };

    return {
      phase: this.arena.phase,
      timer: this.arena.timer,
      ring: this.arena.ringRadius,
      winner: this.arena.winner,
      players,
      chunks: this.arena.chunks.map((c) => blob(String(c.id), c.x, c.y, c.r, c.hue)),
      pellets: this.arena.pellets.map((g) => blob(String(g.id), g.x, g.y, g.r, g.hue)),
      me: me && me.alive ? blob(me.id, me.x, me.y, me.r, me.hue) : null,
      alive: !!me?.alive,
      mass: me?.mass ?? 0,
      kills: me?.kills ?? 0,
      hits: events.hits,
      deaths: events.deaths,
      dashes: events.dashes,
    };
  }
}

const BOT_NAMES = ['Blorp', 'Gloop', 'Squish', 'Wobble', 'Splat', 'Dollop', 'Gunk', 'Ooze'];
