using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // Daylight that comes through a window is *sky* light: vanilla caps an ordinary glower's ground glow at 0.5, and adds
    // glowers together, which is wrong for a hole in a wall. So the window keeps its glower for the picture (it is what
    // draws the light), and this class keeps a second, tiny grid of "daylight through windows", which GroundGlowAt takes
    // the max of:
    //
    //   - it may go above 0.5, because the sky does (user, 2026-10-27);
    //   - it never adds up: a lamp, a second window and the daylight itself all merge by max, not by sum;
    //   - it is blocked by walls exactly like the glow grid.
    //
    // The grid is rebuilt at most once per WindowLightSettings interval, from the sources the window/half-wall comps
    // report every time they refresh (SPEC §30.10).
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
            public readonly List<int> touched = new List<int>();
        }

        private static readonly Dictionary<Map, Field> fields = new Dictionary<Map, Field>();
        private static AccessTools.FieldRef<GlowGrid, Map> mapOfGrid;
        private static Map lastMap;
        private static Field lastField;
        private static int builds;

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
            if (f == null || f.value == null)
            {
                return 0f;
            }
            if (!c.InBounds(map))
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
            if (++builds % 200 == 0)
            {
                Prune();
            }
            f.generation++;
            f.touched.Clear();
            foreach (Source s in f.sources.Values)
            {
                Flood(map, f, s);
            }
        }

        private static void Flood(Map map, Field f, Source s)
        {
            int radius = Mathf.CeilToInt(s.radius);
            if (radius <= 0)
            {
                return;
            }
            IntVec3 start = s.pos;
            if (!start.InBounds(map))
            {
                return;
            }
            // Breadth first, four-way, walls (light blockers) stop it - the same shape the glow grid uses.
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
                    if (f.stamp[idx] != f.generation || v > f.value[idx])
                    {
                        f.stamp[idx] = f.generation;
                        f.value[idx] = v;
                        f.touched.Add(idx);
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
                    if (seen.Contains(nidx))
                    {
                        continue;
                    }
                    if ((n - start).LengthHorizontal > radius)
                    {
                        continue;
                    }
                    Building ed = n.GetEdifice(map);
                    if (ed != null && ed.def != null && ed.def.blockLight)
                    {
                        continue;
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
            if (mapOfGrid == null)
            {
                mapOfGrid = AccessTools.FieldRefAccess<GlowGrid, Map>("map");
            }
            return mapOfGrid(grid);
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

        // Every rebuild now and then, drop the maps that are no longer loaded (the dictionary would otherwise keep a
        // whole map alive after it is closed).
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

    // Ground glow asks for a float; the window's daylight may be higher than the 0.5 vanilla allows a lamp, so the max
    // is taken here. ignoreSky is respected: this is sky light.
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
}
