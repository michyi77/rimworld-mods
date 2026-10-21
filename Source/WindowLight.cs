using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // The light a window lets through (user, 2026-10-20).
    //
    // Three things the game cannot do by itself:
    //   - its light blocker is one yes/no per cell (GlowGrid.lightBlockers, set from def.blockLight), so a half-open window
    //     either passed a lamp's light whole or not at all; the player wants it in proportion to how far the boards are
    //     apart.
    //   - a light on the glow grid shines in every direction, so the window that lit the room also glowed back into the
    //     daylight side and looked like a lamp. The light should go only towards the darker side.
    //   - its own light reads cap a light source at half brightness (GlowGrid.GroundGlowAt), so even a wide open window
    //     could never read like the barricade a half wall without a window is. A window that is fully open should let the
    //     day in whole.
    //
    // So a window puts nothing on the glow grid. Its transmission is worked out here, cell by cell: which side of the
    // window is the brighter one, what colour that light has and how bright it is, and how far it reaches (all with
    // openPct). The result is two small tables per map - a colour for the picture and a brightness for the gameplay
    // figure - and the game's own light reads are answered with them added on top.
    //
    // The map mesh has to be told as well: the light the player sees is baked into the light overlay per map section, and
    // sections only rebuild when they are dirtied. (That was the bug the user hit on 2026-10-20: the gameplay read said
    // 50%, the picture never changed.)
    public static class WindowLight
    {
        // How far past the window the light reaches, in cells: just cracked open, fully open.
        private const float MinReach = 1.2f;

        private const float MaxReach = 4f;

        // Two sides closer than this in brightness have nothing to pass through.
        private const float MinDifference = 0.05f;

        // The bare half wall (no window in it): low enough for the day to come over its top.
        private const string BareHalfWallDefName = "PZ_HalfWall";

        // ProjectZ's gap rows between storeys (ProjectZ.IsVoid): open air on the map, but no sky - the annex keeps one
        // band of rows per floor with a void band between them, and VoidRule already marks those rows as light blockers.
        // They must not count as "outside" here either, or every storey would get a lit strip along its map edges.
        private const string VoidTerrainDefName = "PZ_OpenAir";

        // What a roof takes of the sky, as the light overlay marks it (SectionLayer_LightingOverlay:
        // RoofedAreaMinSkyCover = 100 out of 255). The shader keeps 1 - RoofCover of the sky under a roof, so a roofed
        // cell the game does not treat as an indoor room is already this bright and only this much is missing.
        private const float RoofCover = 100f / 255f;

        // A window being worked asks on every step of the slide; the tables are worked out at most this often.
        private const int RebuildTicks = 8;

        // ...and the plain periodic look (for the sun, the lamps and any roof that changed) is this often.
        private const int RefreshTicks = 60;

        private sealed class Field
        {
            // The tables the game reads.
            public Color32[] colours;
            public byte[] levels;

            // The tables being built; swapped in when they are done.
            public Color32[] builtColours;
            public byte[] builtLevels;

            // Cells each table wrote to, so only cells whose light changed get their map mesh dirtied.
            public List<int> touched = new List<int>();
            public List<int> builtTouched = new List<int>();
        }

        private static readonly Dictionary<Map, Field> fields = new Dictionary<Map, Field>();

        private static readonly Dictionary<Map, int> builtTicks = new Dictionary<Map, int>();

        private static readonly List<Map> gone = new List<Map>();

        // Set while the tables are being worked out: the reading they are worked out from must be the glow grid's own,
        // never the tables of the build before them.
        public static bool Computing;

        // The field the patches asked for last time (the light map asks for thousands of cells in a row).
        private static Map cachedMap;
        private static Field cachedField;

        private static AccessTools.FieldRef<GlowGrid, Map> mapField;
        private static bool mapFieldTried;

        // One line in the log the first time light actually goes into the map's picture, so a report can tell whether the
        // patches are doing their work at all.
        private static bool logged;

        public static void RequestRebuild(Map map)
        {
            if (map == null)
            {
                return;
            }
            int last;
            if (builtTicks.TryGetValue(map, out last) && GenTicks.TicksGame - last < RebuildTicks)
            {
                return;
            }
            Rebuild(map, false);
        }

        // The slow look: the sun moved, a lamp was switched, or a roof went up or came down. Many things ask (every window
        // and every bare half wall), so it is throttled hard - one rebuild a second at most.
        public static void RequestRefresh(Map map)
        {
            if (map == null)
            {
                return;
            }
            int last;
            if (builtTicks.TryGetValue(map, out last) && GenTicks.TicksGame - last < RefreshTicks)
            {
                return;
            }
            Rebuild(map, true);
        }

        public static void Rebuild(Map map)
        {
            Rebuild(map, true);
        }

        public static void Rebuild(Map map, bool withOpenings)
        {
            if (map == null || map.cellIndices.NumGridCells <= 0)
            {
                return;
            }
            Field f = FieldFor(map);
            Array.Clear(f.builtColours, 0, f.builtColours.Length);
            Array.Clear(f.builtLevels, 0, f.builtLevels.Length);
            f.builtTouched.Clear();
            builtTicks[map] = GenTicks.TicksGame;
            Prune(map);
            Computing = true;
            try
            {
                Deposit(map, f, Building_WindowGlass.ClosedDefName);
                Deposit(map, f, Building_WindowGlass.OpenDefName);
                DepositHalfWalls(map, f);
                // The roof-edge openings only change when roofs or walls do, so they are looked at on the slow refresh
                // rather than on every step of a window being slid open.
                if (withOpenings)
                {
                    DepositOpenEdges(map, f);
                }
            }
            catch (Exception e)
            {
                // A half-worked table must not take the game down with it: keep the previous light and say so once.
                Log.ErrorOnce("[ProjectZ] windows: working out the light failed, the windows keep their last light: " + e, 0x5A17C7);
                Computing = false;
                return;
            }
            finally
            {
                Computing = false;
            }
            DirtyChanged(map, f);
            // Swap the finished tables in.
            Color32[] colours = f.colours;
            f.colours = f.builtColours;
            f.builtColours = colours;
            byte[] levels = f.levels;
            f.levels = f.builtLevels;
            f.builtLevels = levels;
            List<int> touched = f.touched;
            f.touched = f.builtTouched;
            f.builtTouched = touched;
            cachedMap = map;
            cachedField = f;
        }

        private static Field FieldFor(Map map)
        {
            Field f;
            int cells = map.cellIndices.NumGridCells;
            // A map can come back with a different cell count (a new map object, or a layout change): never trust an old
            // table then, it would be indexed past its end.
            if (fields.TryGetValue(map, out f) && f.colours.Length == cells)
            {
                return f;
            }
            f = new Field
            {
                colours = new Color32[cells],
                levels = new byte[cells],
                builtColours = new Color32[cells],
                builtLevels = new byte[cells]
            };
            fields[map] = f;
            return f;
        }

        // Drops the tables of maps that are no longer loaded (the one being worked on is always kept).
        private static void Prune(Map keep)
        {
            if (fields.Count == 0 || Find.Maps == null)
            {
                return;
            }
            gone.Clear();
            foreach (Map map in fields.Keys)
            {
                if (map != keep && !Find.Maps.Contains(map))
                {
                    gone.Add(map);
                }
            }
            for (int i = 0; i < gone.Count; i++)
            {
                fields.Remove(gone[i]);
                builtTicks.Remove(gone[i]);
                if (cachedMap == gone[i])
                {
                    cachedMap = null;
                    cachedField = null;
                }
            }
            gone.Clear();
        }

        // Only the cells whose light changed are dirtied: the picture is the map's light overlay, which is rebuilt per
        // section and only when its mesh is dirty.
        private static void DirtyChanged(Map map, Field f)
        {
            if (map.mapDrawer == null)
            {
                return;
            }
            for (int i = 0; i < f.touched.Count; i++)
            {
                DirtyIfChanged(map, f, f.touched[i]);
            }
            for (int i = 0; i < f.builtTouched.Count; i++)
            {
                DirtyIfChanged(map, f, f.builtTouched[i]);
            }
        }

        private static void DirtyIfChanged(Map map, Field f, int index)
        {
            Color32 was = f.colours[index];
            Color32 now = f.builtColours[index];
            if (was.r == now.r && was.g == now.g && was.b == now.b && f.levels[index] == f.builtLevels[index])
            {
                return;
            }
            map.mapDrawer.MapMeshDirty(map.cellIndices.IndexToCell(index), (ulong)MapMeshFlagDefOf.GroundGlow);
        }

        private static void Deposit(Map map, Field f, string windowDefName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(windowDefName);
            if (def == null)
            {
                return;
            }
            List<Thing> windows = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < windows.Count; i++)
            {
                Building_WindowGlass window = windows[i] as Building_WindowGlass;
                if (window != null && window.Spawned)
                {
                    DepositOne(map, f, window);
                }
            }
        }

        // The light this one window passes, written into the cells of the darker side.
        private static void DepositOne(Map map, Field f, Building_WindowGlass window)
        {
            CompWindowLatch latch = window.Latch;
            if (latch == null)
            {
                return;
            }
            IntVec3 pos = window.Position;
            // Which way the opening really faces is a question about the wall, not about the window's own turn (see
            // WindowOrientation): a window set across the wall has the wall running along its own facing axis, so its two
            // sides are the cells across the wall instead.
            bool sideways;
            IntVec3 axis = WindowOrientation.Axis(window, out sideways);
            // A sideways window is always open to the light: its boards slide along the wall and never cover the opening,
            // whatever the player asked them to do (user, 2026-10-20).
            float open = sideways ? 1f : Mathf.Clamp01(latch.openPct);
            if (open <= 0.001f)
            {
                return;
            }
            IntVec3 sideA = pos + axis;
            IntVec3 sideB = pos - axis;
            float levelA;
            float levelB;
            Color colourA;
            Color colourB;
            if (!SideLight(map, sideA, out levelA, out colourA) || !SideLight(map, sideB, out levelB, out colourB))
            {
                return;
            }
            float difference = levelA - levelB;
            if (Mathf.Abs(difference) <= MinDifference)
            {
                return;
            }
            // Only the darker side is lit: the sunlit side of a window must not glow back into the day.
            bool aIsBrighter = difference > 0f;
            IntVec3 darkCell = aIsBrighter ? sideB : sideA;
            Color bright = aIsBrighter ? colourA : colourB;
            float brightLevel = aIsBrighter ? levelA : levelB;
            DepositField(map, f, darkCell, bright, brightLevel, open);
        }

        // Bare half walls let the day in over their top (user, 2026-10-20): a waist-high wall under a roof leaves a gap
        // between its top and the roof, so light from the open side reaches the floor behind it. The game cannot do this -
        // its daylight is one number per roofed-or-not cell - so the same kind of light is laid beside them, at full
        // brightness (a half wall has no boards to open) and only towards the roofed side.
        private static void DepositHalfWalls(Map map, Field f)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(BareHalfWallDefName);
            if (def == null || map.roofGrid == null || map.skyManager == null)
            {
                return;
            }
            float sky = Mathf.Clamp01(map.skyManager.CurSkyGlow);
            if (sky <= 0.01f)
            {
                return;
            }
            Color bright = map.skyManager.CurSky.colors.sky * sky;
            List<Thing> walls = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < walls.Count; i++)
            {
                Building wall = walls[i] as Building;
                if (wall == null || !wall.Spawned)
                {
                    continue;
                }
                IntVec3 skyCell;
                IntVec3 darkCell;
                if (!SkyAcross(map, wall.Position, new IntVec3(1, 0, 0), out skyCell, out darkCell)
                    && !SkyAcross(map, wall.Position, new IntVec3(0, 0, 1), out skyCell, out darkCell))
                {
                    continue;
                }
                DepositField(map, f, darkCell, bright, sky, 1f);
            }
        }

        // Real sky: open to the weather and not one of ProjectZ's gap rows between storeys.
        private static bool OpenSky(Map map, IntVec3 c)
        {
            if (c.InBounds(map) == false || map.roofGrid.Roofed(c))
            {
                return false;
            }
            TerrainDef terrain = map.terrainGrid.TerrainAt(c);
            return terrain == null || terrain.defName != VoidTerrainDefName;
        }

        // The axis of a bare half wall that has the weather on one side and a roof on the other: that is the way the day
        // comes over it. A wall with roofs (or open sky) on both sides has nothing to bring in.
        private static bool SkyAcross(Map map, IntVec3 pos, IntVec3 axis, out IntVec3 skyCell, out IntVec3 darkCell)
        {
            skyCell = IntVec3.Invalid;
            darkCell = IntVec3.Invalid;
            IntVec3 a = pos + axis;
            IntVec3 b = pos - axis;
            if (!a.InBounds(map) || !b.InBounds(map))
            {
                return false;
            }
            bool roofedA = map.roofGrid.Roofed(a);
            if (roofedA == map.roofGrid.Roofed(b))
            {
                return false;
            }
            skyCell = roofedA ? b : a;
            darkCell = roofedA ? a : b;
            return OpenSky(map, skyCell);
        }

        // A roofed cell standing under open sky, with nothing but light-transparent things in it, is an opening in the roof
        // line: the day comes in there. That is the other half of what the player asked about (user, 2026-10-20) - take
        // the half wall away and the roof is still there, so the game shows nothing at all. Only real rooms are lit, and
        // only a cell that looks out on open sky on at least one side, so an ordinary roofed room (walls all round) is
        // untouched.
        private static void DepositOpenEdges(Map map, Field f)
        {
            if (map.roofGrid == null || map.skyManager == null)
            {
                return;
            }
            float sky = Mathf.Clamp01(map.skyManager.CurSkyGlow);
            if (sky <= 0.01f)
            {
                return;
            }
            Color bright = map.skyManager.CurSky.colors.sky * sky;
            foreach (IntVec3 c in map.AllCells)
            {
                if (!map.roofGrid.Roofed(c))
                {
                    continue;
                }
                Thing edifice = c.GetEdifice(map);
                if (edifice != null && edifice.def.blockLight)
                {
                    continue;   // a wall or a door: not an opening
                }
                // A walkable roofed cell always has a room; a wall cell (no region) does not, and is not an opening.
                Room room = c.GetRoom(map);
                if (room == null)
                {
                    continue;
                }
                bool skyNext = false;
                for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                {
                    if (OpenSky(map, c + GenAdj.CardinalDirections[i]))
                    {
                        skyNext = true;
                        break;
                    }
                }
                if (skyNext)
                {
                    DepositField(map, f, c, bright, sky, 1f);
                }
            }
        }

        // Spreads one opening's light into the darker side: brightest at the opening, fading to nothing at the reach, and
        // only inside that side's own room (light must not pass the room's walls into the next room).
        private static void DepositField(Map map, Field f, IntVec3 darkCell, Color bright, float brightLevel, float open)
        {
            float reach = Mathf.Lerp(MinReach, MaxReach, open);
            Room darkRoom = darkCell.GetRoom(map);
            if (darkRoom == null)
            {
                return;
            }
            // How much of the day is missing, not how much daylight there is. The shader (IsoRimSolid.frag.glsl,
            // GlowLight) keeps 1 - g.a of the sky under a roof, and a roofed cell's cover is RoofCover, so an ordinary
            // indoor room (which the game's own overlay leaves dark) needs the whole day, while a roofed place that the
            // game does not draw as indoor already keeps 1 - RoofCover of it and only the covered part has to be filled
            // in. Adding a whole sky on top of that part is what made a roof in the open brighter than the field around
            // it (user, 2026-10-20).
            float gain = darkRoom.ProperRoom ? 1f : RoofCover;
            int radius = Mathf.CeilToInt(reach);
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);
                    if (distance > reach)
                    {
                        continue;
                    }
                    IntVec3 c = darkCell + new IntVec3(dx, 0, dz);
                    if (!c.InBounds(map) || c.GetRoom(map) != darkRoom)
                    {
                        continue;
                    }
                    float falloff = (1f - distance / reach) * open;
                    // The colour is scaled by the gain (how much of the day is missing there); the figure is not - a cell
                    // standing in the sun reads as fully lit whether or not its roof covers it (user, 2026-10-20).
                    AddToCell(map, f, c, bright * (falloff * gain), brightLevel * falloff);
                }
            }
        }

        // What light a cell has, and of what colour: the sky for a cell under no roof, else the glow grid's own lights
        // (lamps, fires). This reads the grid, never the tables, so the window it is working on cannot light itself.
        private static bool SideLight(Map map, IntVec3 c, out float level, out Color colour)
        {
            level = 0f;
            colour = Color.black;
            if (!c.InBounds(map))
            {
                return false;
            }
            float sky = 0f;
            Color skyColour = Color.black;
            if (map.skyManager != null && map.roofGrid != null && !map.roofGrid.Roofed(c))
            {
                sky = Mathf.Clamp01(map.skyManager.CurSkyGlow);
                skyColour = map.skyManager.CurSky.colors.sky * sky;
            }
            Color32 glow = map.glowGrid.VisualGlowAt(c);
            // The glow grid marks an over-lit cell (a sun lamp, say) with alpha 1; GroundGlowAt reads that as full
            // brightness, so the window must pass it on as full brightness too - that is what makes a wide open window
            // read like the bare half wall the same lamp shines through.
            float lamp = glow.a == 1
                ? 1f
                : Mathf.Min(0.5f, Mathf.Max(glow.r, Mathf.Max(glow.g, glow.b)) / 255f * 3.6f);
            if (sky >= lamp)
            {
                level = sky;
                colour = skyColour;
            }
            else
            {
                level = lamp;
                colour = new Color(glow.r / 255f, glow.g / 255f, glow.b / 255f);
            }
            return true;
        }

        private static void AddToCell(Map map, Field f, IntVec3 c, Color colour, float level)
        {
            int index = map.cellIndices.CellToIndex(c);
            int levelByte = Mathf.Clamp(Mathf.RoundToInt(level * 255f), 0, 255);
            // Two windows over the same cell do not add up: the brightest one wins. A second opening cannot make a cell
            // brighter than the light it lets in - with the sum, two windows turned a 50% room into 88% (user,
            // 2026-10-20).
            if (levelByte <= f.builtLevels[index])
            {
                return;
            }
            f.builtLevels[index] = (byte)levelByte;
            f.builtColours[index] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(colour.r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(colour.g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(colour.b * 255f), 0, 255),
                0);
            f.builtTouched.Add(index);
        }

        private static Field FieldFor(GlowGrid grid)
        {
            if (grid == null)
            {
                return null;
            }
            Map map = MapOf(grid);
            if (map == null)
            {
                return null;
            }
            if (map == cachedMap)
            {
                return cachedField;
            }
            Field f;
            if (fields.TryGetValue(map, out f))
            {
                cachedMap = map;
                cachedField = f;
                return f;
            }
            return null;
        }

        private static Map MapOf(GlowGrid grid)
        {
            if (!mapFieldTried)
            {
                mapFieldTried = true;
                try
                {
                    mapField = AccessTools.FieldRefAccess<GlowGrid, Map>("map");
                }
                catch (Exception e)
                {
                    Log.ErrorOnce("[ProjectZ] windows: GlowGrid.map not found, the windows will not let light in: " + e, 0x5A17C6);
                }
            }
            return mapField == null ? null : mapField(grid);
        }

        // Patched on its own (not through PatchAll), so that a game version without one of these private details costs
        // the window light and nothing else: every other patch in the addon still goes on.
        public static void TryPatch(Harmony harmony)
        {
            var glowRead = AccessTools.Method(typeof(GlowGrid), "GetAccumulatedGlowAt", new[] { typeof(int), typeof(bool) });
            if (glowRead != null)
            {
                harmony.Patch(glowRead, postfix: new HarmonyMethod(typeof(WindowLight), nameof(OnGlowRead)));
            }
            else
            {
                Log.Error("[ProjectZ] windows: GlowGrid.GetAccumulatedGlowAt is missing; the windows will not light the picture");
            }
            var groundGlow = AccessTools.Method(typeof(GlowGrid), nameof(GlowGrid.GroundGlowAt), new[] { typeof(IntVec3), typeof(bool), typeof(bool) });
            if (groundGlow != null)
            {
                harmony.Patch(groundGlow, postfix: new HarmonyMethod(typeof(WindowLight), nameof(OnGroundGlow)));
            }
            else
            {
                Log.Error("[ProjectZ] windows: GlowGrid.GroundGlowAt is missing; the windows will not read as lit");
            }
        }

        // Every light read funnels through this private method (both VisualGlowAt overloads call it, and GroundGlowAt goes
        // through them), so one patch covers the light overlay, the 3D view IsoRim builds from it, and the plain reads.
        public static void OnGlowRead(GlowGrid __instance, int index, ref Color32 __result)
        {
            if (Computing)
            {
                return;
            }
            Field f = FieldFor(__instance);
            if (f == null || index < 0 || index >= f.colours.Length)
            {
                return;
            }
            Color32 add = f.colours[index];
            if (add.r == 0 && add.g == 0 && add.b == 0)
            {
                return;
            }
            if (!logged)
            {
                logged = true;
                Log.Message("[ProjectZ] windows: window light is reaching the map's light");
            }
            __result = new Color32(
                (byte)Mathf.Min(255, __result.r + add.r),
                (byte)Mathf.Min(255, __result.g + add.g),
                (byte)Mathf.Min(255, __result.b + add.b),
                __result.a);
        }

        // The gameplay figure: a fully open window has to read like the bare half wall it stands in (the sky's own
        // brightness), which the glow grid's own half-brightness cap on light sources would never reach.
        public static void OnGroundGlow(GlowGrid __instance, IntVec3 c, bool ignoreSky, ref float __result)
        {
            if (Computing || ignoreSky)
            {
                return;   // the darkness overlay asks for the light without the sky; the window must not answer for it
            }
            Map map = MapOf(__instance);
            if (map == null || !c.InBounds(map))
            {
                return;
            }
            Field f = FieldFor(__instance);
            if (f == null || f.levels.Length == 0)
            {
                return;
            }
            float level = f.levels[map.cellIndices.CellToIndex(c)] / 255f;
            if (level > __result)
            {
                __result = level;
            }
        }

        // The game's own figure only looks at the roof; the light the picture gets has to be filled in by the shader, so
        // the figure is answered from the tables only (a roofed cell with no light let into it reads dark, as it looks).
    }
}
