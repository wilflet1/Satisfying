# Handover: things only a machine with Unity on it can settle

You are Claude Code running **locally**, on the developer's PC, in a checkout of Satisfying. The work
in this branch was written in a cloud container with **no Unity install**, so a specific set of
claims are unverified. You have the editor. Go and check them.

Read `CLAUDE.md` first - it has the conventions and the three headless commands. Everything below
assumes you have.

**Ground rules for this session**

- Fix what you find. Do not just report it.
- Run the three headless commands before and after any change you make:
  `dotnet run --project tools/SimTests`, `dotnet build tools/UnityCheck`,
  `dotnet run --project tools/Playground`.
- Anything you can only verify by looking at the screen, **look at it**, and say in your report that
  you looked. Do not infer it from the source.
- Commit as you go, on this branch, with the message style already in the log.
- If a claim below turns out to be right, say so in one line and move on. Most of them should be.

---

## 1. Does it actually compile in Unity?

This is first because it is the one that blocks everything else, and because it has already bitten
once: `tools/UnityCheck` type-checks the Unity layer against a **hand-written stub** of UnityEngine,
and a stub can be confidently wrong. A previous session had `PlayerSettings.defaultInterfaceOrientation`
typed as `ScreenOrientation` instead of `UIOrientation`; UnityCheck was perfectly happy and the real
editor threw CS0266.

Open the project in Unity 6000.3 and read the console.

- Zero errors, or fix them.
- For **every** error you fix, also fix `tools/UnityCheck/UnityEngineStub.cs` so the stub would have
  caught it. That file's header says a wrong stub is worse than a missing one; make it true.
- Newly added stub surface worth double-checking specifically: `Mesh.vertices` / `normals` / `uv` /
  `triangles` / `RecalculateNormals` / `RecalculateBounds`, and `GameObject.AddComponent<MeshFilter>()`.

## 2. Look at the character

`Blockout.Duellist` was rewritten this week from eleven boxes to lofted meshes driven by
`BodyPose`. `tools/SimTests` proves every generated shape is closed, wound outwards and exactly the
size it claims - but nobody has ever seen one.

Press Play, host a duel, and add a bot (or use **Satisfying → Playtest → Launch a second player**).
Then get eyes on the opponent and screenshot each of these:

- Standing, front and side.
- Crouched, front and side.
- Prone, front and side.
- Sprinting (the walk cycle), and mid-slide.
- Dead - the collapse animation folds the skeleton about the hips and drops it.

What to look for, in order of how badly it would matter:

1. **Anything inside out.** A shape wound backwards is invisible from outside and solid from inside.
2. **Gaps at the joints.** Bones meet end to end with a chamfer; if you can see through a shoulder or
   a knee, the `Fill` or the chamfer profile is wrong.
3. **Proportions.** He should read as a person, not a mannequin. Arms 0.65 m shoulder to grip, legs
   folding rather than shrinking when he crouches, shoulders clearly wider than the waist.
4. **Prone.** He lays out roughly 1.2 m *behind* his own head. Check that reads as a man lying down
   and not a man being dragged.
5. **The gun in his hands.** The right hand should be closed on the grip and the left on the
   handguard - both come from `BodyPose`, and the foregrip anchor is derived from
   `supportHandReach`/`supportHandRise`, so if the hand is floating those numbers are wrong for that
   weapon.

## 3. The reported bug: the sight picture crouched and prone

The original report was *"when crouched the ads lineup is bad, it's hard to see anything; prone is
even worse."* Two causes were found and fixed. **Confirm both fixes with your own eyes**, because
this is the one the developer will judge the session on.

For **each of the three weapons** (1/2/3) and **each of the three sights** (gear menu, `G`):
stand, crouch, go prone, aim down sights, and screenshot.

- The rear notch and front post must line up on the exact centre of the screen in all three stances.
  Not approximately - the fix was to make every pose flourish fade out with `Ads`, and there is a
  test (`ads/the sight lands dead centre in every stance`) asserting it to half a millimetre.
- Nothing may be blocking the view. The old bug was your own leg: the view posed the character by
  writing `localScale = (1, heightFactor, 1)` onto the leg boxes, whose scale *is* their size, so
  each leg became a one-metre slab you were standing inside.
- Also look **down** in each stance. You have a first-person body; your chest and legs should be
  below you where they belong, and nothing should be clipping the near plane.

## 4. Hit zones

The body is now fifteen capsules and seven zones - head, neck, chest, stomach, arm, leg, foot -
where it used to be head/body/limb. The kill feed labels the zone.

Use the shooting range lane and a second player (or a bot standing still):

- Shoot each part and check the feed says what you shot. Head, neck, chest, stomach, arm, leg, foot.
- Shoot the **gap between someone's legs** at close range from the front. It should miss. That gap is
  new and deliberate.
- Shoot an arm held out over cover during a blind fire (`V`). It should register as an arm.
- Check no part of him is visible but unshootable - especially the helmet, the ear cups and the
  boots. The rule is written down in `Assets/_Project/Scripts/Unity/Art/README.md`: nothing drawn may
  stick out past its capsule. The shoulders are the one deliberate exception.

## 5. Two players, for real

**Satisfying → Playtest → Launch a second player.** Everything above again, but watching the *other*
instance's model rather than your own:

- Lean, slow lean, free lean, prone lean - does the head really move out past cover on the other
  screen, and does shooting it connect?
- Vault, mantle, slide - the poses for these live in `BodyPose` now, so the model and the hitbox
  agree by construction. Check they look like what they are.
- Reload and the empty-magazine bolt hold-open. The M4 and USP lock back on empty; the MP5
  deliberately does not.

## 6. The Android build

`Satisfying → Build → Android APK`. It has never been run. Needs the Android Build Support module.

- If it builds, install it (`adb install -r Builds\Android\Satisfying.apk`) and join a duel on the
  same Wi-Fi as the PC. Move around, shoot, look at the touch controls.
- If it does not build, fix `Assets/_Project/Editor/BuildScript.cs` and, again, fix the stub so the
  failure would have been caught headlessly.

## 7. Anything left over

- `Satisfying → Set up project` should be safe to re-run. Confirm it is.
- The tuning panel (`F1`) is built by reflection over `[Tune]` fields; several were added this week
  (`neckMultiplier`, `stomachMultiplier`, `supportHandReach`, `supportHandRise`). Check they appear
  and that moving them does what they say.
- Frame rate with two players and the new meshes. A duellist is a few thousand triangles and about
  twenty-six renderers; if that is a problem, say so with numbers rather than guessing.

---

## Report back like this

When you are done, write a short summary in this shape and leave it in the terminal:

```
verified   - things that were claimed and are true
fixed      - things that were wrong, and what you changed
still bad  - things that are wrong and you could not fix, with what you tried
not done   - anything you skipped, and why
```

Attach the screenshots. The developer has been asked to judge this work on a screenshot twice now and
has been right both times.
