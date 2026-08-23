# Satisfying — working notes

A 1v1 FPS built around movement: lean, slow lean, free lean, prone lean, side step, blind fire,
sprint slides, vaulting, mantling, an analog speed dial, a stock bash that breaks glass, and objects
you drag by their weight. Custom UDP netcode with client prediction. Everything is generated in
code — no scenes, prefabs, models, textures or audio files.

Read `README.md` first, then `docs/GDD.md` for what the game is trying to be. `docs/NETCODE.md`
explains the wire protocol and why it is shaped that way.

## The one rule that matters

**`Assets/_Project/Scripts/Shared/` must never reference UnityEngine.** Its asmdef sets
`noEngineReferences: true`, so Unity enforces it. That constraint is what lets the exact same
source run headless under plain .NET, which is where nearly every bug in this project has been
found. Geometry access goes through `ICollisionWorld`; there is no `Vector3`, no `Time`, no
`Random` down there.

If you need a new engine-free type, put it in `Shared/`. If you need to touch the engine, you are
in `Scripts/Unity/` and you are writing presentation, not simulation.

## Layout

```
Assets/_Project/
  Scripts/Shared/      simulation, netcode, tuning - engine free
    Math/              Vec3, MathK, ViewMath
    Sim/               MovementCore is the single source of truth for how a player moves
    Combat/            deterministic spread, hitboxes, damage
    World/             breakable glass, draggable objects, practice targets
    Net/               protocol, server, predicting client, port mapping
    Config/            every [Tune] value in the game
  Scripts/Unity/       everything you can see and hear
  Editor/              project setup, builds, two-instance playtest
docs/                  design, netcode, controls, tuning, running a server
tools/                 SimTests, UnityCheck, Playground - plain .NET, outside Unity
```

## Verifying a change

Three commands, none of which need Unity. Run all three before saying something works:

```bash
dotnet run --project tools/SimTests      # the real Shared source, ~99 tests
dotnet build tools/UnityCheck            # type-checks the Unity + Editor layer against a stub
dotnet run --project tools/Playground    # runs a real server + predicting client + bots headlessly
```

- **SimTests** compiles `Shared/**` verbatim and runs movement, combat and netcode tests over an
  in-process link with injected latency, jitter and loss. Add a test for anything you fix.
- **UnityCheck** compiles the Unity layer against `tools/UnityCheck/UnityEngineStub.cs`. It cannot
  prove the game runs, but it proves every symbol resolves. If you use a Unity API the stub lacks,
  add it to the stub rather than working around it.
- **Playground** launches the actual game with no renderer: `--mode drill` runs the movement course,
  `--mode duel` fights bots. Read its summary — the drag pacing problem and the respawn correction
  storm were both found by looking at those numbers, not by playing.

Do not claim something is fixed on the strength of reading the diff. This project has a headless
harness precisely so that claims can be checked.

## Tuning

Every gameplay number is a `[Tune]` field in `Scripts/Shared/Config/`. The attribute carries a
category, a range and a tip, and the panel is built from it by reflection — adding a knob is one
line and no UI work. Simulation values are host-authoritative and pushed to clients as a text diff;
feel values are local and saved to PlayerPrefs.

`F1` in game opens the panel. Its **copy changes** button puts every value moved away from the
defaults on the clipboard as text, which is the right way to receive tuning from a playtest.

## Conventions

- C# 9 (Unity's level). No `var` in this codebase; explicit types throughout.
- Comments explain *why*, especially where a decision looks odd. Do not narrate what the code does.
- No `.meta` files are committed; Unity regenerates them.
- Deterministic randomness only in simulation: `DeterministicRandom.ForShot(...)`. Never
  `UnityEngine.Random` anywhere that affects the sim.
- Both ends build the map from the same code, so only *state* is replicated, never shapes.

## Things that have bitten before

- **Two actions on one key both fire.** Grab and lean-right shared `E`, so every grab also leaned
  you. `InputBindings.AllConflicts()` exists now; the defaults must stay clash-free.
- **A button edge only exists on the tick it happened.** Presses travel as counters
  (`InputCommand.GrabSeq` / `MeleeSeq`) compared with a half-space test, so loss cannot swallow one
  and a repeat cannot invent one. Do not add new edge-triggered buttons without a counter.
- **Gating world effects on match phase.** Shots used to resolve only when the match was live, so
  alone in the arena nothing broke glass. Damage waits for the match; the world does not.
- **A wall and the pane filling it must be built by one call** (`ArenaBuilder.GlazedWall`), or the
  round stops on the wall and the glass can never be shot out.
- **Client tick numbering starts at the server's.** Anything that sanity-checks an incoming tick
  must do so relative to a stream that has actually been adopted.

## Running it

Open in Unity 6000.3 (2021.3+ works; no packages beyond built-in modules). The editor script
creates the boot scene, layers and build settings on first load — **Satisfying → Set up project**
to re-run. Press Play, host a duel, and use **Satisfying → Playtest → Launch a second player** for
a real 1v1 against yourself.

Unity's own log is the place to look when the editor misbehaves:
`%LOCALAPPDATA%\Unity\Editor\Editor.log` on Windows.
