# Add windows to your house！

Half walls, and the sliding windows that go on them, for RimWorld 1.7 with
[IsoRim Waylith](https://github.com/peren717/IsoRim-1.7) and **IsoRim ProjectZ**.

## Install

1. Close RimWorld.
2. Download the latest `AddWindowsToYourHouse-*.zip` from the [Releases](../../releases) page and unzip it into
   `RimWorld\Mods\` - you should end up with `Mods\AddWindowsToYourHouse\About\About.xml` (one folder, not loose files).
3. Install **IsoRim ProjectZ** the same way if you do not have it (this mod needs it) and make sure **Harmony** and
   **IsoRim Waylith** are installed.
4. Start the game, open **Options > Mods**, and enable, in this order:
   **Harmony** -> **IsoRim Waylith** -> **IsoRim ProjectZ** -> **Add windows to your house！**
5. Restart the game when it asks.

Requires RimWorld 1.7, Harmony, IsoRim Waylith and IsoRim ProjectZ.

Works with **IsoRim ProjectZ as it is published** - nothing in the base mod has to be changed or updated for this
addon: every rule it needs from the base is patched from this side.

Taking it out again: remove the half walls and windows of a save first. Removing the mod while a save still holds them
leaves them as missing defs, and RimWorld deletes unknown things - the same as removing any mod.

## What it adds

- **Half walls**: a waist-high wall nothing crosses. Like a wall it closes a room, but it holds up no roof - and the day
  comes in over its top. Drag one over a standing wall to cut that wall down.
- **Windows**: fit a window on top of a half wall (the button on the wall, or the Structure menu) and a colonist builds
  it, for two fifths of a door's cost. One half wall takes one window. Once a window is in, that half wall counts as a
  real wall: it closes a room and holds a roof.
- Two solid boards in a frame, no glass. Open and close a window by hand: a colonist slides the boards apart or shut,
  and a half-open window carries on from where it stopped.
- **Light**: shut, the window blocks light like a wall; open, it lets the day in. How bright it gets and how far it
  reaches follow how far the boards are apart, and a lamp's light passes through an open window too. The light only
  goes to the darker side - a window never glows back into the sunlight it faces, and two windows never add up.
  A window set across the wall (sideways) is always open to the light, always ventilates at a fully open vent's rate
  and always leaks vacuum like a fully open window, because its boards slide along the wall and cover nothing. Shots
  go through it too, but only as a slot (below).
- **Shooting**: any crack lets a shot through. From 50% to 99% open - and always for a sideways window - the opening
  is a slot: shots go, but part of them are stopped by the window itself. Fully open (100%) is a clean shot. Below
  half open it is a crack: nine in ten shots are stopped by the window. Only a window shut tight is a wall - and even
  then the window can be attacked and shot apart like any building. It works both ways: colonists fire out, enemies
  fire in, and grenades, explosions and beam weapons pass too. Whoever stands beside the window is covered by the half
  wall like behind a barricade (0.55), not like a full wall (0.75). A shot a window stops lands on the window and
  damages it - a window shot apart leaves the bare half wall; a fully open window is never hit by bullets.
- **Ventilation**: shut, the window seals the room like a brick wall; open, heat passes through in proportion to how far
  the boards have moved, up to a vent's rate when it is fully open - with the next room and with the outdoors alike.
  On an orbital map an open window also lets the vacuum in. A window set across the wall always ventilates and always
  leaks vacuum, whatever its boards are doing.
- HP, build time, opening speed, flammability and beauty match a door of the same material; a wooden window costs 10
  wood, two fifths of a wooden door. Deconstruct the window and half of that comes back; take the wall down with the
  window still in it and the glazing is broken out instead, returning a quarter.

MIT License.

## Changelog

### 1.1.1

- Stays out of the way of IsoRim ProjectZ's own inter-floor sight rule: the walk that lets a shot through an open window
  now leaves lines that cross floors to the base mod, the same for its shot report.

### 1.1.0

- Shooting through windows: from half open up a window lets sight and shots through in both directions - colonists fire
  out, enemies fire in - and explosions, beam weapons and grenades pass too. Fully open (100%) is a clean shot; from
  50% to 99% open, and always for a window set across the wall (sideways), the opening is a slot: shots go, but part of
  them are stopped by the window itself. Below half open it is a crack: nine in ten shots are stopped by the window.
  A window shut tight blocks shots, but can still be attacked and shot apart like any building. The half wall under a
  window covers whoever stands beside it like a barricade (0.55), not like a wall (0.75).
- A window catches the bullets it stops: the boards, not the half wall under them, take the damage, and a window shot
  apart leaves the bare half wall.
- Ventilation with the outdoors as well as with the next room, for both window kinds.
- A window set across the wall always ventilates at a fully open vent's rate, always leaks vacuum, and always lets
  shots through as a slot, whatever its boards are doing.
- Fitting, sliding or taking out a window no longer throws the room's heat (and vacuum) away: the game rebuilds the
  room around the new wall, and a rebuilt room would otherwise start at the outdoor temperature.

### 1.0.0

- Light passes through windows: shut a window blocks it like a wall, open it lets the day in, in proportion to how far
  the boards are apart. A lamp's light goes through an open window as well, and the light only ever goes to the darker
  side.
- Sideways windows (set across the wall) are always open to the light.
- A bare half wall lets the day in over its top, and a roofed place that lost its wall stays lit by the opening.
- One window per half wall: a second one on the same cell, at any angle, is refused.
- Half walls are impassable, like a wall; a ring of them closes a room (they still hold no roof).
- Taking a glazed half wall down returns a quarter of the window's cost, the same as a window that was destroyed;
  deconstructing the window itself still returns half.
- Two rooms no longer both glow into each other, and stacked windows no longer add up past the light they let in.

## Building from source

The build needs the game's assemblies and IsoRim's, which are **not** in this repository. Put them in
`..\rimworld\refs\` (the ProjectZ checkout next to this folder) or point `-p:RefsDir=...` at them:

```
Assembly-CSharp.dll   from RimWorld/RimWorldWin64_Data/Managed/
UnityStubs.dll        the game's Unity API stubs
Waylith.dll           the Waylith runtime
IsoRim.dll            from the IsoRim Waylith workshop mod
0Harmony.dll          from the Harmony workshop mod
```

Then, with the .NET 10 SDK installed:

```powershell
.\build.cmd              # compile
.\build.cmd -Deploy      # compile and copy into RimWorld/Mods/IsoRimProjectZ-Windows
```
