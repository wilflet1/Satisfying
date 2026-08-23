# Controls

Everything here is rebindable in game (**F2**), including chords. Bindings are saved to PlayerPrefs.

## Default bindings

| Action | Key | Notes |
|--------|-----|-------|
| Move | W A S D | |
| Sprint | Left Shift | Forward only, stands you up, drains stamina |
| Jump / mantle / vault | Space | At a ledge you climb it; at a thin railing you go over it |
| **Sprint slide** | Sprint, then tap crouch | Needs real speed; goes lower than a crouch; jump out of it to keep the momentum |
| Crouch | C | Toggle by default |
| Prone | X | Toggle by default; from prone, C brings you to a crouch |
| Walk slowly | Left Ctrl | Hold to force the speed dial to its minimum |
| Speed dial | Mouse wheel | Continuous walk speed, shown on the HUD |
| Fire | Left mouse | |
| Aim | Right mouse | Lines the weapon's own sight up with the screen centre |
| Reload | R | Running dry starts it for you |
| **Lean** | Q / E | Hold, or switch to toggle in the controls panel |
| **Slow lean** | Alt + Q / Alt + E | Same lean at a fraction of the speed |
| **Free lean** | Alt + Q/E, then move the mouse | Analog lean; your view is locked while you set it |
| **Side step** | Alt + A / Alt + D | Shifts your body sideways without turning |
| **Blind fire** | V | Weapon over cover, head stays hidden |
| **Melee** | F | Bash with the stock; breaks glass and drops a man at arm's length |
| **Grab / drop** | E | Take hold of a movable object; press again to let go |
| Vault | Space | Only over obstacles 0.50–1.30 m with floor beyond; anything solid gets climbed instead |
| Blind fire angle | Mouse wheel while holding V | The only way to aim a shot you cannot see |
| Weapons | 1 / 2 / 3 | M4A1, MP5, USP45 |
| Scoreboard | Tab | |
| Tuning panel | F1 | |
| Controls panel | F2 | |
| Gear menu | G | Fit an optic to each weapon |
| Net graph | F3 | |
| Menu | Esc | |

## Chords

A binding can be a plain key or a key behind a modifier. While the modifier is held, the plain
binding on that key stands down — which is why `Alt+A` side steps instead of strafing, with no
special cases in the movement code.

To make one: open the controls panel, click the binding, then hold the modifier and press the key.

## Panels and the cursor

The menu (**Esc**) is modal. The tuning (**F1**) and controls (**F2**) panels are not: they release
the cursor so you can drag a slider, but the keyboard stays live — you can strafe, lean and change
stance while you tune, which is the only way to judge a movement value. Mouse look, firing and
aiming are held while the cursor is free, so clicking a slider never fires your weapon.

## Hold or toggle

Crouch, prone and lean can each be a hold or a toggle (controls panel). Free lean can be turned off
entirely if you would rather the mouse always turned your view.
