# Netcode

The goal is that **your own movement never waits for the network**, while the server still decides
what actually happened. That means prediction on the client, authority on the server, and a
reconciliation step that is quiet enough you never notice it.

There is no netcode package. The whole stack is about 1,500 lines in
`Assets/_Project/Scripts/Shared/Net`, has no UnityEngine dependency, and is exercised end to end by
`tools/SimTests` over a simulated lossy link.

## Shape of a session

Hosting starts a real server and then connects to it over loopback like any other client, so there
is exactly one gameplay path — the host is never a special case, and a bug that only shows up for
clients cannot hide.

```
host process                                   client process
+-------------------+                          +-------------------+
| NetServer         |  <-- inputs (unreliable) | NetClient         |
|  authoritative    |                          |  prediction       |
|  simulation       |  snapshots (unreliable)  |  reconciliation   |
|  lag compensation | -->                      |  interpolation    |
+-------------------+                          +-------------------+
        ^                                              |
        | loopback UDP                                 | UDP
        +---------------- NetClient (host's own) <-----+
```

## The tick

Everything runs at **64 Hz** (`Protocol.TickRate`). The client samples input once per tick, steps
`MovementCore` immediately with it, and sends it. The server steps the same `MovementCore` with the
same command and broadcasts the result.

Because both sides run identical code over identical inputs, the two agree unless something
external happens — which is exactly when a correction is wanted.

## Client to server: inputs

One `InputCommand` is 17 bytes packed (`InputCommand.Write`). Every packet repeats the last
**12** commands, so a lost packet costs nothing as long as one of the next twelve arrives. The
server keys them by client tick and ignores duplicates.

The server holds a small jitter buffer of inputs and reports how deep it is. The client uses that
number to steer its own clock: run slightly fast when the buffer is starving, slightly slow when
it is backing up (`ClientNetTuning.inputBufferTarget`). That keeps latency at the true minimum for
the connection rather than a fixed padding.

If an input never arrives, the server repeats the previous one with the one-shot buttons stripped
(`InputCommand.Repeat`) — holding a direction is a much better guess than freezing.

## Server to client: snapshots

Each snapshot carries the server tick, the last input tick executed for that client, the buffer
depth, the match phase, and one quantised `PlayerNetState` per player (~19 bytes each). A 1v1 duel
runs comfortably under 12 KB/s each way, asserted by a test.

Riding along inside the snapshot is a small reliable channel for things that must not be lost:
spawns, deaths, hit confirmations, score, match phase and tuning pushes. It numbers payloads,
repeats them until acked, drops duplicates, and fragments anything larger than 700 bytes.

The ack is the highest **contiguous** sequence received, not the highest seen — acking the highest
seen would silently drop a payload that fell in a hole.

## Prediction and reconciliation

The client keeps the last 256 ticks of `(input, resulting state)`. When a snapshot says "at your
tick 4,910 you were here", the client compares that with what it predicted for 4,910:

- Within 3.5 cm and 1.2 m/s → do nothing. This is the normal case.
- Otherwise → snap to the server's state and **re-simulate every input since**, in one frame.

Re-simulation is why the collision code is a plain function over an interface rather than a
`CharacterController`: it has to be safe to run ten times inside one frame.

A correction that would visibly jolt the camera is smoothed instead: the positional error is kept
as a decaying render-only offset (`errorSmoothTime`), so the camera glides the last few
centimetres while the simulation is already correct.

## Remote players

Other players are drawn from snapshots interpolated ~55 ms in the past
(`ClientNetTuning.interpolationDelayMs`). Never lerp toward the latest position — that is what
rubber-banding looks like. A test walks an opponent around and asserts no frame-to-frame jump
larger than 20 cm.

## Lag compensation

Every input command carries the fractional server tick the client was *rendering others at*. When
the server processes a shot it rewinds each target to that tick, rebuilds the hitboxes from the
historical state, and tests the ray against those.

The hitboxes come from the same `PlayerSimState` that drives the visuals, so a lean really does
move the head you are shooting at. One second of history is kept per player.

Two tests hold this honest: one fires at a strafing opponent through 140 ms of latency and asserts
the hits land; the other turns `NetServer.LagCompensation` off and asserts the same shots miss.

## What the server does not trust

Clients send intent only. The server owns position, health, ammo, damage, score and the match
clock. Weapon spread is derived from a deterministic PRNG seeded by
`(playerId, shotIndex, pellet)`, so the client can draw the exact ray the server will test without
being asked what it hit.

The one thing the client is trusted with is **aim angles**, because recoil has to be applied
locally to feel instant. A no-recoil cheat is therefore possible; that is the accepted trade for a
duel test bench, and the note is here so nobody assumes otherwise.

## Testing it

`tools/SimTests` runs a real `NetServer` and two real `NetClient`s over an in-process transport
that injects latency, jitter and loss, stepping a virtual clock. That is where prediction quality,
lag compensation, reliability under 30% loss, bandwidth and the match flow are all asserted.

The same conditioning is available in the game: the network simulator sliders in the menu wrap the
real UDP transport, so you can feel 150 ms before you ship anything.
