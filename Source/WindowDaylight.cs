using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // Daylight that comes through a window is *sky* light: vanilla caps an ordinary glower's ground glow at 0.5 and adds
    // lights together, which is wrong for a hole in a wall. So the windows' and bare half walls' daylight lives here,
    // in its own small per-map grid, and both the light level and the light overlay take the max of it and the vanilla
    // light:
    //
    //   - it may go above 0.5, because the sky does (user, 2026-10-27);
    //   - it never adds up: a lamp, a second window and the daylight itself all merge by max (user, 2026-10-27);
    //   - walls block it, and it is gated by roof/sky in CompWindowSkyLight;
    //   - no glower is registered at the window at all, so the light grid cannot round it to 0.5 or paint a halo.
    //
    // The grid is rebuilt at most once per WindowLightSettings interval, from the sources the comps report each refresh
    // (SPEC §30.12).
    public static class WindowDaylight
    {
        private sealed class Source
        {
            public IntVec3 pos;
            public float radius;
            public float level;
        }

        private sealed class Field
        {
            public float[] value;
            public int[] stamp;          // rebuild generation per cell, so clearing is cheap
            public int generation = 1;
            public int lastBuild = -99999;
            public readonly Dictionary<int, Source> sources = new Dictionary<int, Source>();
            public readonly List<int> touched = new List<int>();   // cells written in the latest generation
        }

        private static readonly Dictionary<Map, Field> fields = new Dictionary<Map, Field>();
        private static AccessTools.FieldRef<GlowGrid, Map> mapOfGrid;
        private static Map lastMap;
        private static Field lastField;
        private static int builds;
        private static bool mapFieldWarned;

        public static void Report(Map map, int id, IntVec3 pos, float radius, float level)
        {
            if (map == null || level <= 0f)
            {
                Remove(map, id);
                return;
            }
            Field f = FieldFor(map);
            Source s;
            if (!f.sources.TryGetValue(id, out s))
            {
                s = new Source();
                f.sources[id] = s;
            }
            s.pos = pos;
            s.radius = radius;
            s.level = level;
        }

        public static void Remove(Map map, int id)
        {
            Field f;
            if (map != null && fields.TryGetValue(map, out f))
            {
                f.sources.Remove(id);
            }
        }

        public static float At(Map map, IntVec3 c)
        {
            if (map == null)
            {
                return 0f;
            }
            if (map != lastMap)
            {
                lastMap = map;
                if (!fields.TryGetValue(map, out lastField))
                {
                    lastField = null;
                }
            }
            Field f = lastField;
            if (f == null || f.value == null || !c.InBounds(map))
            {
                return 0f;
            }
            int idx = map.cellIndices.CellToIndex(c);
            return f.stamp[idx] == f.generation ? f.value[idx] : 0f;
        }

        // Called by the comps as they refresh; the work itself is throttled here.
        public static void RebuildIfDue(Map map)
        {
            if (map == null)
            {
                return;
            }
            Field f = FieldFor(map);
            int now = Find.TickManager.TicksGame;
            if (now - f.lastBuild < WindowLightSettings.IntervalTicks)
            {
                return;
            }
            Rebuild(map, f);
        }

        private static void Rebuild(Map map, Field f)
        {
            f.lastBuild = Find.TickManager.TicksGame;
            // Cells that were lit last time but may not be now have to be redrawn too.
            for (int i = 0; i < f.touched.Count; i++)
            {
                map.glowGrid.DirtyCell(map.cellIndices.IndexToCell(f.touched[i]));
            }
            f.touched.Clear();
            f.generation++;
            if (++builds % 200 == 0)
            {
                Prune();
            }
            foreach (Source s in f.sources.Values)
            {
                Flood(map, f, s);
            }
        }

        private static void Flood(Map map, Field f, Source s)
        {
            int radius = Mathf.CeilToInt(s.radius);
            if (radius <= 0 || !s.pos.InBounds(map))
            {
                return;
            }
            IntVec3 start = s.pos;
            var queue = new Queue<IntVec3>();
            var seen = new HashSet<int>();
            queue.Enqueue(start);
            seen.Add(map.cellIndices.CellToIndex(start));
            while (queue.Count > 0)
            {
                IntVec3 c = queue.Dequeue();
                int dist = Mathf.RoundToInt((c - start).LengthHorizontal);
                float v = s.level * Mathf.Clamp01(1f - (float)dist / (s.radius + 0.5f));
                if (v > 0f)
                {
                    int idx = map.cellIndices.CellToIndex(c);
                    if (f.stamp[idx] != f.generation)
                    {
                        f.stamp[idx] = f.generation;
                        f.value[idx] = v;
                        f.touched.Add(idx);
                        map.glowGrid.DirtyCell(c);
                    }
                    else if (v > f.value[idx])
                    {
                        f.value[idx] = v;
                        map.glowGrid.DirtyCell(c);
                    }
                }
                for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                {
                    IntVec3 n = c + GenAdj.CardinalDirections[i];
                    if (!n.InBounds(map))
                    {
                        continue;
                    }
                    int nidx = map.cellIndices.CellToIndex(n);
                    if (seen.Contains(nidx) || (n - start).LengthHorizontal > radius)
                    {
                        continue;
                    }
                    Building ed = n.GetEdifice(map);
                    if (ed != null && ed.def != null && ed.def.blockLight)
                    {
                        continue;   // walls stop the daylight, exactly like the glow grid
                    }
                    seen.Add(nidx);
                    queue.Enqueue(n);
                }
            }
        }

        private static Field FieldFor(Map map)
        {
            Field f;
            if (!fields.TryGetValue(map, out f))
            {
                f = new Field();
                f.value = new float[map.cellIndices.NumGridCells];
                f.stamp = new int[map.cellIndices.NumGridCells];
                fields[map] = f;
            }
            return f;
        }

        internal static Map MapOf(GlowGrid grid)
        {
            try
            {
                if (mapOfGrid == null)
                {
                    mapOfGrid = AccessTools.FieldRefAccess<GlowGrid, Map>("map");
                }
                return mapOfGrid(grid);
            }
            catch (Exception e)
            {
                if (!mapFieldWarned)
                {
                    mapFieldWarned = true;
                    Log.Warning("[ProjectZ] windows: GlowGrid.map not found, the window daylight is off: " + e.Message);
                }
                return null;
            }
        }

        public static void Forget(Map map)
        {
            fields.Remove(map);
            if (map == lastMap)
            {
                lastMap = null;
                lastField = null;
            }
        }

        // Every rebuild now and then, drop maps that are no longer loaded (the dictionary would keep a whole map alive).
        private static void Prune()
        {
            if (Find.Maps == null)
            {
                return;
            }
            var gone = new List<Map>();
            foreach (Map map in fields.Keys)
            {
                if (!Find.Maps.Contains(map))
                {
                    gone.Add(map);
                }
            }
            for (int i = 0; i < gone.Count; i++)
            {
                Forget(gone[i]);
            }
        }
    }

    // The light *level*: above 0.5 is allowed for daylight, merged by max with whatever lamps already give.
    [HarmonyPatch(typeof(GlowGrid), nameof(GlowGrid.GroundGlowAt))]
    internal static class WindowDaylightGroundGlow
    {
        public static void Postfix(GlowGrid __instance, IntVec3 c, bool ignoreCavePlants, bool ignoreSky,
            ref float __result)
        {
            if (ignoreSky)
            {
                return;
            }
            float day = WindowDaylight.At(WindowDaylight.MapOf(__instance), c);
            if (day > __result)
            {
                __result = day;
            }
        }
    }

    // The picture: the light overlay is drawn from VisualGlowAt, so the daylight is stamped in here, max'ed with the
    // lamps' colour instead of being added to it. DirtyCell (in the rebuild) makes the section redraw.
    [HarmonyPatch(typeof(GlowGrid), nameof(GlowGrid.VisualGlowAt))]
    internal static class WindowDaylightVisualGlow
    {
        public static void Postfix(GlowGrid __instance, int index, ref Color32 __result)
        {
            Map map = WindowDaylight.MapOf(__instance);
            if (map == null)
            {
                return;
            }
            float day = WindowDaylight.At(map, map.cellIndices.IndexToCell(index));
            if (day <= 0f)
            {
                return;
            }
            int v = Mathf.Clamp(Mathf.RoundToInt(day * 255f * 1.45f), 0, 255);
            if (v > Mathf.Max(__result.r, Mathf.Max(__result.g, __result.b)))
            {
                __result = new Color32((byte)v, (byte)v, (byte)v, 0);
            }
        }
    }
}
