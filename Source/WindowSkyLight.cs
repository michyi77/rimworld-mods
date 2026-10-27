using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectZ.Windows
{
    // A window is a hole in a wall, so what lights the room through it is the sky, not a lamp. The light level and the
    // picture are both handled by WindowDaylight (a small grid that GroundGlowAt and VisualGlowAt take the max of), so
    // no glower is registered at the window: a glower's level is capped at 0.5 by the game and adds to the lamps, and
    // that is exactly what this must not do (SPEC §30.12).
    //
    // This comp only says how much daylight the opening lets in - the sky, dimmed by the weather, times how far the
    // boards are open - and how often that is looked at, per the mod settings.
    public class CompProperties_WindowSkyLight : CompProperties
    {
        public float glowRadius = 7f;
        public float skyStrength = 1f;   // brightness at full daylight with the boards wide open
        public float dimmest = 0.04f;    // below this the opening reports nothing

        public CompProperties_WindowSkyLight()
        {
            compClass = typeof(CompWindowSkyLight);
        }
    }

    public class CompWindowSkyLight : ThingComp
    {
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
                float sky = map.skyManager != null ? DaylightSky(map.skyManager) : 0f;
                CompWindowLatch latch = Latch;
                float open = latch != null ? Mathf.Clamp01(latch.openPct) : 1f;
                return Mathf.Clamp01(sky * open * Props.skyStrength);
            }
        }

        // How much daylight the sky is putting down. CurSkyGlow is the celestial glow and does not drop for weather
        // (rain and storms only change the sky *colour*), so the colour's brightness is taken with it: a clear day is
        // white (1.0), rain is 0.8, a thunderstorm darker still (user, 2026-10-27).
        private static float DaylightSky(SkyManager sky)
        {
            float glow = sky.CurSkyGlow;
            Color colour = sky.CurSky.colors.sky;
            float luma = 0.299f * colour.r + 0.587f * colour.g + 0.114f * colour.b;
            return glow * Mathf.Clamp01(luma);
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
            WindowDaylight.Remove(map, parent.thingIDNumber);
        }

        public override void CompTick()
        {
            base.CompTick();
            Refresh(false);
        }

        // Offered to the latch when the boards move: throttled and change-checked like the tick.
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
            // Lamp light through an open window is a matter of the light blocker, not of the sky: synced either way.
            SyncBlocker(map);
            if (!WindowLightSettings.On || !NearRoof(map, parent.Position) || !SeesSky(map, parent.Position))
            {
                WindowDaylight.Remove(map, parent.thingIDNumber);
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
            float level = Level;
            WindowDaylight.Report(map, parent.thingIDNumber, parent.Position, Props.glowRadius, level);
            WindowDaylight.RebuildIfDue(map);
        }

        // A window between two roofed rooms sees no sky at all, so it lets no daylight in.
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

        // Out in the open there is nothing to light: the ground already has the sky on it.
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
