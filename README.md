# BLOB BRAWL

> Fire yourself. Eat what's left.

An online arena brawl for up to 8 players. You are a blob of liquid metal. Your
attack is to **fire a chunk of yourself** at someone — it costs real mass, and
if it lands it tears mass off them. Everything the loser drops is loose goo that
anyone can eat, so every exchange is a visible transfer: the score isn't a
number going up, it's material flowing from one body into another.

Rounds are 90 seconds in a ring that closes in. Biggest blob standing wins.

The whole scene is one screen-space signed-distance field. Players, chunks in
flight, free pellets and splash all union with a polynomial smooth-min, so mass
moving between them reads as liquid rather than as sprites being swapped.

## Play it

```bash
npm install
npm run build
npm run server          # http://localhost:8787
```

Open the URL in two tabs (or two phones on the same network) to fight someone
real. Add `?offline=1` to play the same game against bots with no server.

Controls: twin analog sticks. **Left stick** moves, **right stick** aims — and
pushing the right stick *past the dashed ring* fires. **PULL** drags nearby
loose goo toward you so you can grow back after a fight. On desktop: WASD, mouse
aims, click fires, `E` pulls.

## Deploy it

The server is one Durable Object per match, with the built client served as
static assets from the same Worker — one origin, no CORS.

```bash
npx wrangler login
npm run deploy
```

Cloudflare's free plan covers roughly **29 concurrently-running matches**
(~230 players) before it costs anything: the free allowance is 313,000 GB-s/day
and a live match ticks 20 times a second, so it never hibernates while it's
being played.

## Test it

```bash
npm run sim-test        # balance soak, no browser, ~2s
npm run net-test        # 8 real WebSocket clients against the real server
npm run browser-test    # two real browsers in one arena, end to end
```

The simulation is fully deterministic — bots draw from the arena's seeded RNG,
not `Math.random` — so a seed reproduces a round exactly and the balance numbers
can be trusted and bisected.

| Test | What only it can catch |
|---|---|
| `sim-test` | Stalemates, bloodbaths, a leaking mass economy, bots that won't fight |
| `net-test` | Invisible players, ack regressions, bandwidth blowouts |
| `browser-test` | Shader compile failures, black frames, broken input, dead HUD |

## Layout

| Path | What's in it |
|---|---|
| `src/sim/` | The authoritative simulation and bot brains. Pure logic, no I/O. |
| `src/net/` | Wire protocol, the networked client, and the offline stand-in. |
| `src/shaders/` | The SDF scene pass, bloom, and final grade. |
| `server/room.ts` | Transport-agnostic match logic — runs on Node *and* Cloudflare. |
| `server/node.ts` | Local server: matchmaking plus static hosting. |
| `server/worker.ts` | Cloudflare Worker + Durable Object adapter. |
| `docs/GDD.md` | Design doc. `docs/LATER.md` — what was consciously cut. |

## Status

Vertical slice, verified end to end. **No audio yet**, and nothing has been
played by a human against another human — every number here comes from bots and
scripted clients, which is enough to prove the systems work and not enough to
prove the game is fun.
