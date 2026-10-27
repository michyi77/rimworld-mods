using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // A window is a hole in a wall, so what lights the room through it is the sky, not a lamp. Vanilla's GlowGrid has a
    // public entry for a light that is not a CompGlower - RegisterCustomGlower(cell, radius, colour) - and the same job
    // that spreads a lamp's light spreads this one: walls block it, the light level counts for work speed and plant
    // growth, and nothing leaks into a room that has no window (SPEC §30).
    //
    // The colour follows SkyManager.CurSkyGlow and the boards' opening, and the light is re-registered only when that
    // changed by more than the mod setting says: every registration makes the grid recompute the cells around the
    // window, which is the expensive part.
    public class CompProperties_WindowSkyLight : CompProperties
    {
        public float glowRadius = 7f;
        public float skyStrength = 1f;   // brightness at full daylight with the boards wide open
        public float dimmest = 0.04f;    // below this nothing is registered at all

        public CompProperties_WindowSkyLight()
        {
            compClass = typeof(CompWindowSkyLight);
        }
    }

    public class CompWindowSkyLight : ThingComp
    {
        private int handle;              // the custom glower's handle; negative, and only valid while lit
        private bool lit;
        private float lastLevel = -1f;
        private int lastCheck = -99999;
        private bool lastOpen;
        private string lastWallDef;

        private CompProperties_WindowSkyLight Props
        {
            get { return (CompProperties_WindowSkyLight)props; }
        }

        private CompWindowLatch Latch
        {
            get { return parent.GetComp<CompWindowLatch>(); }
        }

        // 0..1: how much daylight this opening lets in right now.
        public float Level
        {
            get
            {
                Map map = parent.Map;
                if (map == null || !parent.Spawned || parent.Destroyed)
                {
                    return 0f;
                }
                float sky = map.skyManager != null ? map.skyManager.CurSkyGlow : 0f;
                CompWindowLatch latch = Latch;
                float open = latch != null ? Mathf.Clamp01(latch.openPct) : 1f;
                return Mathf.Clamp01(sky * open * Props.skyStrength);
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            Refresh(true);
        }

        public override void PostMapInit()
        {
            base.PostMapInit();
            Refresh(true);
        }

        public override void PostSwapMap()
        {
            base.PostSwapMap();
            Refresh(true);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            Drop(map);
        }

        public override void CompTick()
        {
            base.CompTick();
            Refresh(false);
        }

        // Offered to the latch when the boards move: throttled and change-checked like the tick, so calling it every
        // step of a slide costs nothing.
        public void RefreshSoon()
        {
            Refresh(false);
        }

        private void Refresh(bool force)
        {
            try
            {
                RefreshInner(force);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[ProjectZ] windows: refreshing the daylight failed: " + e, 0x5A17C8);
            }
        }

        private void RefreshInner(bool force)
        {
            Map map = parent.Map;
            if (map == null || !parent.Spawned || parent.Destroyed)
            {
                return;
            }
            if (!WindowLightSettings.On)
            {
                Drop(map);
                return;
            }
            if (!force)
            {
                int now = Find.TickManager.TicksGame;
                if (now - lastCheck < WindowLightSettings.IntervalTicks)
                {
                    return;
                }
                lastCheck = now;
            }
            // Out in the open there is nothing to light: the ground already has the sky on it, and a glower there only
            // paints a bright disc around every half wall on the map (user, 2026-10-27: "半墙变成了一个光源比自然光亮一圈").
            // Only light a cell that is under a roof, or next to one - that is where a window or a half wall actually
            // makes a shaded place brighter.
            // The blocker is about lamp light passing through an open window, which an inner window does too: it is
            // synced whatever the sky does.
            SyncBlocker(map);
            if (!NearRoof(map, parent.Position) || !SeesSky(map, parent.Position))
            {
                Drop(map);
                return;
            }
            float level = Level;
            if (!force && Mathf.Abs(level - lastLevel) < WindowLightSettings.Step)
            {
                return;
            }
            lastLevel = level;
            Drop(map);
            if (level < Props.dimmest)
            {
                return;
            }
            // dawn and dusk are warm, midday is white; alpha 0 like a vanilla glower, so the grid does not read it as
            // "overlit" (GlowGrid.AddColors uses the alpha as the overlight flag)
            float sky = map.skyManager != null ? map.skyManager.CurSkyGlow : 1f;
            Color colour = Color.Lerp(new Color(1f, 0.84f, 0.62f), Color.white, Mathf.Clamp01(sky * 1.6f));
            ColorInt light = new ColorInt(Mathf.RoundToInt(colour.r * 255f), Mathf.RoundToInt(colour.g * 255f),
                Mathf.RoundToInt(colour.b * 255f), 0) * (1.45f * level);
            handle = map.glowGrid.RegisterCustomGlower(parent.Position, Props.glowRadius, light);
            lit = true;
        }

        // A window between two roofed rooms sees no sky at all, so it lets no daylight in: there has to be an unroofed
        // cell next to it (or over it) for the light to come from (user, 2026-10-27: an inner window lit both rooms).
        private static bool SeesSky(Map map, IntVec3 c)
        {
            if (!map.roofGrid.Roofed(c))
            {
                return true;
            }
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                if (!map.roofGrid.Roofed(c + GenAdj.CardinalDirections[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool NearRoof(Map map, IntVec3 c)
        {
            if (map.roofGrid.Roofed(c))
            {
                return true;
            }
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                if (map.roofGrid.Roofed(c + GenAdj.CardinalDirections[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private void Drop(Map map)
        {
            if (lit && map != null && map.glowGrid != null)
            {
                map.glowGrid.DeregisterCustomGlower(handle);
            }
            lit = false;
        }

        // An open window is a hole: take the cell out of the light-blocker set so a lamp on one side reaches the other,
        // and put it back when the boards are shut. Only windows have a latch; a bare half wall never blocks light.
        private void SyncBlocker(Map map)
        {
            CompWindowLatch latch = Latch;
            if (latch == null || map == null || map.glowGrid == null)
            {
                return;
            }
            // The half wall is swapped for another def whenever the boards cross the open line, and a swapped-in wall
            // puts the blocker back - so the cell's def name is part of the key, not just the open flag.
            Building wall = HalfWallState.WallAt(parent.Position, map);
            string wallDef = wall == null || wall.def == null ? null : wall.def.defName;
            bool open = latch.openPct > 0.001f;
            if (wallDef == lastWallDef && open == lastOpen)
            {
                return;
            }
            lastWallDef = wallDef;
            lastOpen = open;
            if (open)
            {
                map.glowGrid.LightBlockerRemoved(parent.Position);
            }
            else
            {
                map.glowGrid.LightBlockerAdded(parent.Position);
            }
        }
    }
}
