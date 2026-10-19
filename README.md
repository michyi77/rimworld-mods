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

Taking it out again: remove the half walls and windows of a save first. Removing the mod while a save still holds them
leaves them as missing defs, and RimWorld deletes unknown things - the same as removing any mod.

## What it adds

- **Half walls**: a waist-high wall that acts as cover. It is a barricade, not a wall, so it encloses no room and holds
  up no roof. Drag one over a standing wall to cut that wall down.
- **Windows**: fit a window on top of a half wall (the button on the wall, or the Structure menu) and a colonist builds
  it, for two fifths of a door's cost. Once a window is in, that half wall counts as a real wall: it closes a room and
  holds a roof.
- Two solid boards in a frame, no glass. Open and close a window by hand: a colonist slides the boards apart or shut,
  and a half-open window carries on from where it stopped.
- **Ventilation**: shut, the window seals the room like a brick wall; open, heat passes through in proportion to how far
  the boards have moved, up to a vent's rate when it is fully open. On an orbital map an open window also lets the
  vacuum in.
- HP, build time, opening speed, flammability and beauty match a door of the same material; a wooden window costs 10
  wood, two fifths of a wooden door.

MIT License.

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
