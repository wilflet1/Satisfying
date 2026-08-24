# Playing on a phone

A phone joins a duel the same way a PC does — same protocol, same simulation, same server. What
changes is the input: thumbs instead of a keyboard, producing the identical `InputCommand`, so
nothing below that line knows the difference.

## Building the APK

You need Unity with the **Android Build Support** module (Unity Hub → your version → Add modules →
Android Build Support, including *OpenJDK* and *Android SDK & NDK Tools*). That download is a few
gigabytes and is the only slow part.

Then: **Satisfying → Build → Android APK**.

It lands in `Builds/Android/Satisfying.apk`. From a terminal instead:

```
Unity -batchmode -quit -projectPath . -executeMethod Satisfying.Editor.BuildScript.BuildFromCommandLine -target android
```

The build sets what an installable APK needs, rather than leaving it to whoever last opened the
inspector: ARM64 with IL2CPP (32-bit-only builds are refused by Play and by most modern phones),
the INTERNET permission forced on (the netcode is raw UDP; Unity usually infers this, and "usually"
is how you ship a build that cannot open a socket), landscape only, and minimum API 24.

**Development APK** is the other menu item. Same thing with a profiler and a readable log:

```
adb logcat -s Unity
```

## Getting it onto the phone

Copy the `.apk` across and open it — Android will ask you to allow installs from that app. Or with
the phone plugged in and USB debugging on:

```
adb install -r Builds/Android/Satisfying.apk
```

`-r` reinstalls over the top, keeping your settings.

## The controls

| Thumb | Does |
|-------|------|
| **Left, anywhere on the left half** | Move. The stick appears where you put your thumb rather than sitting in a fixed circle you have to find |
| **Push the stick to its edge** | Sprint. No separate button — the stick already knows how hard you are pushing |
| **Right, anywhere not on a button** | Look |
| **FIRE** (bottom right) | Shoot |
| **JUMP** | Jump, mantle, vault |
| **CRCH** | Crouch. Latches, so it does not tie up a thumb |
| **Q / E** (top right) | Lean |

A finger keeps whatever job it started with until it lifts. That is the whole trick: without it,
sliding your left thumb past the middle of the screen starts turning the camera, and reaching for
the trigger yanks your aim. There are ten tests on that behaviour alone.

Not on the phone build: prone, side step, blind fire, the speed dial, the stock bash, dragging.
They need either a modifier or a spare thumb, and a first mobile pass is better honest than
cluttered.

## Joining

Type the address the host was given — `1.2.3.4:7777` works with the port on it. On the same Wi-Fi
the host appears by itself under *on your network*; tap it and you are in.

**Same Wi-Fi is by far the easiest test** and needs no port forwarding at all. Over mobile data you
need the host reachable from the internet, which is `docs/SERVER.md`.

## What to expect

The whole game runs on the phone — it is a full client, predicting and reconciling exactly like the
desktop one, not a remote view. The arena is built from the same code, so it costs a few hundred
draw calls and a mid-range handset holds 60.

Mobile data adds latency but the netcode is built for it: prediction hides your own movement
entirely, and opponents are interpolated. 5 KB/s down and 10 KB/s up, so a duel is a few megabytes.

## Trying the controls without a phone

```
Satisfying -touch
```

Forces the touch path on desktop, where a mouse acts as a single finger. Enough to check the layout
and that the buttons are where they should be; not enough to judge how it feels, since one finger
cannot move and look at once.
