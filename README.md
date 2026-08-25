# Satisfying

A 1v1 first-person duel built around Escape-from-Tarkov-style movement: lean, slow lean, free
lean, prone lean, side step, blind fire, sprint slides, vaulting, an analog speed dial, and
mantling — plus a stock bash that breaks glass, objects you can drag around by their weight, and
footsteps you can hear through a doorway but not through a wall. Server authoritative netcode,
client prediction, and every value tweakable while you play.

Everything is generated in code. There are no scenes, prefabs, models, textures or audio files to
import: clone it, open it, press play.

## Quick start

**Windows, in one paste:** run `tools/Install-Satisfying.ps1` in a PowerShell window. It downloads
the project, registers it with Unity Hub and opens it. Once you have a copy,
`tools/Update-Satisfying.ps1` pulls the latest and reopens it — it stashes anything you changed
rather than clobbering it, and refuses to pull while the editor has the project open. Otherwise:

1. Open the folder in Unity **6000.3** (anything from 2021.3 up will work — the project uses no
   packages beyond Unity's built-in modules, so there is nothing to download and nothing to resolve).
2. On the first load the editor script creates the boot scene, the custom layers and the build
   settings for you. If you ever need to re-run it: **Satisfying → Set up project**.
3. Press play. Enter a name, click **host a duel**.

### Two maps

Pick one before you host:

- **Duel arena** — the match map: hard corners, window sills at crouch and prone height, a roof you
  can only reach by mantling, and one long sightline.
- **Test range** — a drill course, one lane per ability: a vault row of railings at 0.55 / 0.80 /
  1.05 / 1.25 / 1.60 m, a sprint runway into tunnels only a slide fits through, a slide-jump gap, a
  mantle stack, a window to vault through, a lean gallery with dummies at increasing offsets, a
  prone bar, a side-step alley and a shooting range with posts at 10 / 20 / 40 / 80 m. Walk up to
  any station and the HUD tells you what it is for.

The server decides the map and clients rebuild to match, because prediction is only quiet when both
machines collide with the same geometry.

### Practising alone

Host a duel, open the menu and click **add a training bot**. It is a real player as far as the
server is concerned — it moves, holds angles, leans, changes stance and shoots back, and it makes
the match go live so hit registration and scoring work exactly as they will against a human.

### Getting a second player in

- **Same machine** (the fastest way to feel the netcode): with the editor hosting, use
  **Satisfying → Playtest → Launch a second player**. It builds a development player and starts it
  with `-connect 127.0.0.1`.
- **Same network**: the host broadcasts a LAN beacon; the other player just clicks the entry that
  appears under *on your network*.
- **Anywhere else**: while you host, the game asks your router to forward the port for it — UPnP
  first, then NAT-PMP — and shows the address to hand out with a copy button. If the router refuses,
  or you are behind carrier-grade NAT, run a dedicated server instead: a headless build, a script
  that installs it as a service, and a free-tier host that works from anywhere are all in
  **[docs/SERVER.md](docs/SERVER.md)**.

Command line, for scripted playtests:

```
Satisfying -host -port 7777
Satisfying -connect 192.168.1.20 -name challenger
Satisfying -batchmode -nographics -server -port 7777 -bots 1    # no player of its own
```

## The movement

| Move | Input | What it is |
|------|-------|------------|
| Lean | Q / E | Head slides out past cover; body and hitbox move with it |
| Slow lean | Alt + Q/E | The same lean at a quarter speed — creep an angle open |
| Free lean | Alt + Q/E, then move the mouse | Analog lean; hold any partial angle, view stays locked |
| Prone lean | Q/E while prone | A low roll, reduced angle, very slow turning |
| Side step | Alt + A / Alt + D | Shift your whole body sideways without turning |
| Blind fire | Hold V | Weapon over cover, head hidden; the wheel aims it |
| Speed dial | Mouse wheel | Continuous walk speed from a creep to a stride |
| Sprint slide | Sprint, then tap crouch | Spends the speed you built up; goes lower than a crouch |
| Slide jump | Jump out of a slide | Keeps the momentum instead of stopping dead |
| Vault | Space at a railing | Goes *over* thin obstacles and out the far side, still moving |
| Mantle | Space at a ledge | Pull yourself *onto* sills, crates and rooftops |
| Stances | C / X | Stand, crouch, prone, with real transition times and headroom checks |
| Stock bash | F | Melee with the butt of the weapon; kills at arm's length and puts glass out |
| Drag | E | Hands attach to an object; drag speed falls off with its mass |

Full list, chords and rebinding: **[docs/CONTROLS.md](docs/CONTROLS.md)**.

### On a phone

**Satisfying → Build → Android APK** produces a client that joins the same servers over the same
protocol — a full client, predicting and reconciling like the desktop one, not a remote view. The
left thumb is a stick that appears where you put it and sprints when pushed to its edge; the right
one looks, with fire, jump, crouch and lean under it. A finger keeps whatever job it started with
until it lifts, which is the difference between controls that work and controls that fight you.
**[docs/MOBILE.md](docs/MOBILE.md)**.

## A map you can take apart

**Glass** fills several of the openings. It stops nothing — a round passes through and takes the
pane with it — but while it is whole it muffles sound like any other wall. Break it, with a shot or
with the stock, and you open a firing lane and a listening post in the same motion.

**Objects** — crates, barrels, pallets, from a few kilos to a couple of hundred — can be dragged.
Hold **E** near one and both hands attach to it; how fast it moves and how much it slows *you* down
are both set by its mass, so a pallet is portable cover and a crate is a project. They are
simulated in the shared core and predicted on the client, so the thing you are pushing is where you
think it is at 250 ms.

**Footsteps carry the geometry.** Every remote sound is traced to your ear with three lines; the
more of them a wall blocks, the quieter and duller it gets. A player behind a wall is a rumour, one
through a doorway is a position — and because a broken pane has no collider, smashing a window
means hearing through it as well as shooting through it.

Every player, yourself included, is a full body: your legs, arms, lean and stance are rendered from
the same replicated fields your opponent sees, so looking down shows you what they are shooting at.

## Tweaking it while you play

**F1** opens the tuning panel. Every simulation and feel value in the game is there — over 240
sliders once the per-weapon and per-optic sets are expanded, generated by reflection, grouped by
category, with presets you can save and share.

**G** opens the gear menu: fit iron sights, a red dot or a holo to each weapon. The optic is a
simulation value, not a decoration — it scales aim-in time, aimed spread and zoom, and it travels
in the input stream so the server applies exactly what the client predicted.

The host owns simulation values and pushes changes to the client live, because prediction only
stays quiet when both machines run identical numbers. Feel values (FOV, bob, sway, sensitivity,
interpolation delay) are local and saved for you. See **[docs/TUNING.md](docs/TUNING.md)**.

**F3** shows the net graph: ping, jitter, input buffer depth, corrections, bandwidth.

## Netcode

64 Hz tick, client prediction with rewind-and-replay reconciliation, snapshot interpolation for
opponents, and lag-compensated hitscan that rewinds targets to what the shooter actually saw.
Written from scratch over UDP — about 1,500 engine-free lines. The menu has latency, jitter and
packet loss sliders so you can play at 150 ms without leaving your desk.

Details and the reasoning: **[docs/NETCODE.md](docs/NETCODE.md)**.

## Testing

The simulation and netcode have **no UnityEngine dependency at all**, which means the exact source
Unity compiles also compiles and runs under plain .NET:

```bash
dotnet run --project tools/SimTests    # 99 tests: movement, lean, slide, vault, melee, props, combat, netcode
dotnet build tools/UnityCheck          # type-checks the Unity layer against a stub UnityEngine
dotnet run --project tools/Playground  # launches the real game headlessly and reports what happened
```

`tools/SimTests` runs a real server and two real clients over an in-process link with injected
latency, jitter and loss, and asserts the things that actually matter: that prediction stays
silent, that hits register on a strafing target through 140 ms of lag, that they *stop* registering
when lag compensation is switched off, that reliable events survive 30% packet loss, and that a
duel bandwidth stays under 12 KB/s.

`tools/UnityCheck` compiles the Unity layer against a stub UnityEngine. It cannot prove the game
runs, but it proves every symbol resolves — it is how a `UnityEngine.EventType` collision was found
without ever opening an editor.

`tools/Playground` **launches the game** — a real authoritative server, a real predicting client,
real training bots, over a link with real latency and loss, with everything except the renderer. A
scripted runner drills the movement course, smashes a pane with the stock and drags a crate across
the yard; `--mode duel` fights the bots instead:

```
  mode drill   tick 64 Hz   link 60 ms one way, 12 ms jitter, 3% loss   bots 1

   time   phase      player                       speed  stance   event
    3.3s  Live      (  -0.5, -0.0, -16.4)     6.4  slide    slide
   14.1s  Live      (   0.1,  0.2,  20.9)     4.1  vault    VAULT
   84.3s  Live      ( -12.8, -0.0,  -4.6)     2.0  stand    grab

  slides 3   vaults 18   mantles 1   panes broken 1 of 1
  objects dragged 5.3 m over 5.0 s   round trip 149 ms
  prediction corrections 300  (last 2.3 cm)   4.2 KB/s down, 11.6 KB/s up
```

It is worth running for its own sake, not just as a check: capping a carrying player to the pace of
what they are dragging, and stopping a respawn from throwing away the prediction history, were both
found by reading these numbers rather than by playing.

All three are plain .NET projects outside `Assets/`, so Unity never sees them.

## Project layout

```
Assets/_Project/
  Scripts/Shared/      engine-free simulation, netcode and tuning (asmdef: noEngineReferences)
    Math/              vectors and helpers, plus the lofted geometry the character is built from
    Sim/               MovementCore - the single source of truth for how a player moves
                       BodyPose - and the single source of truth for where their bones are
    Combat/            deterministic spread, hitboxes, damage
    World/             breakable glass and draggable objects, simulated identically both ends
    Net/               protocol, transports, server, predicting client
    Config/            every [Tune] value in the game
  Scripts/Unity/       everything you can see and hear
    Core/              bootstrap, collision world, conversions
    Art/               procedural arena, character, weapons, materials, synthesised audio
    View/              camera rig, viewmodel, IK arms, opponent rendering, effects
    Input/             rebindable bindings with chords, input sampling
    UI/                menu, HUD, tuning panel, controls panel
  Editor/              project setup, builds, two-instance playtest
docs/                  design, netcode, controls, tuning, running a server, playing on a phone
tools/                 headless test harness and type checker (.NET, outside Unity)
```

The `Satisfying.Shared` assembly definition sets `noEngineReferences`, so Unity itself enforces
that the simulation never reaches for the engine. That constraint is what makes the headless tests
possible.

## Weapons and art

Three weapons — **M4A1**, **MP5**, **USP45** — built from the same box vocabulary and three shared
materials so they read as one set, each with its own gunshot, and each able to carry irons, a red
dot or a holo. The iron sights are a real notch and post with a gap you can see the target through,
not a solid block; the optics mount above them and put a reticle on the glass. Fire and reload are
animated procedurally from simulation state
(the bolt cycles when a round actually leaves; the magazine drops on the same timeline the server
is counting down), and the hands are two-bone IK rigs pinned to anchors on each weapon, so the
support hand follows the magazine through a reload without a single animation clip.

Swapping in real models is a documented, code-free-ish path:
**[Assets/_Project/Art/README.md](Assets/_Project/Art/README.md)**.

## Known limitations

- **Players do not collide with each other.** Predicting a collision against an opponent who is
  rendered in the past causes constant corrections; blocking is worth doing properly or not at all.
- **Aim angles are client authoritative** so recoil can be instant. A no-recoil cheat is possible.
  Fine for a duel test bench, not for a ranked shooter.
- **Direct IP and LAN only.** No relay, no NAT punch-through, no matchmaking. Port forward UDP 7777
  to play over the internet.
- **Dragged objects are kinematic, not physical.** They slide to where your hands are and stop
  against walls; they do not tumble, stack or fall. A rigidbody would need its own replication
  scheme to stay predictable, and the point of them here is cover you can move.
- **Glass is binary.** A pane is whole or gone — no cracks, no partial holes, no shards that
  persist.
- **No `.meta` files are committed.** Unity generates them on first import; commit them after that
  if you want stable GUIDs across machines.
- Desktop only — the netcode uses raw UDP sockets, which WebGL cannot do.
